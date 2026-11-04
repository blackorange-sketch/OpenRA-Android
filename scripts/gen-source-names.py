#!/usr/bin/env python3
"""Lists the file names the OpenRA content installers read from a game install / disc dump.

usage: gen-source-names.py <out> <installer.yaml>...

The launcher's folder import copies only files with these names (keeping their sub-folders), so the engine's own
installer can then extract the music, videos and game data from them.
"""
import os, re, sys

out, sources = sys.argv[1], sys.argv[2:]
names = set()


def add(raw):
    raw = raw.strip().strip('"').split("#")[0].strip()
    if not raw or raw.startswith("^") or raw == ".":
        return
    base = re.split(r"[\\/]", raw)[-1]
    if base:
        names.add(base.lower())


for src in sources:
    block = None  # ("idfiles" | "volumes", indent)
    action = None  # name of the last Copy / Extract* action, to tell files on the disc from extraction results
    lines = open(src, encoding="utf-8").read().split("\n")
    for line in lines:
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        indent = len(line) - len(line.lstrip("\t"))
        body = line.strip()

        if block and indent <= block[1]:
            block = None

        if body.startswith("IDFiles:"):
            block = ("idfiles", indent)
            continue
        if body.startswith("Volumes:"):
            block = ("volumes", indent)
            continue

        if block and block[0] == "idfiles" and indent == block[1] + 1 and re.match(r"^[^:]+:(\s+[0-9a-fA-F]{40})?\s*$", body):
            add(body.split(":")[0])
            continue
        if block and block[0] == "volumes":
            m = re.match(r"^\d+:\s*(\S+)", body)
            if m:
                add(m.group(1))
            continue

        m = re.match(r"^(Extract[A-Za-z]*|Copy|Delete)(@\d+)?:\s*(\S.*)?$", body)
        if m:
            action = m.group(1)
            if action.startswith("Extract") and m.group(3) and not m.group(3).startswith("^"):
                add(m.group(3))
            continue

        # `Copy: .` entries: `^SupportDir|Content/...: file-on-the-disc` (extraction results are not disc files)
        m = re.match(r"^\^SupportDir\|[^:]+:\s*(\S+)\s*(#.*)?$", body)
        if m and action == "Copy":
            add(m.group(1))

if not names:
    sys.exit("no source names parsed")

with open(out, "w", encoding="utf-8") as f:
    f.write("\n".join(sorted(names)) + "\n")
print(f"{len(names)} source file names")
