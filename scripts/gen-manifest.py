#!/usr/bin/env python3
"""Builds content-manifest.txt (kind<TAB>dest path) from OpenRA's d2k installer definition."""
import re, sys

src, out = sys.argv[1], sys.argv[2]
entries = {}
pattern = re.compile(r"^\s*\^SupportDir\|Content/d2k/v3/(.+?):\s*(\S+)\s*$")
with open(src, encoding="utf-8") as f:
    for line in f:
        m = pattern.match(line.rstrip("\n"))
        if m:
            dest = m.group(1).strip()
            entries[dest] = ("movies" if dest.startswith("Movies/")
                             else "music" if dest.startswith("Music/") else "base")
counts = {k: sum(1 for v in entries.values() if v == k) for k in ("base", "movies", "music")}
print("manifest entries:", counts)
if not entries:
    sys.exit("no entries parsed")
with open(out, "w", encoding="utf-8") as f:
    for dest, kind in entries.items():
        f.write(f"{kind}\t{dest}\n")
