import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { endedSaid, NOTICES_OFF, QUALITIES, savings, useCompressionEnded, useNoticeSwitches } from '../../lib/compress';
import type { CompressibleVideoDto, CompressionStateDto } from '../../types';
import { photo } from '../components/phone/groups';

const api = vi.hoisted(() => ({ useCompression: vi.fn(), refreshCompressed: vi.fn(), useNotices: vi.fn(), changeNotices: vi.fn(), openPhotoSettings: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const MB = 1024 ** 2;
const state = (more: Partial<CompressionStateDto> = {}): CompressionStateDto => ({
  stage: 'done', quality: 'balanced', total: 1, done: 1, current: null, progress: 1, results: [{ id: 'a', fileName: 'IMG_1.MOV', before: 900 * MB, after: 120 * MB }],
  saved: 780 * MB, originalsDeleted: true, problem: null, ...more,
});

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  api.refreshCompressed.mockResolvedValue(undefined);
});

describe('the qualities and what compressing saves', () => {
  it('offers High, Balanced and Small, each with what it makes of a video', () => {
    expect(QUALITIES.map(q => `${q.label}: ${q.note}`)).toEqual(['High: Original resolution, HEVC', 'Balanced: 1080p, HEVC', 'Small: 720p, HEVC']);
  });

  it('adds up what the videos worth compressing would save at a quality, leaving out those with no estimate', () => {
    const videos: CompressibleVideoDto[] = [
      { video: photo('a', { fileSizeBytes: 900 * MB }), estimates: { high: 400 * MB, balanced: 120 * MB, small: null } },
      { video: photo('b', { fileSizeBytes: 300 * MB }), estimates: { high: null, balanced: 60 * MB, small: null } },
    ];
    expect(savings(videos, 'balanced')).toEqual({ count: 2, bytes: 1020 * MB, ids: ['a', 'b'] });
    expect(savings(videos, 'high')).toEqual({ count: 1, bytes: 500 * MB, ids: ['a'] });
    expect(savings(videos, 'small')).toEqual({ count: 0, bytes: 0, ids: [] });
  });
});

describe('what a compression says when it ends', () => {
  it('says what it saved and where the originals wait', () => {
    expect(endedSaid(state(), 'Recently Deleted')).toEqual({ message: 'Saved 780.0 MB. The original is in Recently Deleted for 30 days.', kind: 'ok' });
    const two = state({ results: [...state().results, { id: 'b', fileName: 'IMG_2.MOV', before: 300 * MB, after: 60 * MB }], saved: 1020 * MB });
    expect(endedSaid(two, 'the bin')).toEqual({ message: 'Saved 1020.0 MB. The originals are in the bin for 30 days.', kind: 'ok' });
  });

  it('says both are kept when the person said no to deleting the originals', () => {
    expect(endedSaid(state({ originalsDeleted: false }), 'Recently Deleted')).toEqual({ message: 'The original and the smaller copy are both kept.', kind: 'ok' });
    const two = state({ originalsDeleted: false, results: [...state().results, ...state().results] });
    expect(endedSaid(two, 'Recently Deleted').message).toBe('The originals and the smaller copies are both kept.');
  });

  it('says why it stopped, and what was compressed before', () => {
    expect(endedSaid(state({ stage: 'stopped', results: [], originalsDeleted: null }), 'Recently Deleted')).toEqual({ message: 'Stopped. Nothing was compressed.', kind: 'info' });
    expect(endedSaid(state({ stage: 'stopped' }), 'Recently Deleted').message).toBe('Stopped. 1 compressed. The original is in Recently Deleted for 30 days.');
    expect(endedSaid(state({ stage: 'failed', problem: 'IMG_2.MOV could not be read.' }), 'Recently Deleted'))
      .toEqual({ message: 'IMG_2.MOV could not be read. 1 compressed first. The original is in Recently Deleted for 30 days.', kind: 'error' });
    expect(endedSaid(state({ stage: 'failed', results: [], originalsDeleted: null }), 'Recently Deleted').message).toBe('The video could not be compressed.');
  });
});

describe('watching a compression', () => {
  it('says once that it ended, and reads again what it changed; not before it ran, nor while it runs', async () => {
    const notify = vi.fn();
    let current: CompressionStateDto | undefined;
    api.useCompression.mockImplementation(() => ({ data: current }));
    const { rerender } = renderHook(() => useCompressionEnded(true, notify, 'Recently Deleted'));
    current = state();
    rerender();
    expect(notify).not.toHaveBeenCalled();
    current = state({ stage: 'running', results: [], originalsDeleted: null });
    rerender();
    current = state();
    rerender();
    expect(notify).toHaveBeenCalledExactlyOnceWith('Saved 780.0 MB. The original is in Recently Deleted for 30 days.', 'ok', undefined);
    expect(api.refreshCompressed).toHaveBeenCalledOnce();
    expect(api.useCompression).toHaveBeenLastCalledWith(true);
  });

  it('offers to share what was saved once the originals went, and not when they were kept', () => {
    const notify = vi.fn(), onShare = vi.fn();
    let current: CompressionStateDto | undefined = state({ stage: 'running', results: [], originalsDeleted: null });
    api.useCompression.mockImplementation(() => ({ data: current }));
    const { rerender } = renderHook(() => useCompressionEnded(true, notify, 'the bin', onShare));
    current = state();
    rerender();
    notify.mock.lastCall![2].run();
    expect(onShare).toHaveBeenCalledWith(780 * MB, 1);
    current = state({ stage: 'running', results: [], originalsDeleted: null });
    rerender();
    current = state({ originalsDeleted: false });
    rerender();
    expect(notify.mock.lastCall![2]).toBeUndefined();
  });
});

describe('the notifications\' switches', () => {
  it('turns one on or off, and says where to allow notifications when the phone will not', async () => {
    const notify = vi.fn();
    api.useNotices.mockReturnValue({ data: { lowStorage: false, trialReminder: false, allowed: false } });
    const { result } = renderHook(() => useNoticeSwitches(true, notify));
    expect(result.current.state).toEqual({ lowStorage: false, trialReminder: false, allowed: false });
    expect(api.useNotices).toHaveBeenLastCalledWith(true);

    api.changeNotices.mockResolvedValueOnce({ lowStorage: true, trialReminder: false, allowed: true });
    await act(() => result.current.change({ lowStorage: true }));
    expect(api.changeNotices).toHaveBeenLastCalledWith({ lowStorage: true });
    expect(notify).not.toHaveBeenCalled();

    api.changeNotices.mockResolvedValueOnce({ lowStorage: false, trialReminder: false, allowed: false });
    await act(() => result.current.change({ trialReminder: true }));
    expect(notify).toHaveBeenLastCalledWith(NOTICES_OFF, 'error', expect.objectContaining({ label: 'Settings' }));
    notify.mock.lastCall![2].run();
    expect(api.openPhotoSettings).toHaveBeenCalledOnce();

    // Turning one off says nothing, allowed or not.
    api.changeNotices.mockResolvedValueOnce({ lowStorage: false, trialReminder: false, allowed: false });
    await act(() => result.current.change({ lowStorage: false }));
    expect(notify).toHaveBeenCalledTimes(1);
  });

  it('says why a change could not be made', async () => {
    const notify = vi.fn();
    api.useNotices.mockReturnValue({});
    const { result } = renderHook(() => useNoticeSwitches(false, notify));
    api.changeNotices.mockRejectedValueOnce(new Error('Only a phone sends notifications.')).mockRejectedValueOnce('gone');
    await act(() => result.current.change({ lowStorage: true }));
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('Only a phone sends notifications.', 'error'));
    await act(() => result.current.change({ lowStorage: true }));
    expect(notify).toHaveBeenLastCalledWith('gone', 'error');
  });
});
