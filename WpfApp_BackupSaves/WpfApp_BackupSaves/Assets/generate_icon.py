"""Generate multi-size app.ico: flat blue 3.5\" floppy, transparent background."""
from __future__ import annotations

import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw

OUT_DIR = Path(__file__).resolve().parent
# Win10/11 shell + taskbar / jump list / alt-tab DPI set
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

BLUE = (30, 136, 229, 255)
BLUE_DARK = (21, 101, 192, 255)
SILVER = (176, 190, 197, 255)
SILVER_DARK = (120, 144, 156, 255)
LABEL = (227, 242, 253, 255)
LABEL_LINE = (144, 202, 249, 255)
HUB_RING = (66, 66, 66, 255)
HUB_HOLE = (33, 33, 33, 255)
ARROW = (84, 110, 122, 255)


def draw_floppy(size: int) -> Image.Image:
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    pad = max(1, round(size * 0.06))
    x0, y0 = pad, pad
    x1, y1 = size - pad - 1, size - pad - 1
    w = x1 - x0
    h = y1 - y0

    notch_w = max(1, round(w * 0.09))
    notch_h = max(2, round(h * 0.14))
    notch_y0 = y0 + round(h * 0.18)
    notch_y1 = notch_y0 + notch_h

    body = [
        (x0, y0),
        (x1, y0),
        (x1, notch_y0),
        (x1 - notch_w, notch_y0),
        (x1 - notch_w, notch_y1),
        (x1, notch_y1),
        (x1, y1),
        (x0, y1),
    ]
    d.polygon(body, fill=BLUE, outline=BLUE_DARK)

    inset = max(1, round(size * 0.03))
    shut_h = max(2, round(h * 0.22))
    shut_y1 = y0 + shut_h
    d.rectangle([x0 + inset, y0 + inset, x1 - inset, shut_y1], fill=SILVER)
    if size >= 24:
        mid = (y0 + inset + shut_y1) // 2
        d.line(
            [x0 + round(w * 0.22), mid, x1 - round(w * 0.22), mid],
            fill=SILVER_DARK,
            width=max(1, size // 128),
        )

    if size >= 28:
        ax = x0 + round(w * 0.2)
        ay = (y0 + inset + shut_y1) // 2
        s = max(2, round(size / 28))
        d.polygon([(ax, ay - s), (ax + s * 2, ay), (ax, ay + s)], fill=ARROW)

    lab_top = shut_y1 + max(1, round(h * 0.05))
    lab_bot = y0 + round(h * (0.48 if size <= 20 else 0.54))
    if lab_bot > lab_top + 1:
        d.rectangle(
            [x0 + round(w * 0.12), lab_top, x1 - round(w * 0.12), lab_bot],
            fill=LABEL,
        )
        if size >= 48:
            lx0, lx1 = x0 + round(w * 0.16), x1 - round(w * 0.16)
            for i in range(3):
                ly = lab_top + (lab_bot - lab_top) * (i + 1) // 4
                d.line([lx0, ly, lx1, ly], fill=LABEL_LINE, width=max(1, size // 128))

    cx = (x0 + x1) // 2
    cy = y0 + round(h * (0.78 if size <= 20 else 0.72))
    r_outer = max(1, round(min(w, h) * (0.11 if size <= 20 else 0.15)))
    r_inner = max(1, round(r_outer * 0.42)) if size >= 24 else 0
    d.ellipse([cx - r_outer, cy - r_outer, cx + r_outer, cy + r_outer], fill=HUB_RING)
    if r_inner > 0:
        d.ellipse([cx - r_inner, cy - r_inner, cx + r_inner, cy + r_inner], fill=HUB_HOLE)

    if size >= 48:
        ix = cx - round(r_outer * 1.75)
        ir = max(1, round(size * 0.025))
        d.ellipse([ix - ir, cy - ir, ix + ir, cy + ir], fill=HUB_HOLE)

    return img


def write_ico(path: Path, images: list[Image.Image]) -> None:
    """Write Vista+ ICO with PNG-compressed frames (preserves alpha)."""
    frames: list[bytes] = []
    for im in images:
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        frames.append(buf.getvalue())

    count = len(images)
    # ICONDIR
    header = struct.pack("<HHH", 0, 1, count)
    entries = bytearray()
    offset = 6 + 16 * count
    data = bytearray()

    for im, png in zip(images, frames):
        w = 0 if im.width >= 256 else im.width
        h = 0 if im.height >= 256 else im.height
        entries += struct.pack(
            "<BBBBHHII",
            w,
            h,
            0,  # color palette
            0,  # reserved
            1,  # planes
            32,  # bit count
            len(png),
            offset,
        )
        data += png
        offset += len(png)

    path.write_bytes(header + entries + data)


def main() -> None:
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    images = [draw_floppy(s) for s in SIZES]
    images[-1].save(OUT_DIR / "floppy-disk-256.png")

    ico_path = OUT_DIR / "app.ico"
    write_ico(ico_path, images)

    png_dir = OUT_DIR / "icon-png"
    png_dir.mkdir(exist_ok=True)
    for im, s in zip(images, SIZES):
        im.save(png_dir / f"floppy-{s}.png")

    with Image.open(ico_path) as ico:
        sizes = ico.ico.sizes() if hasattr(ico, "ico") else set()
        print(f"Wrote {ico_path}")
        print(f"ICO frames: {sorted(sizes)}")


if __name__ == "__main__":
    main()
