// Touch gestures for OpenRA on Android. Added by the OpenRA-Android port (not part of upstream).
//
//   one finger tap         left click (snapped to the nearest button within reach of a fingertip);
//                          with the Classic control scheme this both selects and orders
//   one finger drag        battlefield: pan the camera; lists: scroll; other UI: left drag (sliders, ...)
//   long press, then drag  selection box;  long press and release: right click (deselect / cancel)
//   building placement     first tap / drag only moves the ghost; a second tap near it confirms
//   second finger tap      right click at the second finger
//   two finger drag        pan the battlefield, pinch zooms
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using OpenRA.Primitives;
using OpenRA.Widgets;
using SDL2;

namespace OpenRA.Platforms.Default
{
	sealed class Sdl2Touch
	{
		sealed class Finger
		{
			public float X, Y, StartX, StartY;
			public uint StartTick;
		}

		enum Mode { Idle, Pending, Drag, PlaceDrag, PanDrag, ScrollDrag, LongPress, Multi, Ignore }

		// Vertical calibration in physical pixels (set in the launcher): positive moves the touch point up.
		static readonly int TouchDy = int.TryParse(Environment.GetEnvironmentVariable("OPENRA_TOUCH_DY"), out var dy) ? dy : 0;

		static readonly string LogPath = Environment.GetEnvironmentVariable("OPENRA_TOUCH_LOG");
		static int logged;
		bool geometryLogged;

		internal static void LogLine(string line) => Log(line);

		static void Log(string line)
		{
			if (string.IsNullOrEmpty(LogPath) || logged > 600)
				return;

			logged++;
			try
			{
				File.AppendAllText(LogPath, line + "\n");
			}
			catch
			{
				// Logging must never break input.
			}
		}

		static string WidgetInfo()
		{
			var w = Ui.MouseOverWidget;
			if (w == null)
				return "none";

			var r = w.EventBounds;
			return $"{w.GetType().Name}#{w.Id} [{r.X},{r.Y} {r.Width}x{r.Height}]";
		}

		void LogGeometry(Sdl2PlatformWindow device)
		{
			geometryLogged = true;
			Log($"geometry: SurfaceSize={device.SurfaceSize} EffectiveWindowSize={device.EffectiveWindowSize} " +
				$"EffectiveScale={device.EffectiveWindowScale} NativeScale={device.NativeWindowScale} TouchDy={TouchDy}");
			try
			{
				var win = SDL.SDL_GL_GetCurrentWindow();
				SDL.SDL_GetWindowSize(win, out var ww, out var wh);
				SDL.SDL_GL_GetDrawableSize(win, out var dw, out var dh);
				SDL.SDL_GetWindowPosition(win, out var wx, out var wy);
				SDL.SDL_GetDisplayBounds(0, out var db);
				SDL.SDL_GetDisplayUsableBounds(0, out var ub);
				Log($"sdl: window={ww}x{wh}@{wx},{wy} drawable={dw}x{dh} display={db.w}x{db.h} usable={ub.x},{ub.y} {ub.w}x{ub.h}");
			}
			catch (Exception e)
			{
				Log("sdl geometry failed: " + e.Message);
			}
		}

		const uint LongPressMs = 400;
		const uint SecondTapMs = 300;

		readonly Dictionary<long, Finger> fingers = new();
		readonly List<long> order = new();

		Mode mode = Mode.Idle;
		MouseButton bits = MouseButton.None;
		bool panning;
		float startCx, startCy, startDist, lastCx, lastCy, lastDist, zoomAccum;
		float lastX, lastY;

		// Two-step building placement: the ghost follows taps/drags, a second tap close to it confirms.
		bool placePending;
		float placeX, placeY;
		const float PlaceLift = 0.09f; // ghost is drawn this fraction of the screen height above the finger while dragging

		Func<int, int, int2> lastToPos;

		public Sdl2Touch()
		{
			Game.TouchOverlay = DrawOverlay;
		}

		// Ring that fills while a finger is held; full and bright once the long press is armed.
		void DrawOverlay()
		{
			if (lastToPos == null || fingers.Count != 1 || (mode != Mode.Pending && mode != Mode.LongPress))
				return;

			var fi = fingers[order[0]];
			var held = SDL.SDL_GetTicks() - fi.StartTick;
			if (mode == Mode.Pending && held < 120)
				return;

			var t = mode == Mode.LongPress ? 1f : Math.Min(1f, held / (float)LongPressMs);
			var c = lastToPos((int)fi.StartX, (int)fi.StartY);
			var color = mode == Mode.LongPress ? Color.FromArgb(230, 255, 215, 90) : Color.FromArgb(170, 255, 255, 255);
			var radius = 24f;
			var segments = 32;
			var count = Math.Max(1, (int)(segments * t));
			var rc = Game.Renderer.RgbaColorRenderer;
			for (var i = 0; i < count; i++)
			{
				var a0 = -Math.PI / 2 + 2 * Math.PI * i / segments;
				var a1 = -Math.PI / 2 + 2 * Math.PI * (i + 1) / segments;
				var p0 = new Vector2(c.X + radius * (float)Math.Cos(a0), c.Y + radius * (float)Math.Sin(a0));
				var p1 = new Vector2(c.X + radius * (float)Math.Cos(a1), c.Y + radius * (float)Math.Sin(a1));
				rc.DrawLine(p0, p1, mode == Mode.LongPress ? 4 : 3, color);
			}
		}

		float scrollAccum, lastTapX, lastTapY;
		uint lastTapTick;

		static bool InScrollPanel()
		{
			for (var w = Ui.MouseOverWidget; w != null; w = w.Parent)
				if (w.GetType().Name == "ScrollPanelWidget")
					return true;

			return false;
		}

		static bool OverWorld()
		{
			var n = Ui.MouseOverWidget?.GetType().Name;
			return n == "WorldInteractionControllerWidget" || n == "ViewportControllerWidget";
		}

		static byte Sdl(MouseButton b) =>
			b == MouseButton.Right ? (byte)SDL.SDL_BUTTON_RIGHT
			: b == MouseButton.Middle ? (byte)SDL.SDL_BUTTON_MIDDLE
			: (byte)SDL.SDL_BUTTON_LEFT;

		static void Send(IInputHandler h, MouseInputEvent ev, MouseButton btn, int2 pos, int2 delta, Modifiers mods, int taps = 0)
		{
			try
			{
				h.OnMouseInput(new MouseInput(ev, btn, pos, delta, mods, taps));
			}
			catch (InvalidOperationException e)
			{
				// Two taps in quick succession can reach a widget while its previous dialog is still closing
				// (e.g. "attach a panel to an open dropdown"). Dropping that tap is harmless; crashing is not.
				Log("input ignored: " + e.Message);
			}
		}

		void Down(IInputHandler h, MouseButton b, int2 pos, Modifiers mods)
		{
			bits |= b;
			Send(h, MouseInputEvent.Down, b, pos, int2.Zero, mods, MultiTapDetection.DetectFromMouse(Sdl(b), pos));
		}

		void Up(IInputHandler h, MouseButton b, int2 pos, Modifiers mods)
		{
			bits &= ~b;
			Send(h, MouseInputEvent.Up, b, pos, int2.Zero, mods, MultiTapDetection.InfoFromMouse(Sdl(b)));
		}

		const int SnapRadius = 26;

		static bool IsTarget(Widget w)
		{
			var n = w.GetType().Name;
			return n.EndsWith("ButtonWidget", StringComparison.Ordinal) || n.EndsWith("CheckboxWidget", StringComparison.Ordinal);
		}

		static bool Disabled(Widget w)
		{
			try
			{
				var t = w.GetType();
				object v = t.GetField("IsDisabled", BindingFlags.Public | BindingFlags.Instance)?.GetValue(w)
					?? t.GetProperty("IsDisabled", BindingFlags.Public | BindingFlags.Instance)?.GetValue(w);
				return v is Func<bool> f && f();
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Finger-sized taps: if nothing clickable is under the point, use the nearest button within reach.</summary>
		static int2 Snap(int2 pos)
		{
			Widget best = null;
			var bestDist = double.MaxValue;
			var inside = false;

			void Visit(Widget w)
			{
				if (inside || !w.IsVisible())
					return;

				if (IsTarget(w) && !Disabled(w))
				{
					var r = w.EventBounds;
					var dx = Math.Max(Math.Max(r.Left - pos.X, 0), pos.X - r.Right);
					var dy = Math.Max(Math.Max(r.Top - pos.Y, 0), pos.Y - r.Bottom);
					var d = Math.Sqrt(dx * dx + dy * dy);
					if (d <= 0)
					{
						inside = true;
						return;
					}

					if (d < bestDist)
					{
						bestDist = d;
						best = w;
					}
				}

				foreach (var c in w.Children)
					Visit(c);
			}

			Visit(Ui.Root);
			if (inside || best == null || bestDist > SnapRadius)
				return pos;

			var b = best.EventBounds;
			return new int2(b.Left + b.Width / 2, b.Top + b.Height / 2);
		}

		void Click(IInputHandler h, MouseButton b, int2 pos, Modifiers mods)
		{
			var before = pos;
			if (b == MouseButton.Left)
				pos = Snap(pos);

			Send(h, MouseInputEvent.Move, MouseButton.None, pos, int2.Zero, mods);
			Log($"click {b} pos={before} snapped={pos} over={WidgetInfo()}");
			Down(h, b, pos, mods);
			Up(h, b, pos, mods);
		}

		void Centroid(out float cx, out float cy, out float dist)
		{
			var a = fingers[order[0]];
			var b = fingers[order[1]];
			cx = (a.X + b.X) / 2;
			cy = (a.Y + b.Y) / 2;
			var dx = a.X - b.X;
			var dy = a.Y - b.Y;
			dist = (float)Math.Sqrt(dx * dx + dy * dy);
		}

		public void Handle(SDL.SDL_Event e, Sdl2PlatformWindow device, IInputHandler h, Modifiers mods, Func<int, int, int2> toPos)
		{
			var w = device.SurfaceSize.Width;
			var height = device.SurfaceSize.Height;
			var slop = height * 0.02f;
			var f = e.tfinger;
			var x = f.x * w;
			var y = Math.Max(0, f.y * height - TouchDy);
			int2 P(float px, float py) => toPos((int)px, (int)py);

			lastToPos = toPos;
			if (!geometryLogged)
				LogGeometry(device);

			switch (e.type)
			{
				case SDL.SDL_EventType.SDL_FINGERDOWN:
				{
					Log($"down raw=({f.x:0.000},{f.y:0.000}) phys=({x:0},{y:0}) pos={P(x, y)} fingers={fingers.Count + 1}");
					fingers[f.fingerId] = new Finger { X = x, Y = y, StartX = x, StartY = y, StartTick = SDL.SDL_GetTicks() };
					order.Add(f.fingerId);

					if (fingers.Count == 1)
					{
						mode = Mode.Pending;
						lastX = x;
						lastY = y;
						Send(h, MouseInputEvent.Move, MouseButton.None, P(x, y), int2.Zero, mods);
					}
					else if (fingers.Count == 2)
					{
						if (mode == Mode.Drag)
							Up(h, MouseButton.Left, P(lastX, lastY), mods);
						else if (mode == Mode.PanDrag)
							Up(h, MouseButton.Middle, P(lastX, lastY), mods);

						mode = Mode.Multi;
						panning = false;
						zoomAccum = 0;
						Centroid(out startCx, out startCy, out startDist);
						lastCx = startCx;
						lastCy = startCy;
						lastDist = startDist;
					}
					else
						mode = Mode.Ignore;

					break;
				}

				case SDL.SDL_EventType.SDL_FINGERMOTION:
				{
					if (!fingers.TryGetValue(f.fingerId, out var fi))
						break;

					fi.X = x;
					fi.Y = y;

					if (mode == Mode.Pending || mode == Mode.LongPress)
					{
						var dx = x - fi.StartX;
						var dy = y - fi.StartY;
						if (dx * dx + dy * dy > slop * slop)
						{
							var armed = mode == Mode.LongPress;
							var start = P(fi.StartX, fi.StartY);
							if (armed)
							{
								// Long press then drag: selection box.
								mode = Mode.Drag;
								Down(h, MouseButton.Left, start, mods);
							}
							else if (Game.IsPlacingBuilding && OverWorld())
								mode = Mode.PlaceDrag;
							else if (InScrollPanel())
							{
								mode = Mode.ScrollDrag;
								scrollAccum = 0;
							}
							else if (OverWorld())
							{
								// Drag on the battlefield moves the camera (scroll button + "Standard" scroll type).
								mode = Mode.PanDrag;
								Down(h, MouseButton.Middle, start, mods);
							}
							else
							{
								mode = Mode.Drag;
								Down(h, MouseButton.Left, start, mods);
							}
						}
					}

					if (mode == Mode.PlaceDrag)
					{
						placeX = x;
						placeY = Math.Max(0, y - height * PlaceLift);
						Send(h, MouseInputEvent.Move, MouseButton.None, P(placeX, placeY), int2.Zero, mods);
						placePending = true;
						lastX = x;
						lastY = y;
					}
					else if (mode == Mode.PanDrag)
					{
						var from = P(lastX, lastY);
						var to = P(x, y);
						Send(h, MouseInputEvent.Move, bits, to, to - from, mods);
						lastX = x;
						lastY = y;
					}
					else if (mode == Mode.ScrollDrag)
					{
						scrollAccum += y - lastY;
						lastY = y;
						var stepPx = height * 0.045f;
						while (Math.Abs(scrollAccum) >= stepPx)
						{
							var dir = scrollAccum > 0 ? 1 : -1;
							Send(h, MouseInputEvent.Scroll, MouseButton.None, P(x, y), new int2(0, dir), mods);
							scrollAccum -= dir * stepPx;
						}
					}
					else if (mode == Mode.Drag)
					{
						var from = P(lastX, lastY);
						var to = P(x, y);
						Send(h, MouseInputEvent.Move, bits, to, to - from, mods);
						lastX = x;
						lastY = y;
					}
					else if (mode == Mode.Multi && fingers.Count >= 2)
					{
						Centroid(out var cx, out var cy, out var dist);

						if (!panning)
						{
							var mx = cx - startCx;
							var my = cy - startCy;
							if (mx * mx + my * my > slop * slop || Math.Abs(dist - startDist) > slop * 1.5f)
							{
								panning = true;
								Send(h, MouseInputEvent.Move, MouseButton.None, P(cx, cy), int2.Zero, mods);
								Down(h, MouseButton.Middle, P(cx, cy), mods);
								lastCx = cx;
								lastCy = cy;
								lastDist = dist;
							}
						}
						else
						{
							var from = P(lastCx, lastCy);
							var to = P(cx, cy);
							Send(h, MouseInputEvent.Move, bits, to, to - from, mods);

							zoomAccum += dist - lastDist;
							var step = height * 0.08f;
							while (Math.Abs(zoomAccum) >= step)
							{
								var dir = zoomAccum > 0 ? 1 : -1;
								Send(h, MouseInputEvent.Scroll, MouseButton.None, to, new int2(0, dir), mods);
								zoomAccum -= dir * step;
							}

							lastCx = cx;
							lastCy = cy;
							lastDist = dist;
						}
					}

					break;
				}

				case SDL.SDL_EventType.SDL_FINGERUP:
				{
					if (!fingers.TryGetValue(f.fingerId, out var fi))
						break;

					fi.X = x;
					fi.Y = y;
					var wasSecond = order.Count > 1 && order[1] == f.fingerId;

					if (!Game.IsPlacingBuilding)
						placePending = false;

					if (mode == Mode.Pending && fingers.Count == 1)
					{
						var placingOnWorld = false;
						if (Game.IsPlacingBuilding)
						{
							Send(h, MouseInputEvent.Move, MouseButton.None, P(fi.StartX, fi.StartY), int2.Zero, mods);
							placingOnWorld = OverWorld();
						}

						if (placingOnWorld)
						{
							var near = height * 0.12f;
							var ddx = fi.StartX - placeX;
							var ddy = fi.StartY - placeY;
							if (placePending && ddx * ddx + ddy * ddy < near * near)
							{
								// Second tap near the ghost: build it where the ghost is.
								var at = P(placeX, placeY);
								Send(h, MouseInputEvent.Move, MouseButton.None, at, int2.Zero, mods);
								Down(h, MouseButton.Left, at, mods);
								Up(h, MouseButton.Left, at, mods);
								placePending = false;
							}
							else
							{
								// First tap: only show where it would be built.
								Log($"place-ghost pos={P(fi.StartX, fi.StartY)} over={WidgetInfo()}");
								placeX = fi.StartX;
								placeY = fi.StartY;
								placePending = true;
							}
						}
						else
						{
							// A quick second tap lands exactly where the first did, so double-tap (select all of a
							// kind) works despite finger jitter.
							var tx = fi.StartX;
							var ty = fi.StartY;
							var now = SDL.SDL_GetTicks();
							var jx = tx - lastTapX;
							var jy = ty - lastTapY;
							if (now - lastTapTick < 350 && jx * jx + jy * jy < (height * 0.06f) * (height * 0.06f))
							{
								tx = lastTapX;
								ty = lastTapY;
							}

							lastTapX = tx;
							lastTapY = ty;
							lastTapTick = now;
							Click(h, MouseButton.Left, P(tx, ty), mods);
						}
					}
					else if (mode == Mode.PanDrag && fingers.Count == 1)
						Up(h, MouseButton.Middle, P(x, y), mods);
					else if (mode == Mode.ScrollDrag)
					{
					}
					else if (mode == Mode.LongPress && fingers.Count == 1)
						Click(h, MouseButton.Right, P(fi.StartX, fi.StartY), mods);
					else if (mode == Mode.PlaceDrag && fingers.Count == 1)
					{
						// Leave the ghost where it is; tap near it to confirm.
					}
					else if (mode == Mode.Drag && fingers.Count == 1)
						Up(h, MouseButton.Left, P(x, y), mods);
					else if (mode == Mode.Multi)
					{
						if (panning)
							Up(h, MouseButton.Middle, P(lastCx, lastCy), mods);
						else if (wasSecond && SDL.SDL_GetTicks() - fi.StartTick < SecondTapMs)
							Click(h, MouseButton.Right, P(fi.StartX, fi.StartY), mods);

						mode = Mode.Ignore;
					}

					fingers.Remove(f.fingerId);
					order.Remove(f.fingerId);
					if (fingers.Count == 0)
					{
						mode = Mode.Idle;
						panning = false;
						bits = MouseButton.None;
					}

					break;
				}
			}
		}

		/// <summary>Called once per input pump: fires the long-press right click.</summary>
		public void Tick(Sdl2PlatformWindow device, IInputHandler h, Modifiers mods, Func<int, int, int2> toPos)
		{
			lastToPos = toPos;
			if (mode != Mode.Pending || fingers.Count != 1)
				return;

			var fi = fingers[order[0]];
			if (SDL.SDL_GetTicks() - fi.StartTick < LongPressMs)
				return;

			// Armed: releasing without moving is a right click, dragging draws a selection box.
			mode = Mode.LongPress;
			Log("long press armed");
		}
	}
}
