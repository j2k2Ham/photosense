# Which tags differ between an original and its edited render, but never between a HEIC and its JPEG copy?
import json, sys, collections, re
exec(open(sys.argv[3], encoding='utf-8').read().split("paths = sorted(pix)")[0])
paths = sorted(pix)
H = {p: int(pix[p]['dhash'], 16) for p in paths}
def edited(p): return bool(re.match(r'IMG_E\d', p.replace('\\', '/').split('/')[-1], re.I))
def agree(a, b): return (H[a] ^ H[b]).bit_count() <= 8 and aspect_ok(pix[a], pix[b]) and diff(pix[a], pix[b]) <= 0.5
f = KEYS['DateTimeOriginal+SubSec']
groups = collections.defaultdict(list)
for p in paths:
    v = f(meta[p])
    if v is not None: groups[v].append(p)
pairs = [(ps[i], ps[j]) for ps in groups.values() for i in range(len(ps)) for j in range(i + 1, len(ps))]
copies = [(a, b) for a, b in pairs if ext(a) != ext(b) and not edited(a) and not edited(b) and agree(a, b)]
edits = [(a, b) for a, b in pairs if edited(a) != edited(b)]
def vals(r):
    d = {}
    for t in r['tags']:
        if t['d'] in ('File Type', 'Huffman', 'JPEG', 'JFIF', 'QuickTime File Type', 'HEIC Primary Item Properties', 'HEIC Thumbnail Properties', 'HEIC Thumbnail Data', 'Exif Thumbnail', 'XMP'): continue
        k = (t['d'], t['n'] if t['d'] == 'XMPP' else t['id'])
        d.setdefault(k, t['v'])
    return d
def tally(ps):
    c = collections.Counter()
    for a, b in ps:
        va, vb = vals(meta[a]), vals(meta[b])
        for k in set(va) | set(vb):
            c[(k, 'differ' if va.get(k) != vb.get(k) and k in va and k in vb else 'one lacks' if (k in va) != (k in vb) else 'equal')] += 1
    return c
cc, ce = tally(copies), tally(edits)
print(f"copies (HEIC-JPEG, no edit name, pixels agree): {len(copies)}; original vs edited render: {len(edits)}")
keys = {k for k, _ in ce}
rows = []
for k in keys:
    ed = ce[(k, 'differ')] + ce[(k, 'one lacks')]
    cd = cc[(k, 'differ')] + cc[(k, 'one lacks')]
    if ed >= len(edits) * 0.3: rows.append((ed, cd, k))
for ed, cd, k in sorted(rows, reverse=True)[:30]:
    print(f"  {str(k)[:70]:70} edits differ {ed:4}/{len(edits)}  copies differ {cd:4}/{len(copies)}")
aae = sum(1 for a, b in edits)
print('original has a .AAE beside it, among originals in edit pairs:')
import os
root = sys.argv[4]
c = collections.Counter()
for a, b in edits:
    o = b if edited(a) else a
    stem = os.path.splitext(o)[0]
    c[os.path.exists(os.path.join(root, stem + '.AAE')) or os.path.exists(os.path.join(root, stem + '.aae'))] += 1
print(' ', c)
