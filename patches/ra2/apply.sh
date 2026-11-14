#!/usr/bin/env bash
# Ports OpenRA/ra2 (written for engine release-20231010) to the pinned engine commit.
# usage: apply.sh <engine dir>; the mod's C# project must already be copied to <engine dir>/OpenRA.Mods.RA2.
set -euo pipefail
python3 - "$1/OpenRA.Mods.RA2" <<'PY'
import os, re, sys

root = sys.argv[1]


def edit(rel, fn):
    path = os.path.join(root, rel)
    s = open(path, encoding="utf-8-sig").read()
    n = fn(s)
    if n == s:
        sys.exit(f"patch did not change {rel}")
    open(path, "w", encoding="utf-8").write(n)


def must_replace(s, old, new, count=1):
    if old not in s:
        sys.exit(f"missing text: {old[:60]!r}")
    return s.replace(old, new) if count == 0 else s.replace(old, new, count)


# [Sync] became [VerifySync]
for base, _, files in os.walk(root):
    for name in files:
        if not name.endswith(".cs"):
            continue
        path = os.path.join(base, name)
        s = open(path, encoding="utf-8-sig").read()
        n = re.sub(r"\[Sync\]", "[VerifySync]", s)
        if n != s:
            open(path, "w", encoding="utf-8").write(n)

# float3 -> System.Numerics.Vector3
edit("Graphics/TintedCell.cs", lambda s: must_replace(
    must_replace(s, "float3[] screen;", "Vector3[] screen;"),
    "using System.Linq;\n", "using System.Linq;\nusing System.Numerics;\n"))


# IRenderActorPreviewVoxelsInfo now gets the model cache; the world no longer owns it
def heli(s):
    s = must_replace(s, "RenderPreviewVoxels(\n\t\t\tActorPreviewInitializer init,", "RenderPreviewVoxels(IModelCache cache,\n\t\t\tActorPreviewInitializer init,")
    s = must_replace(s, "init.World.ModelCache.GetModelSequence(image, Sequence)", "cache.GetModelSequence(image, Sequence)")
    s = must_replace(s, "self.World.ModelCache.GetModelSequence(rv.Image, info.Sequence)", "rv.Renderer.ModelCache.GetModelSequence(rv.Image, info.Sequence)")
    return s


edit("Traits/Render/WithVoxelHelicopterBody.cs", heli)

# Harvester / refinery notifications were replaced by the dock client interfaces
edit("Traits/ChronoResourceDelivery.cs", lambda s: (lambda t: t)(
    must_replace(
    must_replace(
    must_replace(
    must_replace(
    must_replace(
    must_replace(
    must_replace(
    must_replace(s,
        "public class ChronoResourceDelivery : INotifyHarvesterAction, ITick",
        "public class ChronoResourceDelivery : INotifyHarvestAction, INotifyDockClientMoving, ITick"),
        "\t\tActor refinery;\n", "\t\tActor dockHostActor;\n\t\tIDockHost dockHost;\n"),
        "void INotifyHarvesterAction.MovingToResources(Actor self, CPos targetCell)\n\t\t{\n\t\t\tReset();\n\t\t}",
        "void INotifyHarvestAction.MovingToResources(Actor self, CPos targetCell)\n\t\t{\n\t\t\tReset();\n\t\t}\n\n\t\tvoid INotifyHarvestAction.Harvested(Actor self, string resourceType) { }\n\n\t\tvoid INotifyHarvestAction.MovementCancelled(Actor self)\n\t\t{\n\t\t\tReset();\n\t\t}"),
        "void INotifyHarvesterAction.MovingToRefinery(Actor self, Actor refineryActor)\n\t\t{\n\t\t\tvar iao = refineryActor.Trait<IAcceptResources>();\n\t\t\tvar targetCell = self.World.Map.CellContaining(iao.DeliveryPosition);",
        "void INotifyDockClientMoving.MovingToDock(Actor self, Actor hostActor, IDockHost host)\n\t\t{\n\t\t\tvar targetCell = self.World.Map.CellContaining(host.DockPosition);"),
        "\t\t\trefinery = refineryActor;\n\t\t\tdestination = targetCell;",
        "\t\t\tdockHostActor = hostActor;\n\t\t\tdockHost = host;\n\t\t\tdestination = targetCell;"),
        "\t\tpublic void MovementCancelled(Actor self)\n\t\t{\n\t\t\tReset();\n\t\t}\n\n\t\tpublic void Harvested(Actor self, string resourceType) { }\n\t\tpublic void Docked() { }\n\t\tpublic void Undocked() { }\n",
        "\t\tvoid INotifyDockClientMoving.MovementCancelled(Actor self)\n\t\t{\n\t\t\tReset();\n\t\t}\n"),
        "\t\t\t// HACK: Cancelling the current activity will call Reset, so cache the destination here\n\t\t\tvar dest = destination.Value;",
        "\t\t\t// HACK: Cancelling the current activity will call Reset, so cache the destination here\n\t\t\tvar dest = destination.Value;\n\t\t\tvar hostActor = dockHostActor;\n\t\t\tvar host = dockHost;"),
        "\t\t\t\tself.QueueActivity(new DeliverResources(self, refinery));\n\t\t\t\tself.QueueActivity(new FindAndDeliverResources(self, refinery));",
        "\t\t\t\tself.QueueActivity(new MoveToDock(self, hostActor, host));\n\t\t\t\tself.QueueActivity(new FindAndDeliverResources(self));")
).replace("\t\t\trefinery = null;\n", "\t\t\tdockHostActor = null;\n\t\t\tdockHost = null;\n"))
PY

python3 - "$1/OpenRA.Mods.RA2" <<'PY'
import os, re, sys

root = sys.argv[1]


def edit(rel, fn):
    path = os.path.join(root, rel)
    s = open(path, encoding="utf-8-sig").read()
    n = fn(s)
    if n == s:
        sys.exit(f"patch did not change {rel}")
    open(path, "w", encoding="utf-8").write(n)


def add_using(s, ns):
    line = f"using {ns};\n"
    if line in s:
        return s
    m = re.search(r"^using OpenRA", s, re.M)
    return s[:m.start()] + line + s[m.start():]


# Stream.WriteArray(byte[]) -> Stream.Write(ReadOnlySpan<byte>)
edit("FileSystem/BagFile.cs", lambda s: s.replace(".WriteArray(", ".Write("))

# float3 -> Vector3
edit("Graphics/ArcRenderable.cs", lambda s: add_using(s.replace("new float3[", "new Vector3["), "System.Numerics"))

# IEnumerable<WPos>.PositionClosestTo -> ClosestToIgnoringPath
edit("Projectiles/ArcLaserZap.cs", lambda s: s.replace(".PositionClosestTo(", ".ClosestToIgnoringPath("))

# Sprite.Size is a Vector3 now and UISpriteRenderable takes a Vector2 screen position
for rel in ("Traits/Render/WithCarrierParentPipsDecoration.cs", "Traits/Render/WithMindControllerPipsDecoration.cs"):
    edit(rel, lambda s: s.replace(
        "var pipSize = pips.Image.Size.XY.ToInt2();",
        "var pipSize = new int2((int)pips.Image.Size.X, (int)pips.Image.Size.Y);").replace(
        "self.CenterPosition, screenPos, 0, palette, 1f)",
        "self.CenterPosition, screenPos.ToVector2(), 0, palette, 1f)"))
PY

python3 - "$1/OpenRA.Mods.RA2" <<'PY'
import os, sys

root = sys.argv[1]


def edit(rel, fn):
    path = os.path.join(root, rel)
    s = open(path, encoding="utf-8-sig").read()
    n = fn(s)
    if n == s:
        sys.exit(f"patch did not change {rel}")
    open(path, "w", encoding="utf-8").write(n)


# Mobile.Nudge(self) is now the Nudge activity
edit("Traits/SpawnSurvivors.cs", lambda s: s.replace(
    "mobile?.Nudge(unit);",
    "if (mobile != null)\n\t\t\t\t\t\tunit.QueueActivity(false, new OpenRA.Mods.Common.Traits.Nudge(unit));"))

# WarheadDebugOverlay.AddImpact takes an ImmutableArray<WDist>
edit("Warheads/CreateTintedCellsWarhead.cs", lambda s: s.replace(
    "var range = Exts.MakeArray(Range.Length, i => WDist.FromCells(Range[i].Length));",
    "var range = System.Collections.Immutable.ImmutableArray.Create(Exts.MakeArray(Range.Length, i => WDist.FromCells(Range[i].Length)));"))

# The RA2 map importer is a command line tool for desktop; it is not needed in the APK
cmd = os.path.join(root, "UtilityCommands", "ImportRA2MapCommand.cs")
if not os.path.exists(cmd):
    sys.exit("ImportRA2MapCommand.cs not found")
os.remove(cmd)
PY

# --- world rules: the engine no longer creates the voxel cache / model renderer itself, the world actor must have them ---
python3 - "$1/mods/ra2/rules/world.yaml" <<'PY'
import sys

path = sys.argv[1]
s = open(path, encoding="utf-8-sig").read()
old = "\tActorMap:\n\tScreenMap:\n"
if old not in s:
    sys.exit("world.yaml: ActorMap/ScreenMap not found")
s = s.replace(old, old + "\tVoxelCache:\n\tModelRenderer:\n", 1)
open(path, "w", encoding="utf-8").write(s)
PY

# --- map actors placed outside the map (RA2 maps have fences there) make FrozenUnderFog throw: don't spawn them ---
python3 - "$1/OpenRA.Mods.Common/Traits/World/SpawnMapActors.cs" <<'PY'
import sys

path = sys.argv[1]
s = open(path, encoding="utf-8-sig").read()
old = "\t\t\t\tvar actor = world.CreateActor(true, actorReference);\n"
if old not in s:
    sys.exit("SpawnMapActors: CreateActor call not found")
new = ("\t\t\t\tvar locInit = actorReference.GetOrDefault<LocationInit>();\n"
       "\t\t\t\tif (locInit != null && !world.Map.Contains(locInit.Value))\n"
       "\t\t\t\t\tcontinue;\n\n" + old)
s = s.replace(old, new, 1)
open(path, "w", encoding="utf-8").write(s)
PY

# --- mod.yaml: move the manifest to the format of the pinned engine ---
python3 - "$1/mods/ra2/mod.yaml" <<'PY'
import re, sys

path = sys.argv[1]
lines = open(path, encoding="utf-8-sig").read().split("\n")


def block(start_key):
    """(start, end) line indexes of a top-level section: its header line and the lines indented below it."""
    for i, l in enumerate(lines):
        if l.startswith(start_key):
            j = i + 1
            while j < len(lines) and (lines[j].startswith("\t") or lines[j].strip() == ""):
                j += 1
            # keep trailing blank lines out of the block
            while lines[j - 1].strip() == "":
                j -= 1
            return i, j
    sys.exit(f"section {start_key} not found")


# Packages -> FileSystem: DefaultFileSystem / Packages
i, j = block("Packages:")
body = ["\t" + l for l in lines[i + 1:j]]
lines[i:j] = ["FileSystem: DefaultFileSystem", "\tPackages:"] + body

# Assemblies: one comma separated line of file names
i, j = block("Assemblies:")
lines[i:j] = ["Assemblies: OpenRA.Mods.Common.dll, OpenRA.Mods.Cnc.dll, OpenRA.Mods.RA2.dll"]

# Translations -> FluentMessages; the common messages moved to common|fluent
i, j = block("Translations:")
lines[i:j] = [
    "FluentMessages:",
    "\tcommon|fluent/common.ftl",
    "\tcommon|fluent/chrome.ftl",
    "\tcommon|fluent/ingame-debug.ftl",
    "\tcommon|fluent/hotkeys.ftl",
    "\tcommon|fluent/rules.ftl",
    "\tra2|languages/rules/en.ftl",
]

# {DEV_VERSION} is only substituted by the desktop packaging scripts; show a real version in the main menu
lines[:] = [("\tVersion: Android 0.1" if l.strip() == "Version: {DEV_VERSION}" else l) for l in lines]

# The engine has no ModelSequenceFormat entry any more (voxels are listed under ModelSequences)
lines[:] = [l for l in lines if not l.startswith("ModelSequenceFormat:")]

# The engine's content installer is a separate *-content mod now (ModContent needs a Mod field); the APK imports
# the player's files itself, so the section is dropped
i, j = block("ModContent:")
del lines[i:j]

# Server traits: LobbySettingsNotification is gone, SkirmishLogic is new
i, j = block("ServerTraits:")
lines[i:j] = ["ServerTraits:", "\tLobbyCommands", "\tSkirmishLogic", "\tPlayerPinger", "\tMasterServerPinger"]

open(path, "w", encoding="utf-8").write("\n".join(lines))
PY
