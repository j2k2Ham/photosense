import { describe, expect, it } from 'vitest';
import { compareRows, differences, groupSummary, leaf, matchMeaning, sameFolder, timeLeft, formatBytes, formatCoordinates, formatDimensions, formatDuration, formatFile, formatPlace, formatTaken, hasCoordinates, linkedFiles, mapUrl, matchLabel } from '../../lib/format';
import { group, member, photo, video } from '../fixtures';

describe('formatBytes', () => {
  it.each([
    [0, '1 KB'],                       // never shown as nothing
    [900, '1 KB'],
    [150_000, '146 KB'],
    [1024 ** 2 - 1, '1024 KB'],
    [1024 ** 2, '1.0 MB'],
    [6_093_000, '5.8 MB'],
    [1024 ** 3, '1.00 GB'],
    [1_653_000_000, '1.54 GB'],
  ])('shows %d bytes as %s', (bytes, expected) => expect(formatBytes(bytes)).toBe(expected));
});

describe('formatTaken', () => {
  it('says so when the file records no capture date', () => {
    expect(formatTaken(undefined)).toBe('No capture date');
    expect(formatTaken('')).toBe('No capture date');
  });

  it('shows the time as the camera recorded it', () => {
    const shown = formatTaken('2024-04-07T17:35:59');
    expect(shown).toBe(new Date(2024, 3, 7, 17, 35, 59).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'medium' }));
    expect(shown).toContain('2024');
  });

  it('shows what it was given when that is not a date', () => expect(formatTaken('last Tuesday')).toBe('last Tuesday'));
});

describe('formatDimensions', () => {
  it('gives width by height', () => expect(formatDimensions(photo())).toBe('4032 × 3024'));
  it('says the size is unknown for a file that was never decoded', () => expect(formatDimensions(photo({ width: 0, height: 0 }))).toBe('Size unknown'));
});

describe('formatDuration', () => {
  it.each([
    [0, '0:00'],
    [7.4, '0:07'],
    [84, '1:24'],
    [599.6, '10:00'],
    [3600, '1:00:00'],
    [3725, '1:02:05'],
  ])('shows %d seconds as %s', (seconds, expected) => expect(formatDuration(seconds)).toBe(expected));
});

describe('formatFile', () => {
  it('lists size, format and file size for a picture', () => expect(formatFile(photo())).toBe('4032 × 3024 · JPEG · 5.8 MB'));
  it('adds the playing time for a video', () => expect(formatFile(video())).toBe('1920 × 1080 · MOV · 1:24 · 5.8 MB'));
  it('includes a playing time of nothing', () => expect(formatFile(video({ durationSeconds: 0 }))).toContain('· 0:00 ·'));
  it('marks a format it was not told', () => expect(formatFile(photo({ format: undefined, width: 0 }))).toBe('Size unknown · ? · 5.8 MB'));
});

describe('where a picture was taken', () => {
  const buxton = photo({ latitude: 35.2677, longitude: -75.5424 });
  const sydney = photo({ latitude: -33.8568, longitude: 151.2153 });

  it('knows a position only when both halves are there', () => {
    expect(hasCoordinates(buxton)).toBe(true);
    expect(hasCoordinates(photo())).toBe(false);
    expect(hasCoordinates(photo({ latitude: 35.2 }))).toBe(false);
    expect(hasCoordinates(photo({ longitude: -75.5 }))).toBe(false);
    expect(hasCoordinates(photo({ latitude: 0, longitude: 0 }))).toBe(true);
  });

  it('writes coordinates with their hemispheres', () => {
    expect(formatCoordinates(buxton)).toBe('35.2677° N, 75.5424° W');
    expect(formatCoordinates(sydney)).toBe('33.8568° S, 151.2153° E');
    expect(formatCoordinates(photo({ latitude: 0, longitude: 0 }))).toBe('0.0000° N, 0.0000° E');
    expect(formatCoordinates(photo())).toBe('No location in this file');
  });

  it('prefers the name of the place to its coordinates', () => {
    expect(formatPlace(photo({ ...buxton, placeName: 'Buxton, North Carolina, US' }))).toBe('Buxton, North Carolina, US');
    expect(formatPlace(buxton)).toBe('35.2677° N, 75.5424° W');
    expect(formatPlace(photo())).toBe('No location in this file');
  });

  it('links to a map only for a known position', () => {
    expect(mapUrl(buxton)).toBe('https://www.openstreetmap.org/?mlat=35.2677&mlon=-75.5424#map=15/35.2677/-75.5424');
    expect(mapUrl(photo())).toBeUndefined();
  });
});

describe('linkedFiles', () => {
  it.each([
    [0, ''],
    [1, ' and 1 linked file'],
    [3, ' and 3 linked files'],
  ])('describes %d companions as "%s"', (count, expected) => expect(linkedFiles(count)).toBe(expected));
});

describe('differences', () => {
  const original = photo({ takenOn: '2024-04-07T17:35:59' });
  const said = (copy: Parameters<typeof differences>[1]) => differences(original, copy).map(d => `${d.label}: ${d.text}`);

  it('finds none between a file and itself', () => expect(differences(original, original)).toEqual([]));

  it('tells a HEIC from the JPEG beside it by its name, its format and its size', () => {
    expect(said(photo({ ...original, fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 })))
      .toEqual(['Name: the original is IMG_4198.JPG', 'Format: HEIC, the original is JPEG', 'File size: 2.9 MB, the original is 5.8 MB']);
  });

  it('tells an identical copy by where it is and what it is called, naming folders by their last part', () => {
    expect(said(photo({ ...original, folder: "C:\\Users\\jamie\\Pictures\\Jamie's Phone" }))).toEqual(["Folder: Jamie's Phone, the original is in 2024"]);
    expect(said(photo({ ...original, fileName: 'IMG_4198 (1).JPG' }))).toEqual(['Name: the original is IMG_4198.JPG']);
  });

  it('tells a resized copy by its pixels', () => {
    expect(said(photo({ ...original, width: 1600, height: 1200 }))).toEqual(['Pixel size: 1600 × 1200, the original is 4032 × 3024']);
    expect(said(photo({ ...original, height: 3000 }))).toEqual(['Pixel size: 4032 × 3000, the original is 4032 × 3024']);
  });

  it('tells a copy that lost its capture date, or carries another', () => {
    expect(said(photo({ ...original, takenOn: undefined }))).toEqual([`Taken: No capture date, the original: ${formatTaken(original.takenOn)}`]);
    expect(said(photo({ ...original, takenOn: '2024-04-07T17:36:00' }))[0]).toMatch(/^Taken: /);
  });

  it('passes over sizes too close to show, and names a format it was not told', () => {
    expect(said(photo({ ...original, fileSizeBytes: original.fileSizeBytes + 10 }))).toEqual([]);
    expect(said(photo({ ...original, format: undefined }))).toEqual(['Format: ?, the original is JPEG']);
    expect(differences(photo({ ...original, format: undefined }), original).map(d => d.text)).toEqual(['JPEG, the original is ?']);
    expect(differences(photo({ ...original, format: undefined }), photo({ ...original, format: undefined }))).toEqual([]);
  });
});

describe('leaf', () => {
  it.each([
    ['C:\\Users\\jamie\\Phone Pictures', 'Phone Pictures'],
    ['C:\\Users\\jamie\\Phone Pictures\\', 'Phone Pictures'],
    ['/home/jamie/photos', 'photos'],
    ['C:\\', 'C:'],
    ['', ''],
  ])('names %j by its last part, %j', (path, name) => expect(leaf(path)).toBe(name));
});

describe('timeLeft', () => {
  it('says so while the service has nothing to go by', () => {
    expect(timeLeft()).toBe('Working out how long this will take');
    expect(timeLeft(null)).toBe('Working out how long this will take');
  });

  it.each([
    [0, 'Less than a minute left'], [44, 'Less than a minute left'], [45, 'About 1 minute left'], [89, 'About 1 minute left'],
    [90, 'About 2 minutes left'], [780, 'About 13 minutes left'], [3569, 'About 59 minutes left'],
    [3570, 'About 1 h 0 min left'], [5400, 'About 1 h 30 min left'], [9000, 'About 2 h 30 min left'],
  ])('puts %d seconds into words', (seconds, words) => {
    expect(timeLeft(seconds)).toBe(words);
  });
});

describe('sameFolder', () => {
  it('takes two spellings of one Windows folder for the same folder', () => {
    expect(sameFolder('C:\\Users\\jamie\\Phone Pictures', ' c:\\users\\jamie\\phone pictures\\ ')).toBe(true);
    expect(sameFolder('\\\\NAS\\Photos', '\\\\nas\\photos/')).toBe(true);
    expect(sameFolder('C:\\', 'c:')).toBe(true);
    expect(sameFolder('C:\\photos', 'C:\\photos\\2024')).toBe(false);
  });

  it('tells folders apart by case where the system does', () => {
    expect(sameFolder('/home/jamie/Photos', '/home/jamie/Photos/')).toBe(true);
    expect(sameFolder('/home/jamie/Photos', '/home/jamie/photos')).toBe(false);
  });

  it('does not take two empty boxes for a folder given twice', () => {
    expect(sameFolder('', '  ')).toBe(false);
    expect(sameFolder('C:\\photos', '')).toBe(false);
  });
});

describe('compareRows', () => {
  const original = photo({ takenOn: '2024-04-07T17:35:59', latitude: 35.2677, longitude: -75.5424, placeName: 'Buxton, North Carolina, US', cameraModel: 'iPhone 12 Pro Max' });

  it('puts what differs first and marks it', () => {
    const rows = compareRows(original, photo({ ...original, fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 }));
    expect(rows.map(r => r.label)).toEqual(['Name', 'Format', 'File size', 'Folder', 'Taken', 'Place', 'Pixel size', 'Camera']);
    expect(rows.filter(r => r.differs).map(r => [r.label, r.original, r.copy])).toEqual([
      ['Name', 'IMG_4198.JPG', 'IMG_4198.HEIC'], ['Format', 'JPEG', 'HEIC'], ['File size', '5.8 MB', '2.9 MB'],
    ]);
  });

  it('gives a path its own face and a place its map', () => {
    const rows = compareRows(original, photo({ ...original, folder: 'D:\\backup', cameraModel: undefined }));
    expect(rows.map(r => r.label).slice(0, 2)).toEqual(['Folder', 'Camera']);
    expect(rows.find(r => r.label === 'Folder')).toMatchObject({ mono: true, original: 'C:\\photos\\2024', copy: 'D:\\backup' });
    expect(rows.find(r => r.label === 'Camera')).toMatchObject({ original: 'iPhone 12 Pro Max', copy: 'Not recorded' });
    expect(rows.find(r => r.label === 'Place')?.map).toContain('openstreetmap.org');
    expect(compareRows(photo(), photo()).find(r => r.label === 'Place')?.map).toBeUndefined();
    expect(compareRows(photo({ format: undefined }), photo({ format: undefined })).find(r => r.label === 'Format')).toMatchObject({ original: '?', differs: false });
  });

  it('gives a video its playing time beside its size', () => {
    const rows = compareRows(video(), video({ id: 'v2', durationSeconds: 85 }));
    expect(rows[0]).toMatchObject({ label: 'Video', original: '1920 × 1080 · 1:24', copy: '1920 × 1080 · 1:25', differs: true });
  });
});

describe('groupSummary', () => {
  const copies = (...kept: boolean[]) => kept.map((k, n) => member({ id: `m${n}`, kept: k }));
  it.each([
    [copies(false), 'duplicates', '1 duplicate · 5.8 MB', false],
    [copies(false, true, false), 'duplicates', '2 duplicates · 5.8 MB', false],
    [copies(true), 'duplicates', '1 copy, all marked keep', true],
    [copies(true, true), 'duplicates', '2 copies, all marked keep', true],
    [copies(false), 'similar', '1 similar shot', false],
    [copies(false, true), 'similar', '2 similar shots', false],
  ] as const)('sums up a group', (members, mode, text, allKept) => {
    expect(groupSummary(group({ members: [...members] }), mode)).toEqual({ text, allKept });
  });
});

describe('matchLabel', () => {
  it('names each kind of match', () => expect(matchLabel).toEqual({ identical: 'Identical file', samePicture: 'Same picture', similar: 'Similar' }));
  it('says what each kind means', () => expect(Object.keys(matchMeaning)).toEqual(['identical', 'samePicture', 'similar']));
});
