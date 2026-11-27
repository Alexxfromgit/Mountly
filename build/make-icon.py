#!/usr/bin/env python3
"""Renders Mountly's icon: an iridescent disc (teal -> violet -> magenta -> amber sheen) with a dark hub.

Pure Python (no Pillow): supersampled rendering, PNG via zlib, and an .ico holding PNG frames.
Writes src/Mountly.App/Assets/mountly.ico, mountly.png (512 px) and branding/mountly-256.png.

Usage: python build/make-icon.py
"""
import math
import pathlib
import struct
import zlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
ASSETS = ROOT / "src" / "Mountly.App" / "Assets"
BRANDING = ROOT / "branding"

# Sheen colours around the disc (angle order), shared with the app's DiscGlyph control.
STOPS = [
    (0.00, (45, 212, 191)),   # teal  #2DD4BF
    (0.22, (96, 165, 250)),   # sky   #60A5FA
    (0.42, (139, 92, 246)),   # violet #8B5CF6
    (0.62, (255, 79, 163)),   # magenta #FF4FA3
    (0.80, (245, 184, 74)),   # amber #F5B84A
    (1.00, (45, 212, 191)),
]
HUB = (16, 14, 22)


def lerp(a, b, t):
    return a + (b - a) * t


def sheen(angle01):
    for (p0, c0), (p1, c1) in zip(STOPS, STOPS[1:]):
        if p0 <= angle01 <= p1:
            t = (angle01 - p0) / (p1 - p0)
            return tuple(lerp(x, y, t) for x, y in zip(c0, c1))
    return STOPS[-1][1]


def sample(x, y):
    """Colour (r, g, b, a) at a point in [-1, 1]^2."""
    r = math.hypot(x, y)
    if r > 0.94:
        return (0, 0, 0, 0)
    if r < 0.13:
        return (0, 0, 0, 0)  # the hole
    if r < 0.30:
        # Hub: dark, with a faint ring.
        ring = 1.0 if 0.27 < r < 0.30 else 0.0
        c = tuple(lerp(h, 70, 0.35 * ring) for h in HUB)
        return (*c, 255)
    angle = (math.atan2(y, x) / (2 * math.pi) + 0.5 + 0.08) % 1.0
    base = sheen(angle)
    # Grooves: subtle concentric banding; a soft highlight sweep from the top left.
    groove = 0.06 * math.sin(r * 90)
    light = max(0.0, math.cos(math.atan2(y, x) + 2.4)) ** 6 * 0.35
    shade = 0.78 + 0.22 * (1 - (r - 0.30) / 0.64) + groove
    c = tuple(min(255.0, v * shade + 255 * light) for v in base)
    # Bright rim and the inner edge of the data area.
    if r > 0.90 or 0.30 <= r < 0.33:
        c = tuple(lerp(v, 255, 0.35) for v in c)
    return (*c, 255)


def render(size, ss=4):
    rows = []
    for py in range(size):
        row = bytearray()
        for px in range(size):
            acc = [0.0, 0.0, 0.0, 0.0]
            for sy in range(ss):
                for sx in range(ss):
                    x = ((px + (sx + 0.5) / ss) / size) * 2 - 1
                    y = ((py + (sy + 0.5) / ss) / size) * 2 - 1
                    r, g, b, a = sample(x, y)
                    acc[0] += r * a
                    acc[1] += g * a
                    acc[2] += b * a
                    acc[3] += a
            n = ss * ss
            alpha = acc[3] / n
            if alpha > 0:
                row += bytes((int(acc[0] / acc[3]), int(acc[1] / acc[3]), int(acc[2] / acc[3]), int(alpha)))
            else:
                row += b"\0\0\0\0"
        rows.append(bytes(row))
    return rows


def png(size, rows):
    def chunk(kind, data):
        body = kind + data
        return struct.pack(">I", len(data)) + body + struct.pack(">I", zlib.crc32(body) & 0xFFFFFFFF)

    raw = b"".join(b"\0" + r for r in rows)
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", size, size, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))


def ico(frames):
    header = struct.pack("<HHH", 0, 1, len(frames))
    entries, data = b"", b""
    offset = 6 + 16 * len(frames)
    for size, blob in frames:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(blob), offset)
        data += blob
        offset += len(blob)
    return header + entries + data


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)
    BRANDING.mkdir(parents=True, exist_ok=True)
    frames = []
    for size in (16, 20, 24, 32, 40, 48, 64, 128, 256):
        frames.append((size, png(size, render(size, ss=6 if size <= 32 else 4))))
        print(f"rendered {size}px")
    (ASSETS / "mountly.ico").write_bytes(ico(frames))
    big = png(512, render(512, ss=2))
    (ASSETS / "mountly.png").write_bytes(big)
    (BRANDING / "mountly-256.png").write_bytes(dict(frames)[256])
    print("wrote", ASSETS / "mountly.ico", ASSETS / "mountly.png")


if __name__ == "__main__":
    main()
