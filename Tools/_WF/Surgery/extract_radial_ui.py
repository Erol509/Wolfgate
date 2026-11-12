"""Extract the radial menu sprites used by the surgical wheel from tgstation's icons/hud/radial.dmi."""

import json
import re
import sys
from pathlib import Path

from PIL import Image

src = Path(sys.argv[1] if len(sys.argv) > 1 else r"C:/Users/Konrad/Downloads/radial.dmi")
out = Path("Resources/Textures/_WF/Surgery/RadialUI.rsi")
out.mkdir(parents=True, exist_ok=True)

wanted = ["radial_slice", "radial_slice_focus", "radial_center", "radial_center_focus", "radial_highlight", "radial_next"]

im = Image.open(src)
desc = im.info["Description"]
w = int(re.search(r"width = (\d+)", desc).group(1))
h = int(re.search(r"height = (\d+)", desc).group(1))
cols = im.width // w

states = {}
index = 0
for block in desc.split("state = ")[1:]:
    name = re.match(r'"([^"]+)"', block).group(1)
    dirs = int(re.search(r"dirs = (\d+)", block).group(1)) if "dirs =" in block else 1
    frames = int(re.search(r"frames = (\d+)", block).group(1)) if "frames =" in block else 1
    if name in wanted:
        states[name] = (index, dirs, frames)
    index += dirs * frames

missing = [name for name in wanted if name not in states]
if missing:
    raise SystemExit(f"missing states: {missing}")

for name, (idx, _, _) in states.items():
    x = (idx % cols) * w
    y = (idx // cols) * h
    im.crop((x, y, x + w, y + h)).save(out / f"{name}.png", optimize=True)

meta = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Extracted from tgstation's icons/hud/radial.dmi (tgstation contributors)",
    "size": {"x": w, "y": h},
    "states": [
        {"name": name, "directions": dirs, "delays": [[1.0]] * frames}
        for name, (_, dirs, frames) in states.items()
    ],
}
(out / "meta.json").write_text(json.dumps(meta, indent=2) + "\n", encoding="utf-8", newline="\n")
print(f"{len(states)} states written to {out}")
