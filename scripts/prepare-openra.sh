#!/usr/bin/env bash
# Fetches OpenRA at the pinned commit, patches it, builds the managed assemblies
# the d2k mod needs and packs everything the game reads from disk into
# android/OpenRA.Android/Assets/payload.zip (extracted on first launch).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REF="$(tr -d '[:space:]' < "$ROOT/OPENRA_REF")"
OPENRA="$ROOT/openra"
STAGE="$ROOT/native-build/payload"
ASSETS="$ROOT/android/OpenRA.Android/Assets"

if [ ! -d "$OPENRA/.git" ]; then
	mkdir -p "$OPENRA"
	git -C "$OPENRA" init -q
	git -C "$OPENRA" remote add origin https://github.com/OpenRA/OpenRA.git
fi
git -C "$OPENRA" fetch -q --depth 1 origin "$REF"
git -C "$OPENRA" checkout -q -f FETCH_HEAD
git -C "$OPENRA" clean -q -fdx
echo "==> OpenRA at $(git -C "$OPENRA" rev-parse HEAD)"

bash "$ROOT/patches/apply.sh" "$OPENRA"

for proj in OpenRA.Mods.Common OpenRA.Mods.Cnc OpenRA.Mods.D2k OpenRA.Platforms.Default; do
	echo "==> Building $proj"
	dotnet build "$OPENRA/$proj/$proj.csproj" -c Release --nologo -v:minimal
done

echo "==> Staging payload"
rm -rf "$STAGE"; mkdir -p "$STAGE/bin" "$STAGE/mods"
cp "$OPENRA"/bin/*.dll "$STAGE/bin/"
cp "$OPENRA"/bin/*.deps.json "$STAGE/bin/" 2>/dev/null || true
for mod in all common common-content d2k d2k-content; do
	cp -r "$OPENRA/mods/$mod" "$STAGE/mods/$mod"
done
cp -r "$OPENRA/glsl" "$STAGE/glsl"
cp "$OPENRA/global mix database.dat" "$STAGE/"
echo "android-m1" > "$STAGE/VERSION"

for f in OpenRA.Mods.Common.dll OpenRA.Mods.Cnc.dll OpenRA.Mods.D2k.dll OpenRA.Platforms.Default.dll; do
	[ -f "$STAGE/bin/$f" ] || { echo "missing $f in payload" >&2; exit 1; }
done

echo "==> payload/bin:"; ls -1 "$STAGE/bin" | sed 's/^/    /'

echo "==> Assemblies requested by the d2k mod manifest:"
grep -A8 '^Assemblies:' "$OPENRA/mods/d2k/mod.yaml" || true

mkdir -p "$ASSETS"
python3 "$ROOT/scripts/gen-manifest.py" "$OPENRA/mods/d2k-content/installer/d2k.yaml" "$ASSETS/content-manifest.txt"
python3 - "$STAGE" "$ASSETS" <<'PY'
import hashlib, os, sys, zipfile

stage, assets = sys.argv[1], sys.argv[2]
zip_path = os.path.join(assets, "payload.zip")
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    for base, _, files in os.walk(stage):
        for name in sorted(files):
            full = os.path.join(base, name)
            z.write(full, os.path.relpath(full, stage).replace(os.sep, "/"))

h = hashlib.sha256()
with open(zip_path, "rb") as f:
    for chunk in iter(lambda: f.read(1 << 20), b""):
        h.update(chunk)
with open(os.path.join(assets, "payload.stamp"), "w") as f:
    f.write(h.hexdigest()[:16])
print(f"payload.zip: {os.path.getsize(zip_path) / 1e6:.1f} MB, stamp {h.hexdigest()[:16]}")
PY
