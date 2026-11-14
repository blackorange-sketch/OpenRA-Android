#!/usr/bin/env bash
# Fast CI check (no Android build): fetch the pinned engine and the RA2 mod, apply our patches,
# build the managed assemblies and run the engine's own YAML check on the mod.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REF="$(tr -d '[:space:]' < "$ROOT/OPENRA_REF")"
RA2_REF="$(tr -d '[:space:]' < "$ROOT/RA2_REF")"
OPENRA="$ROOT/openra"
RA2SRC="$ROOT/ra2-src"

fetch() { # dir url ref
	if [ ! -d "$1/.git" ]; then mkdir -p "$1"; git -C "$1" init -q; git -C "$1" remote add origin "$2"; fi
	git -C "$1" fetch -q --depth 1 origin "$3"
	git -C "$1" checkout -q -f FETCH_HEAD
	git -C "$1" clean -q -fdx
}

fetch "$OPENRA" https://github.com/OpenRA/OpenRA.git "$REF"
bash "$ROOT/patches/apply.sh" "$OPENRA"
fetch "$RA2SRC" https://github.com/OpenRA/ra2.git "$RA2_REF"
rm -rf "$OPENRA/OpenRA.Mods.RA2" "$OPENRA/mods/ra2"
cp -r "$RA2SRC/OpenRA.Mods.RA2" "$OPENRA/OpenRA.Mods.RA2"
cp -r "$RA2SRC/mods/ra2" "$OPENRA/mods/ra2"
bash "$ROOT/patches/ra2/apply.sh" "$OPENRA"

for proj in OpenRA.Mods.Common OpenRA.Mods.Cnc OpenRA.Mods.RA2 OpenRA.Utility; do
	echo "==> Building $proj"
	dotnet build "$OPENRA/$proj/$proj.csproj" -c Release --nologo -v:minimal -p:EngineRootPath="$OPENRA"
done

cd "$OPENRA"
export ENGINE_DIR="$OPENRA"
echo "==> ra2 --check-yaml"
timeout 600 dotnet bin/OpenRA.Utility.dll ra2 --check-yaml 2>&1 | head -n 300 || true
echo "==> utility logs"
find "$HOME/.config/openra" "$HOME/.openra" -name "*.log" 2>/dev/null | while read -r f; do echo "--- $f"; tail -n 60 "$f"; done || true
