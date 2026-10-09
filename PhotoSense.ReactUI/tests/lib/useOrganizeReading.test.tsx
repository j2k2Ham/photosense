import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ASK_EVERY_MS, ASK_LATER_BY_MS, useOrganizeReading } from '../../lib/useOrganizeReading';
import type { OrganizeProgressDto } from '../../types';
import { deferred } from '../fakeSignalR';
import { organizeFile as file, organizePlace as place } from '../fixtures';

const api = vi.hoisted(() => ({ fetchOrganizeProgress: vi.fn() }));
vi.mock('../../lib/apiClient', () => api);

const ROOT = 'F:\\PC_backup', OTHER = 'D:\\Backup\\Phone 2022';
const answer = (changes: Partial<OrganizeProgressDto> = {}): OrganizeProgressDto => ({ reading: true, readingId: 'r1', total: 6, done: 0, from: 0, more: false, files: [], places: [], ...changes });
const idle = answer({ reading: false, readingId: null, total: 0 });
const f1 = file({ id: 'f1', place: 0 }), f2 = file({ id: 'f2', place: null }), f3 = file({ id: 'f3', place: 1 });
const butte = place({ town: 'Butte' }), anaconda = place();

// What the service says each time it is asked; the last answer is given from then on.
let answers: (OrganizeProgressDto | Error)[];
const asked = () => api.fetchOrganizeProgress.mock.calls.map(c => c.slice(1));
const after = (ms: number) => act(async () => { await vi.advanceTimersByTimeAsync(ms); });
const watch = (initialProps: { root?: string; reading: boolean } = { root: ROOT, reading: true }) => renderHook(p => useOrganizeReading(p.root, p.reading), { initialProps });

beforeEach(() => {
  vi.useFakeTimers();
  api.fetchOrganizeProgress.mockReset();
  api.fetchOrganizeProgress.mockImplementation(async () => {
    const next = answers.length > 1 ? answers.shift()! : answers[0];
    if (next instanceof Error) throw next;
    return next;
  });
});
afterEach(() => { vi.useRealTimers(); });

describe('what has been read of a folder so far', () => {
  it('is not asked for until there is a folder that is being read', async () => {
    answers = [answer()];
    const { result, rerender } = watch({ reading: true });
    rerender({ root: ROOT, reading: false });
    await after(10 * ASK_EVERY_MS);
    expect(api.fetchOrganizeProgress).not.toHaveBeenCalled();
    expect(result.current).toBeUndefined();
  });

  it('grows as the service reads on: each time it is asked only for what has been read since', async () => {
    answers = [
      idle,
      answer(),
      answer({ done: 2, files: [f1, f2], places: [butte] }),
      answer({ done: 2, from: 2 }),
      answer({ done: 3, from: 2, files: [f3], places: [anaconda] }),
      idle,
    ];
    const { result } = watch();
    // Not yet begun: the service is still finding out what the folder holds.
    await after(0);
    expect(asked()).toEqual([[0, 0]]);
    expect(result.current).toBeUndefined();

    // Begun, with nothing read so far.
    await after(ASK_EVERY_MS);
    expect(result.current).toEqual({ root: ROOT, reading: true, total: 6, done: 0, listing: undefined });

    await after(ASK_EVERY_MS);
    const first = result.current!.listing;
    expect(first).toEqual({ root: ROOT, fromScan: false, places: [butte], files: [f1, f2] });
    expect(result.current).toMatchObject({ reading: true, total: 6, done: 2 });

    // Nothing new: the list is the very one there was, so that nothing is arranged again for it.
    await after(ASK_EVERY_MS);
    expect(asked().at(-1)).toEqual([2, 1]);
    expect(result.current!.listing).toBe(first);

    await after(ASK_EVERY_MS);
    expect(result.current!.listing).toEqual({ root: ROOT, fromScan: false, places: [butte, anaconda], files: [f1, f2, f3] });
    expect(result.current!.listing).not.toBe(first);

    // The service has been through every file and is putting its answer together: what was shown stays.
    await after(ASK_EVERY_MS);
    expect(asked().at(-1)).toEqual([3, 2]);
    expect(result.current).toMatchObject({ reading: false, total: 6, done: 3, listing: { files: [f1, f2, f3] } });
  });

  it('asks again at once while the service has more than it handed over, and less often as the list grows', async () => {
    const batch = (count: number) => new Array(count).fill(f2);
    answers = [
      answer({ total: 80_000, done: 4000, more: true, files: batch(4000) }),
      answer({ total: 80_000, done: 8000, from: 4000, files: batch(4000) }),
      answer({ total: 80_000, done: 68_000, from: 8000, files: batch(60_000) }),
      answer({ total: 80_000, done: 68_000, from: 68_000 }),
    ];
    const { result } = watch();
    await after(0);
    expect(asked()).toEqual([[0, 0], [4000, 0]]);
    expect(result.current!.listing!.files).toHaveLength(8000);

    // Eight thousand files: a little over half a second later than otherwise.
    const later = Math.round(8000 / 15);
    await after(ASK_EVERY_MS + later - 1);
    expect(asked()).toHaveLength(2);
    await after(1);
    expect(asked().at(-1)).toEqual([8000, 0]);

    // A very long list is still asked about every four seconds.
    await after(ASK_EVERY_MS + ASK_LATER_BY_MS - 1);
    expect(asked()).toHaveLength(3);
    await after(1);
    expect(asked().at(-1)).toEqual([68_000, 0]);
  });

  it('starts from the beginning when the service has begun reading the folder anew', async () => {
    answers = [
      answer({ done: 2, files: [f1, f2], places: [butte] }),
      answer({ readingId: 'r2', done: 1, from: 1, files: [] }),
      answer({ readingId: 'r2', done: 1, files: [f3], places: [anaconda] }),
    ];
    const { result } = watch();
    await after(0);
    expect(result.current!.listing!.files).toEqual([f1, f2]);

    // It is asked from the beginning without waiting.
    await after(ASK_EVERY_MS);
    await after(1);
    expect(asked()).toEqual([[0, 0], [2, 1], [0, 0]]);
    expect(result.current!.listing).toEqual({ root: ROOT, fromScan: false, places: [anaconda], files: [f3] });
  });

  it('keeps asking when the service does not answer, and stops once the folder is no longer being read', async () => {
    answers = [new Error('Failed to fetch'), answer({ done: 1, files: [f1], places: [butte] })];
    const { result, rerender } = watch();
    await after(0);
    expect(result.current).toBeUndefined();
    await after(ASK_EVERY_MS);
    expect(result.current!.listing!.files).toEqual([f1]);

    // The whole list has come: what was read so far is let go of, and nothing more is asked.
    rerender({ root: ROOT, reading: false });
    expect(result.current).toBeUndefined();
    const before = asked().length;
    await after(10 * ASK_EVERY_MS);
    expect(asked()).toHaveLength(before);
  });

  it('forgets what was read of one folder when another is chosen', async () => {
    answers = [answer({ done: 1, files: [f1], places: [butte] })];
    const { result, rerender } = watch();
    await after(0);
    expect(result.current!.listing!.root).toBe(ROOT);

    answers = [answer({ readingId: 'r9', total: 2, done: 1, files: [f3], places: [anaconda] })];
    rerender({ root: OTHER, reading: true });
    expect(result.current).toBeUndefined();
    await after(0);
    expect(api.fetchOrganizeProgress).toHaveBeenLastCalledWith(OTHER, 0, 0);
    expect(result.current).toEqual({ root: OTHER, reading: true, total: 2, done: 1, listing: { root: OTHER, fromScan: false, places: [anaconda], files: [f3] } });
  });

  it('takes no notice of an answer that comes after it has stopped watching', async () => {
    for (const settle of ['resolve', 'reject'] as const) {
      const pending = deferred<OrganizeProgressDto>();
      api.fetchOrganizeProgress.mockReset();
      api.fetchOrganizeProgress.mockReturnValue(pending.promise);
      const { result, unmount } = watch();
      expect(asked()).toHaveLength(1);
      unmount();
      if (settle === 'resolve') pending.resolve(answer({ done: 1, files: [f1] }));
      else pending.reject(new Error('Failed to fetch'));
      await after(10 * ASK_EVERY_MS);
      expect(asked()).toHaveLength(1);
      expect(result.current).toBeUndefined();
    }
  });
});
