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

		[DllImport("openramain")]
		static extern void OpenRA_SetMain(IntPtr fn);

		[DllImport("SDL2")]
		static extern void SDL_SetMainReady();

		public static void Configure(string engineRoot, string filesRoot, string scale)
		{
			uiScale = scale;
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

				var args = new[]
				{
					"Engine.EngineDir=" + engineDir,
					"Engine.SupportDir=" + supportDir,
					"Game.Mod=d2k",
					"Graphics.UIScale=" + uiScale
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
			}
		}
	}
}
