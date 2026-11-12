import { renderHook } from '@testing-library/react';
import React from 'react';
import { SWRConfig } from 'swr';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  albumsOf, byMonth, deleteQuestion, filterCounts, isUnsorted, items, latestFirst, monthName, newAlbumBase, shows, takenAt, useLibrary, whereIs,
} from '../../lib/library';
import { WORDS } from '../../lib/phone';
import { organizeFile } from '../fixtures';

const api = vi.hoisted(() => ({ libraryRoot: vi.fn(), useOrganizeFiles: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));
const reading = vi.hoisted(() => ({ useOrganizeReading: vi.fn() }));
vi.mock('../../lib/useOrganizeReading', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/useOrganizeReading')>()), ...reading }));

const IOS = '/Photo Library', PHONE = '/Phone';

describe('the library\'s files', () => {
  it('are latest first, a Live Photo\'s video going with its picture', () => {
    const files = [organizeFile({ id: 'old', date: '2021-01-02T10:00:00' }), organizeFile({ id: 'new', date: '2023-12-27T23:34:00' }), organizeFile({ id: 'live', isVideo: true, livePhoto: true })];
    expect(latestFirst(files).map(f => f.id)).toEqual(['new', 'old']);
  });

  it('are not organized at the top of the library, and on Android also where the camera put them', () => {
    const top = organizeFile({ folder: IOS }), album = organizeFile({ folder: `${IOS}/Anaconda` });
    expect([isUnsorted(top, IOS, 'ios'), isUnsorted(album, IOS, 'ios')]).toEqual([true, false]);
    const camera = organizeFile({ folder: `${PHONE}/DCIM/Camera` });
    expect([isUnsorted(camera, PHONE, 'android'), isUnsorted(camera, PHONE, 'ios')]).toEqual([true, false]);
  });

  it('are shown, and counted, by each filter', () => {
    const files = [
      organizeFile({ id: 'a', folder: IOS, place: 0 }),
      organizeFile({ id: 'b', folder: `${IOS}/Butte`, place: null }),
      organizeFile({ id: 'c', folder: IOS, isVideo: true }),
    ];
    expect(files.filter(f => shows('videos', f, IOS, 'ios')).map(f => f.id)).toEqual(['c']);
    expect(filterCounts(files, IOS, 'ios')).toEqual({ all: 3, unsorted: 2, unplaced: 2, videos: 1 });
  });

  it('are split by month, each month named, its files counted', () => {
    const months = byMonth([organizeFile({ id: 'a', date: '2023-12-27T10:00:00' }), organizeFile({ id: 'b', date: '2023-12-01T10:00:00' }), organizeFile({ id: 'c', date: '2023-11-30T10:00:00' })]);
    expect(months.map(m => [m.month, m.files.map(f => f.id)])).toEqual([['2023-12', ['a', 'b']], ['2023-11', ['c']]]);
    expect([monthName('2023-12'), items(1), items(1500)]).toEqual(['December 2023', '1 item', '1,500 items']);
  });

  it('say when they were taken on a twelve-hour clock', () => {
    expect([takenAt('2023-12-27T23:34:00'), takenAt('2023-12-27T00:05:00'), takenAt('2023-06-09T12:00:00'), takenAt('2023-06-09T09:41:00')])
      .toEqual(['Dec 27, 2023, 11:34 PM', 'Dec 27, 2023, 12:05 AM', 'Jun 9, 2023, 12:00 PM', 'Jun 9, 2023, 9:41 AM']);
  });

  it('say where they are in the platform\'s words', () => {
    expect([whereIs(IOS, IOS, WORDS.ios), whereIs(`${IOS}/Trips/Butte`, IOS, WORDS.ios), whereIs(`${PHONE}/DCIM/Camera`, PHONE, WORDS.android)])
      .toEqual(['Photo Library', 'Photo Library › Trips › Butte', 'Phone/DCIM/Camera']);
  });
});

describe('albums and folders', () => {
  it('are those the files are in, by name, apart from the top of the library', () => {
    const files = [organizeFile({ folder: `${IOS}/Uptown Butte` }), organizeFile({ folder: IOS }), organizeFile({ folder: `${IOS}/Anaconda` }), organizeFile({ folder: `${IOS}/Anaconda` })];
    expect(albumsOf(files, IOS, WORDS.ios)).toEqual([{ path: `${IOS}/Anaconda`, label: 'Anaconda' }, { path: `${IOS}/Uptown Butte`, label: 'Uptown Butte' }]);
    expect(albumsOf([organizeFile({ folder: `${PHONE}/DCIM/Camera` })], PHONE, WORDS.android)).toEqual([{ path: `${PHONE}/DCIM/Camera`, label: 'DCIM/Camera' }]);
  });

  it('are made at the top of an iPhone\'s library, and in Pictures on Android', () => {
    expect([newAlbumBase(IOS, 'ios'), newAlbumBase(PHONE, 'android')]).toEqual([IOS, `${PHONE}/Pictures`]);
  });
});

describe('the question before deleting', () => {
  it('names what goes, where it goes and how it comes back', () => {
    expect(deleteQuestion(['IMG_9154.HEIC', 'IMG_9080.JPG', 'IMG_9043.HEIC'], WORDS.ios)).toEqual({
      title: 'Delete 3 items?',
      lines: ['IMG_9154.HEIC, IMG_9080.JPG, IMG_9043.HEIC go to PhotoSense Removed.', 'Undo can bring them back. Delete permanently, in Settings, sends them to Recently Deleted.'],
      button: 'Delete 3 items',
    });
    expect(deleteQuestion(['IMG_1.JPG'], WORDS.android)).toEqual({
      title: 'Delete 1 item?',
      lines: ['IMG_1.JPG goes to Pictures/PhotoSense Removed.', 'Undo can bring it back. Delete permanently, in Settings, sends it to the bin.'],
      button: 'Delete 1 item',
    });
    expect(deleteQuestion(['a', 'b', 'c', 'd', 'e'], WORDS.ios).lines[0]).toBe('a, b, c and 2 more go to PhotoSense Removed.');
  });
});

describe('reading the library', () => {
  const fresh = ({ children }: { children: React.ReactNode }) => <SWRConfig value={{ provider: () => new Map(), dedupingInterval: 0 }}>{children}</SWRConfig>;
  beforeEach(() => {
    api.libraryRoot.mockResolvedValue(IOS);
    api.useOrganizeFiles.mockReturnValue({});
    reading.useOrganizeReading.mockReturnValue(undefined);
  });

  it('asks nothing until it is on', () => {
    const { result } = renderHook(() => useLibrary(false), { wrapper: fresh });
    expect(result.current).toEqual({ root: undefined, files: [], listing: undefined, reading: undefined, error: undefined });
    expect(api.libraryRoot).not.toHaveBeenCalled();
    expect(api.useOrganizeFiles).toHaveBeenLastCalledWith(undefined);
  });

  it('shows what has been read as it comes, then the whole library, latest first', async () => {
    const { result, rerender } = renderHook(() => useLibrary(true), { wrapper: fresh });
    await vi.waitFor(() => expect(result.current.root).toBe(IOS));
    expect(result.current.reading).toEqual({ done: 0, total: 0 });
    expect(reading.useOrganizeReading).toHaveBeenLastCalledWith(IOS, true);

    const first = organizeFile({ id: 'first', date: '2022-01-01T00:00:00' });
    reading.useOrganizeReading.mockReturnValue({ reading: true, done: 1, total: 2, listing: { root: IOS, fromScan: false, places: [], files: [first] } });
    rerender();
    expect([result.current.reading, result.current.files.map(f => f.id)]).toEqual([{ done: 1, total: 2 }, ['first']]);

    api.useOrganizeFiles.mockReturnValue({ data: { root: IOS, fromScan: true, places: [], files: [first, organizeFile({ id: 'second', date: '2023-01-01T00:00:00' })] } });
    rerender();
    expect([result.current.reading, result.current.files.map(f => f.id)]).toEqual([undefined, ['second', 'first']]);

    api.useOrganizeFiles.mockReturnValue({ error: new Error('gone') });
    rerender();
    expect([result.current.reading, (result.current.error as Error).message]).toEqual([undefined, 'gone']);
  });
});
