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
