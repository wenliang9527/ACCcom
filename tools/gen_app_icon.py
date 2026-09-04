"""Generate ACCcom app icon: rounded-square violet gradient with a glowing
green ECG heartbeat line (serial/comm theme), 256px master downscaled to
standard .ico sizes.

Design notes:
- Violet gradient background (theme Accent family) keeps brand identity.
- The waveform uses a bright signal-green (theme StatusGreen) — high
  luminance contrast on the dark violet, so it stays legible at 16px.
- The ECG trace: flat baseline, a sharp R-wave spike, a small T-wave,
  then flat tail. Drawn with joint="curve" so the polyline corners blend
  into a smooth heartbeat curve.

IMPORTANT: the gloss layer must KEEP its 26/255 alpha — putalpha(mask)
replaces the tint with an opaque mask and turns the whole icon white.
"""
from PIL import Image, ImageDraw

S = 256
R = 56  # corner radius

# ---- violet gradient background ----
top = (0x7C, 0x3A, 0xED)   # Light-theme Accent
bot = (0x4C, 0x1D, 0x95)   # deeper violet for contrast
img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
px = img.load()
for y in range(S):
    t = y / (S - 1)
    c = tuple(int(top[i] + (bot[i] - top[i]) * t) for i in range(3))
    for x in range(S):
        px[x, y] = (*c, 255)

# ---- rounded-rect mask ----
mask = Image.new("L", (S, S), 0)
md = ImageDraw.Draw(mask)
md.rounded_rectangle([0, 0, S - 1, S - 1], radius=R, fill=255)
img.putalpha(mask)

d = ImageDraw.Draw(img)

# ---- ECG heartbeat trace (signal green) ----
GREEN = (0x4A, 0xDE, 0x80)  # theme StatusGreen
trace = [
    (42, 152),              # start of baseline
    (84, 152),              # flat run
    (98, 58),               # R-wave peak
    (112, 152),             # back to baseline
    (134, 118),             # T-wave rise
    (156, 152),             # T-wave settle
    (214, 152),             # flat tail
]
d.line(trace, fill=GREEN, width=16, joint="curve")

# ---- subtle gloss (keep translucency!) ----
gloss = Image.new("RGBA", (S, S), (0, 0, 0, 0))
gd = ImageDraw.Draw(gloss)
gd.rounded_rectangle([0, 0, S - 1, S - 1], radius=R, fill=(255, 255, 255, 26))
gloss.putalpha(Image.composite(gloss.getchannel("A"), Image.new("L", (S, S), 0), mask))
img = Image.alpha_composite(img, gloss)

img.save("src/ACCcom/Assets/app.png")
sizes = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
img.save("src/ACCcom/Assets/app.ico", sizes=sizes)
print("saved app.png + app.ico", sizes)
