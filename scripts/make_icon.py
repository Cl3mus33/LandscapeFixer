"""Draws the Landscape Fixer icon and writes native/LandscapeFixer/resources/icon.ico (+ a PNG preview).

    python scripts/make_icon.py

Concept: a mountain whose slope blends from snow to bare rock to green - the two things the tool
does (snow and blend) in one picture - on a deep teal tile.
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

S = 1024  # drawing size; downsampled for every icon size
root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
out_dir = os.path.join(root, 'native', 'LandscapeFixer', 'resources')


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def vgradient(size, stops):
    """Vertical gradient image from [(pos 0..1, (r, g, b)), ...]."""
    w, h = size
    img = Image.new('RGB', size)
    px = img.load()
    for y in range(h):
        t = y / (h - 1)
        for i in range(len(stops) - 1):
            p0, c0 = stops[i]
            p1, c1 = stops[i + 1]
            if p0 <= t <= p1:
                col = lerp(c0, c1, 0 if p1 == p0 else (t - p0) / (p1 - p0))
                break
        else:
            col = stops[-1][1]
        for x in range(w):
            px[x, y] = col
    return img


def polygon_mask(points):
    m = Image.new('L', (S, S), 0)
    ImageDraw.Draw(m).polygon(points, fill=255)
    return m


def paste_masked(base, layer, mask):
    base.paste(layer, (0, 0), mask)


# --- tile -------------------------------------------------------------------------------------
tile = Image.new('RGBA', (S, S), (0, 0, 0, 0))
sky = vgradient((S, S), [(0.0, (14, 38, 56)), (0.55, (34, 86, 104)), (1.0, (62, 128, 128))])
radius = int(S * 0.22)
tile_mask = Image.new('L', (S, S), 0)
ImageDraw.Draw(tile_mask).rounded_rectangle([0, 0, S - 1, S - 1], radius=radius, fill=255)
tile.paste(sky, (0, 0), tile_mask)

canvas = tile.copy()

# soft sun / moon disc behind the ridges
disc = Image.new('L', (S, S), 0)
ImageDraw.Draw(disc).ellipse([int(S * .60), int(S * .13), int(S * .80), int(S * .33)], fill=255)
disc = disc.filter(ImageFilter.GaussianBlur(6))
glow = Image.new('RGB', (S, S), (214, 232, 238))
canvas.paste(glow, (0, 0), ImageChops.multiply(disc, tile_mask).point(lambda v: int(v * 0.55)))

# --- back ridge (smaller peak, right) ---------------------------------------------------------
back_pts = [(int(S * .40), int(S * .80)), (int(S * .62), int(S * .36)), (int(S * .70), int(S * .48)),
            (int(S * .80), int(S * .30)), (int(S * 1.02), int(S * .80))]
back = vgradient((S, S), [(0.0, (236, 244, 247)), (0.30, (236, 244, 247)), (0.42, (122, 150, 168)), (1.0, (70, 104, 128))])
paste_masked(canvas, back, ImageChops.multiply(polygon_mask(back_pts), tile_mask))

# --- front mountain: snow -> rock -> green, blended -----------------------------------------
peak = (int(S * .34), int(S * .17))
front_pts = [(int(S * -0.04), int(S * .86)), (int(S * .16), int(S * .55)), (int(S * .24), int(S * .40)), peak,
             (int(S * .43), int(S * .34)), (int(S * .52), int(S * .50)), (int(S * .60), int(S * .58)),
             (int(S * .78), int(S * .86))]
front = vgradient((S, S), [(0.0, (250, 252, 253)), (0.30, (244, 249, 251)), (0.42, (150, 170, 182)),
                           (0.58, (118, 132, 136)), (0.72, (92, 140, 96)), (1.0, (54, 112, 76))])
front_mask = ImageChops.multiply(polygon_mask(front_pts), tile_mask)
paste_masked(canvas, front, front_mask)

# crisp snow cap with a jagged lower edge (reads as "snow" even at 16 px)
cap_pts = [(int(S * .20), int(S * .47)), (int(S * .24), int(S * .40)), peak, (int(S * .43), int(S * .34)),
           (int(S * .49), int(S * .45)), (int(S * .44), int(S * .43)), (int(S * .41), int(S * .50)),
           (int(S * .36), int(S * .44)), (int(S * .31), int(S * .52)), (int(S * .27), int(S * .46)),
           (int(S * .23), int(S * .53))]
cap_mask = ImageChops.multiply(polygon_mask(cap_pts), front_mask)
canvas.paste(Image.new('RGB', (S, S), (248, 252, 254)), (0, 0), cap_mask)

# light shade on the right face of the front mountain
shade_pts = [peak, (int(S * .43), int(S * .34)), (int(S * .52), int(S * .50)), (int(S * .60), int(S * .58)),
             (int(S * .78), int(S * .86)), (int(S * .40), int(S * .86)), (int(S * .36), int(S * .46))]
shade = Image.new('RGBA', (S, S), (10, 34, 52, 0))
shade_mask = ImageChops.multiply(ImageChops.multiply(polygon_mask(shade_pts), front_mask), tile_mask).point(lambda v: int(v * 0.30))
canvas.paste(Image.new('RGB', (S, S), (10, 34, 52)), (0, 0), shade_mask)

# --- foreground hill ----------------------------------------------------------------------------
hill = Image.new('L', (S, S), 0)
ImageDraw.Draw(hill).ellipse([int(S * -.25), int(S * .74), int(S * .95), int(S * 1.45)], fill=255)
hill2 = Image.new('L', (S, S), 0)
ImageDraw.Draw(hill2).ellipse([int(S * .30), int(S * .82), int(S * 1.30), int(S * 1.5)], fill=255)
hills = ImageChops.lighter(hill, hill2)
hill_col = vgradient((S, S), [(0.0, (74, 140, 92)), (0.78, (58, 118, 78)), (1.0, (30, 78, 56))])
paste_masked(canvas, hill_col, ImageChops.multiply(hills, tile_mask))

canvas = canvas.convert('RGBA')
canvas.putalpha(tile_mask)

sizes = [16, 24, 32, 48, 64, 128, 256]
images = [canvas.resize((s, s), Image.LANCZOS) for s in sizes]
ico_path = os.path.join(out_dir, 'icon.ico')
images[-1].save(ico_path, format='ICO', sizes=[(s, s) for s in sizes], append_images=images[:-1])
canvas.resize((256, 256), Image.LANCZOS).save(os.path.join(root, 'scripts', 'icon-preview.png'))
sheet = Image.new('RGBA', (16 + 24 + 32 + 48 + 64 + 6 * 12, 80), (40, 40, 40, 255))
x = 12
for im in images[:5]:
    sheet.paste(im, (x, 8), im)
    x += im.size[0] + 12
sheet.save(os.path.join(root, 'scripts', 'icon-sizes.png'))
print('wrote', ico_path, os.path.getsize(ico_path))
