// Touch gestures for OpenRA on Android. Added by the OpenRA-Android port (not part of upstream).
//
//   one finger tap         left click (snapped to the nearest button within reach of a fingertip)
//   one finger drag        left button drag (selection box, sliders, ...)
//   long press (0.4 s)     right click
//   second finger tap      right click at the second finger
//   two finger drag        pan the battlefield (middle button drag)
//   pinch                  zoom (mouse wheel)
using System;
using System.Collections.Generic;
using System.Reflection;
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

		enum Mode { Idle, Pending, Drag, LongPress, Multi, Ignore }

		const uint LongPressMs = 400;
		const uint SecondTapMs = 300;

		readonly Dictionary<long, Finger> fingers = new();
		readonly List<long> order = new();

		Mode mode = Mode.Idle;
		MouseButton bits = MouseButton.None;
		bool panning;
		float startCx, startCy, startDist, lastCx, lastCy, lastDist, zoomAccum;
		float lastX, lastY;

		static byte Sdl(MouseButton b) =>
			b == MouseButton.Right ? (byte)SDL.SDL_BUTTON_RIGHT
			: b == MouseButton.Middle ? (byte)SDL.SDL_BUTTON_MIDDLE
			: (byte)SDL.SDL_BUTTON_LEFT;

		static void Send(IInputHandler h, MouseInputEvent ev, MouseButton btn, int2 pos, int2 delta, Modifiers mods, int taps = 0)
		{
			h.OnMouseInput(new MouseInput(ev, btn, pos, delta, mods, taps));
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
			if (b == MouseButton.Left)
				pos = Snap(pos);

			Send(h, MouseInputEvent.Move, MouseButton.None, pos, int2.Zero, mods);
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
			var y = f.y * height;
			int2 P(float px, float py) => toPos((int)px, (int)py);

			switch (e.type)
			{
				case SDL.SDL_EventType.SDL_FINGERDOWN:
				{
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

					if (mode == Mode.Pending)
					{
						var dx = x - fi.StartX;
						var dy = y - fi.StartY;
						if (dx * dx + dy * dy > slop * slop)
						{
							mode = Mode.Drag;
							Down(h, MouseButton.Left, P(fi.StartX, fi.StartY), mods);
						}
					}

					if (mode == Mode.Drag)
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

					if (mode == Mode.Pending && fingers.Count == 1)
						Click(h, MouseButton.Left, P(fi.StartX, fi.StartY), mods);
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
			if (mode != Mode.Pending || fingers.Count != 1)
				return;

			var fi = fingers[order[0]];
			if (SDL.SDL_GetTicks() - fi.StartTick < LongPressMs)
				return;

			mode = Mode.LongPress;
			Click(h, MouseButton.Right, toPos((int)fi.StartX, (int)fi.StartY), mods);
		}
	}
}
