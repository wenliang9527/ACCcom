"""Generate ACCcom app icon: rounded-square violet gradient (Light-theme Accent
#7C3AED -> #5B21B6) with a white data-pulse waveform, at 256px master and
downscaled to standard .ico sizes."""
from PIL import Image, ImageDraw

S = 256
R = 56  # corner radius

# vertical gradient background
top = (0x7C, 0x3A, 0xED)
bot = (0x5B, 0x21, 0xB6)
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
px = img.load()
for y in range(S):
    t = y / (S - 1)
    c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
    for x in range(S):
        px[x, y] = (*c, 255)

# rounded-rect mask
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=R, fill=255)
img.putalpha(mask)

d = ImageDraw.Draw(img)

# white data-pulse waveform (RX/TX stream): three pulses riding a baseline
W = 20   # stroke width
gap = 26
x0, x1 = 46, 210
y_base = 168
pulses = [(x0, 78), (x0 + gap, 78), (x0 + 2 * gap, 132)]
for i, (x, y_top) in enumerate(pulses):
    xl = x if i == 0 else x - W // 2
    d.line([(xl, y_top), (x, y_top)], fill=(255, 255, 255, 255), width=W)
    d.line([(x, y_top), (x, y_base)], fill=(255, 255, 255, 255), width=W)
    # pulse cap (rounded top)
    d.ellipse([x - W // 2, y_top - W // 2, x + W // 2, y_top + W // 2],
              fill=(255, 255, 255, 255))
# baseline
d.line([(x0, y_base), (x1, y_base)], fill=(255, 255, 255, 255), width=14)

# subtle gloss: top sheen. IMPORTANT: keep the 26/255 alpha — calling
# putalpha(mask) replaces the tint with an opaque mask and turns the whole
# icon solid white, hiding the violet gradient underneath.
gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(gloss)
gd.rounded_rectangle([0, 0, S - 1, S - 1], radius=R, fill=(255, 255, 255, 26))
gloss.putalpha(Image.composite(gloss.getchannel("A"), Image.new("L", (S, S), 0), mask))
img = Image.alpha_composite(img, gloss)

img.save("src/ACCcom/Assets/app.png")
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save("src/ACCcom/Assets/app.ico", sizes=sizes)
print("saved app.png + app.ico", sizes)
