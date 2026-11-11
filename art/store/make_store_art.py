"""Draws the Play Store icon (512x512) and feature graphic (1024x500) in the game's own palette.
Usage: python3 art/store/make_store_art.py <gameplay_screenshot.png> <out_dir>
"""
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

INK = (26, 28, 23)
PLATE = (60, 64, 48)
EDGE = (120, 125, 95)
BONE = (230, 224, 202)
ACCENT = (232, 130, 43)
FONT = "/usr/share/fonts/noto/NotoSans-Black.ttf"
FONT_B = "/usr/share/fonts/noto/NotoSans-Bold.ttf"


def vertical_gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size)
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return img


def chamfer(box, cut):
    x0, y0, x1, y1 = box
    return [(x0 + cut, y0), (x1 - cut, y0), (x1, y0 + cut), (x1, y1 - cut),
            (x1 - cut, y1), (x0 + cut, y1), (x0, y1 - cut), (x0, y0 + cut)]


def chevrons(draw, cx, cy, size, color, count=2):
    """Upward chevrons: the 'advance' mark."""
    t = size * 0.17
    for i in range(count):
        oy = cy + size * 0.30 - i * size * 0.42
        pts = [(cx - size * 0.5, oy), (cx, oy - size * 0.45), (cx + size * 0.5, oy),
               (cx + size * 0.5, oy + t), (cx, oy - size * 0.45 + t), (cx - size * 0.5, oy + t)]
        draw.polygon(pts, fill=color)


def emblem(img, cx, cy, size):
    d = ImageDraw.Draw(img)
    half = size / 2
    box = (cx - half, cy - half, cx + half, cy + half)
    d.polygon(chamfer(box, size * 0.22), fill=PLATE, outline=EDGE)
    inset = size * 0.05
    d.polygon(chamfer((box[0] + inset, box[1] + inset, box[2] - inset, box[3] - inset), size * 0.18), outline=EDGE, width=max(2, int(size * 0.012)))
    chevrons(d, cx, cy + size * 0.02, size * 0.58, ACCENT)


def icon(path):
    img = vertical_gradient((512, 512), (46, 50, 38), (22, 24, 19))
    d = ImageDraw.Draw(img)
    for k in range(-512, 512, 48):  # faint diagonal stripes
        d.line([(k, 512), (k + 512, 0)], fill=(52, 56, 43), width=10)
    emblem(img, 256, 256, 400)
    img.save(path)


def feature(path, shot):
    w, h = 1024, 500
    bg = Image.open(shot).convert("RGB")
    bw, bh = bg.size
    crop = bg.crop((int(bw * 0.40), int(bh * 0.18), int(bw * 0.80), int(bh * 0.62)))
    crop = crop.resize((w, int(w * crop.size[1] / crop.size[0])))
    crop = crop.crop((0, (crop.size[1] - h) // 2, w, (crop.size[1] - h) // 2 + h))
    crop = crop.filter(ImageFilter.GaussianBlur(3))
    shade = Image.new("RGB", (w, h), INK)
    img = Image.blend(crop, shade, 0.62)
    # darker band on the left for the title
    band = Image.new("L", (w, h))
    bd = ImageDraw.Draw(band)
    for x in range(w):
        bd.line([(x, 0), (x, h)], fill=int(max(0, 215 - x * 0.28)))
    img.paste(Image.new("RGB", (w, h), INK), (0, 0), band)

    d = ImageDraw.Draw(img)
    title = ImageFont.truetype(FONT, 96)
    d.text((58, 120), "IRON", font=title, fill=BONE)
    d.text((58, 212), "DOCTRINE", font=title, fill=BONE)
    d.rectangle((60, 330, 60 + 170, 338), fill=ACCENT)
    tag = ImageFont.truetype(FONT_B, 30)
    d.text((60, 354), "Build. Command. Conquer.", font=tag, fill=(200, 196, 176))
    emblem(img, 815, 250, 330)
    img.save(path)


if __name__ == "__main__":
    shot, out = sys.argv[1], sys.argv[2]
    icon(out + "/icon-512.png")
    feature(out + "/feature-1024x500.png", shot)
