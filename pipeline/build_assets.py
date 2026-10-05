#!/usr/bin/env python3
"""Turn raw DreamLayer outputs (art/raw) into game-ready assets (public/assets).

Driven by art/assets.json. Entry types:
  opaque  - full-frame painting: resize to fit max_w
  cutout  - transparent PNG: trim to the visible pixels, resize to a target height
  gloom   - derived locally from another processed asset (desaturate, darken, cool tint)
  hero    - sprite-sheet ZIPs + one pose PNG, normalised onto one shared canvas

Every processed destination is recorded in art/real.txt so placeholders.py never
overwrites real art again.
"""
import io
import json
import re
import sys
import zipfile
from pathlib import Path

from PIL import Image, ImageEnhance

ROOT = Path(__file__).resolve().parent.parent
RAW = ROOT / "art" / "raw"
OUT = ROOT / "public" / "assets"
CFG = ROOT / "art" / "assets.json"
REAL = ROOT / "art" / "real.txt"
GEN_TS = ROOT / "src" / "generated" / "sprites.ts"

real: set[str] = set(REAL.read_text().split()) if REAL.exists() else set()


def save(img: Image.Image, rel: str) -> None:
    p = OUT / rel
    p.parent.mkdir(parents=True, exist_ok=True)
    if rel.endswith(".jpg"):
        img.convert("RGB").save(p, quality=88, optimize=True, progressive=True)
    else:
        img.save(p, optimize=True)
    real.add(rel)
    print(f"  {rel:34} {img.size[0]}x{img.size[1]}  {p.stat().st_size // 1024} KB")


def trim(img: Image.Image, pad: int = 4) -> Image.Image:
    img = img.convert("RGBA")
    box = img.getchannel("A").point(lambda a: 255 if a > 10 else 0).getbbox()
    if not box:
        return img
    l, t, r, b = box
    return img.crop((max(0, l - pad), max(0, t - pad), min(img.width, r + pad), min(img.height, b + pad)))


def fit_height(img: Image.Image, h: int) -> Image.Image:
    w = max(1, round(img.width * h / img.height))
    return img.resize((w, h), Image.LANCZOS)


def fit_width(img: Image.Image, w: int) -> Image.Image:
    if img.width <= w:
        return img
    return img.resize((w, max(1, round(img.height * w / img.width))), Image.LANCZOS)


def gloomify(img: Image.Image) -> Image.Image:
    """Local stand-in for a DreamLayer gloom edit, used only for small props."""
    img = img.convert("RGBA")
    alpha = img.getchannel("A")
    rgb = img.convert("RGB")
    rgb = ImageEnhance.Color(rgb).enhance(0.22)
    rgb = ImageEnhance.Brightness(rgb).enhance(0.6)
    tint = Image.new("RGB", rgb.size, (52, 64, 96))
    rgb = Image.blend(rgb, tint, 0.28)
    out = rgb.convert("RGBA")
    out.putalpha(alpha)
    return out


def drop_floor_shadow(img: Image.Image) -> Image.Image:
    """Background removal keeps a soft floor shadow under floating things; cut at the empty band above it."""
    a = img.getchannel("A")
    w, h = img.size
    rows = [sum(1 for x in range(0, w, 3) if a.getpixel((x, y)) > 40) for y in range(h)]
    top = next((y for y, n in enumerate(rows) if n), 0)
    for y in range(h - 1, top + h // 3, -1):
        if rows[y] == 0 and any(rows[y + 1:]):
            gap_top = y
            while gap_top > top and rows[gap_top - 1] == 0:
                gap_top -= 1
            if y - gap_top >= 3:
                return img.crop((0, 0, w, gap_top))
    return img


def tileable(img: Image.Image, overlap: float = 0.12) -> Image.Image:
    """Cross-fades the right end into the left end so the strip repeats without a seam."""
    w, h = img.size
    o = max(2, int(w * overlap))
    base = img.crop((0, 0, w - o, h))
    tail = img.crop((w - o, 0, w, h))
    mask = Image.linear_gradient("L").rotate(-90, expand=True).resize((o, h))  # 255 at left -> 0 at right
    head = base.crop((0, 0, o, h))
    base.paste(Image.composite(tail, head, mask), (0, 0))
    return base


def surface_row(img: Image.Image, coverage: float = 0.4) -> int:
    """First row (from the top) where the painting is solid across most of its width: the walkable top."""
    a = img.getchannel("A")
    w, h = img.size
    px = a.load()
    for y in range(h):
        if sum(1 for x in range(0, w, 2) if px[x, y] > 128) * 2 >= coverage * w:
            return y
    return 0


def align_surface(img: Image.Image, ratio: float, row: int | None = None) -> Image.Image:
    """Pads with transparent rows so the walkable surface sits at `ratio` of the height, which is
    where the game expects it (platform colliders are placed from that line)."""
    s = surface_row(img) if row is None else row
    w, h = img.size
    if s / h < ratio:
        top = round((ratio * h - s) / (1 - ratio))
        out = Image.new("RGBA", (w, h + top), (0, 0, 0, 0))
        out.paste(img, (0, top))
    else:
        out = Image.new("RGBA", (w, max(h, round(s / ratio))), (0, 0, 0, 0))
        out.paste(img, (0, 0))
    return out


def pair_cutout(spec: dict) -> None:
    """A lit cutout plus its DreamLayer gloom edit. The gloom edit is a full image (DreamLayer edits keep
    the composition), so it takes the lit cutout's alpha: both paintings share one silhouette and line up."""
    lit = Image.open(RAW / spec["lit"]).convert("RGBA")
    gloom = Image.open(RAW / spec["gloom"]).convert("RGBA").resize(lit.size, Image.LANCZOS)
    gloom.putalpha(lit.getchannel("A"))
    box = lit.getchannel("A").point(lambda a: 255 if a > 10 else 0).getbbox()
    if box:
        pad = 4
        l, t, r, b = box
        box = (max(0, l - pad), max(0, t - pad), min(lit.width, r + pad), min(lit.height, b + pad))
        lit, gloom = lit.crop(box), gloom.crop(box)
    if spec.get("tileable"):
        lit, gloom = tileable(lit), tileable(gloom)
    if "surface" in spec:
        row = surface_row(lit)
        lit, gloom = align_surface(lit, spec["surface"], row), align_surface(gloom, spec["surface"], row)
    for img, dst in ((lit, spec["dst_lit"]), (gloom, spec["dst_gloom"])):
        save(fit_height(img, spec["height"]) if "height" in spec else img, dst)


# ---------------------------------------------------------------- hero sprites

def load_sprite_zip(path: Path) -> tuple[list[Image.Image], list[int], dict]:
    with zipfile.ZipFile(path) as z:
        names = z.namelist()
        atlas = {}
        atlas_name = next((n for n in names if n.endswith("atlas.json")), None)
        if atlas_name:
            atlas = json.loads(z.read(atlas_name))
        frame_names = sorted(
            (n for n in names if re.search(r"(^|/)frames/.*\.png$", n)),
            key=lambda n: [int(x) for x in re.findall(r"\d+", Path(n).stem)] or [0],
        )
        frames = [Image.open(io.BytesIO(z.read(n))).convert("RGBA") for n in frame_names]
        if not frames and atlas.get("frames"):
            # No frames/ folder: slice sheet.png using the atlas rectangles instead.
            sheet = Image.open(io.BytesIO(z.read(atlas.get("image", "sheet.png")))).convert("RGBA")
            for key in sorted(atlas["frames"], key=lambda k: [int(x) for x in re.findall(r"\d+", k)] or [0]):
                r = atlas["frames"][key].get("frame", atlas["frames"][key])
                frames.append(sheet.crop((r["x"], r["y"], r["x"] + r["w"], r["y"] + r["h"])))
    durations = atlas.get("frame_durations_ms") or []
    if len(durations) != len(frames):
        d = atlas.get("duration_ms") or 100
        durations = [int(d)] * len(frames)
    return frames, [int(x) for x in durations], atlas


def union_box(frames: list[Image.Image]) -> tuple[int, int, int, int]:
    boxes = [f.getchannel("A").point(lambda a: 255 if a > 10 else 0).getbbox() for f in frames]
    boxes = [b for b in boxes if b]
    return (min(b[0] for b in boxes), min(b[1] for b in boxes), max(b[2] for b in boxes), max(b[3] for b in boxes))


def build_hero(spec: dict) -> None:
    char_h = spec.get("char_h", 240)
    anims: dict[str, tuple[list[Image.Image], list[int], bool]] = {}
    for name, a in spec["anims"].items():
        src = RAW / a["src"]
        if src.suffix == ".zip":
            frames, durs, _atlas = load_sprite_zip(src)
        else:
            frames, durs = [Image.open(src).convert("RGBA")], [100]
        # Sprite jobs open with the reference pose easing into the action, and their timing follows
        # the source video's sampling; pick the loop frames and retime them to game speed.
        first = a.get("first", 0)
        count = a.get("count", 0) or len(frames) - first
        frames, durs = frames[first:first + count], durs[first:first + count]
        if a.get("seconds"):
            k = a["seconds"] * 1000 / sum(durs)
            durs = [d * k for d in durs]
        speed = a.get("speed", 1.0)
        durs = [max(16, round(d / speed)) for d in durs]
        anims[name] = (frames, durs, a.get("loop", True))

    # Scale each animation so the character is char_h tall, then place every frame on
    # one shared canvas with the feet on the bottom edge and the body centred.
    placed: dict[str, list[Image.Image]] = {}
    max_w = 0
    for name, (frames, _d, _l) in anims.items():
        l, t, r, b = union_box(frames)
        s = char_h * spec["anims"][name].get("height_ratio", 1.0) / (b - t)
        crops = [f.crop((l, t, r, b)).resize((max(1, round((r - l) * s)), max(1, round((b - t) * s))), Image.LANCZOS) for f in frames]
        placed[name] = crops
        max_w = max(max_w, max(c.width for c in crops))
    canvas_w = max_w + 8
    canvas_w += canvas_w % 2
    canvas_h = char_h + 16

    entries = []
    for name, crops in placed.items():
        strip = Image.new("RGBA", (canvas_w * len(crops), canvas_h), (0, 0, 0, 0))
        for i, c in enumerate(crops):
            strip.alpha_composite(c, (i * canvas_w + (canvas_w - c.width) // 2, canvas_h - c.height - 2))
        rel = f"hero/hero_{name}.png"
        save(strip, rel)
        _f, durs, loop = anims[name]
        entries.append(
            f"  {name}: {{ key: 'hero_{name}', path: 'assets/{rel}', frames: {len(crops)}, durations: {json.dumps(durs)}, loop: {str(loop).lower()} }},"
        )

    GEN_TS.write_text(
        "// AUTO-GENERATED by tools/build_assets.py. Do not edit by hand.\n"
        f"export const HERO_FRAME = {{ w: {canvas_w}, h: {canvas_h}, charH: {char_h} }} as const;\n"
        "export const HERO_ANIMS = {\n" + "\n".join(entries) + "\n} as const;\n"
    )
    print(f"  src/generated/sprites.ts  canvas {canvas_w}x{canvas_h}")


# ---------------------------------------------------------------- main

def main(only: list[str]) -> None:
    cfg = json.loads(CFG.read_text())
    for key, spec in cfg.items():
        if key.startswith("_") or (only and key not in only):
            continue
        kind = spec["type"]
        src = RAW / spec["src"] if "src" in spec else None
        if src and not src.exists():
            print(f"- {key}: waiting for {src.name}")
            continue
        print(f"- {key} ({kind})")
        if kind == "opaque":
            img = Image.open(src).convert("RGBA")
            if "crop" in spec:
                l, t, r, b = spec["crop"]
                img = img.crop((round(l * img.width), round(t * img.height), round(r * img.width), round(b * img.height)))
            save(fit_width(img, spec.get("max_w", 2048)), spec["dst"])
        elif kind == "cutout":
            img = trim(Image.open(src))
            if spec.get("drop_floor_shadow"):
                img = trim(drop_floor_shadow(img))
            if "crop" in spec:
                l, t, r, b = spec["crop"]
                img = trim(img.crop((round(l * img.width), round(t * img.height), round(r * img.width), round(b * img.height))))
            if "surface" in spec:
                img = align_surface(img, spec["surface"])
            if "height" in spec:
                img = fit_height(img, spec["height"])
            elif "width" in spec:
                img = fit_width(img, spec["width"]) if img.width > spec["width"] else img.resize(
                    (spec["width"], max(1, round(img.height * spec["width"] / img.width))), Image.LANCZOS)
            save(img, spec["dst"])
        elif kind == "gloom":
            base = OUT / spec["from"]
            if not base.exists():
                print(f"  waiting for {spec['from']}")
                continue
            save(gloomify(Image.open(base)), spec["dst"])
        elif kind == "pair_cutout":
            missing = [spec[k] for k in ("lit", "gloom") if not (RAW / spec[k]).exists()]
            if missing:
                print(f"  waiting for {', '.join(missing)}")
                continue
            pair_cutout(spec)
        elif kind == "hero":
            missing = [a["src"] for a in spec["anims"].values() if not (RAW / a["src"]).exists()]
            if missing:
                print(f"  waiting for {', '.join(missing)}")
                continue
            build_hero(spec)
        else:
            sys.exit(f"unknown type {kind} for {key}")
    REAL.write_text("\n".join(sorted(real)) + "\n")


if __name__ == "__main__":
    main(sys.argv[1:])
