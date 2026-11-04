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
PY
