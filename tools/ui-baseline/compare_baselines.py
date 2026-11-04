"""Compares two sets of baseline screenshots taken by Capture-UiBaseline.ps1, picture by picture.

Usage: python -I tools/ui-baseline/compare_baselines.py <baseline folder> <new folder> [--diff <folder>]

For each picture in both folders it prints how many pixels differ (any channel by more than the
tolerance) and the box around them. With --diff it saves an image per differing picture with the
changed pixels in magenta over a faded copy of the new picture. Needs Pillow and NumPy.
"""
import argparse
import pathlib
import sys

import numpy as np
from PIL import Image

TOLERANCE = 24  # per channel; absorbs anti-aliasing and the faint wallpaper at the window edge
EDGE = 3        # pixels at the window border, where the desktop shows through, are ignored


def compare(old_path, new_path):
    old = np.asarray(Image.open(old_path).convert("RGB"), dtype=np.int16)
    new = np.asarray(Image.open(new_path).convert("RGB"), dtype=np.int16)
    if old.shape != new.shape:
        return None, f"size {old.shape[1]}x{old.shape[0]} -> {new.shape[1]}x{new.shape[0]}", None
    changed = (np.abs(old - new) > TOLERANCE).any(axis=2)
    changed[:EDGE, :] = changed[-EDGE:, :] = False
    changed[:, :EDGE] = changed[:, -EDGE:] = False
    count = int(changed.sum())
    if count == 0:
        return 0, "", changed
    ys, xs = np.nonzero(changed)
    return count, f"box x {xs.min()}-{xs.max()}, y {ys.min()}-{ys.max()}", changed


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("baseline")
    parser.add_argument("candidate")
    parser.add_argument("--diff")
    args = parser.parse_args()
    baseline, candidate = pathlib.Path(args.baseline), pathlib.Path(args.candidate)
    diff = pathlib.Path(args.diff) if args.diff else None
    if diff:
        diff.mkdir(parents=True, exist_ok=True)
    names = sorted({p.name for p in baseline.glob("*.png")} | {p.name for p in candidate.glob("*.png")})
    differing = 0
    for name in names:
        old, new = baseline / name, candidate / name
        if not old.exists() or not new.exists():
            print(f"{name}: only in {'baseline' if old.exists() else 'new set'}")
            differing += 1
            continue
        count, where, changed = compare(old, new)
        if count == 0:
            continue
        differing += 1
        total = changed.size if changed is not None else 0
        share = f" ({count / total:.2%})" if total else ""
        print(f"{name}: {count if count is not None else 'n/a'} pixels{share} {where}")
        if diff and changed is not None:
            picture = np.asarray(Image.open(new).convert("RGB"), dtype=np.float32) * 0.35 + 255 * 0.65
            picture[changed] = (255, 0, 255)
            Image.fromarray(picture.astype(np.uint8)).save(diff / name)
    print(f"{len(names)} pictures, {differing} differ")
    return 0


if __name__ == "__main__":
    sys.exit(main())
