# Pairs HEIC and JPEG files by their pixels (Magick.NET, 32x32 grey signature and 64-bit dHash),
# then counts, for each metadata key, how often pairs share it. Prints counts only, never values or names.
import json, sys, collections, hashlib

meta = {}
for l in open(sys.argv[1], encoding='utf-8'):
    r = json.loads(l); meta[r['path']] = r
pix = {}
for l in open(sys.argv[2], encoding='utf-8'):
    r = json.loads(l)
    if 'sig' in r: pix[r['path']] = r

def ext(p): return 'HEIC' if p.upper().endswith('.HEIC') else 'JPG'

def tag(r, d, i=None, n=None):
    for t in r['tags']:
        if t['d'] == d and (i is None or t['id'] == i) and (n is None or t['n'] == n):
            return t['v']
    return None

def apple(r, i): return tag(r, 'Apple Makernote', i)
def sub(r, i): return tag(r, 'Exif SubIFD', i)

def runtime(r):
    vals = [f"{t['n']}={t['v']}" for t in r['tags'] if t['d'] == 'Apple Run Time']
    return '|'.join(sorted(vals)) or None

def join(*xs):
    return None if any(x is None for x in xs) else '|'.join(map(str, xs))

KEYS = {
    'ContentIdentifier (0x0011)': lambda r: apple(r, 0x11),
    'ImageCaptureRequestID (0x0020)': lambda r: apple(r, 0x20),
    'PhotoIdentifier (0x002b)': lambda r: apple(r, 0x2b),
    'BurstUUID (0x000b)': lambda r: apple(r, 0x0b),
    'LivePhotoVideoIndex (0x0017)': lambda r: apple(r, 0x17),
    'RunTime (0x0003)': runtime,
    'AccelerationVector (0x0008)': lambda r: apple(r, 0x08),
    'DateTimeOriginal': lambda r: sub(r, 0x9003),
    'DateTimeOriginal+SubSec': lambda r: join(sub(r, 0x9003), sub(r, 0x9291)),
    'DateTimeOriginal+SubSec+Offset': lambda r: join(sub(r, 0x9003), sub(r, 0x9291), sub(r, 0x9011)),
    'GPS lat+lon': lambda r: join(tag(r, 'GPS', 2), tag(r, 'GPS', 4)),
    'Exposure (time,f,ISO,focal,brightness)': lambda r: join(sub(r, 0x829a), sub(r, 0x829d), sub(r, 0x8827), sub(r, 0x920a), sub(r, 0x9203)),
    'Lens model': lambda r: sub(r, 0xa434),
    'XMP photoshop:DateCreated': lambda r: tag(r, 'XMPP', n='photoshop:DateCreated'),
}

def aspect_ok(a, b):
    ra, rb = a['w'] / a['h'], b['w'] / b['h']
    return abs(ra - rb) / max(ra, rb) <= 0.02

def diff(a, b):
    sa, sb = bytes.fromhex(a['sig']), bytes.fromhex(b['sig'])
    return sum(abs(x - y) for x, y in zip(sa, sb)) / len(sa)

paths = sorted(pix)
heics = [p for p in paths if ext(p) == 'HEIC']
jpgs = [p for p in paths if ext(p) == 'JPG']
H = {p: int(pix[p]['dhash'], 16) for p in paths}

def near_pairs(A, B, same_set=False):
    out = []
    for i, a in enumerate(A):
        ha = H[a]
        for b in (B[i + 1:] if same_set else B):
            if (ha ^ H[b]).bit_count() <= 8 and aspect_ok(pix[a], pix[b]):
                d = diff(pix[a], pix[b])
                if d <= 2.0: out.append((a, b, d))
    return out

def identical(a, b):
    return meta[a]['size'] == meta[b]['size'] and pix[a]['sig'] == pix[b]['sig']

hj = near_pairs(heics, jpgs)
same = [(a, b, d) for a, b, d in hj if d <= 0.5]
near = [(a, b, d) for a, b, d in hj if d > 0.5]
print(f"decoded: {len(heics)} HEIC, {len(jpgs)} JPEG; HEIC-JPEG pairs within 8 bits, same shape, <=2.0 grey: {len(hj)}")
print(f"  same picture by pixels (<=0.5): {len(same)}; look-alike only (0.5-2.0): {len(near)}")
ds = sorted(d for _, _, d in same)
if ds: print(f"  same-picture differences: min {ds[0]:.2f}, median {ds[len(ds)//2]:.2f}, max {ds[-1]:.2f}")
hs = collections.Counter(a for a, _, _ in same); js = collections.Counter(b for _, b, _ in same)
print(f"  distinct HEICs {len(hs)}, distinct JPEGs {len(js)}; HEICs with >1 JPEG {sum(1 for v in hs.values() if v>1)}; JPEGs with >1 HEIC {sum(1 for v in js.values() if v>1)}")

def tally(pairs, label):
    print(f"\n== {label} ({len(pairs)} pairs): equal / differ / one side lacks / both lack")
    for k, f in KEYS.items():
        c = collections.Counter()
        for a, b, _ in pairs:
            va, vb = f(meta[a]), f(meta[b])
            c['equal' if va is not None and va == vb else 'differ' if va is not None and vb is not None else 'both lack' if va is None and vb is None else 'one lacks'] += 1
        print(f"  {k:42} {c['equal']:5} {c['differ']:5} {c['one lacks']:5} {c['both lack']:5}")

tally(same, 'HEIC-JPEG, same picture by pixels')
tally(near, 'HEIC-JPEG, look-alike only (burst frames, retakes)')

# Which JPEGs in same-picture pairs look like which route.
def profile(j, h):
    r = meta[j]; hr = pix[h]; jr = pix[j]
    full = (jr['w'], jr['h']) == (hr['w'], hr['h'])
    return (
        'full size' if full else 'smaller' if jr['w'] * jr['h'] < hr['w'] * hr['h'] else 'larger',
        'Exif' if tag(r, 'Exif SubIFD', 0x9003) else 'no Exif date',
        'Apple makernote' if any(t['d'] == 'Apple Makernote' for t in r['tags']) else 'no makernote',
        'XMP' if any(t['d'] == 'XMP' for t in r['tags']) else 'no XMP',
        'JFIF' if any(t['d'] == 'JFIF' for t in r['tags']) else 'no JFIF',
        'ICC' if any(t['d'] == 'ICC Profile' for t in r['tags']) else 'no ICC',
    )
print("\n== JPEG side of same-picture pairs, by what it carries")
for p, n in collections.Counter(profile(b, a) for a, b, _ in same).most_common():
    print(f"  {n:5}  {', '.join(p)}")

# Exact-value false ties across the whole library: pairs sharing a value that pixels do not call the same picture.
same_set = {(a, b) for a, b, _ in same}
near_set = {(a, b) for a, b, _ in near}
print("\n== Every HEIC-JPEG pair sharing the value: same picture / look-alike / unrelated by pixels")
for k, f in KEYS.items():
    idx = collections.defaultdict(list)
    for j in jpgs:
        v = f(meta[j])
        if v is not None: idx[v].append(j)
    c = collections.Counter()
    for h in heics:
        v = f(meta[h])
        if v is None: continue
        for j in idx.get(v, []):
            c['same' if (h, j) in same_set else 'look-alike' if (h, j) in near_set else 'unrelated'] += 1
    print(f"  {k:42} {c['same']:6} {c['look-alike']:6} {c['unrelated']:7}")

print("\n== Same-format pairs sharing the value (byte-identical copies left out): same / look-alike / unrelated by pixels")
for kind, group in (('HEIC-HEIC', heics), ('JPEG-JPEG', jpgs)):
    for k, f in KEYS.items():
        idx = collections.defaultdict(list)
        for p in group:
            v = f(meta[p])
            if v is not None: idx[v].append(p)
        c = collections.Counter()
        for v, ps in idx.items():
            for i in range(len(ps)):
                for j in range(i + 1, len(ps)):
                    a, b = ps[i], ps[j]
                    if identical(a, b): c['identical'] += 1; continue
                    if (H[a] ^ H[b]).bit_count() <= 8 and aspect_ok(pix[a], pix[b]):
                        d = diff(pix[a], pix[b]); c['same' if d <= 0.5 else 'look-alike' if d <= 2.0 else 'unrelated'] += 1
                    else: c['unrelated'] += 1
        print(f"  {kind} {k:42} same {c['same']:5} look-alike {c['look-alike']:5} unrelated {c['unrelated']:6} (identical files {c['identical']})")
