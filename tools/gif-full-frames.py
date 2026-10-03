# Rebuild a GIF as the simplest possible animation for a firmware decoder:
# full-size frames at 0,0 (the firmware Max Image Size), one global 256-colour palette, no local tables, no transparency.
# Usage: python gif-full-frames.py input.gif output.gif [width height]   (default 1920 1080; requires: pip install pillow)
import sys, struct
from PIL import Image, ImageSequence

def lzw(data):
    clear, eoi, size, nxt = 256, 257, 9, 258
    table = {bytes([i]): i for i in range(256)}
    out, acc, bits = bytearray(), 0, 0
    def emit(code):
        nonlocal acc, bits
        acc |= code << bits; bits += size
        while bits >= 8: out.append(acc & 255); acc >>= 8; bits -= 8
    emit(clear); w = b''
    for c in data:
        wc = w + bytes([c])
        if wc in table: w = wc; continue
        emit(table[w])
        if nxt < 4096:
            table[wc] = nxt; nxt += 1
            if nxt > (1 << size) and size < 12: size += 1
        else:
            emit(clear); table = {bytes([i]): i for i in range(256)}; size, nxt = 9, 258
        w = bytes([c])
    emit(table[w]); emit(eoi)
    if bits: out.append(acc & 255)
    return out

W, H = (int(sys.argv[3]), int(sys.argv[4])) if len(sys.argv) > 4 else (1920, 1080)
src = Image.open(sys.argv[1])
src.seek(src.n_frames - 1); pal = src.convert('RGB').quantize(256, dither=Image.Dither.NONE)
palette = bytes(pal.getpalette()[:768]).ljust(768, b'\0')
g = bytearray(b'GIF89a' + struct.pack('<HHBBB', W, H, 0xF7, 0, 0) + palette)
g += b'\x21\xFF\x0BNETSCAPE2.0\x03\x01\x00\x00\x00'
for f in ImageSequence.Iterator(src):
    canvas = Image.new('RGB', (W, H))
    canvas.paste(f.convert('RGB'), ((W - f.width) // 2, (H - f.height) // 2))
    idx = canvas.quantize(palette=pal, dither=Image.Dither.NONE).tobytes()
    g += b'\x21\xF9\x04' + struct.pack('<BHBB', 1 << 2, f.info['duration'] // 10, 0, 0)
    g += b'\x2C' + struct.pack('<HHHHB', 0, 0, W, H, 0) + b'\x08'
    d = lzw(idx)
    for i in range(0, len(d), 255): g += bytes([len(d[i:i+255])]) + d[i:i+255]
    g += b'\x00'
g += b'\x3B'
open(sys.argv[2], 'wb').write(g)

# Self-check: Pillow decodes every frame back to exactly the indices we encoded.
chk = Image.open(sys.argv[2]); assert chk.n_frames == src.n_frames
src.seek(src.n_frames - 1); chk.seek(chk.n_frames - 1)
c = Image.new('RGB', (W, H)); c.paste(src.convert('RGB'), ((W - src.width) // 2, (H - src.height) // 2))
assert chk.convert('RGB').tobytes() == c.quantize(palette=pal, dither=Image.Dither.NONE).convert('RGB').tobytes()
print('ok', len(g), 'bytes')
