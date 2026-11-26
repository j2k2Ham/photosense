# For pairs that share the capture moment (DateTimeOriginal+SubSec) or PhotoIdentifier, how well each
# guard keeps the true copies (pixels agree) and drops the edits (pixels disagree). Counts only.
import json, sys, collections, re
exec(open(sys.argv[3], encoding='utf-8').read().split("paths = sorted(pix)")[0])
paths = sorted(pix)
H = {p: int(pix[p]['dhash'], 16) for p in paths}
def edited(p): return bool(re.match(r'IMG_E\d', p.replace('\\', '/').split('/')[-1], re.I))
def agree(a, b):
    return (H[a] ^ H[b]).bit_count() <= 8 and aspect_ok(pix[a], pix[b]) and diff(pix[a], pix[b]) <= 0.5
def ifd0(r, i): return tag(r, 'Exif IFD0', i)
def eq(f):
    return lambda a, b: f(meta[a]) == f(meta[b])
GUARDS = {
    'neither or both an edited-render name': lambda a, b: edited(a) == edited(b),
    'ModifyDate (IFD0 DateTime) equal': eq(lambda r: ifd0(r, 0x132)),
    'CustomRendered equal': eq(lambda r: sub(r, 0xa401)),
    'Exif dims equal': eq(lambda r: join(sub(r, 0xa002), sub(r, 0xa003))),
    'Software equal': eq(lambda r: ifd0(r, 0x131)),
    'ModifyDate+CustomRendered+dims equal': lambda a, b: all(GUARDS[g](a, b) for g in ('ModifyDate (IFD0 DateTime) equal', 'CustomRendered equal', 'Exif dims equal')),
}
for keyname in ('PhotoIdentifier (0x002b)', 'DateTimeOriginal+SubSec'):
    f = KEYS[keyname]
    groups = collections.defaultdict(list)
    for p in paths:
        v = f(meta[p])
        if v is not None: groups[v].append(p)
    pairs = [(ps[i], ps[j]) for ps in groups.values() for i in range(len(ps)) for j in range(i + 1, len(ps))]
    for scope in ('HEIC-JPEG', 'all formats'):
        ps = [(a, b) for a, b in pairs if scope == 'all formats' or ext(a) != ext(b)]
        ag = [(a, b) for a, b in ps if agree(a, b)]
        dis = [(a, b) for a, b in ps if not agree(a, b)]
        print(f"\n== sharing {keyname}, {scope}: {len(ag)} pixels agree, {len(dis)} disagree")
        for g, gf in GUARDS.items():
            print(f"  {g:42} keeps {sum(1 for a, b in ag if gf(a, b)):4} of {len(ag):4}; lets through {sum(1 for a, b in dis if gf(a, b)):4} of {len(dis):4}")
same_pairs = [(a, b) for a in paths if ext(a) == 'HEIC' for b in paths if ext(b) == 'JPG' and agree(a, b)]
print('\nsame-picture HEIC-JPEG pairs with an edited-render name: one side', sum(1 for a, b in same_pairs if edited(a) != edited(b)), 'both', sum(1 for a, b in same_pairs if edited(a) and edited(b)))
