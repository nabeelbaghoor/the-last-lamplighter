#!/usr/bin/env python3
"""Pulls the processed DreamLayer art from the shared art pipeline into the Unity project.

The browser version (../lamplighter) owns the pipeline: tools/dl.py talks to DreamLayer and
tools/build_assets.py turns raw outputs into game-ready PNGs. This copies those PNGs into
Assets/Resources/Art and the hero sprite-sheet ZIPs (untouched DreamLayer exports) into art/sprites,
where the DreamLayer importer picks them up at build time.

  python3 tools/sync_art.py
"""
import json
import shutil
from pathlib import Path

from PIL import Image

UNITY = Path(__file__).resolve().parent.parent
SHARED = UNITY.parent / "lamplighter"
SRC = SHARED / "public" / "assets"
RAW = SHARED / "art" / "raw"
DST = UNITY / "Assets" / "Resources" / "Art"
SPRITES = UNITY / "art" / "sprites"
REAL = SHARED / "art" / "real.txt"


def pad_to_4(src: Path, dst: Path) -> None:
    """Block compression (and crunch) needs sizes in multiples of 4. Pad transparent pixels on the
    right and on top, so bottom-anchored paintings keep their footing."""
    img = Image.open(src).convert("RGBA")
    w, h = img.size
    W, H = (w + 3) // 4 * 4, (h + 3) // 4 * 4
    if (W, H) == (w, h):
        shutil.copyfile(src, dst)
        return
    out = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    out.paste(img, (0, H - h))
    out.save(dst, optimize=True)


def resize_to_4(src: Path, dst: Path) -> None:
    """Props tile or sit on colliders, so padding would leave gaps: resize to the nearest multiple of 4
    instead (a pixel or two, invisible at game scale) so they can be block-compressed too."""
    img = Image.open(src).convert("RGBA")
    w, h = img.size
    W, H = max(4, round(w / 4) * 4), max(4, round(h / 4) * 4)
    if (W, H) == (w, h):
        shutil.copyfile(src, dst)
        return
    img.resize((W, H), Image.LANCZOS).save(dst, optimize=True)


def main() -> None:
    real = set(REAL.read_text().split()) if REAL.exists() else set()
    copied = 0
    for rel in sorted(real):
        if rel.startswith("hero/"):
            continue  # the Phaser strips; Unity imports the ZIPs directly
        src, dst = SRC / rel, DST / rel
        if src.exists():
            dst.parent.mkdir(parents=True, exist_ok=True)
            if rel.startswith(("bg/", "ui/")):
                pad_to_4(src, dst)
            else:
                resize_to_4(src, dst)
            copied += 1
            print(f"  art  {rel}")
    # Hero: every DreamLayer ZIP that art/sprites/clips.json cuts clips from.
    clips = json.loads((SPRITES / "clips.json").read_text())["clips"]
    for zip_name in sorted({c["zip"] for c in clips}):
        src = RAW / zip_name
        if src.exists():
            shutil.copyfile(src, SPRITES / zip_name)
            copied += 1
            print(f"  zip  {zip_name}")
        else:
            print(f"  zip  missing {zip_name} in {RAW}")
    print(f"{copied} files synced")


if __name__ == "__main__":
    main()
