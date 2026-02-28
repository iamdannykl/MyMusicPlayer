"""生成一个简单的音乐播放器图标 (1024x1024 PNG)"""
import struct, zlib, os

def png(w, h, pixels_rgba):
    def chunk(name, data):
        c = zlib.crc32(name + data) & 0xFFFFFFFF
        return struct.pack('>I', len(data)) + name + data + struct.pack('>I', c)
    sig = b'\x89PNG\r\n\x1a\n'
    ihdr = chunk(b'IHDR', struct.pack('>IIBBBBB', w, h, 8, 2, 0, 0, 0))
    raw = b''
    for row in range(h):
        raw += b'\x00'
        for col in range(w):
            raw += bytes(pixels_rgba[row * w + col][:3])
    idat = chunk(b'IDAT', zlib.compress(raw, 9))
    iend = chunk(b'IEND', b'')
    return sig + ihdr + idat + iend

S = 1024
pixels = []
cx, cy = S//2, S//2
for y in range(S):
    for x in range(S):
        dx, dy = x - cx, y - cy
        dist = (dx*dx + dy*dy) ** 0.5
        # background gradient: deep purple
        r = int(max(0, min(255, 20 + (x/S)*60)))
        g = int(max(0, min(255, 10 + (y/S)*20)))
        b = int(max(0, min(255, 80 + (x/S)*80)))
        # circle border
        if abs(dist - S*0.44) < S*0.03:
            r, g, b = 107, 53, 245
        # play triangle
        tx, ty = x - cx - S*0.04, y - cy
        if -S*0.22 < ty < S*0.22 and tx > -S*0.14 and tx < S*0.22 - abs(ty)*1.1:
            r, g, b = 255, 255, 255
        pixels.append((r, g, b, 255))

out = os.path.join(os.path.dirname(__file__), 'AppIcon.png')
with open(out, 'wb') as f:
    f.write(png(S, S, pixels))
print('written', out)

