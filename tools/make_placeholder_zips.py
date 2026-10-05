"""Placeholder hero animations packaged exactly like DreamLayer sprite ZIPs
(sheet.png + atlas.json + frames/), so the real importer path runs before real art exists."""
import io, json, math, sys, zipfile
from pathlib import Path
from PIL import Image

sys.path.insert(0, str(Path.home() / "CC Projects" / "lamplighter" / "tools"))
from placeholders import hero_frame  # noqa: E402

OUT = Path(__file__).resolve().parent.parent / "art" / "sprites"
CELL = 256

def to_cell(img):
    cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    scaled = img.resize((round(img.width * 1.1), round(img.height * 1.1)))
    cell.alpha_composite(scaled, ((CELL - scaled.width) // 2, CELL - scaled.height - 2))
    return cell

def write_zip(name, frames, durations, sampling):
    cols = math.ceil(math.sqrt(len(frames)))
    rows = math.ceil(len(frames) / cols)
    sheet = Image.new("RGBA", (cols * CELL, rows * CELL), (0, 0, 0, 0))
    atlas_frames = {}
    for i, f in enumerate(frames):
        x, y = (i % cols) * CELL, (i // cols) * CELL
        sheet.paste(f, (x, y))
        rect = {"x": x, "y": y, "w": CELL, "h": CELL}
        atlas_frames[f"{name}_{i + 1:02d}"] = {"frame": rect, "rotated": False, "trimmed": False,
                                               "spriteSourceSize": {"x": 0, "y": 0, "w": CELL, "h": CELL},
                                               "sourceSize": {"w": CELL, "h": CELL}, **rect}
    atlas = {"action": name, "alignment": "ground_anchor", "cell": CELL, "columns": cols, "rows": rows,
             "duration_ms": sum(durations), "frame_count": len(frames), "frame_durations_ms": durations,
             "frames": atlas_frames, "image": "sheet.png", "sampling": sampling, "scale": 1.0,
             "schema_version": 1, "size": {"w": sheet.width, "h": sheet.height}}
    OUT.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUT / f"{name}.zip", "w") as z:
        buf = io.BytesIO(); sheet.save(buf, "PNG"); z.writestr("sheet.png", buf.getvalue())
        z.writestr("atlas.json", json.dumps(atlas))
        for i, f in enumerate(frames):
            b = io.BytesIO(); f.save(b, "PNG"); z.writestr(f"frames/{name}_{i + 1:02d}.png", b.getvalue())
    print("wrote", OUT / f"{name}.zip", len(frames), "frames")

write_zip("hero_idle", [to_cell(hero_frame(i / 7 * math.tau, False)) for i in range(7)], [140] * 7, "cycle")
write_zip("hero_run", [to_cell(hero_frame(i / 12 * math.tau, True)) for i in range(12)], [70] * 12, "cycle")
write_zip("hero_jump", [to_cell(hero_frame(0, False, jump=True))] * 1 + [to_cell(hero_frame(0.4, False, jump=True))], [120, 120], "sequence")
