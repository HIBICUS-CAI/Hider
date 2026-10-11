"""Generate Hider's icons (FR-71) into src/Hider/Assets: hider.svg, hider-<size>.png and hider.ico.

The icon is a 4 x 4 mosaic in the app's grey-blue palette on a rounded square. Uses only the standard library, so it
runs anywhere Python does; the generated files are committed and the build does not need Python.

    python scripts/make_icons.py
"""
import os, struct, zlib
from common import ROOT_DIR

ASSETS = os.path.join(ROOT_DIR, 'src', 'Hider', 'Assets')
PALETTE = [(0x3E, 0x47, 0x56), (0x68, 0x76, 0x8A), (0x8A, 0x8F, 0x99), (0xC8, 0xCE, 0xD8)]
PNG_SIZES = [16, 20, 24, 32, 48, 64, 128, 256]
ICO_SIZES = [16, 20, 24, 32, 48, 256]
GRID = 4
SUPERSAMPLE = 4


# A pixelated light-to-dark diagonal with some variation: reads as "mosaic of something" even at 16 px.
LAYOUT = [
    [3, 2, 3, 1],
    [2, 3, 1, 2],
    [1, 2, 0, 1],
    [2, 0, 1, 0],
]


def cell_color(cx, cy):
    return PALETTE[LAYOUT[cy][cx]]


def corner_radius(size):
    return size / 8.0


def inside_rounded(x, y, size):
    """Whether (x, y) in [0, size) lies inside the rounded square."""
    r = corner_radius(size)
    dx = max(r - x, 0.0, x - (size - r))
    dy = max(r - y, 0.0, y - (size - r))
    return dx * dx + dy * dy <= r * r


def render(size):
    """RGBA rows: cells snap to whole pixels at small sizes, corners are antialiased by supersampling."""
    edges = [round(i * size / GRID) for i in range(GRID + 1)]
    rows = []
    for y in range(size):
        cy = max(i for i in range(GRID) if edges[i] <= y)
        row = bytearray()
        for x in range(size):
            cx = max(i for i in range(GRID) if edges[i] <= x)
            hits = 0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    if inside_rounded(x + (sx + 0.5) / SUPERSAMPLE, y + (sy + 0.5) / SUPERSAMPLE, size):
                        hits += 1
            alpha = round(255 * hits / (SUPERSAMPLE * SUPERSAMPLE))
            r, g, b = cell_color(cx, cy)
            row += bytes((r, g, b, alpha))
        rows.append(bytes(row))
    return rows


def png(size):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind + data) & 0xFFFFFFFF)
    raw = b''.join(b'\x00' + row for row in render(size))
    header = struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)  # 8-bit RGBA
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', header) + chunk(b'IDAT', zlib.compress(raw, 9)) + chunk(b'IEND', b'')


def ico(images):
    """An .ico whose entries are PNG images (supported since Windows Vista)."""
    out = struct.pack('<HHH', 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, payload = b'', b''
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset + len(payload))
        payload += data
    return out + entries + payload


def svg():
    size = 64
    step = size // GRID
    rects = '\n'.join(
        f'    <rect x="{cx * step}" y="{cy * step}" width="{step}" height="{step}" fill="#{"%02X%02X%02X" % cell_color(cx, cy)}"/>'
        for cy in range(GRID) for cx in range(GRID))
    r = corner_radius(size)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 {size} {size}">\n'
            f'  <clipPath id="c"><rect width="{size}" height="{size}" rx="{r:g}"/></clipPath>\n'
            f'  <g clip-path="url(#c)" shape-rendering="crispEdges">\n{rects}\n  </g>\n</svg>\n')


def main():
    os.makedirs(ASSETS, exist_ok=True)
    pngs = {size: png(size) for size in PNG_SIZES}
    for size, data in pngs.items():
        with open(os.path.join(ASSETS, f'hider-{size}.png'), 'wb') as f:
            f.write(data)
    with open(os.path.join(ASSETS, 'hider.ico'), 'wb') as f:
        f.write(ico([(size, pngs[size]) for size in ICO_SIZES]))
    with open(os.path.join(ASSETS, 'hider.svg'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(svg())
    print(f'Wrote {len(pngs)} PNG files, hider.ico ({", ".join(map(str, ICO_SIZES))}) and hider.svg to {ASSETS}')


if __name__ == '__main__':
    main()
