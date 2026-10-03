# Usage: python gif-info.py file.gif  -- prints size, palette and the layout of every frame.
import sys, struct
b = open(sys.argv[1], 'rb').read()
w, h, flags = struct.unpack('<HHB', b[6:11])
print('header', b[:6], w, h, 'GCT' if flags & 0x80 else 'noGCT', 'gct size', 2 << (flags & 7))
p = 13 + (3 * (2 << (flags & 7)) if flags & 0x80 else 0)
gce = None; n = 0
def skip(p):
    while b[p]: p += b[p] + 1
    return p + 1
while p < len(b):
    t = b[p]
    if t == 0x21:
        lbl = b[p+1]
        if lbl == 0xF9:
            pk = b[p+3]; gce = ((pk >> 2) & 7, struct.unpack('<H', b[p+4:p+6])[0], bool(pk & 1))
        elif lbl == 0xFF:
            print('app ext', b[p+3:p+14], 'loop', struct.unpack('<H', b[p+16:p+18])[0] if b[p+3:p+14] == b'NETSCAPE2.0' else '')
        p = skip(p + 2)
    elif t == 0x2C:
        x, y, fw, fh, f = struct.unpack('<HHHHB', b[p+1:p+10])
        p += 10 + (3 * (2 << (f & 7)) if f & 0x80 else 0)
        p = skip(p + 1)
        print(f'frame {n}: {fw}x{fh} at {x},{y} LCT={bool(f&0x80)} interlace={bool(f&0x40)} disposal,delay,transp={gce}')
        n += 1; gce = None
    elif t == 0x3B: print('trailer ok'); break
    else: print('bad byte', hex(t), 'at', p); break
