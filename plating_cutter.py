
from pathlib import Path
import re
import traceback
import tkinter as tk
from tkinter import filedialog, messagebox

import yaml
from PIL import Image, ImageDraw, ImageChops


ROOT = Path(r"D:\Wolfgate")
YAML_FILE = ROOT / "Resources/Prototypes/_Mono/Turf/generated.yml"
OUTPUT_FOLDER = "plating/script"


def walk(obj):
    if isinstance(obj, dict):
        yield obj
        for value in obj.values():
            yield from walk(value)
    elif isinstance(obj, list):
        for value in obj:
            yield from walk(value)


def parse_vertices(vertices):
    points = []

    for vertex in vertices:
        if isinstance(vertex, str):
            match = re.fullmatch(
                r"\s*(-?\d+(?:\.\d*)?|-?\.\d+)\s*,\s*"
                r"(-?\d+(?:\.\d*)?|-?\.\d+)\s*",
                vertex,
            )
            if not match:
                raise ValueError(f"Invalid vertex: {vertex!r}")
            x, y = map(float, match.groups())

        elif isinstance(vertex, (list, tuple)) and len(vertex) == 2:
            x, y = map(float, vertex)

        else:
            raise ValueError(f"Invalid vertex: {vertex!r}")

        points.append((x, y))

    if len(points) < 3:
        raise ValueError("A polygon needs at least 3 vertices.")

    return points


def find_targets():
    with YAML_FILE.open("r", encoding="utf-8-sig") as file:
        documents = list(yaml.safe_load_all(file))

    targets = {}
    sprite_entries = 0

    for document in documents:
        for item in walk(document):
            sprite = item.get("sprite")
            vertices = item.get("vertices")

            if not isinstance(sprite, str):
                continue

            sprite = sprite.replace("\\", "/")
            if f"/{OUTPUT_FOLDER}/" not in sprite.lower():
                continue

            sprite_entries += 1

            if not isinstance(vertices, list):
                print(f"SKIP: no vertex list: {sprite}")
                continue

            try:
                targets[sprite] = parse_vertices(vertices)
            except (ValueError, TypeError) as exc:
                print(f"SKIP: {sprite}: {exc}")

    print(f"Sprite entries in YAML: {sprite_entries}")
    print(f"Usable polygon definitions: {len(targets)}")
    return targets





def cut_polygon(source, vertices, destination):
    import numpy as np
    from PIL import Image, ImageDraw, ImageChops

    BORDER = 3
    width, height = source.size

    if width <= BORDER * 2 or height <= BORDER * 2:
        raise ValueError("Source image is too small for a 3-pixel border.")

    src = source.convert("RGBA")
    src_rgb = np.asarray(src.convert("RGB"))
    src_alpha = np.asarray(src.getchannel("A"))

    polygon = [
        (round(x * (width - 1)), round((1.0 - y) * (height - 1)))
        for x, y in vertices
    ]

    # Polygon silhouette.
    mask_img = Image.new("L", (width, height), 0)
    ImageDraw.Draw(mask_img).polygon(polygon, fill=255)
    mask = np.asarray(mask_img) > 0

    # Keep the original image intact as the starting point.
    painted = src_rgb.copy()

    # Build the original tile's four border strips.
    borders = {
        "top": src_rgb[:BORDER, :, :],
        "bottom": src_rgb[height - BORDER:, :, :],
        "left": src_rgb[:, :BORDER, :],
        "right": src_rgb[:, width - BORDER:, :],
    }

    # Determine the closest original border orientation for each new
    # polygon edge, then paint its 3-pixel strip inward along that edge.
    for i, (x1, y1) in enumerate(polygon):
        x2, y2 = polygon[(i + 1) % len(polygon)]

        dx, dy = x2 - x1, y2 - y1
        length = max(1, int(np.hypot(dx, dy)))

        # Ignore degenerate edges.
        if length <= 1:
            continue

        # In image coordinates, choose a source border whose direction
        # most closely matches this new seam.
        angle = np.arctan2(dy, dx)
        candidates = {
            "top": 0.0,
            "bottom": 0.0,
            "left": np.pi / 2,
            "right": np.pi / 2,
        }
        orientation = min(
            candidates,
            key=lambda key: abs(
                np.sin(angle - candidates[key])
            ),
        )

        strip = borders[orientation]

        # Sample the source border along the seam's length.
        if orientation in ("top", "bottom"):
            samples = np.linspace(0, width - 1, length).astype(int)
            colors = strip[0 if orientation == "top" else -1, samples]
        else:
            samples = np.linspace(0, height - 1, length).astype(int)
            colors = strip[samples, 0 if orientation == "left" else -1]

        # Paint a 3-pixel band just inside the polygon edge.
        for step in range(length + 1):
            t = min(step / length, 1.0)
            px = int(round(x1 + dx * t))
            py = int(round(y1 + dy * t))

            color = colors[min(step, length - 1)]

            # Inward normal; choose the side that lies inside the polygon.
            nx, ny = -dy / length, dx / length
            for sign in (-1, 1):
                tx = int(round(px + sign * nx))
                ty = int(round(py + sign * ny))
                if 0 <= tx < width and 0 <= ty < height and mask[ty, tx]:
                    nx_in, ny_in = -sign * nx, -sign * ny
                    for depth in range(BORDER):
                        qx = int(round(px + nx_in * depth))
                        qy = int(round(py + ny_in * depth))
                        if 0 <= qx < width and 0 <= qy < height and mask[qy, qx]:
                            painted[qy, qx] = color
                    break

    # Preserve original alpha and make everything outside the polygon
    # transparent.
    output = Image.fromarray(
        np.dstack((painted, src_alpha)), "RGBA"
    )
    polygon_alpha = Image.fromarray(mask.astype(np.uint8) * 255, "L")
    output.putalpha(
        ImageChops.multiply(output.getchannel("A"), polygon_alpha)
    )

    destination.parent.mkdir(parents=True, exist_ok=True)
    output.save(destination)





def main():
    if not YAML_FILE.exists():
        raise FileNotFoundError(f"YAML file not found:\n{YAML_FILE}")

    root = tk.Tk()
    root.withdraw()
    root.attributes("-topmost", True)

    try:
        selected = filedialog.askopenfilename(
            parent=root,
            title="Select the source plating PNG",
            initialdir=str(ROOT),
            filetypes=[("PNG images", "*.png")],
        )

        if not selected:
            print("Cancelled: no image selected.")
            return

        source_path = Path(selected)
        targets = find_targets()

        if not targets:
            raise RuntimeError(
                "No sprite definitions for plating/script were found. "
                "Check the printed counts and make sure the local generated.yml "
                "contains the plating variant definitions from PR #2874."
            )

        with Image.open(source_path) as image:
            source = image.convert("RGBA")

        generated = 0
        failed = []

        for sprite, vertices in targets.items():
            try:
                # /Textures/... maps to <repo>/Resources/Textures/...
                relative = sprite.lstrip("/")
                if not relative.lower().startswith("textures/"):
                    raise ValueError(f"Unexpected sprite path: {sprite}")

                destination = ROOT / "Resources" / relative
                cut_polygon(source, vertices, destination)
                generated += 1
                print(f"Created: {destination}")

            except Exception as exc:
                failed.append(f"{sprite}: {exc}")
                print(f"FAILED: {sprite}: {exc}")

        report = (
            f"Generated {generated} of {len(targets)} PNGs.\n\n"
            f"Source: {source_path}\n"
            f"Output: {ROOT / 'Resources/Textures/_Mono/Tiles/plating/script'}"
        )

        if failed:
            report += "\n\nErrors:\n" + "\n".join(failed)

        print("\n" + report)
        messagebox.showinfo("Plating sprite cutter", report, parent=root)

    finally:
        root.destroy()


if __name__ == "__main__":
    try:
        main()
    except Exception:
        error = traceback.format_exc()
        print(error)

        try:
            popup = tk.Tk()
            popup.withdraw()
            messagebox.showerror("Sprite cutter error", error, parent=popup)
            popup.destroy()
        except Exception:
            pass

        input("\nPress Enter to close...")
