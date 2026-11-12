from PIL import Image
from pathlib import Path
import re
import json

src = Path(r"C:/Users/Konrad/AppData/Local/Temp/opencode/tgref/surgery_radial.dmi")
out = Path("Resources/Textures/_WF/Surgery/RadialIcons.rsi")
out.mkdir(parents=True, exist_ok=True)

im = Image.open(src)
desc = im.info["Description"]
w = int(re.search(r"width = (\d+)", desc).group(1))
h = int(re.search(r"height = (\d+)", desc).group(1))
cols = im.width // w

states = []
index = 0
for block in desc.split("state = ")[1:]:
    name = re.match(r'"([^"]+)"', block).group(1)
    dirs = int(re.search(r"dirs = (\d+)", block).group(1)) if "dirs =" in block else 1
    frames = int(re.search(r"frames = (\d+)", block).group(1)) if "frames =" in block else 1
    states.append((name, index, dirs, frames))
    index += dirs * frames

for name, idx, dirs, frames in states:
    x = (idx % cols) * w
    y = (idx // cols) * h
    im.crop((x, y, x + w, y + h)).save(out / f"{name}.png")

meta = {
    "version": 1,
    "license": "CC-BY-SA-3.0",
    "copyright": "Extracted from tgstation's icons/hud/surgery_radial.dmi (tgstation contributors)",
    "size": {"x": w, "y": h},
    "states": [
        {"name": name, "directions": dirs, "delays": [[1.0]] * frames}
        for name, _, dirs, frames in states
    ],
}
(out / "meta.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
print(f"{len(states)} states written to {out}")
