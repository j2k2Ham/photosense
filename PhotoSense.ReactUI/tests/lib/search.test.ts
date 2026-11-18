import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it } from 'vitest';
import { fileOf, foundLine, placeLine, proLine, RECENT_SEARCHES, shareLine, useRecentSearches } from '../../lib/search';
import type { PhotoDto } from '../../types';
import { organizeFile } from '../fixtures';

beforeEach(() => localStorage.clear());

describe('search', () => {
  it('says what the free plan shows when a search names things in photos', () => {
    expect(proLine([{ kind: 'inPhoto', text: 'dog' }, { kind: 'inPhoto', text: 'beach' }, { kind: 'inPhoto', text: 'ball' }, { kind: 'place', text: 'Butte' }, { kind: 'year', text: '2023' }]))
      .toBe('Searching for what is in photos, like “dog” and “beach”, is part of Pro. Showing photos that match Butte, 2023.');
    expect(proLine([{ kind: 'inPhoto', text: 'dog' }])).toBe('Searching for what is in photos, like “dog”, is part of Pro.');
  });

  it('counts what matches, finds the library\'s own file for one, and says what a shared copy leaves out', () => {
    expect([foundLine(0, 0), foundLine(1, 0), foundLine(1200, 0), foundLine(1, 1), foundLine(3, 3), foundLine(1203, 3), foundLine(2, 1)])
      .toEqual(['No matches', '1 photo', '1,200 photos', '1 video', '3 videos', '1,200 photos and 3 videos', '1 photo and 1 video']);
    const file = organizeFile({ id: 'f1', name: 'IMG_1.HEIC', folder: '/Photo Library/Trips' });
    expect(fileOf({ sourcePath: '/Photo Library/Trips/IMG_1.HEIC' } as PhotoDto, [file])).toBe(file);
    expect(fileOf({ sourcePath: '/Photo Library/IMG_1.HEIC' } as PhotoDto, [file])).toBeUndefined();
    expect([shareLine(true, false), shareLine(false, true), shareLine(true, true), shareLine(false, false)])
      .toEqual(['Shares a copy without location', 'Shares a copy without camera details', 'Shares a copy without location or camera details', 'Shares the original file']);
    expect([placeLine('Butte', true), placeLine('Butte', false), placeLine(undefined, true)])
      .toEqual(['Butte is left out of the copy.', 'The copy includes Butte.', 'This photo does not say where it was taken.']);
  });

  it('keeps the latest searches once each, and reads nothing from what it cannot', () => {
    const { result } = renderHook(() => useRecentSearches());
    expect(result.current[0]).toEqual([]);
    for (const q of ['one', 'two', 'three', 'four', 'five', 'six']) act(() => result.current[1](q));
    expect(result.current[0]).toEqual(['six', 'five', 'four', 'three', 'two']);
    expect(result.current[0]).toHaveLength(RECENT_SEARCHES);
    act(() => result.current[1]('THREE'));
    expect(result.current[0]).toEqual(['THREE', 'six', 'five', 'four', 'two']);

    localStorage.setItem('photosense-searches', JSON.stringify(['kept', 3, null]));
    expect(renderHook(() => useRecentSearches()).result.current[0]).toEqual(['kept']);
    localStorage.setItem('photosense-searches', JSON.stringify({ not: 'a list' }));
    expect(renderHook(() => useRecentSearches()).result.current[0]).toEqual([]);
    localStorage.setItem('photosense-searches', '{');
    expect(renderHook(() => useRecentSearches()).result.current[0]).toEqual([]);
  });
});
