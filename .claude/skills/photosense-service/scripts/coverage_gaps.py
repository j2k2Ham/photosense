# The totals of the newest coverlet (OpenCover) report in a folder, and every line and branch it has not covered, by file.
# python -I .claude/skills/photosense-service/scripts/coverage_gaps.py PhotoSense.Tests/coverage
import glob
import os
import sys
import xml.etree.ElementTree as ET

folder = sys.argv[1] if len(sys.argv) > 1 else 'PhotoSense.Tests/coverage'
reports = glob.glob(os.path.join(folder, '*.xml'))
if not reports:
    sys.exit(f'No coverage report in {folder}: run the tests with --coverlet first.')
newest = max(reports, key=os.path.getmtime)
root = ET.parse(newest).getroot()
files = {f.get('uid'): f.get('fullPath') for f in root.iter('File')}
lines = branches = lines_hit = branches_hit = 0
gaps = {}
for method in root.iter('Method'):
    for point in method.iter('SequencePoint'):
        lines += 1
        if int(point.get('vc')) > 0:
            lines_hit += 1
        else:
            gaps.setdefault(files.get(point.get('fileid')), set()).add(('line', int(point.get('sl'))))
    for point in method.iter('BranchPoint'):
        branches += 1
        if int(point.get('vc')) > 0:
            branches_hit += 1
        else:
            gaps.setdefault(files.get(point.get('fileid')), set()).add(('branch', int(point.get('sl'))))
print(os.path.basename(newest))
print(f'lines {lines_hit}/{lines}, branches {branches_hit}/{branches}')
for path, missing in sorted(gaps.items(), key=lambda g: str(g[0])):
    print(path, ', '.join(f'{kind} {line}' for kind, line in sorted(missing, key=lambda m: m[1])))
