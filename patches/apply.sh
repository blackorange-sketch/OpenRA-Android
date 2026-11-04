#!/usr/bin/env bash
# Applies the small source patches OpenRA needs to run on Android.
# Usage: apply.sh <path-to-openra-checkout>
# Every patch asserts that its anchor exists exactly once, so an engine update
# that moves the code fails loudly instead of silently skipping the change.
set -euo pipefail

OPENRA="${1:?usage: apply.sh <openra-checkout>}"

python3 - "$OPENRA" <<'PY'
import sys, pathlib

root = pathlib.Path(sys.argv[1])

def patch(rel, old, new):
    p = root / rel
    with open(p, encoding="utf-8", newline="") as f:
        s = f.read()
    if s.count(old) != 1:
        sys.exit(f"patch anchor not found exactly once in {rel}: {old!r}")
    with open(p, "w", encoding="utf-8", newline="") as f:
        f.write(s.replace(old, new))
    print(f"patched {rel}")

# 1. Binaries live in the extracted payload, not next to the process.
#    The Android host sets OPENRA_BIN_DIR before the game starts.
patch(
    "OpenRA.Game/Platform.cs",
    "var dir = AppDomain.CurrentDomain.BaseDirectory;",
    'var dir = Environment.GetEnvironmentVariable("OPENRA_BIN_DIR") ?? AppDomain.CurrentDomain.BaseDirectory;',
)

# 2. AssemblyLoader on Android: DependencyContext.Default is null there (the APK has no entry
#    deps.json), so reading a mod's deps.json threw, the exception was swallowed and no
#    managed dependency (SDL2-CS, ...) was ever registered.
patch(
    "OpenRA.Game/Support/AssemblyLoader.cs",
    ": DependencyContext.Default.RuntimeGraph;",
    ": (DependencyContext.Default?.RuntimeGraph ?? []);",
)

# 3. Independent of deps.json: fall back to a plain DLL next to the loaded assembly.
patch(
    "OpenRA.Game/Support/AssemblyLoader.cs",
    "\t\t\t\treturn LoadFromAssemblyPath(path);\n\n\t\t\treturn null;\n",
    "\t\t\t\treturn LoadFromAssemblyPath(path);\n\n"
    "\t\t\tvar sideBySide = Path.Combine(basePath, assemblyName.Name + \".dll\");\n"
    "\t\t\tif (File.Exists(sideBySide))\n"
    "\t\t\t\treturn LoadFromAssemblyPath(sideBySide);\n\n"
    "\t\t\treturn null;\n",
)

# 4. Mono on Android does not support changing the LOH compaction mode (throws PlatformNotSupportedException).
patch(
    "OpenRA.Game/Game.cs",
    "GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;",
    "try { GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce; } catch (PlatformNotSupportedException) { }",
)

# 5. Touch gestures: SDL's touch->mouse emulation is disabled by the host; Sdl2Touch turns raw finger
#    events into the mouse input OpenRA expects.
patch(
    "OpenRA.Platforms.Default/Sdl2Input.cs",
    "\t\tMouseButton lastButtonBits = MouseButton.None;\n",
    "\t\tMouseButton lastButtonBits = MouseButton.None;\n\t\treadonly Sdl2Touch touch = new();\n",
)
patch(
    "OpenRA.Platforms.Default/Sdl2Input.cs",
    "\t\t\t\t\tcase SDL.SDL_EventType.SDL_TEXTINPUT:\n",
    "\t\t\t\t\tcase SDL.SDL_EventType.SDL_FINGERDOWN:\n"
    "\t\t\t\t\tcase SDL.SDL_EventType.SDL_FINGERMOTION:\n"
    "\t\t\t\t\tcase SDL.SDL_EventType.SDL_FINGERUP:\n"
    "\t\t\t\t\t\ttouch.Handle(e, device, inputHandler, mods, (px, py) => EventPosition(device, px, py));\n"
    "\t\t\t\t\t\tbreak;\n\n"
    "\t\t\t\t\tcase SDL.SDL_EventType.SDL_TEXTINPUT:\n",
)
patch(
    "OpenRA.Platforms.Default/Sdl2Input.cs",
    "\n\t\t\tif (pendingMotion != null)\n\t\t\t\tinputHandler.OnMouseInput(pendingMotion.Value);\n\t\t}\n",
    "\n\t\t\ttouch.Tick(device, inputHandler, mods, (px, py) => EventPosition(device, px, py));\n"
    "\n\t\t\tif (pendingMotion != null)\n\t\t\t\tinputHandler.OnMouseInput(pendingMotion.Value);\n\t\t}\n",
)

# 6. Phones are short: allow a lower minimum effective height so the UI can be scaled up more.
patch(
    "OpenRA.Game/WorldViewportSizes.cs",
    "new(1024, 720);",
    "new(1024, 600);",
)

# 7. Touch UI: a larger UI scale while a game is running (menus keep their own scale), and a flag the
#    touch layer uses to turn building placement into a two-step "preview, then confirm" interaction.
patch(
    "OpenRA.Game/Game.cs",
    "\t\tinternal static void StartGame(Map map, WorldType type)\n\t\t{\n",
    "\t\tpublic static bool IsPlacingBuilding =>\n"
    "\t\t\tworldRenderer?.World?.OrderGenerator?.GetType().Name == \"PlaceBuildingOrderGenerator\";\n\n"
    "\t\tinternal static void StartGame(Map map, WorldType type)\n\t\t{\n"
    "\t\t\t// Android host: separate UI scales for menus and for a running game.\n"
    "\t\t\tvar hostScale = Environment.GetEnvironmentVariable(type == WorldType.Regular ? \"OPENRA_UI_SCALE_GAME\" : \"OPENRA_UI_SCALE_MENU\");\n"
    "\t\t\tif (float.TryParse(hostScale, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hostScaleValue))\n"
    "\t\t\t\tRenderer.SetUIScale(hostScaleValue);\n\n",
)

# 8. Hook that lets the touch layer draw feedback (long-press ring) on top of the UI.
patch(
    "OpenRA.Game/Game.cs",
    "\t\t\t\t\tUi.Draw();\n",
    "\t\t\t\t\tUi.Draw();\n\t\t\t\t\tTouchOverlay?.Invoke();\n",
)
patch(
    "OpenRA.Game/Game.cs",
    "\t\tpublic static bool IsPlacingBuilding =>\n",
    "\t\tpublic static Action TouchOverlay;\n\n\t\tpublic static bool IsPlacingBuilding =>\n",
)
PY

# 9. Finger-sized in-game command bar.
python3 "$(dirname "$0")/touch-ui.py" "$OPENRA"

cp "$(dirname "$0")/files/Sdl2Touch.cs" "$OPENRA/OpenRA.Platforms.Default/Sdl2Touch.cs"
