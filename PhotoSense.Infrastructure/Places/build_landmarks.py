"""Builds landmarks.tsv.gz from the GeoNames allCountries dump: the sights, parks and districts a picture
can be said to be taken AT, each with how far its name reaches. See PhotoSense.Infrastructure/Places/README.md.

Usage: python build_landmarks.py allCountries.zip landmarks.tsv.gz
"""
import collections, gzip, io, re, sys, zipfile

# Feature code -> reach in metres. A point stands for the whole feature, so the reach is kept short:
# within it a picture was taken at the place, not merely near it.
REACH = {
    'S.MNMT': 400, 'S.MUS': 300, 'S.CSTL': 400, 'S.PAL': 400, 'S.LTHSE': 400, 'S.STDM': 400, 'S.ZOO': 500,
    'L.AMUS': 700, 'S.THTR': 200, 'S.OPRA': 200, 'S.PYR': 500, 'S.ARCH': 300, 'S.RUIN': 300, 'S.HSTS': 300,
    'S.ANS': 400, 'S.GDN': 300, 'S.OBPT': 300, 'S.PIER': 300, 'S.SQR': 200, 'S.RSRT': 800, 'S.TMPL': 300,
    'H.FLLS': 400, 'H.GYSR': 300, 'T.BCH': 500, 'L.PRK': 300, 'P.PPLX': 1000,
}
# The United States list files statues, plaques and ball fields as parks; these are not places to name a folder after.
MINOR = re.compile(r'\b(statue|marker|plaque|field|fields|playground|stand|plaza|mall|parking|lot|triangle|circle|strip|median|fountain|courts?|pool|ballpark|diamond|tot ?lot)\b|\(historical\)', re.I)

src, out = sys.argv[1], sys.argv[2]
rows, kinds = set(), collections.Counter()
with zipfile.ZipFile(src) as z, z.open('allCountries.txt') as raw:
    for line in io.TextIOWrapper(raw, encoding='utf-8'):
        p = line.split('\t')
        code = p[6] + '.' + p[7]
        name = p[1].strip()
        # Geysers are filed with the springs.
        reach = 300 if code == 'H.SPNG' and re.search(r'\bgeyser\b', name, re.I) else REACH.get(code)
        if reach is None or not name or '\t' in name or MINOR.search(name):
            continue
        rows.add(f'{name}\t{float(p[4]):.4f}\t{float(p[5]):.4f}\t{reach}')
        kinds[code] += 1

with gzip.GzipFile(out, 'wb', compresslevel=9, mtime=0) as f:
    f.write('\n'.join(sorted(rows)).encode('utf-8'))
print(len(rows), 'landmarks')
for k, n in kinds.most_common():
    print(' ', k, n)
