using System;
using System.IO;
using System.Runtime.InteropServices;
using Android.Util;

namespace OpenRAHost
{
	/// <summary>
	/// Managed side of the SDL_main bridge. SDLActivity calls SDL_main() on its own thread;
	/// libopenramain.so forwards that call to <see cref="RunGame"/>.
	/// </summary>
	static class OpenRAEntry
	{
		const string Tag = "OpenRA";

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		delegate int MainDelegate(int argc, IntPtr argv);

		// Kept in a static field so the delegate (and its native thunk) is never collected.
		static readonly MainDelegate EntryPoint = RunGame;

		static string engineDir;
		static string supportDir;
		static string uiScale = "1";
		static string gameScale = "1";
		static int touchDy;

		[DllImport("openramain")]
		static extern void OpenRA_SetMain(IntPtr fn);

		[DllImport("SDL2")]
		static extern void SDL_SetMainReady();

		[DllImport("SDL2")]
		static extern int SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

		public static void Configure(string engineRoot, string filesRoot, string scale, string inGameScale, int touchOffset)
		{
			uiScale = scale;
			gameScale = inGameScale;
			touchDy = touchOffset;
			engineDir = engineRoot;
			supportDir = Path.Combine(filesRoot, "support");
			Directory.CreateDirectory(supportDir);
			OpenRA_SetMain(Marshal.GetFunctionPointerForDelegate(EntryPoint));
		}

		static int RunGame(int argc, IntPtr argv)
		{
			try
			{
				Environment.SetEnvironmentVariable("OPENRA_BIN_DIR", Path.Combine(engineDir, "bin"));
				SDL_SetMainReady();
				var logDir = Path.Combine(supportDir, "Logs");
				Directory.CreateDirectory(logDir);
				var touchLog = Path.Combine(logDir, "touch.log");
				try { File.Delete(touchLog); File.Delete(Path.Combine(logDir, "window.log")); } catch { }
				Environment.SetEnvironmentVariable("OPENRA_TOUCH_LOG", touchLog);
				Environment.SetEnvironmentVariable("OPENRA_UI_SCALE_MENU", uiScale);
				Environment.SetEnvironmentVariable("OPENRA_UI_SCALE_GAME", gameScale);
				Environment.SetEnvironmentVariable("OPENRA_TOUCH_DY", touchDy.ToString(System.Globalization.CultureInfo.InvariantCulture));

				// SDL otherwise rotates the activity to match the (portrait) desktop size when it creates
				// the window, which produced a cropped, mis-sized game view. Keep it landscape.
				SDL_SetHint("SDL_IOS_ORIENTATIONS", "LandscapeLeft LandscapeRight");

				// Raw finger events are turned into mouse input by Sdl2Touch (patched into OpenRA).
				SDL_SetHint("SDL_TOUCH_MOUSE_EVENTS", "0");

				var args = new[]
				{
					"Engine.EngineDir=" + engineDir,
					"Engine.SupportDir=" + supportDir,
					"Game.Mod=d2k",
					"Graphics.UIScale=" + uiScale,
					"Game.MouseScroll=Standard",
					"Game.MouseControlStyle=Classic",
					"Game.UseAlternateScrollButton=True",
					"Game.ViewportEdgeScroll=False"
				};

				Log.Info(Tag, "Starting OpenRA with " + string.Join(" ", args));
				return (int)OpenRA.Game.InitializeAndRun(args);
			}
			catch (Exception e)
			{
				Log.Error(Tag, "OpenRA crashed: " + e);
				try
				{
					File.WriteAllText(Path.Combine(supportDir, "host-crash.txt"), e.ToString());
				}
				catch
				{
					// Nothing more we can do.
				}

				return 1;
			}
			finally
			{
				OpenRA.Log.Dispose();

				// OpenRA keeps process-wide static state (engine dir, settings, ...) and cannot be started a
				// second time in the same process, so a finished game ends the process. The next launch
				// from the icon starts clean.
				Android.OS.Process.KillProcess(Android.OS.Process.MyPid());
			}
		}
	}
}
