#!/usr/bin/env python3
"""Builds the Russian text set for the game.

usage: merge-ftl.py <openra-checkout> <translations/ru dir> <out dir>

For every translated file the English original is copied to <out>/en/<path> and the English file with the
translations applied is written to <out>/ru/<path> (<path> is relative to the OpenRA checkout, e.g.
mods/d2k/fluent/lua.ftl). Anything that is not translated stays English, so a missing line is never a crash.
The Android host copies one of the two sets over mods/ before the game starts.

Translation files hold either whole messages (`id = text`, optionally multi-line with 4-space indented
continuation lines) or single attributes of an existing message (`id.attr = text`).
"""
import glob, os, re, shutil, sys

root, tdir, out = sys.argv[1:4]

TARGETS = {
    "campaign.ftl": "mods/d2k/fluent/campaign.ftl",
    "lua.ftl": "mods/d2k/fluent/lua.ftl",
    "d2k.ftl": "mods/d2k/fluent/d2k.ftl",
    "d2k-chrome.ftl": "mods/d2k/fluent/chrome.ftl",
    "d2k-rules.ftl": "mods/d2k/fluent/rules.ftl",
    "common-common.ftl": "mods/common/fluent/common.ftl",
    "common-chrome.ftl": "mods/common/fluent/chrome.ftl",
}

ID = re.compile(r"^(-?[A-Za-z][\w-]*)\s*=(.*)$")
ATTR_PATCH = re.compile(r"^([A-Za-z][\w-]*)\.([\w-]+)\s*=\s*(.*)$")
VARS = re.compile(r"\$[A-Za-z][\w-]*")


def entries(text):
    """-> list of [id or None, [lines]]; None entries are comments / blank runs."""
    res, cur = [], None
    for line in text.split("\n"):
        m = ID.match(line)
        if m:
            cur = [m.group(1), [line]]
            res.append(cur)
        elif cur is not None and (line.startswith(" ") or line.strip() == ""):
            cur[1].append(line)
        else:
            cur = None
            res.append([None, [line]])
    return res


def parse_translation(text):
    whole, attrs = {}, {}
    for key, lines in entries(text):
        if key is None:
            m = ATTR_PATCH.match(lines[0]) if lines else None
            if m:
                attrs.setdefault(m.group(1), {})[m.group(2)] = m.group(3)
            continue
        whole[key] = "\n".join(lines).rstrip("\n")
    return whole, attrs


def apply(en_text, whole, attrs, name):
    out_entries, replaced = [], 0
    for key, lines in entries(en_text):
        if key is not None and key in whole:
            en_vars = set(VARS.findall("\n".join(lines)))
            ru_vars = set(VARS.findall(whole[key]))
            if en_vars <= ru_vars:
                blanks = []
                while lines and lines[-1].strip() == "":
                    blanks.append(lines.pop())
                lines = whole[key].split("\n") + blanks
                replaced += 1
            else:
                print(f"  skip {name}:{key}: placeables differ {en_vars ^ ru_vars}")
        if key is not None and key in attrs:
            new = []
            for line in lines:
                m = re.match(r"^(\s+)\.([\w-]+)(\s*=\s*)(.+)$", line)
                if m and m.group(2) in attrs[key]:
                    line = f"{m.group(1)}.{m.group(2)}{m.group(3)}{attrs[key][m.group(2)]}"
                    replaced += 1
                new.append(line)
            lines = new
        out_entries.append((key, lines))
    return "\n".join("\n".join(l) for _, l in out_entries), replaced


def put(rel, ru_text, en_text):
    for sub, txt in (("en", en_text), ("ru", ru_text)):
        p = os.path.join(out, sub, rel)
        os.makedirs(os.path.dirname(p), exist_ok=True)
        with open(p, "w", encoding="utf-8", newline="") as f:
            f.write(txt)


shutil.rmtree(out, ignore_errors=True)
total = 0
for fname, rel in TARGETS.items():
    tp = os.path.join(tdir, fname)
    if not os.path.exists(tp):
        continue
    en = open(os.path.join(root, rel), encoding="utf-8", newline="").read()
    whole, attrs = parse_translation(open(tp, encoding="utf-8").read())
    ru, n = apply(en, whole, attrs, fname)
    put(rel, ru, en)
    total += n
    print(f"{rel}: {n} lines translated")

# Mission briefings: matched by the start of the English text.
sys.path.insert(0, tdir)
from briefings import B  # noqa: E402

nb = 0
for path in sorted(glob.glob(os.path.join(root, "mods/d2k/maps/*/map.ftl"))):
    en = open(path, encoding="utf-8", newline="").read()
    parts = en.split("briefing =", 1)
    if len(parts) != 2:
        continue
    norm = re.sub(r"\s+", " ", parts[1]).strip()
    ru_body = next((v for k, v in B.items() if norm.startswith(k)), None)
    if ru_body is None:
        print("  no briefing translation for", path)
        continue
    body = "\n\n".join("    " + p.strip() for p in ru_body.strip().split("\n\n"))
    rel = os.path.relpath(path, root).replace(os.sep, "/")
    put(rel, parts[0] + "briefing =\n" + body + "\n", en)
    nb += 1
print(f"briefings translated: {nb}")
