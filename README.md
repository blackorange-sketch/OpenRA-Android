# OpenRA-Android

Native Android port of [OpenRA](https://github.com/OpenRA/OpenRA): **Dune 2000** (`d2k`), plus **Red Alert** (`ra`), **Tiberian Dawn** (`cnc`) and **Tiberian Sun** (`ts`). The launcher picks the game.
The APK ships no game data. Dune 2000: the user picks the folder with their own files. Red Alert / Tiberian Dawn / Tiberian Sun are freeware: the engine's own content installer (quick install) downloads them on first start.

Pinned engine: `OpenRA/OpenRA` commit `9636b63eaee62fcb60ef2e896e7985bdf2f1ee68` (see `OPENRA_REF`).

## Findings from the engine source (recon)

- Engine targets `net10.0`; window, input and audio go through SDL2 + OpenAL, fonts through FreeType, scripting through Lua 5.1 (`OpenRA-Eluant`).
- The renderer already has a GLES 3.0 path (`GLProfile.Embedded` / `ANGLE` in `OpenRA.Platforms.Default/Sdl2PlatformWindow.cs`), so no renderer rewrite is expected. Shaders in `glsl/` still need a check against real GLES drivers.
- Native libs come only as desktop NuGet packages (`OpenRA-SDL2-CS`, `OpenRA-OpenAL-CS`, `OpenRA-Freetype6`, `OpenRA-Eluant`). Android arm64 builds of SDL2, openal-soft, FreeType and Lua 5.1 must be built from source.
- `Sdl2Input.cs` handles only mouse events. Touch must be mapped to mouse/keyboard actions.
- User data paths come from `Platform.cs` (`SupportDir`); on Android they must point into app-private storage.
- Mod assemblies are loaded by `OpenRA.Game/Support/AssemblyLoader.cs` (AssemblyLoadContext). Trimming/AOT must not remove them.

## Milestones

1. **M1 - window.** CI builds the native libs for arm64-v8a and an APK that creates an SDL window with a GLES 3.0 context and runs the OpenRA main loop to the mod chooser.
2. **M2 - content.** Folder picker (SAF) for the original Dune 2000 files, copy into app storage, hook into the D2k content installer.
3. **M3 - touch.** Tap = left click, long press = right click, drag = selection box, two-finger drag = camera, on-screen buttons for hotkeys.
4. **M4 - polish.** Performance on Snapdragon 8 Gen 2, audio latency, back button/pause handling, settings.

## Open risks

- Hosting the SDL main loop from managed code under .NET for Android (activity lifecycle, GL context loss on pause).
- Whether the user's repack data (Russian localisation) is compatible with the OpenRA D2k importer. Untested.
- Runtime speed of the .NET runtime on device. Untested.

## Layout (planned)

```
OPENRA_REF            pinned OpenRA commit
scripts/              native library build scripts (NDK)
android/              .NET for Android host project (no .java files in here, see build-java.sh)
patches/              patches applied on top of OpenRA
.github/workflows/    APK build
```

## Building

Push to `main` or run **Build APK** from the repository's Actions tab. The workflow:
builds SDL2/OpenAL/FreeType/Lua for arm64 with the NDK, compiles SDL's Java glue, fetches
and patches OpenRA at `OPENRA_REF`, packs the engine payload and publishes a signed APK.

Artifacts: `OpenRA-Android-apk` (the APK) and `build-logs` (one log per stage).
The signing key is cached between runs so new builds install over old ones; if the cache is
evicted, uninstall the old APK first.

## Debugging on device

`adb logcat -s OpenRA mono-stdout` shows the host and engine output. A managed crash is also
written to `files/support/host-crash.txt` and OpenRA's own logs go to `files/support/Logs/`.
