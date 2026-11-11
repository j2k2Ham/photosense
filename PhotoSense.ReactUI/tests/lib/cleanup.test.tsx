import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  allGone, allQuestion, copyGone, copyQuestion, detailRows, duplicates, keepSaid, matchNote, scannedLine, useCleanUp, whereOf,
} from '../../lib/cleanup';
import { WORDS } from '../../lib/phone';
import type { DuplicateGroupDto, GroupMode } from '../../types';
import { group, groupsPage, photo } from '../components/phone/groups';

const api = vi.hoisted(() => ({ useGroupsThrough: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const ios = WORDS.ios, android = WORDS.android;
const where = (p: { folder: string }) => whereOf(p as never, ROOT, ios);

describe('what Clean up says on a phone', () => {
  it('counts duplicates, and says where a file is in the platform\'s words, or by its folder\'s name until the library is known', () => {
    expect([duplicates(1), duplicates(1264)]).toEqual(['1 duplicate', '1,264 duplicates']);
    const inButte = photo('IMG_1.JPG', { folder: `${ROOT}/Trips/Butte` });
    expect(whereOf(inButte, ROOT, ios)).toBe('Photo Library › Trips › Butte');
    expect(whereOf(photo('IMG_1.JPG'), ROOT, android)).toBe('Phone');
    expect(whereOf(inButte, undefined, ios)).toBe('Butte');
  });

  it('says why a copy is in its group', () => {
    const keeper = photo('IMG_1.JPG');
    const note = (copy = photo('IMG_1.HEIC'), match: 'identical' | 'samePicture' | 'similar' = 'samePicture', copies = [copy]) => matchNote(group(keeper, copies, match), group(keeper, copies, match).members[0]);
    expect(note(photo('IMG_1 (2).JPG'), 'identical')).toBe('Same bytes, saved twice.');
    expect(note(photo('a.JPG'), 'identical', [photo('a.JPG'), photo('b.JPG')])).toBe('Same bytes, saved three times.');
    expect(note(photo('a.JPG'), 'identical', [photo('a.JPG'), photo('b.JPG'), photo('c.JPG')])).toBe('Same bytes, saved four times.');
    expect(note(photo('a.JPG'), 'identical', ['a', 'b', 'c', 'd'].map(n => photo(`${n}.JPG`)))).toBe('Same bytes, saved 5 times.');
    expect(note(photo('IMG_2.JPG'), 'similar')).toBe('A burst frame or an edited version.');
    expect(note(photo('IMG_1.HEIC', { width: 1000 }))).toBe('Converted and resized.');
    expect(note()).toBe('The same picture, as JPG and as HEIC.');
    expect(note(photo('IMG_1 small.JPG', { height: 10 }))).toBe('The same picture, resized.');
    expect(note(photo('IMG_1 again.JPG'))).toBe('The same picture, saved again.');
    // A file whose format could not be told is "?", as everywhere else.
    expect(note(photo('IMG_1', { format: undefined }))).toBe('The same picture, as JPG and as ?.');
    expect(matchNote(group(photo('X', { format: undefined }), [photo('Y.JPG')]), group(photo('X', { format: undefined }), [photo('Y.JPG')]).members[0])).toBe('The same picture, as ? and as JPG.');
  });

  it('compares the two files detail by detail, in the design\'s order, the names and places in the code face', () => {
    const keeper = photo('IMG_4299.JPG', { fileSizeBytes: 24 * 1024 ** 2 });
    const copy = photo('IMG_4299.HEIC', { folder: `${ROOT}/Butte`, takenOn: undefined, placeName: undefined, format: undefined });
    expect(detailRows(keeper, copy, ROOT, ios)).toEqual([
      { label: 'Name', original: 'IMG_4299.JPG', copy: 'IMG_4299.HEIC', differs: true, code: true },
      { label: 'Format', original: 'JPG', copy: '?', differs: true, code: false },
      { label: 'File size', original: '24.0 MB', copy: '12.0 MB', differs: true, code: false },
      { label: 'Album', original: 'Photo Library', copy: 'Photo Library › Butte', differs: true, code: true },
      { label: 'Taken', original: 'Dec 27, 2023, 11:34 PM', copy: 'No date', differs: true, code: false },
      { label: 'Place', original: 'Anaconda, Montana, US', copy: 'No location', differs: true, code: false },
      { label: 'Pixel size', original: '4032 × 3024', copy: '4032 × 3024', differs: false, code: false },
    ]);
    const video = photo('DLWL2718.MP4', { isVideo: true, width: 1920, height: 1080, durationSeconds: 47 });
    expect(detailRows(video, video, ROOT, android).slice(3)).toEqual([
      { label: 'Folder', original: 'Phone', copy: 'Phone', differs: false, code: true },
      { label: 'Taken', original: 'Dec 27, 2023, 11:34 PM', copy: 'Dec 27, 2023, 11:34 PM', differs: false, code: false },
      { label: 'Place', original: 'Anaconda, Montana, US', copy: 'Anaconda, Montana, US', differs: false, code: false },
      { label: 'Video', original: '1920 × 1080 · 0:47', copy: '1920 × 1080 · 0:47', differs: false, code: false },
    ]);
  });

  it('says how much the last scan took in, and when: today, yesterday, or on a date, with the year when it was another', () => {
    const now = new Date(2026, 10, 11, 9, 0);
    const at = (...d: [number, number, number, number, number]) => new Date(...d).toISOString();
    expect(scannedLine(6941, at(2026, 10, 11, 16, 25), now)).toBe('Scanned 6,941 photos and videos today at 4:25 PM.');
    expect(scannedLine(1, at(2026, 10, 10, 0, 5), now)).toBe('Scanned 1 photo or video yesterday at 12:05 AM.');
    expect(scannedLine(2, at(2026, 9, 28, 12, 0), now)).toBe('Scanned 2 photos and videos on Oct 28 at 12:00 PM.');
    expect(scannedLine(2, at(2025, 11, 31, 11, 59), now)).toBe('Scanned 2 photos and videos on Dec 31, 2025 at 11:59 AM.');
  });

  it('asks before one copy goes, naming the file that stays and the file that goes, each with where it is', () => {
    const keeper = photo('IMG_4299.JPG'), copy = photo('IMG_4299.HEIC');
    expect(copyQuestion(copy, keeper, where, ios)).toEqual({
      title: 'Delete IMG_4299.HEIC?',
      lines: ['IMG_4299.JPG stays, in Photo Library.', 'IMG_4299.HEIC, in Photo Library, goes to PhotoSense Removed, where Undo can still bring it back.'],
      button: 'Delete IMG_4299.HEIC',
    });
  });

  it('asks before every duplicate goes, with what they take', () => {
    expect(allQuestion(264, 795 * 1024 ** 2, android)).toEqual({
      title: 'Delete all duplicates?',
      lines: ['264 copies go to Pictures/PhotoSense Removed (795.0 MB).', 'The best copy of each picture or video stays. Copies marked keep are skipped.', 'Similar shots are not included.'],
      button: 'Delete 264 duplicates',
    });
    expect(allQuestion(1, 1024, ios).lines[0]).toBe('1 copy goes to PhotoSense Removed (1 KB).');
    expect(allQuestion(1, 1024, ios).button).toBe('Delete 1 duplicate');
  });

  it('says what went and what stays; two files of one name by where each is', () => {
    expect(copyGone(photo('IMG_4299.HEIC'), photo('IMG_4299.JPG'), 0, where)).toBe('IMG_4299.HEIC deleted. IMG_4299.JPG stays.');
    expect(copyGone(photo('IMG_1.JPG', { folder: `${ROOT}/Butte` }), photo('img_1.jpg'), 1, where))
      .toBe('IMG_1.JPG and 1 linked file, in Photo Library › Butte, deleted. The one in Photo Library stays.');
    expect([allGone(264, 0), allGone(1, 2)]).toEqual(['264 duplicates deleted. The best copy of each stays.', '1 duplicate and 2 linked files deleted. The best copy of each stays.']);
  });

  it('says when a copy is marked keep, and what that means for each kind', () => {
    const p = photo('IMG_0377 (2C9F41A0).JPG');
    expect(keepSaid(p, true, 'duplicates')).toBe('IMG_0377 (2C9F41A0).JPG is marked keep. Delete all duplicates skips it.');
    expect(keepSaid(p, true, 'similar')).toBe('IMG_0377 (2C9F41A0).JPG is marked keep.');
    expect(keepSaid(p, false, 'duplicates')).toBe('IMG_0377 (2C9F41A0).JPG is no longer marked keep.');
  });
});

describe('Clean up\'s state on a phone or tablet', () => {
  let groups: Record<GroupMode, DuplicateGroupDto[]>;
  let totalPages: number;
  // What the service has: the groups of the kind asked for, unless a test says it is still reading.
  let reading: GroupMode | undefined;
  beforeEach(() => {
    groups = {
      duplicates: [group(photo('A.JPG'), [photo('A.HEIC'), photo('A 2.HEIC')]), group(photo('B.JPG'), [photo('B.HEIC')]), group(photo('C.JPG'), [photo('C.HEIC')])],
      similar: [group(photo('S.JPG'), [photo('S2.JPG')], 'similar')],
    };
    totalPages = 1;
    reading = undefined;
    api.useGroupsThrough.mockReset();
    api.useGroupsThrough.mockImplementation((mode: GroupMode) => ({ data: groupsPage(reading === mode ? (mode === 'duplicates' ? 'similar' : 'duplicates') : mode, groups[mode], { totalPages }) }));
  });

  it('on a phone reviews only the group tapped, and goes back to the list when that group has gone', () => {
    const { result, rerender } = renderHook(() => useCleanUp(false));
    expect([result.current.mode, result.current.items.length, result.current.group, result.current.copy]).toEqual(['duplicates', 3, undefined, 0]);
    act(() => result.current.review('group-B.JPG'));
    expect(result.current.group?.keeper.fileName).toBe('B.JPG');
    groups.duplicates = groups.duplicates.filter(g => g.key !== 'group-B.JPG');
    rerender();
    expect(result.current.group).toBeUndefined();
    // Undo brings it back; the list stays as it is.
    groups.duplicates.splice(1, 0, group(photo('B.JPG'), [photo('B.HEIC')]));
    rerender();
    expect(result.current.group).toBeUndefined();
  });

  it('on a tablet shows the first group, and when the one shown goes, the one that took its place', () => {
    const { result, rerender } = renderHook(() => useCleanUp(true));
    expect(result.current.group?.keeper.fileName).toBe('A.JPG');
    act(() => result.current.review('group-B.JPG'));
    groups.duplicates = groups.duplicates.filter(g => g.key !== 'group-B.JPG');
    rerender();
    expect(result.current.group?.keeper.fileName).toBe('C.JPG');
    groups.duplicates = [];
    rerender();
    expect(result.current.group).toBeUndefined();
  });

  it('keeps the copy chosen within its group, and starts each review and each kind from the first', () => {
    const { result, rerender } = renderHook(() => useCleanUp(true));
    act(() => result.current.chooseCopy(1));
    expect(result.current.copy).toBe(1);
    groups.duplicates[0] = group(photo('A.JPG'), [photo('A.HEIC')]);
    rerender();
    expect(result.current.copy).toBe(0);
    act(() => result.current.chooseCopy(1));
    act(() => result.current.review('group-C.JPG'));
    expect(result.current.copy).toBe(0);
    act(() => result.current.turnTo('similar'));
    expect([result.current.mode, result.current.group?.keeper.fileName, result.current.copy]).toEqual(['similar', 'S.JPG', 0]);
  });

  it('shows none of the other kind\'s groups while the kind turned to is being read', () => {
    reading = 'similar';
    const { result } = renderHook(() => useCleanUp(true));
    act(() => result.current.turnTo('similar'));
    expect([result.current.data, result.current.items, result.current.group, result.current.more]).toEqual([undefined, [], undefined, false]);
  });

  it('reads a page more at a time while there are more', () => {
    totalPages = 2;
    const { result } = renderHook(() => useCleanUp(false));
    expect(result.current.more).toBe(true);
    const showMore = result.current.showMore;
    act(() => result.current.showMore());
    expect(api.useGroupsThrough).toHaveBeenLastCalledWith('duplicates', 2);
    expect(result.current.more).toBe(false);
    // The same each time, as the list watches for its end by it.
    expect(result.current.showMore).toBe(showMore);
  });
});
