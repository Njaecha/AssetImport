#!/usr/bin/env python3
"""Compare production BMP/TGA/DDS-to-PNG pixels with independent Pillow decoding.

Requires Pillow, dotnet and the built AssetImport.Pmx.Tests DLL. Synthetic input
files are original fixtures. Optional private folders are read, never packaged.
"""
import argparse
from pathlib import Path
import struct
import subprocess
import tempfile
from PIL import Image


def dds(mode, width=7, height=5, dx10=False, reverse=False):
    header = bytearray(128 + (20 if dx10 else 0))
    def put(offset, value): struct.pack_into('<I', header, offset, value)
    header[:4] = b'DDS '
    for offset, value in ((4, 124), (8, 0x81007), (12, height), (16, width), (76, 32), (80, 4), (108, 0x1000)):
        put(offset, value)
    header[84:88] = b'DX10' if dx10 else {1: b'DXT1', 2: b'DXT3', 3: b'DXT5'}[mode]
    if dx10:
        for offset, value in ((128, {1: 71, 2: 74, 3: 77}[mode]), (132, 3), (140, 1), (144, 1)): put(offset, value)
    blocks = bytearray()
    for block in range(((width + 3) // 4) * ((height + 3) // 4)):
        if mode == 2:
            blocks += bytes(((i + block) % 16) | (((15 - i + block) % 16) << 4) for i in range(8))
        elif mode == 3:
            blocks += bytes((20, 231) if reverse else (231, 20))
            blocks += sum(((i + block) % 8) << (i * 3) for i in range(16)).to_bytes(6, 'little')
        c0, c1 = (0x1357, 0xdba8) if reverse else (0xdba8, 0x1357)
        blocks += struct.pack('<HHI', c0, c1, sum(((i + block) % 4) << (i * 2) for i in range(16)))
    return bytes(header + blocks)


def bmp_masks(bits, alpha=False):
    masks = [0xf800, 0x07e0, 0x001f] if bits == 16 else [0xff0000, 0xff00, 0xff]
    if alpha: masks += [0xff000000]
    dib = 56 if alpha else 40
    offset = 14 + dib + (12 if dib == 40 else 0)
    b = bytearray(offset)
    b[:2] = b'BM'
    for pos, value in ((10, offset), (14, dib), (18, 3), (22, 2), (30, 3)): struct.pack_into('<I', b, pos, value)
    struct.pack_into('<HH', b, 26, 1, bits)
    for i, mask in enumerate(masks): struct.pack_into('<I', b, 54 + i * 4, mask)
    pixels = (0xf800, 0x07e0, 0x001f, 0xffff, 0, 0x7bef) if bits == 16 else (0x80ff0000, 0x0000ff00, 0xff0000ff, 0x44ffffff, 0, 0xff999999)
    for row in range(2):
        raw = b''.join(value.to_bytes(bits // 8, 'little') for value in pixels[row * 3:row * 3 + 3])
        b += raw + b'\0' * ((-len(raw)) % 4)
    struct.pack_into('<I', b, 2, len(b))
    return bytes(b)


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--dotnet', type=Path, required=True)
    p.add_argument('--test-dll', type=Path, required=True)
    p.add_argument('--private-root', type=Path, action='append', default=[])
    a = p.parse_args()
    with tempfile.TemporaryDirectory(prefix='assetimport-texture-') as tmp:
        folder = Path(tmp)
        fixtures = []
        def add(name, data):
            file = folder / name
            file.write_bytes(data)
            fixtures.append(file)
        for mode in (1, 2, 3):
            for dx10 in (False, True):
                for reverse in (False, True):
                    add('bc%d_dx%d_rev%d.dds' % (mode, dx10, reverse), dds(mode, dx10=dx10, reverse=reverse))
        for bits, alpha in ((16, False), (32, False), (32, True)):
            add('mask_%d_%d.bmp' % (bits, alpha), bmp_masks(bits, alpha))
        im = Image.new('RGBA', (7, 5))
        im.putdata([((i * 31) % 256, (i * 59) % 256, (i * 127) % 256, (i * 7) % 256) for i in range(35)])
        for mode in ('1', 'P', 'RGB', 'RGBA'):
            file = folder / ('pillow_' + mode + '.bmp')
            im.convert(mode).save(file)
            fixtures.append(file)
        # Uncompressed DDS covers channel masks, alpha and row origin too.
        file = folder / 'rgba.dds'
        im.save(file)
        fixtures.append(file)
        private = [f for root in a.private_root for f in root.rglob('*') if f.suffix.lower() in ('.bmp', '.tga', '.dds')]
        for source in fixtures + private:
            dest = folder / 'converted.png'
            subprocess.run([str(a.dotnet.resolve()), str(a.test_dll.resolve()), '--image', str(source), str(dest)], check=True, stdout=subprocess.DEVNULL)
            expected = Image.open(source).convert('RGBA')
            actual = Image.open(dest).convert('RGBA')
            assert expected.size == actual.size, source
            assert expected.tobytes() == actual.tobytes(), ('RGBA mismatch', source)
        print('PASS: %d original fixtures + %d private BMP/TGA/DDS files exactly match Pillow RGBA pixels.' % (len(fixtures), len(private)))


if __name__ == '__main__':
    main()
