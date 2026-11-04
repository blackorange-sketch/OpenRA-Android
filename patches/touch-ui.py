#!/usr/bin/env python3
"""Makes the d2k in-game command bar and stance bar finger-sized (hit areas ~2x larger)."""
import re, sys, pathlib

root = pathlib.Path(sys.argv[1])
path = root / "mods/d2k/chrome/ingame-player.yaml"
text = path.read_text(encoding="utf-8")
lines = text.split("\n")

LAYOUT = {
    # container: (x, y-expression, width, height), button (w, h, pitch), icon (x, y)
    "COMMAND_BAR": dict(box=("8", "WINDOW_HEIGHT - HEIGHT - 6", "512", "64"), btn=(60, 64, 64), icon=(17, 19)),
    "STANCE_BAR": dict(box=("540", "WINDOW_HEIGHT - HEIGHT - 12", "200", "48"), btn=(48, 48, 50), icon=(15, 16)),
}

out = []
ctx = None
idx = -1
in_icon = False
hidden_bg = False
changed = 0

for line in lines:
    m = re.match(r"^\t\t(\w+)@(\w+):\s*$", line)
    if m:
        ctx = m.group(2) if m.group(2) in LAYOUT else None
        idx = -1
        in_icon = False
        out.append(line)
        if m.group(2) == "COMMAND_BAR_BACKGROUND":
            # The old background strip no longer matches the bigger buttons.
            out.append("\t\t\tVisible: false")
            hidden_bg = True
        continue

    if ctx:
        lay = LAYOUT[ctx]
        bw, bh, pitch = lay["btn"]

        # Container-level geometry (3 tabs).
        m3 = re.match(r"^\t\t\t(X|Y|Width|Height):", line)
        if m3 and not line.startswith("\t\t\t\t"):
            key = m3.group(1)
            val = {"X": lay["box"][0], "Y": lay["box"][1], "Width": lay["box"][2], "Height": lay["box"][3]}[key]
            out.append(f"\t\t\t{key}: {val}")
            changed += 1
            continue

        # A button starts (4 tabs).
        if re.match(r"^\t\t\t\tButton@\w+:\s*$", line):
            idx += 1
            in_icon = False
            out.append(line)
            out.append(f"\t\t\t\t\tX: {idx * pitch}")
            continue

        if idx >= 0 and line.startswith("\t\t\t\t\t") and not line.startswith("\t\t\t\t\t\t"):
            if re.match(r"^\t\t\t\t\tX:", line):
                continue  # replaced by the pitch-based X above
            if re.match(r"^\t\t\t\t\tWidth:", line):
                out.append(f"\t\t\t\t\tWidth: {bw}")
                changed += 1
                continue
            if re.match(r"^\t\t\t\t\tHeight:", line):
                out.append(f"\t\t\t\t\tHeight: {bh}")
                changed += 1
                continue

        if re.match(r"^\t\t\t\t\t\tImage@ICON:", line):
            in_icon = True
        elif in_icon and re.match(r"^\t\t\t\t\t\t\tX:", line):
            out.append(f"\t\t\t\t\t\t\tX: {lay['icon'][0]}")
            changed += 1
            continue
        elif in_icon and re.match(r"^\t\t\t\t\t\t\tY:", line):
            out.append(f"\t\t\t\t\t\t\tY: {lay['icon'][1]}")
            changed += 1
            continue

    out.append(line)

if not hidden_bg or changed < 40:
    sys.exit(f"touch-ui: unexpected layout (hidden_bg={hidden_bg}, changed={changed})")

path.write_text("\n".join(out), encoding="utf-8")
print(f"touch-ui: patched {path.name} ({changed} values)")
