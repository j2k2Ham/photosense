import { act, renderHook, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Library } from '../../lib/library';
import { countFiles, filesFor, photosAndVideos, someNames, useSend } from '../../lib/send';
import type { OrganizeFileDto, SendStateDto } from '../../types';
import { deferred } from '../deferred';
import { organizeFile } from '../fixtures';

const api = vi.hoisted(() => ({ useSendState: vi.fn(), startSending: vi.fn(), confirmSending: vi.fn(), stopSending: vi.fn(), deleteSent: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
const at = (id: string, more: Partial<OrganizeFileDto> = {}) => organizeFile({ id, name: `${id}.JPG`, folder: ROOT, sizeBytes: 1000, ...more });
// Two at the top of the library, one a Live Photo whose video goes with it; one in an album; a video in an album.
const files = [
  at('IMG_1'), at('IMG_1v', { name: 'IMG_1.MOV', isVideo: true, livePhoto: true, sizeBytes: 500 }), at('IMG_2'),
  at('IMG_3', { folder: `${ROOT}/Trips` }), at('MOV_4', { name: 'MOV_4.MOV', isVideo: true, folder: `${ROOT}/Trips`, sizeBytes: 9000 }),
];
const library = (more: Partial<Library> = {}): Library => ({ root: ROOT, files: files.filter(f => !f.livePhoto), listing: { root: ROOT, fromScan: true, places: [], files }, ...more });
const state = (more: Partial<SendStateDto> = {}): SendStateDto => ({
  stage: 'waiting', code: null, computer: 'JAMIE-LAPTOP', done: 0, total: 3, bytes: 0, totalBytes: 2500, deletable: [], where: null, phone: "Jamie's iPhone", problem: null, current: null, ...more,
});

let now: SendStateDto | undefined;
const notify = vi.fn();
beforeEach(() => {
  for (const mock of [...Object.values(api), notify]) mock.mockReset();
  now = undefined;
  api.useSendState.mockImplementation(() => ({ data: now }));
  api.startSending.mockImplementation(async () => { now = state(); return now; });
  api.confirmSending.mockResolvedValue(undefined);
  api.stopSending.mockImplementation(async () => { now = state({ stage: 'idle' }); return now; });
  api.deleteSent.mockResolvedValue({ deleted: 2 });
});

function hook(lib = library(), watching = true) {
  return renderHook(({ w }) => useSend(lib, 'ios', w, notify, 'Recently Deleted'), { initialProps: { w: watching } });
}

describe('what is sent', () => {
  it('counts what is in no album yet and everything, Live Photo videos with their pictures, and how many were chosen', () => {
    const { result } = hook();
    expect(result.current.sizes).toEqual({ unsorted: { count: 3, bytes: 2500 }, chosen: { count: 0, bytes: 0 }, everything: { count: 5, bytes: 12500 } });
    expect([result.current.what, result.current.mode, result.current.screen, result.current.state]).toEqual(['unsorted', 'copy', 'choose', undefined]);
    act(() => result.current.choose(['IMG_1', 'MOV_4']));
    expect([result.current.what, result.current.chosen, result.current.sizes.chosen]).toEqual(['chosen', ['IMG_1', 'MOV_4'], { count: 2, bytes: 0 }]);
  });

  it('has nothing to count before the library is read', () => {
    const { result } = hook(library({ listing: undefined, root: undefined, files: [] }));
    expect(result.current.sizes.everything).toEqual({ count: 0, bytes: 0 });
  });

  it('finds the Live Photo video of a picture by its folder and name, and words counts and names', () => {
    expect(filesFor(files, f => f.id === 'IMG_1').map(f => f.id)).toEqual(['IMG_1', 'IMG_1v']);
    expect(filesFor(files, f => f.id === 'IMG_3').map(f => f.id)).toEqual(['IMG_3']);
    expect([countFiles(1), countFiles(4212)]).toEqual(['1 file', '4,212 files']);
    expect([[], ['a.jpg'], ['a.jpg', 'b.HEIC'], ['a.MOV'], ['a.mov', 'b.MP4', 'c.jpg']].map(photosAndVideos)).toEqual(['', '1 photo', '2 photos', '1 video', '1 photo and 2 videos']);
    expect([[], ['a'], ['a', 'b'], ['a', 'b', 'c'], ['a', 'b', 'c', 'd', 'e']].map(someNames)).toEqual(['', 'a', 'a and b', 'a, b and c', 'a, b, c and 2 more']);
  });
});

describe('an offer', () => {
  it('offers what is in no album yet without the Live Photo videos, which the phone adds, and follows it while it stands', async () => {
    const { result, rerender } = hook(library(), false);
    expect(api.useSendState).toHaveBeenLastCalledWith(false);
    act(() => result.current.offer());
    expect(api.startSending).toHaveBeenCalledExactlyOnceWith(['IMG_1', 'IMG_2']);
    await waitFor(() => expect(result.current.screen).toBe('waiting'));
    expect(api.useSendState).toHaveBeenLastCalledWith(true);
    expect(result.current.state).toBe(now);

    act(() => result.current.setWhat('everything'));
    act(() => result.current.offer());
    expect(api.startSending).toHaveBeenLastCalledWith(['IMG_1', 'IMG_2', 'IMG_3', 'MOV_4']);
    act(() => { result.current.choose(['IMG_3']); });
    act(() => result.current.offer());
    expect(api.startSending).toHaveBeenLastCalledWith(['IMG_3']);
    rerender({ w: true });
  });

  it('says why an offer could not be made', async () => {
    const { result } = hook();
    api.startSending.mockRejectedValueOnce(new Error('None of those files is there to send any more.'));
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.refused).toBe('None of those files is there to send any more.'));
    api.startSending.mockRejectedValueOnce(new TypeError('fetch failed'));
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.refused).toBe('Cannot reach the PhotoSense server.'));
    api.startSending.mockRejectedValueOnce('no');
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.refused).toBe('no'));
    expect(result.current.screen).toBe('choose');
  });

  it('shows an offer made before the page was drawn while it stands, but not how one ended', () => {
    now = state({ stage: 'sending' });
    const { result, rerender } = hook();
    expect(result.current.screen).toBe('sending');
    now = state({ stage: 'sent' });
    rerender({ w: true });
    expect(result.current.screen).toBe('choose');
  });

  it('gives the answer to the numbers, remembers that they were said to be different, and says when it could not', async () => {
    const { result } = hook();
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.screen).toBe('waiting'));
    const answered = deferred<void>();
    api.confirmSending.mockReturnValueOnce(answered.promise);
    act(() => result.current.answer(true));
    expect([api.confirmSending.mock.lastCall, result.current.busy, result.current.saidDifferent]).toEqual([[true], true, false]);
    await act(async () => answered.resolve());
    expect(result.current.busy).toBe(false);
    api.confirmSending.mockRejectedValueOnce(new Error('Not comparing now.'));
    act(() => result.current.answer(false));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('Not comparing now.', 'error'));
    expect(result.current.saidDifferent).toBe(true);
  });

  it('stops offering, back to the choice, and says when the phone could not stop', async () => {
    const { result, rerender } = hook();
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.screen).toBe('waiting'));
    act(() => result.current.stop());
    await waitFor(() => expect(api.stopSending).toHaveBeenCalledOnce());
    rerender({ w: true });
    expect(result.current.screen).toBe('choose');
    api.stopSending.mockRejectedValueOnce(new Error('No answer.'));
    act(() => result.current.stop());
    await waitFor(() => expect(notify).toHaveBeenCalledWith('No answer.', 'error'));
  });

  it('after a failure offers the same files again, and Done goes back to the choice', async () => {
    const { result, rerender } = hook();
    act(() => result.current.offer());
    await waitFor(() => expect(result.current.screen).toBe('waiting'));
    now = state({ stage: 'failed', problem: 'Sending stopped. Try again.' });
    rerender({ w: true });
    expect(result.current.screen).toBe('failed');
    await act(async () => result.current.again());
    expect(api.startSending).toHaveBeenLastCalledWith(['IMG_1', 'IMG_2']);
    // SWR would draw the page afresh with the phone's answer.
    rerender({ w: true });
    expect(result.current.screen).toBe('waiting');
    now = state({ stage: 'failed' });
    rerender({ w: true });
    act(() => result.current.done());
    expect(result.current.screen).toBe('choose');
  });
});

describe('deleting from the phone what was sent', () => {
  async function sent(lib = library()) {
    const view = hook(lib);
    act(() => view.result.current.offer());
    await waitFor(() => expect(view.result.current.screen).toBe('waiting'));
    return view;
  }

  it('with Copy, deletes when asked, says what went and that the copies stay, and says when nothing was left', async () => {
    const { result, rerender } = await sent();
    now = state({ stage: 'sent', done: 3, deletable: ['IMG_1.JPG', 'IMG_2.JPG'] });
    rerender({ w: true });
    expect([result.current.screen, api.deleteSent.mock.calls.length]).toEqual(['sent', 0]);
    act(() => result.current.remove());
    expect(result.current.busy).toBe(true);
    await waitFor(() => expect(result.current.deleted).toBe(2));
    expect(notify).toHaveBeenLastCalledWith('Deleted 2 photos from this phone. They are in Recently Deleted for 30 days, and the copies on JAMIE-LAPTOP stay.', 'ok');
    expect(result.current.busy).toBe(false);

    api.deleteSent.mockResolvedValueOnce({ deleted: 0 });
    act(() => result.current.remove());
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('There was nothing left to delete.', 'info'));
    api.deleteSent.mockRejectedValueOnce(new Error('You chose not to delete them.'));
    act(() => result.current.remove());
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('You chose not to delete them.', 'error'));

    act(() => result.current.done());
    expect([result.current.screen, result.current.deleted]).toEqual(['choose', undefined]);
  });

  it('with Move, deletes by itself once, as soon as sending is through, naming one as one', async () => {
    const { result, rerender } = await sent();
    act(() => result.current.setMode('move'));
    const deleting = deferred<{ deleted: number }>();
    api.deleteSent.mockReturnValueOnce(deleting.promise);
    now = state({ stage: 'sent', done: 2, deletable: ['IMG_1.JPG'] });
    rerender({ w: true });
    expect([api.deleteSent.mock.calls.length, result.current.moving]).toEqual([1, true]);
    rerender({ w: false });
    await act(async () => deleting.resolve({ deleted: 1 }));
    expect([result.current.moving, result.current.deleted, api.deleteSent.mock.calls.length]).toEqual([false, 1, 1]);
    expect(notify).toHaveBeenLastCalledWith('Moved 1 photo to JAMIE-LAPTOP. It is in Recently Deleted on this phone for 30 days.', 'ok');
  });

  it('with Move, says so when the phone was told no, and leaves deleting to be asked for', async () => {
    const { result, rerender } = await sent();
    act(() => result.current.setMode('move'));
    api.deleteSent.mockRejectedValueOnce(new Error('You chose not to delete them.'));
    now = state({ stage: 'sent', deletable: ['IMG_1.JPG', 'IMG_2.JPG'] });
    rerender({ w: true });
    await waitFor(() => expect(notify).toHaveBeenLastCalledWith('You chose not to delete them.', 'error'));
    expect([result.current.moving, result.current.deleted]).toEqual([false, undefined]);
    // Nothing to delete is not moved.
    now = state({ stage: 'sent', deletable: [] });
    rerender({ w: true });
    expect(api.deleteSent).toHaveBeenCalledOnce();
  });
});
