import { describe, expect, it } from 'vitest';
import { bytesOf, counted, deletedSaid, findRows, freeable, largestIds, namesOf, percentFull, resolution, rowLine, sizeOf } from '../../lib/categories';
import type { CategoryReportDto } from '../../types';
import { photo } from '../components/phone/groups';

const report: CategoryReportDto = {
  categories: [
    { category: 'largeVideos', count: 2, bytes: 900 }, { category: 'screenshots', count: 5, bytes: 50 }, { category: 'bursts', count: 0, bytes: 0 },
    { category: 'blurry', count: 1, bytes: 30 }, { category: 'messaging', count: 0, bytes: 0 }, { category: 'duplicates', count: 3, bytes: 300 },
  ],
  unmeasured: 0, storageTotal: null, storageFree: null,
};

describe('Find\'s rows', () => {
  it('lists every category and the similar shots, largest first, with Messaging apps only where there is something from one, or on Android', () => {
    expect(findRows(report, { count: 4, bytes: 400 }, false).map(r => r.id)).toEqual(['largeVideos', 'similar', 'duplicates', 'screenshots', 'blurry', 'bursts']);
    expect(findRows(report, undefined, true).map(r => r.id)).toEqual(['largeVideos', 'duplicates', 'screenshots', 'blurry', 'bursts', 'messaging']);
    const chatty = { ...report, categories: report.categories.map(c => (c.category === 'messaging' ? { ...c, count: 1, bytes: 10 } : c)) };
    expect(findRows(chatty, undefined, false).map(r => r.id)).toContain('messaging');
    expect(findRows(undefined, undefined, false)).toEqual([]);
  });

  it('says what each row holds, in its own words, or why there is nothing', () => {
    expect(rowLine({ id: 'screenshots', count: 1, bytes: 1024 }, 0)).toBe('1 item · 1 KB');
    expect(rowLine({ id: 'duplicates', count: 1284, bytes: 1024 }, 0)).toBe('1,284 items · 1 KB');
    expect(rowLine({ id: 'bursts', count: 1, bytes: 1024 }, 0)).toBe('1 extra shot · 1 KB');
    expect(rowLine({ id: 'bursts', count: 12, bytes: 1024 }, 0)).toBe('12 extra shots · 1 KB');
    expect(rowLine({ id: 'messaging', count: 2, bytes: 1024 }, 0)).toBe('WhatsApp and Telegram · 2 items · 1 KB');
    expect(rowLine({ id: 'blurry', count: 0, bytes: 0 }, 3)).toBe('Scan again to find blurry shots');
    expect(rowLine({ id: 'blurry', count: 0, bytes: 0 }, 0)).toBe('Nothing left to review');
    expect(rowLine({ id: 'screenshots', count: 0, bytes: 0 }, 3)).toBe('Nothing left to review');
  });

  it('adds up what the categories would free, the similar shots aside', () => {
    expect(freeable(report)).toBe(1280);
    expect(freeable(undefined)).toBe(0);
  });
});

describe('the figures of a category\'s screen', () => {
  it('says how full the storage is, in whole percent', () => {
    expect(percentFull(1000, 130)).toBe(87);
    expect(percentFull(0, 0)).toBe(0);
  });

  it('names a video\'s frame by its shorter side', () => {
    expect(resolution(3840, 2160)).toBe('4K');
    expect(resolution(1080, 1920)).toBe('1080p');
    expect(resolution(1280, 720)).toBe('720p');
    expect(resolution(640, 480)).toBeUndefined();
  });

  it('counts a grid\'s photos by their kind', () => {
    expect(counted('screenshots', 1)).toBe('1 screenshot');
    expect(counted('screenshots', 1284)).toBe('1,284 screenshots');
    expect(counted('blurry', 1)).toBe('1 blurry shot');
    expect(counted('blurry', 318)).toBe('318 blurry shots');
    expect(counted('messaging', 1)).toBe('1 photo or video');
    expect(counted('messaging', 40)).toBe('40 photos and videos');
  });

  it('takes the largest that the free plan allows, and adds up what photos take', () => {
    const photos = [photo('a', { fileSizeBytes: 10 }), photo('b', { fileSizeBytes: 30 }), photo('c', { fileSizeBytes: 20 })];
    expect(largestIds(photos, 2)).toEqual(['b', 'c']);
    expect(largestIds(photos, 10)).toEqual(['b', 'c', 'a']);
    expect(bytesOf(photos)).toBe(60);
    expect(bytesOf([])).toBe(0);
    expect(sizeOf(photos)).toBe('1 KB');
    expect(sizeOf([])).toBe('0 KB');
  });

  it('says what was deleted and where it waits', () => {
    expect(deletedSaid(1, 'Recently Deleted')).toBe('1 item deleted. It waits in Recently Deleted for 30 days.');
    expect(deletedSaid(3, 'the bin')).toBe('3 items deleted. They wait in the bin for 30 days.');
  });

  it('names up to three files, then how many more', () => {
    const named = (...names: string[]) => namesOf(names.map(n => photo(n)));
    expect(named('a')).toBe('a');
    expect(named('a', 'b')).toBe('a and b');
    expect(named('a', 'b', 'c')).toBe('a, b and c');
    expect(named('a', 'b', 'c', 'd')).toBe('a, b and 2 more');
  });
});
