"""scripts/make_icons.py: the mosaic icon as PNG, ICO and SVG (FR-71), and that the committed assets are its output."""

from __future__ import annotations

import struct
import tempfile
import unittest
import xml.etree.ElementTree as ET
import zlib
from pathlib import Path
from typing import ClassVar
from unittest import mock

import common
import make_icons

from .support import REPO_DIR, captured, run_as_main, temp_dir

SVG_NS = '{http://www.w3.org/2000/svg}'


def png_chunks(data: bytes) -> list[tuple[bytes, bytes]]:
    """Split a PNG into (type, data) chunks, checking the signature, the lengths and the CRCs."""
    assert data[:8] == b'\x89PNG\r\n\x1a\n', 'PNG signature'
    chunks: list[tuple[bytes, bytes]] = []
    pos = 8
    while pos < len(data):
        (length,) = struct.unpack('>I', data[pos : pos + 4])
        kind = data[pos + 4 : pos + 8]
        body = data[pos + 8 : pos + 8 + length]
        (crc,) = struct.unpack('>I', data[pos + 8 + length : pos + 12 + length])
        assert crc == zlib.crc32(kind + body) & 0xFFFFFFFF, f'CRC of {kind!r}'
        chunks.append((kind, body))
        pos += 12 + length
    assert pos == len(data), 'trailing bytes'
    return chunks


def decode_png(data: bytes) -> tuple[int, int, list[bytes]]:
    """Width, height and the RGBA rows of a PNG written by make_icons.png (filter type 0 on every row)."""
    chunks = png_chunks(data)
    kinds = [kind for kind, _ in chunks]
    assert kinds == [b'IHDR', b'IDAT', b'IEND'], kinds
    width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', chunks[0][1])
    assert (depth, color, compression, filtering, interlace) == (8, 6, 0, 0, 0)
    raw = zlib.decompress(chunks[1][1])
    stride = 1 + 4 * width
    assert len(raw) == stride * height
    rows = [raw[y * stride : (y + 1) * stride] for y in range(height)]
    assert all(row[0] == 0 for row in rows), 'filter type'
    assert chunks[2][1] == b''
    return width, height, [row[1:] for row in rows]


def pixel(rows: list[bytes], x: int, y: int) -> tuple[int, int, int, int]:
    r, g, b, a = rows[y][4 * x : 4 * x + 4]
    return r, g, b, a


class ConstantsTest(unittest.TestCase):
    def test_st34_assets_folder(self) -> None:
        self.assertEqual(make_icons.ASSETS, REPO_DIR / 'src' / 'Hider' / 'Assets')

    def test_st34_palette_is_the_grey_blue_of_the_app(self) -> None:
        self.assertEqual(
            make_icons.PALETTE, [(0x3E, 0x47, 0x56), (0x68, 0x76, 0x8A), (0x8A, 0x8F, 0x99), (0xC8, 0xCE, 0xD8)]
        )

    def test_st34_sizes(self) -> None:
        self.assertEqual(make_icons.PNG_SIZES, [16, 20, 24, 32, 48, 64, 128, 256])
        self.assertEqual(make_icons.ICO_SIZES, [16, 20, 24, 32, 48, 256])
        # Every ICO entry reuses a PNG.
        self.assertTrue(set(make_icons.ICO_SIZES) <= set(make_icons.PNG_SIZES))

    def test_st34_layout_is_a_grid_of_palette_indexes(self) -> None:
        self.assertEqual(make_icons.GRID, 4)
        self.assertEqual(len(make_icons.LAYOUT), make_icons.GRID)
        for row in make_icons.LAYOUT:
            self.assertEqual(len(row), make_icons.GRID)
            self.assertTrue(all(0 <= index < len(make_icons.PALETTE) for index in row))
        # Neighbouring cells differ, so the cells read as a mosaic.
        for y in range(make_icons.GRID):
            for x in range(make_icons.GRID - 1):
                self.assertNotEqual(make_icons.LAYOUT[y][x], make_icons.LAYOUT[y][x + 1])
                self.assertNotEqual(make_icons.LAYOUT[x][y], make_icons.LAYOUT[x + 1][y])


class CellColorTest(unittest.TestCase):
    def test_st34_follows_the_layout(self) -> None:
        for cy in range(make_icons.GRID):
            for cx in range(make_icons.GRID):
                self.assertEqual(make_icons.cell_color(cx, cy), make_icons.PALETTE[make_icons.LAYOUT[cy][cx]])

    def test_st34_corners(self) -> None:
        # Light top left, dark bottom right.
        self.assertEqual(make_icons.cell_color(0, 0), (0xC8, 0xCE, 0xD8))
        self.assertEqual(make_icons.cell_color(3, 3), (0x3E, 0x47, 0x56))
        # x is the column, y the row.
        self.assertEqual(make_icons.cell_color(3, 0), (0x68, 0x76, 0x8A))
        self.assertEqual(make_icons.cell_color(0, 3), (0x8A, 0x8F, 0x99))

    def test_st34_outside_the_grid_raises(self) -> None:
        with self.assertRaises(IndexError):
            make_icons.cell_color(0, 4)


class CornerRadiusTest(unittest.TestCase):
    def test_st35_is_an_eighth_of_the_size(self) -> None:
        self.assertEqual(make_icons.corner_radius(64), 8.0)
        self.assertEqual(make_icons.corner_radius(16), 2.0)
        self.assertEqual(make_icons.corner_radius(20), 2.5)
        self.assertIsInstance(make_icons.corner_radius(256), float)


class InsideRoundedTest(unittest.TestCase):
    def test_st35_centre_and_edges(self) -> None:
        self.assertTrue(make_icons.inside_rounded(32.0, 32.0, 64))
        # Edge midpoints are inside: only the corners are rounded.
        self.assertTrue(make_icons.inside_rounded(0.0, 32.0, 64))
        self.assertTrue(make_icons.inside_rounded(32.0, 0.0, 64))
        self.assertTrue(make_icons.inside_rounded(63.9, 32.0, 64))
        self.assertTrue(make_icons.inside_rounded(32.0, 63.9, 64))

    def test_st35_corners_are_cut(self) -> None:
        for x, y in [(0.0, 0.0), (63.9, 0.0), (0.0, 63.9), (63.9, 63.9), (1.0, 1.0)]:
            with self.subTest(x=x, y=y):
                self.assertFalse(make_icons.inside_rounded(x, y, 64))

    def test_st35_circle_boundary_is_inside(self) -> None:
        # r = 8: (8, 0) is on the corner circle centred at (8, 8).
        self.assertTrue(make_icons.inside_rounded(8.0, 0.0, 64))
        self.assertTrue(make_icons.inside_rounded(0.0, 8.0, 64))
        self.assertFalse(make_icons.inside_rounded(7.9, 0.0, 64))
        # 45 degrees on the circle, just inside and just outside.
        offset = 8 - 8 / 2**0.5
        self.assertTrue(make_icons.inside_rounded(offset + 0.01, offset + 0.01, 64))
        self.assertFalse(make_icons.inside_rounded(offset - 0.01, offset - 0.01, 64))

    def test_st35_symmetric(self) -> None:
        for x, y in [(1.5, 0.5), (2.0, 3.0), (0.3, 5.0)]:
            expected = make_icons.inside_rounded(x, y, 16)
            self.assertEqual(make_icons.inside_rounded(16 - x, y, 16), expected)
            self.assertEqual(make_icons.inside_rounded(x, 16 - y, 16), expected)
            self.assertEqual(make_icons.inside_rounded(y, x, 16), expected)


class RenderTest(unittest.TestCase):
    def test_st36_shape(self) -> None:
        for size in [1, 4, 16, 18, 20]:
            with self.subTest(size=size):
                rows = make_icons.render(size)
                self.assertEqual(len(rows), size)
                self.assertTrue(all(len(row) == 4 * size for row in rows))

    def test_st36_cells_snap_to_whole_pixels(self) -> None:
        # 18 / 4 = 4.5: edges round(i * 4.5) = 0, 4 (half to even), 9, 14 (half to even), 18.
        rows = make_icons.render(18)
        mid = 9
        columns = [pixel(rows, x, mid)[:3] for x in range(18)]
        row_colors = [pixel(rows, mid, y)[:3] for y in range(18)]
        expected_columns = [make_icons.cell_color(c, 2) for c in [0] * 4 + [1] * 5 + [2] * 5 + [3] * 4]
        expected_rows = [make_icons.cell_color(2, c) for c in [0] * 4 + [1] * 5 + [2] * 5 + [3] * 4]
        self.assertEqual(columns, expected_columns)
        self.assertEqual(row_colors, expected_rows)

    def test_st36_even_cells(self) -> None:
        rows = make_icons.render(16)
        for y in range(16):
            for x in range(16):
                self.assertEqual(pixel(rows, x, y)[:3], make_icons.cell_color(x // 4, y // 4))

    def test_st36_inside_is_opaque(self) -> None:
        rows = make_icons.render(32)
        for y in range(32):
            for x in range(4, 28):
                self.assertEqual(pixel(rows, x, y)[3], 255)

    def test_st36_corners_are_antialiased(self) -> None:
        rows = make_icons.render(64)
        # r = 8: the corner pixel is outside, the pixel on the diagonal at the circle is partly covered.
        self.assertEqual(pixel(rows, 0, 0)[3], 0)
        self.assertEqual(pixel(rows, 63, 63)[3], 0)
        partial = pixel(rows, 2, 2)[3]
        self.assertGreater(partial, 0)
        self.assertLess(partial, 255)
        self.assertEqual(pixel(rows, 8, 8)[3], 255)

    def test_st36_alpha_is_symmetric(self) -> None:
        size = 24
        rows = make_icons.render(size)
        for y in range(size):
            for x in range(size):
                alpha = pixel(rows, x, y)[3]
                self.assertEqual(pixel(rows, size - 1 - x, y)[3], alpha)
                self.assertEqual(pixel(rows, x, size - 1 - y)[3], alpha)
                self.assertEqual(pixel(rows, y, x)[3], alpha)

    def test_st36_alpha_counts_the_samples(self) -> None:
        # Alpha is round(255 * hits / 16): only those 17 values occur.
        allowed = {round(255 * hits / 16) for hits in range(17)}
        rows = make_icons.render(64)
        alphas = {pixel(rows, x, y)[3] for y in range(64) for x in range(64)}
        self.assertTrue(alphas <= allowed)
        self.assertIn(0, alphas)
        self.assertIn(255, alphas)


class PngTest(unittest.TestCase):
    def test_st37_decodes_to_the_rendered_pixels(self) -> None:
        for size in [16, 20, 33]:
            with self.subTest(size=size):
                width, height, rows = decode_png(make_icons.png(size))
                self.assertEqual((width, height), (size, size))
                self.assertEqual(rows, make_icons.render(size))

    def test_st37_is_deterministic(self) -> None:
        self.assertEqual(make_icons.png(24), make_icons.png(24))

    def test_st37_uses_maximum_compression(self) -> None:
        idat = png_chunks(make_icons.png(32))[1][1]
        raw = zlib.decompress(idat)
        self.assertEqual(idat, zlib.compress(raw, 9))


class IcoTest(unittest.TestCase):
    def test_st37_layout(self) -> None:
        images = [(16, b'a' * 10), (48, b'bb' * 7), (256, b'ccc')]
        data = make_icons.ico(images)
        self.assertEqual(struct.unpack('<HHH', data[:6]), (0, 1, 3))
        offset = 6 + 16 * 3
        for i, (size, payload) in enumerate(images):
            entry = struct.unpack('<BBBBHHII', data[6 + 16 * i : 6 + 16 * (i + 1)])
            dim = 0 if size == 256 else size
            self.assertEqual(entry, (dim, dim, 0, 0, 1, 32, len(payload), offset))
            self.assertEqual(data[offset : offset + len(payload)], payload)
            offset += len(payload)
        self.assertEqual(len(data), offset)

    def test_st37_sizes_of_256_and_more_are_written_as_zero(self) -> None:
        data = make_icons.ico([(255, b'x'), (256, b'y'), (512, b'z')])
        dims = [data[6 + 16 * i] for i in range(3)]
        self.assertEqual(dims, [255, 0, 0])

    def test_st37_no_images(self) -> None:
        self.assertEqual(make_icons.ico([]), struct.pack('<HHH', 0, 1, 0))

    def test_st37_entries_hold_pngs(self) -> None:
        pngs = [(size, make_icons.png(size)) for size in [16, 24]]
        data = make_icons.ico(pngs)
        for i, (_, payload) in enumerate(pngs):
            length, offset = struct.unpack('<II', data[6 + 16 * i + 8 : 6 + 16 * (i + 1)])
            self.assertEqual(data[offset : offset + length], payload)


class HexColorTest(unittest.TestCase):
    def test_st38_upper_case_two_digits(self) -> None:
        self.assertEqual(make_icons.hex_color((0x3E, 0x47, 0x56)), '#3E4756')
        self.assertEqual(make_icons.hex_color((0, 0, 0)), '#000000')
        self.assertEqual(make_icons.hex_color((255, 255, 255)), '#FFFFFF')
        self.assertEqual(make_icons.hex_color((1, 10, 171)), '#010AAB')


class SvgTest(unittest.TestCase):
    def test_st38_structure(self) -> None:
        root = ET.fromstring(make_icons.svg())
        self.assertEqual(root.tag, f'{SVG_NS}svg')
        self.assertEqual((root.get('width'), root.get('height'), root.get('viewBox')), ('64', '64', '0 0 64 64'))
        clip = root.find(f'{SVG_NS}clipPath')
        assert clip is not None
        self.assertEqual(clip.get('id'), 'c')
        clip_rect = clip.find(f'{SVG_NS}rect')
        assert clip_rect is not None
        self.assertEqual((clip_rect.get('width'), clip_rect.get('height'), clip_rect.get('rx')), ('64', '64', '8'))
        group = root.find(f'{SVG_NS}g')
        assert group is not None
        self.assertEqual(group.get('clip-path'), 'url(#c)')
        self.assertEqual(group.get('shape-rendering'), 'crispEdges')

    def test_st38_cells(self) -> None:
        group = ET.fromstring(make_icons.svg()).find(f'{SVG_NS}g')
        assert group is not None
        rects = group.findall(f'{SVG_NS}rect')
        self.assertEqual(len(rects), 16)
        expected = [
            (str(cx * 16), str(cy * 16), make_icons.hex_color(make_icons.cell_color(cx, cy)))
            for cy in range(4)
            for cx in range(4)
        ]
        self.assertEqual([(r.get('x'), r.get('y'), r.get('fill')) for r in rects], expected)
        self.assertTrue(all(r.get('width') == '16' and r.get('height') == '16' for r in rects))

    def test_st38_text(self) -> None:
        text = make_icons.svg()
        self.assertTrue(text.startswith('<svg xmlns="http://www.w3.org/2000/svg" width="64"'))
        self.assertTrue(text.endswith('</g>\n</svg>\n'))
        self.assertIn('    <rect x="0" y="0" width="16" height="16" fill="#C8CED8"/>\n', text)
        self.assertNotIn('\r', text)


class MainTest(unittest.TestCase):
    """main as `python scripts/make_icons.py` runs it. Drawing every size takes about a second, so it is done once for
    the class (into a scratch checkout); the overwrite test draws the 16 px size only."""

    root: ClassVar[Path]
    output: ClassVar[str]

    @classmethod
    def setUpClass(cls) -> None:
        holder = tempfile.TemporaryDirectory(prefix='hider-scripts-test-')
        cls.addClassCleanup(holder.cleanup)
        cls.root = Path(holder.name)
        with mock.patch.object(common, 'ROOT_DIR', cls.root), captured() as output:
            run_as_main('make_icons.py')
        cls.output = output.out

    @property
    def assets(self) -> Path:
        return self.root / 'src' / 'Hider' / 'Assets'

    def written(self, folder: Path) -> dict[str, bytes]:
        return {p.name: p.read_bytes() for p in sorted(folder.iterdir())}

    def test_st39_writes_every_file_and_matches_the_committed_assets(self) -> None:
        files = self.written(self.assets)
        expected_names = {f'hider-{size}.png' for size in make_icons.PNG_SIZES} | {'hider.ico', 'hider.svg'}
        self.assertEqual(set(files), expected_names)
        self.assertEqual(
            self.output, f'Wrote 8 PNG files, hider.ico (16, 20, 24, 32, 48, 256) and hider.svg to {self.assets}\n'
        )
        for size in make_icons.PNG_SIZES:
            self.assertEqual(decode_png(files[f'hider-{size}.png'])[:2], (size, size))
        self.assertEqual(files['hider-16.png'], make_icons.png(16))
        self.assertEqual(files['hider.svg'], make_icons.svg().encode('utf-8'))
        self.assertEqual(
            files['hider.ico'], make_icons.ico([(size, files[f'hider-{size}.png']) for size in make_icons.ICO_SIZES])
        )
        # The generated files are committed: they must be what this script makes (run it after changing it).
        committed = {p.name: p.read_bytes() for p in (REPO_DIR / 'src' / 'Hider' / 'Assets').iterdir()}
        for name, data in files.items():
            with self.subTest(name=name):
                self.assertEqual(committed[name], data, f'{name} differs: run python scripts/make_icons.py')

    def test_st39_overwrites_into_an_existing_folder(self) -> None:
        assets = temp_dir(self) / 'Assets'
        assets.mkdir()
        (assets / 'hider.svg').write_text('old\r\n', encoding='utf-8')
        (assets / 'hider-16.png').write_bytes(b'old')
        (assets / 'other.txt').write_text('kept', encoding='utf-8')
        with (
            mock.patch.object(make_icons, 'ASSETS', assets),
            mock.patch.object(make_icons, 'PNG_SIZES', [16]),
            mock.patch.object(make_icons, 'ICO_SIZES', [16]),
            captured() as output,
        ):
            make_icons.main()
        self.assertEqual(output.out, f'Wrote 1 PNG files, hider.ico (16) and hider.svg to {assets}\n')
        self.assertEqual((assets / 'hider.svg').read_bytes(), make_icons.svg().encode('utf-8'))
        self.assertEqual((assets / 'hider-16.png').read_bytes(), make_icons.png(16))
        self.assertEqual((assets / 'hider.ico').read_bytes(), make_icons.ico([(16, make_icons.png(16))]))
        self.assertEqual((assets / 'other.txt').read_text(encoding='utf-8'), 'kept')

    def test_st39_as_a_script(self) -> None:
        # setUpClass ran the file as __main__ with ROOT_DIR pointing at the scratch checkout: it wrote there.
        self.assertIn(f'and hider.svg to {self.assets}', self.output)
        self.assertEqual(len(list(self.assets.iterdir())), 10)

    def test_st39_assets_path(self) -> None:
        self.assertIsInstance(make_icons.ASSETS, Path)
