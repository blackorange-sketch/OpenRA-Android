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
    "\t\t\twhile (SDL.SDL_PollEvent(out var e) != 0)\n\t\t\t{\n",
    "\t\t\twhile (SDL.SDL_PollEvent(out var e) != 0)\n\t\t\t{\n"
    "\t\t\t\t// Mouse events synthesised from touches (which == SDL_TOUCH_MOUSEID) would duplicate every tap.\n"
    "\t\t\t\tif ((e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN || e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONUP\n"
    "\t\t\t\t\t|| e.type == SDL.SDL_EventType.SDL_MOUSEMOTION || e.type == SDL.SDL_EventType.SDL_MOUSEWHEEL)\n"
    "\t\t\t\t\t&& e.button.which == uint.MaxValue)\n"
    "\t\t\t\t{\n"
    "\t\t\t\t\tif (e.type == SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN)\n"
    "\t\t\t\t\t\tSdl2Touch.LogLine(\"ignored SDL touch-mouse button down\");\n"
    "\t\t\t\t\tcontinue;\n"
    "\t\t\t\t}\n\n",
)
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
    "\t\t\t{\n"
    "\t\t\t\tRenderer.SetUIScale(hostScaleValue);\n"
    "\t\t\t\t// Viewport math (world <-> screen, selection boxes, picking) reads the saved setting, so keep it in sync.\n"
    "\t\t\t\tSettings.Graphics.UIScale = hostScaleValue;\n"
    "\t\t\t}\n\n",
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

# 9. State the on-screen touch buttons need: whether to show them, group sizes, pause state, and a haptics hook
#    the Android host fills in.
patch(
    "OpenRA.Game/Game.cs",
    "\t\tpublic static Action TouchOverlay;\n",
    "\t\tpublic static Action TouchOverlay;\n\n"
    "\t\t/// <summary>Android host hook: vibrate for the given number of milliseconds.</summary>\n"
    "\t\tpublic static Action<int> TouchHaptic;\n\n"
    "\t\tpublic static bool IsWorldPaused => worldRenderer?.World?.Paused == true;\n\n"
    "\t\tpublic static bool IsAttackMoveArmed =>\n"
    "\t\t\tworldRenderer?.World?.OrderGenerator?.GetType().Name == \"AttackMoveOrderGenerator\";\n\n"
    "\t\tpublic static int SelectedCount => worldRenderer?.World?.Selection.Actors.Count() ?? 0;\n\n"
    "\t\t/// <summary>True while a normal game is being played (not a menu, shellmap, editor, replay or finished game).</summary>\n"
    "\t\tpublic static bool TouchHudActive\n"
    "\t\t{\n"
    "\t\t\tget\n"
    "\t\t\t{\n"
    "\t\t\t\tvar w = worldRenderer?.World;\n"
    "\t\t\t\tif (w == null || w.Type != WorldType.Regular || w.LocalPlayer == null || w.IsLoadingGameSave || w.IsGameOver)\n"
    "\t\t\t\t\treturn false;\n\n"
    "\t\t\t\tvar menu = Ui.Root?.GetOrNull(\"MENU_ROOT\");\n"
    "\t\t\t\tif (menu != null)\n"
    "\t\t\t\t\tforeach (var c in menu.Children)\n"
    "\t\t\t\t\t\tif (c.IsVisible())\n"
    "\t\t\t\t\t\t\treturn false;\n\n"
    "\t\t\t\treturn true;\n"
    "\t\t\t}\n"
    "\t\t}\n\n"
    "\t\tpublic static int ControlGroupSize(int group)\n"
    "\t\t{\n"
    "\t\t\tvar groups = worldRenderer?.World?.ControlGroups;\n"
    "\t\t\tif (groups == null || group < 0 || group >= groups.Groups.Length)\n"
    "\t\t\t\treturn 0;\n\n"
    "\t\t\treturn groups.GetActorsInControlGroup(group).Count();\n"
    "\t\t}\n",
)
# 9b. Pause a single-player game when the app loses focus (switching apps, notification shade, screen off).
patch(
    "OpenRA.Game/Game.cs",
    "\t\tpublic static bool IsWorldPaused => worldRenderer?.World?.Paused == true;\n\n",
    "\t\tpublic static bool IsWorldPaused => worldRenderer?.World?.Paused == true;\n\n"
    "\t\tpublic static bool IsMultiplayerGame => worldRenderer?.World?.LobbyInfo.NonBotClients.Count() > 1;\n\n"
    "\t\t/// <summary>Android host: called when the window loses focus. Pauses a running game that has no other human players.</summary>\n"
    "\t\tpublic static void PauseForBackground()\n"
    "\t\t{\n"
    "\t\t\tvar w = worldRenderer?.World;\n"
    "\t\t\tif (w == null || w.Type != WorldType.Regular || w.LocalPlayer == null || w.IsGameOver || w.IsLoadingGameSave || w.Paused)\n"
    "\t\t\t\treturn;\n\n"
    "\t\t\tif (w.LobbyInfo.NonBotClients.Count() > 1)\n"
    "\t\t\t\treturn;\n\n"
    "\t\t\tw.SetPauseState(true);\n"
    "\t\t}\n\n",
)
patch(
    "OpenRA.Platforms.Default/Sdl2Input.cs",
    "\t\t\t\t\t\t\tcase SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:\n\t\t\t\t\t\t\t\tdevice.HasInputFocus = false;\n",
    "\t\t\t\t\t\t\tcase SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:\n\t\t\t\t\t\t\t\tdevice.HasInputFocus = false;\n"
    "\t\t\t\t\t\t\t\tGame.PauseForBackground();\n",
)
# 9c. Content installer: folders the launcher imported into SupportDir/Import count as a content source
#     (Android has no discs, registry or Steam to look at).
patch(
    "OpenRA.Mods.Common/Installer/InstallerUtils.cs",
    "\t\tpublic static void CopyStream(",
    "\t\t/// <summary>Android host: game folders imported into SupportDir/Import act as an extra content source.</summary>\n"
    "\t\tpublic static string FindImportedSource(ModContent.ModSource source)\n"
    "\t\t{\n"
    "\t\t\ttry\n"
    "\t\t\t{\n"
    "\t\t\t\tvar root = Path.Combine(Platform.SupportDir, \"Import\");\n"
    "\t\t\t\tif (!Directory.Exists(root))\n"
    "\t\t\t\t\treturn null;\n\n"
    "\t\t\t\tif (IsValidSourcePath(root, source))\n"
    "\t\t\t\t\treturn root;\n\n"
    "\t\t\t\tforeach (var dir in Directory.EnumerateDirectories(root, \"*\", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 6 }))\n"
    "\t\t\t\t\tif (IsValidSourcePath(dir, source))\n"
    "\t\t\t\t\t\treturn dir;\n"
    "\t\t\t}\n"
    "\t\t\tcatch (Exception)\n"
    "\t\t\t{\n"
    "\t\t\t}\n\n"
    "\t\t\treturn null;\n"
    "\t\t}\n\n"
    "\t\tpublic static void CopyStream(",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/InstallFromSourceLogic.cs",
    "\t\t\t\t\tvar path = sourceResolver.FindSourcePath(kv.Value);\n",
    "\t\t\t\t\tvar path = sourceResolver.FindSourcePath(kv.Value) ?? InstallerUtils.FindImportedSource(kv.Value);\n",
)

# 9d. Android host: after the launcher imported a game folder it writes SupportDir/Import/AUTO. The content installer
#     then skips every prompt, installs all packages from the imported source and goes straight into the game.
patch(
    "OpenRA.Mods.Common/Installer/InstallerUtils.cs",
    "\t\tpublic static string FindImportedSource(",
    "\t\tpublic static bool AutoInstallRequested() => File.Exists(Path.Combine(Platform.SupportDir, \"Import\", \"AUTO\"));\n\n"
    "\t\tpublic static void ClearAutoInstall()\n"
    "\t\t{\n"
    "\t\t\ttry\n"
    "\t\t\t{\n"
    "\t\t\t\tFile.Delete(Path.Combine(Platform.SupportDir, \"Import\", \"AUTO\"));\n"
    "\t\t\t}\n"
    "\t\t\tcatch (Exception)\n"
    "\t\t\t{\n"
    "\t\t\t}\n"
    "\t\t}\n\n"
    "\t\tpublic static string FindImportedSource(",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/ModContentLogic.cs",
    "\t\t\tif (!IsModInstalled(content))\n",
    "\t\t\tif (!IsModInstalled(content) && !OpenRA.Mods.Common.Installer.InstallerUtils.AutoInstallRequested())\n",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/ModContentLogic.cs",
    "\t\t\tbackButton.OnClick = () => { Ui.CloseWindow(); onCancel(); };\n\n\t\t\tPopulateContentList();\n",
    "\t\t\tbackButton.OnClick = () => { Ui.CloseWindow(); onCancel(); };\n\n\t\t\tPopulateContentList();\n\n"
    "\t\t\tif (OpenRA.Mods.Common.Installer.InstallerUtils.AutoInstallRequested())\n"
    "\t\t\t\tGame.RunAfterTick(() => sourceButton.OnClick());\n",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/InstallFromSourceLogic.cs",
    "\t\tvoid DetectContentSources()\n\t\t{\n",
    "\t\tbool autoInstall;\n\n"
    "\t\tvoid DetectContentSources()\n\t\t{\n"
    "\t\t\tvar auto = InstallerUtils.AutoInstallRequested();\n"
    "\t\t\tif (auto)\n"
    "\t\t\t\tInstallerUtils.ClearAutoInstall();\n\n",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/InstallFromSourceLogic.cs",
    "\t\t\t\t\t\t// Ignore source if content is already installed\n",
    "\t\t\t\t\t\tif (auto && availablePackages.Length != 0)\n"
    "\t\t\t\t\t\t{\n"
    "\t\t\t\t\t\t\tselectedPackages = availablePackages.ToDictionary(x => x.Identifier, y => true);\n"
    "\t\t\t\t\t\t\tautoInstall = true;\n"
    "\t\t\t\t\t\t\tvar autoSource = kv.Value;\n"
    "\t\t\t\t\t\t\tGame.RunAfterTick(() => InstallFromSource(path, autoSource));\n"
    "\t\t\t\t\t\t\treturn;\n"
    "\t\t\t\t\t\t}\n\n"
    "\t\t\t\t\t\t// Ignore source if content is already installed\n",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Installation/InstallFromSourceLogic.cs",
    "\t\t\t\t\tGame.RunAfterTick(Ui.CloseWindow);\n",
    "\t\t\t\t\tif (autoInstall)\n"
    "\t\t\t\t\t\tGame.RunAfterTick(() => Game.InitializeMod(Game.Mods[content.Mod], new Arguments()));\n"
    "\t\t\t\t\telse\n"
    "\t\t\t\t\t\tGame.RunAfterTick(Ui.CloseWindow);\n",
)

# 10. Mission browser: one tab per house (the stock screen is a single long list of all campaigns).
CB = "OpenRA.Mods.Common/Widgets/Logic/MissionBrowserLogic.cs"
patch(
    CB,
    "\t\tbool minifiedOptions = true;\n",
    "\t\tbool minifiedOptions = true;\n\t\tstring campaignFilter = Game.ModData.Manifest.Id == \"d2k\" ? \"Atreides\" : \"\";\n",
)
patch(
    CB,
    "\t\t\tvar allPreviews = new List<MapPreview>();\n\t\t\tmissionList.RemoveChildren();\n",
    "\t\t\tif (initialMap != null)\n"
    "\t\t\t{\n"
    "\t\t\t\tvar initialUid = modData.MapCache.GetUpdatedMap(initialMap);\n"
    "\t\t\t\tvar initialPreview = initialUid == null ? null : modData.MapCache[initialUid];\n"
    "\t\t\t\tif (initialPreview != null)\n"
    "\t\t\t\t\tforeach (var house in new[] { \"Atreides\", \"Harkonnen\", \"Ordos\" })\n"
    "\t\t\t\t\t\tif (initialPreview.Title != null && initialPreview.Title.StartsWith(house, StringComparison.Ordinal))\n"
    "\t\t\t\t\t\t\tcampaignFilter = house;\n"
    "\t\t\t}\n\n"
    "\t\t\tvoid RebuildList()\n\t\t\t{\n"
    "\t\t\tvar allPreviews = new List<MapPreview>();\n\t\t\tmissionList.RemoveChildren();\n",
)
patch(
    CB,
    "\t\t\t\telse\n\t\t\t\t\tSelectMap(allPreviews[0]);\n",
    "\t\t\t\telse\n\t\t\t\t\tSelectMap(allPreviews.FirstOrDefault(p => string.IsNullOrEmpty(campaignFilter)\n"
    "\t\t\t\t\t\t|| (p.Title != null && p.Title.StartsWith(campaignFilter, StringComparison.Ordinal))) ?? allPreviews[0]);\n",
)
patch(
    CB,
    "\t\t\tvar startButton = widget.Get<ButtonWidget>(\"STARTGAME_BUTTON\");\n",
    "\t\t\t}\n\n"
    "\t\t\tRebuildList();\n\n"
    "\t\t\tforeach (var (tabId, tabFilter, tabLabel) in new[]\n"
    "\t\t\t{\n"
    "\t\t\t\t(\"TAB_ATREIDES\", \"Atreides\", \"Атрейдіси\"),\n"
    "\t\t\t\t(\"TAB_HARKONNEN\", \"Harkonnen\", \"Харконнени\"),\n"
    "\t\t\t\t(\"TAB_ORDOS\", \"Ordos\", \"Ордоси\"),\n"
    "\t\t\t\t(\"TAB_ALL\", \"\", \"Усі\")\n"
    "\t\t\t})\n"
    "\t\t\t{\n"
    "\t\t\t\tvar tab = widget.GetOrNull<ButtonWidget>(tabId);\n"
    "\t\t\t\tif (tab == null)\n"
    "\t\t\t\t\tcontinue;\n\n"
    "\t\t\t\ttab.GetText = () => tabLabel;\n"
    "\t\t\t\ttab.IsHighlighted = () => campaignFilter == tabFilter;\n"
    "\t\t\t\ttab.OnClick = () =>\n"
    "\t\t\t\t{\n"
    "\t\t\t\t\tcampaignFilter = tabFilter;\n"
    "\t\t\t\t\tRebuildList();\n"
    "\t\t\t\t};\n"
    "\t\t\t}\n\n"
    "\t\t\tvar startButton = widget.Get<ButtonWidget>(\"STARTGAME_BUTTON\");\n",
)
patch(
    CB,
    "\t\tvoid CreateMissionGroup(string title, IEnumerable<MapPreview> previews, Action onExit)\n\t\t{\n",
    "\t\tvoid CreateMissionGroup(string title, IEnumerable<MapPreview> previews, Action onExit)\n\t\t{\n"
    "\t\t\tif (!string.IsNullOrEmpty(campaignFilter) && !title.StartsWith(campaignFilter, StringComparison.Ordinal))\n"
    "\t\t\t\treturn;\n\n",
)

patch(
    CB,
    "\t\t\t\tif (map != null && map.Visibility.HasFlag(MapVisibility.MissionSelector))\n",
    "\t\t\t\tif (map != null && map.Visibility.HasFlag(MapVisibility.MissionSelector)\n"
    "\t\t\t\t\t&& (string.IsNullOrEmpty(campaignFilter) || (map.Title != null && map.Title.StartsWith(campaignFilter, StringComparison.Ordinal))))\n",
)

# Mission text sits just below the touch button row.
patch(
    "mods/common/chrome/ingame.yaml",
    "\t\t\t\tLabel@MISSION_TEXT:\n\t\t\t\t\tX: WINDOW_WIDTH / 2 - 256\n\t\t\t\t\tY: 23\n",
    "\t\t\t\tLabel@MISSION_TEXT:\n\t\t\t\t\tX: WINDOW_WIDTH / 2 - 256\n\t\t\t\t\tY: 44\n",
)

# 11. Campaign flow: record results, play defeat / ending videos, reopen on the next mission.
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Ingame/LoadIngamePlayerOrObserverUILogic.cs",
    "\t\t\t\t\t\tvar video = world.LocalPlayer.WinState == WinState.Won ? missionData.WinVideo : missionData.LossVideo;\n",
    "\t\t\t\t\t\tvar won = world.LocalPlayer.WinState == WinState.Won;\n"
    "\t\t\t\t\t\tvar video = won ? missionData.WinVideo : missionData.LossVideo;\n"
    "\t\t\t\t\t\tif (string.IsNullOrEmpty(video))\n"
    "\t\t\t\t\t\t\tvideo = CampaignProgress.EndVideo(world.Map.Uid, won);\n\n"
    "\t\t\t\t\t\tCampaignProgress.RecordResult(world.Map.Uid, won);\n",
)
patch(
    "OpenRA.Mods.Common/Widgets/Logic/MainMenuLogic.cs",
    "\t\t\t\tcase MenuPanel.Missions:\n\t\t\t\t\tOpenMissionBrowserPanel(null);\n",
    "\t\t\t\tcase MenuPanel.Missions:\n\t\t\t\t\tOpenMissionBrowserPanel(CampaignProgress.TakeNextMapUid());\n",
)
patch(
    CB,
    "\t\t\tRebuildList();\n\n\t\t\tforeach (var (tabId",
    "\t\t\tRebuildList();\n\n"
    "\t\t\tif (CampaignProgress.TakeAutoBriefing())\n"
    "\t\t\t\tGame.RunAfterDelay(700, () =>\n"
    "\t\t\t\t{\n"
    "\t\t\t\t\tif (startBriefingVideoButton.IsVisible() && !startBriefingVideoButton.IsDisabled() && startBriefingVideoButton.OnClick != null)\n"
    "\t\t\t\t\t\tstartBriefingVideoButton.OnClick();\n"
    "\t\t\t\t});\n\n"
    "\t\t\tforeach (var (tabId",
)
patch(
    CB,
    "\t\t\t\tvar label = item.Get<LabelWithTooltipWidget>(\"TITLE\");\n",
    "\t\t\t\tvar label = item.Get<LabelWithTooltipWidget>(\"TITLE\");\n"
    "\t\t\t\tvar normalColor = label.GetColor();\n"
    "\t\t\t\tlabel.GetColor = () => CampaignProgress.IsDone(preview) ? OpenRA.Primitives.Color.FromArgb(255, 120, 230, 120) : normalColor;\n",
)

# After a win the game leaves to the menu by itself (the next mission is opened there).
patch(
    "OpenRA.Mods.Common/Widgets/Logic/Ingame/LoadIngamePlayerOrObserverUILogic.cs",
    "\t\t\t\t\t\tif (!string.IsNullOrEmpty(video))\n\t\t\t\t\t\t\tMedia.PlayFMVFullscreen(world, video, () => { });\n",
    "\t\t\t\t\t\tvoid Advance()\n"
    "\t\t\t\t\t\t{\n"
    "\t\t\t\t\t\t\tif (won && CampaignProgress.HasNext)\n"
    "\t\t\t\t\t\t\t\tGame.RunAfterDelay(5000, () =>\n"
    "\t\t\t\t\t\t\t\t{\n"
    "\t\t\t\t\t\t\t\t\tif (Game.IsCurrentWorld(world))\n"
    "\t\t\t\t\t\t\t\t\t\tIngameMenuLogic.OnQuit(world);\n"
    "\t\t\t\t\t\t\t\t});\n"
    "\t\t\t\t\t\t}\n\n"
    "\t\t\t\t\t\tif (!string.IsNullOrEmpty(video))\n\t\t\t\t\t\t\tMedia.PlayFMVFullscreen(world, video, Advance);\n"
    "\t\t\t\t\t\telse\n\t\t\t\t\t\t\tAdvance();\n",
)

CY = "mods/d2k/chrome/missionbrowser.yaml"
patch(
    CY,
    "\t\tScrollPanel@MISSION_LIST:\n\t\t\tX: 20\n\t\t\tY: 50\n\t\t\tWidth: 190\n\t\t\tHeight: 483\n",
    "".join(
        f"\t\tButton@{i}:\n\t\t\tX: {20 + n * 162}\n\t\t\tY: 50\n\t\t\tWidth: 156\n\t\t\tHeight: 25\n\t\t\tFont: Bold\n"
        for n, i in enumerate(["TAB_ATREIDES", "TAB_HARKONNEN", "TAB_ORDOS", "TAB_ALL"])
    )
    + "\t\tScrollPanel@MISSION_LIST:\n\t\t\tX: 20\n\t\t\tY: 82\n\t\t\tWidth: 190\n\t\t\tHeight: 451\n",
)
patch(
    CY,
    "\t\tContainer@MISSION_INFO:\n\t\t\tX: 220\n\t\t\tY: 50\n\t\t\tWidth: PARENT_WIDTH - 240\n\t\t\tHeight: 483\n",
    "\t\tContainer@MISSION_INFO:\n\t\t\tX: 220\n\t\t\tY: 82\n\t\t\tWidth: PARENT_WIDTH - 240\n\t\t\tHeight: 451\n",
)

PY

cp "$(dirname "$0")/files/Sdl2Touch.cs" "$OPENRA/OpenRA.Platforms.Default/Sdl2Touch.cs"
cp "$(dirname "$0")/files/CampaignProgress.cs" "$OPENRA/OpenRA.Mods.Common/CampaignProgress.cs"
