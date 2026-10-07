import { describe, expect, it } from 'vitest';
import { differences, formatBytes, formatCoordinates, formatDimensions, formatDuration, formatFile, formatPlace, formatTaken, hasCoordinates, linkedFiles, mapUrl, matchLabel } from '../../lib/format';
import { photo, video } from '../fixtures';

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

  it('finds none between a file and itself', () => expect(differences(original, original)).toEqual([]));

  it('tells a HEIC from the JPEG beside it by its name, its format and its size', () => {
    const heic = photo({ ...original, fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 });
    expect(differences(original, heic)).toEqual(['Name: the original is IMG_4198.JPG', 'Format: HEIC, the original is JPEG', 'File size: 2.9 MB, the original is 5.8 MB']);
  });

  it('tells an identical copy by where it is and what it is called', () => {
    expect(differences(original, photo({ ...original, folder: 'D:\\backup' }))).toEqual(['Folder: the original is in C:\\photos\\2024']);
    expect(differences(original, photo({ ...original, fileName: 'IMG_4198 (1).JPG' }))).toEqual(['Name: the original is IMG_4198.JPG']);
  });

  it('tells a resized copy by its pixels', () => {
    expect(differences(original, photo({ ...original, width: 1600, height: 1200 }))).toEqual(['Pixels: 1600 × 1200, the original is 4032 × 3024']);
    expect(differences(original, photo({ ...original, height: 3000 }))).toEqual(['Pixels: 4032 × 3000, the original is 4032 × 3024']);
  });

  it('tells a copy that lost its capture date, or carries another', () => {
    expect(differences(original, photo({ ...original, takenOn: undefined }))).toEqual([`Capture date: No capture date, the original: ${formatTaken(original.takenOn)}`]);
    expect(differences(original, photo({ ...original, takenOn: '2024-04-07T17:36:00' }))[0]).toMatch(/^Capture date: /);
  });

  it('passes over sizes too close to show, and names a format it was not told', () => {
    expect(differences(original, photo({ ...original, fileSizeBytes: original.fileSizeBytes + 10 }))).toEqual([]);
    expect(differences(original, photo({ ...original, format: undefined }))).toEqual(['Format: ?, the original is JPEG']);
    expect(differences(photo({ ...original, format: undefined }), original)).toEqual(['Format: JPEG, the original is ?']);
    expect(differences(photo({ ...original, format: undefined }), photo({ ...original, format: undefined }))).toEqual([]);
  });
});

describe('matchLabel', () => {
  it('names each kind of match', () => expect(matchLabel).toEqual({ identical: 'Identical file', samePicture: 'Same picture', similar: 'Similar' }));
});
