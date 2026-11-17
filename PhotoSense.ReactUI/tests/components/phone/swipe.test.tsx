import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SwipePanel, SwipeSession, SwipeSummary } from '../../../components/phone/Swipe';
import type { Library } from '../../../lib/library';
import type { SwipeStateDto } from '../../../types';
import { organizeFile, organizePlace, plan, swipeCard, swipeState } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({ decideSwipe: vi.fn(), undoSwipe: vi.fn(), spareSwipe: vi.fn(), deleteSwiped: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const MB = 1024 ** 2;
const LIBRARY: Library = {
  files: [
    organizeFile({ id: 'a', name: 'IMG_0001.JPG', date: '2022-07-19T10:00:00', place: 0 }),
    organizeFile({ id: 'b', name: 'IMG_0002.MOV', isVideo: true, durationSeconds: 12 }),
    organizeFile({ id: 'c', name: 'IMG_0003.MOV', isVideo: true }),
  ],
  listing: { root: '/Photo Library', fromScan: false, places: [organizePlace({ area: undefined, town: 'Bozeman', state: 'Montana', country: undefined })], files: [] },
};

beforeEach(() => {
  for (const f of Object.values(api)) f.mockReset();
  api.decideSwipe.mockResolvedValue(swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b')] }));
  api.undoSwipe.mockResolvedValue(swipeState());
  api.spareSwipe.mockResolvedValue(swipeState());
});

describe('the Swipe to clean card on Clean up', () => {
  it('says so while today\'s set is being drawn', () => {
    inFrame(<SwipePanel library={LIBRARY} onOpen={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Drawing today\'s set…');
  });

  it('fans out the next photos, says how far through the set, shows the week and the streak, and starts swiping', async () => {
    const onOpen = vi.fn();
    const week = swipeState().week.map((d, i) => ({ ...d, swiped: i >= 4 }));
    inFrame(<SwipePanel state={swipeState({ set: [swipeCard('z', { delete: false }), swipeCard('a'), swipeCard('b')], week, streak: 3 })} library={LIBRARY} onOpen={onOpen} />);
    const panel = screen.getByRole('region', { name: 'Swipe to clean' });
    expect(panel).toHaveTextContent('Today’s set · 1 of 3 reviewed');
    expect(panel).toHaveTextContent('3-day streak');
    // A picture by its preview; a video, which has none, by a play mark; and one tile left empty.
    expect(panel.querySelectorAll('img')).toHaveLength(1);
    expect(panel.querySelector('img')).toHaveAttribute('src', 'http://localhost:7071/api/organize/files/a/thumbnail');
    const days = within(screen.getByRole('list', { name: 'The last seven days' })).getAllByRole('listitem');
    expect(days.map(d => d.getAttribute('aria-label'))).toEqual([
      'Wednesday, not swiped', 'Thursday, not swiped', 'Friday, not swiped', 'Saturday, not swiped', 'Sunday, swiped', 'Monday, swiped', 'Tuesday, swiped',
    ]);
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(onOpen).toHaveBeenCalledWith('cards');
  });

  it('opens the summary when the set is through and deletions wait, and offers nothing once the day is done', async () => {
    const onOpen = vi.fn();
    const { again } = inFrame(<SwipePanel state={swipeState({ set: [swipeCard('a', { delete: true })] })} library={LIBRARY} onOpen={onOpen} />);
    await userEvent.click(screen.getByRole('button', { name: 'Review deletions' }));
    expect(onOpen).toHaveBeenCalledWith('summary');
    again(<SwipePanel state={swipeState({ set: [swipeCard('a', { delete: false })] })} library={LIBRARY} onOpen={onOpen} />);
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Swipe to clean' })).toHaveTextContent('Done for today. A new set is ready tomorrow.');
  });
});

interface SessionOptions { state?: SwipeStateDto; computer?: string | null; left?: number | null; known?: boolean; platform?: 'ios' | 'android' }

function session({ state = swipeState({ set: [swipeCard('a', { onComputer: true, bytes: 5 * MB }), swipeCard('b')] }), computer = 'Studio iMac', left = 50, known = true, platform = 'ios' }: SessionOptions = {}) {
  const notify = vi.fn(), onClose = vi.fn(), onDone = vi.fn();
  const view = inFrame(<SwipeSession state={state} library={LIBRARY} computer={computer} notify={notify} onClose={onClose} onDone={onDone} />, frame(platform),
    gate({ plan: known ? plan({ leftToday: left, tier: left === null ? 'pro' : 'free' }) : undefined }));
  return { ...view, notify, onClose, onDone };
}

const photoCard = () => screen.getByRole('img', { name: /^IMG_|^A photo/ });

describe('swiping', () => {
  it('shows today\'s set one photo at a time: where it is on the computer, when and where it was taken, what it is and its size', () => {
    session();
    const dialog = screen.getByRole('dialog', { name: 'Swipe to clean' });
    expect(dialog).toHaveTextContent('1 of 2');
    expect(within(dialog).getByRole('progressbar', { name: 'Today\'s set' })).toHaveAttribute('aria-valuenow', '0');
    expect(photoCard()).toHaveAccessibleName('IMG_0001.JPG, Jul 19, 2022');
    expect(photoCard()).toHaveTextContent('On Studio iMac');
    expect(photoCard()).toHaveTextContent('Jul 19, 2022');
    expect(photoCard()).toHaveTextContent('Bozeman, Montana · Photo · 5.0 MB');
    expect(photoCard().querySelectorAll('img')).toHaveLength(2);
    expect(dialog).toHaveTextContent('Swipe left to delete, right to keep');
    expect(dialog).toHaveTextContent('Free plan: 50 removals left today.');
    expect(screen.getByRole('button', { name: 'Undo' })).toBeDisabled();
  });

  it('shows a video by a play mark, with its length, and no flag where there is no computer; one Library has not read yet by itself', () => {
    const { unmount } = session({ state: swipeState({ set: [swipeCard('b', { onComputer: true })] }), computer: null });
    expect(photoCard()).toHaveTextContent('Video 0:12');
    expect(photoCard()).not.toHaveTextContent('On ');
    expect(photoCard().querySelector('img')).toBeNull();
    unmount();
    const other = session({ state: swipeState({ set: [swipeCard('c')] }) });
    expect(photoCard()).toHaveTextContent('Video · 1.0 MB');
    other.unmount();
    session({ state: swipeState({ set: [swipeCard('unknown')] }) });
    expect(screen.getByRole('img', { name: 'A photo' })).not.toHaveTextContent(/\d/);
  });

  it('marks to delete or keeps with the buttons and the arrow keys, and the summary follows once the set is through', async () => {
    const { onDone, onClose } = session();
    await userEvent.click(screen.getByRole('button', { name: 'Delete' }));
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', true);
    expect(onDone).not.toHaveBeenCalled();
    api.decideSwipe.mockResolvedValue(swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b', { delete: false })] }));
    await userEvent.click(screen.getByRole('button', { name: 'Keep' }));
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', false);
    expect(onDone).toHaveBeenCalledOnce();
    await act(async () => { fireEvent.keyDown(window, { key: 'ArrowLeft' }); });
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', true);
    await act(async () => { fireEvent.keyDown(window, { key: 'ArrowRight' }); });
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', false);
    fireEvent.keyDown(window, { key: 'Enter' });
    expect(api.decideSwipe).toHaveBeenCalledTimes(4);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('decides when the photo is dragged far enough, left to delete and right to keep, and otherwise lets it go back', async () => {
    session();
    const card = photoCard();
    // Moved before it was taken hold of, nothing moves.
    fireEvent.pointerMove(card, { clientX: 300, clientY: 0 });
    expect(card.style.transform).toBe('translate(0px, 0px) rotate(0deg)');
    fireEvent.pointerDown(card, { clientX: 200, clientY: 100, pointerId: 1 });
    fireEvent.pointerMove(card, { clientX: 150, clientY: 110 });
    expect(card.style.transform).toBe('translate(-50px, 3px) rotate(-2.7777777777777777deg)');
    fireEvent.pointerUp(card);
    expect(api.decideSwipe).not.toHaveBeenCalled();
    expect(card.style.transform).toBe('translate(0px, 0px) rotate(0deg)');
    fireEvent.pointerDown(card, { clientX: 200, clientY: 100, pointerId: 1 });
    fireEvent.pointerMove(card, { clientX: 50, clientY: 100 });
    await act(async () => { fireEvent.pointerUp(card); });
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', true);
    fireEvent.pointerDown(card, { clientX: 0, clientY: 0, pointerId: 1 });
    fireEvent.pointerMove(card, { clientX: 120, clientY: 0 });
    await act(async () => { fireEvent.pointerUp(card); });
    expect(api.decideSwipe).toHaveBeenLastCalledWith('a', false);
    fireEvent.pointerDown(card, { clientX: 0, clientY: 0, pointerId: 1 });
    fireEvent.pointerMove(card, { clientX: 120, clientY: 0 });
    fireEvent.pointerCancel(card);
    expect(card.style.transform).toBe('translate(0px, 0px) rotate(0deg)');
  });

  it('decides one photo at a time, and says what went wrong', async () => {
    let answer: (s: SwipeStateDto) => void = () => {};
    api.decideSwipe.mockReturnValueOnce(new Promise(resolve => { answer = resolve; }));
    const { notify } = session();
    fireEvent.keyDown(window, { key: 'ArrowLeft' });
    fireEvent.keyDown(window, { key: 'ArrowLeft' });
    expect(api.decideSwipe).toHaveBeenCalledOnce();
    await act(async () => answer(swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b')] })));
    api.decideSwipe.mockRejectedValueOnce(new Error('That photo was swiped already. Undo takes back the last one.'));
    await userEvent.click(screen.getByRole('button', { name: 'Keep' }));
    expect(notify).toHaveBeenCalledWith('That photo was swiped already. Undo takes back the last one.', 'error');
  });

  it('takes back the last decision, and says what went wrong', async () => {
    const { notify } = session({ state: swipeState({ set: [swipeCard('a', { delete: true, bytes: 2 * MB }), swipeCard('b')] }) });
    expect(screen.getByRole('dialog')).toHaveTextContent('2 of 2');
    expect(screen.getByRole('dialog')).toHaveTextContent('2.0 MB to free from 1 item');
    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));
    expect(api.undoSwipe).toHaveBeenCalledOnce();
    api.undoSwipe.mockRejectedValueOnce('Nothing swiped today to undo.');
    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));
    expect(notify).toHaveBeenCalledWith('Nothing swiped today to undo.', 'error');
  });

  it('says, once more are marked than the free plan has left today, that the rest wait; nothing of the plan on Pro or before it is known', () => {
    const marked = swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b', { delete: true }), swipeCard('c')] });
    const { unmount } = session({ state: marked, left: 1 });
    expect(screen.getByText('Free plan: 1 removal left today. The rest stay marked for tomorrow.')).toHaveClass('text-amber');
    unmount();
    const pro = session({ state: marked, left: null });
    expect(screen.queryByText(/Free plan/)).not.toBeInTheDocument();
    pro.unmount();
    session({ state: marked, known: false });
    expect(screen.queryByText(/Free plan/)).not.toBeInTheDocument();
  });

  it('with nothing left to swipe, shows no photo and decides nothing', () => {
    session({ state: swipeState({ set: [swipeCard('a', { delete: false })] }) });
    expect(screen.queryByRole('img')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete' })).toBeDisabled();
    fireEvent.keyDown(window, { key: 'ArrowRight' });
    expect(api.decideSwipe).not.toHaveBeenCalled();
    expect(screen.getByRole('dialog')).toHaveTextContent('1 of 1');
  });

  it('counts an empty set as none reviewed', () => {
    session({ state: swipeState() });
    expect(within(screen.getByRole('dialog')).getByRole('progressbar').firstElementChild).toHaveStyle({ width: '0%' });
  });
});

interface SummaryOptions { state?: SwipeStateDto; left?: number | null; known?: boolean; platform?: 'ios' | 'android'; remove?: ReturnType<typeof gate>['remove'] }

function summary({ state = swipeState({
  set: [swipeCard('a', { delete: true, bytes: 3 * MB }), swipeCard('b', { delete: false }), swipeCard('c', { delete: true, spared: true })],
  earlier: [swipeCard('unknown', { delete: true, bytes: 2 * MB })], streak: 7,
}), left = 50, known = true, platform = 'ios', remove }: SummaryOptions = {}) {
  const notify = vi.fn(), onClose = vi.fn();
  const planGate = gate({ plan: known ? plan({ leftToday: left, tier: left === null ? 'pro' : 'free' }) : undefined, ...(remove ? { remove } : {}) });
  const view = inFrame(<SwipeSummary state={state} library={LIBRARY} notify={notify} onClose={onClose} />, frame(platform), planGate);
  return { ...view, notify, onClose, planGate };
}

describe('the swipe summary', () => {
  it('says what is marked and what it frees, the streak, what was reviewed, kept and marked, and every photo marked, tap one to keep it', async () => {
    const { notify } = summary();
    const dialog = screen.getByRole('dialog', { name: 'Swipe summary' });
    expect(dialog).toHaveTextContent('Today’s set is done');
    expect(dialog).toHaveTextContent('5.0 MB');
    expect(dialog).toHaveTextContent('ready to free from 2 items');
    expect(within(dialog).getByRole('region', { name: 'Streak' })).toHaveTextContent('7-day streak');
    expect(within(dialog).getAllByRole('definition').map(d => d.textContent)).toEqual(['3', '2', '2']);
    const marked = within(screen.getByRole('region', { name: 'Marked to delete' })).getAllByRole('button');
    expect(marked.map(b => b.getAttribute('aria-label'))).toEqual(['A photo: marked to delete', 'IMG_0001.JPG: marked to delete', 'IMG_0003.MOV: kept']);
    expect(marked[2]).toHaveTextContent('KEPT');
    expect(marked[2]).toHaveAttribute('aria-pressed', 'false');
    await userEvent.click(marked[1]);
    expect(api.spareSwipe).toHaveBeenLastCalledWith('a', true);
    await userEvent.click(marked[2]);
    expect(api.spareSwipe).toHaveBeenLastCalledWith('c', false);
    api.spareSwipe.mockRejectedValueOnce(new Error('That photo is not marked to delete.'));
    await userEvent.click(marked[0]);
    expect(notify).toHaveBeenCalledWith('That photo is not marked to delete.', 'error');
    expect(dialog).toHaveTextContent('Uses 2 of your 50 free removals left today. Items stay in Recently Deleted for 30 days.');
  });

  it('deletes everything marked in one confirmation, then says what was freed and what stays marked', async () => {
    api.deleteSwiped.mockResolvedValue({ deleted: 2, bytes: 5 * MB, left: 0 });
    const { onClose } = summary();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 2 items' }));
    expect(api.deleteSwiped).toHaveBeenCalledWith(undefined);
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('Space freed');
    expect(dialog).toHaveTextContent('5.0 MB freed');
    expect(dialog).toHaveTextContent('A new set is ready tomorrow.');
    expect(within(dialog).getAllByRole('term').map(t => t.textContent)).toEqual(['Reviewed', 'Kept', 'Deleted']);
    expect(screen.queryByRole('region', { name: 'Marked to delete' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Done' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('after a deletion, says what stays marked, or that the rest of today\'s set waits', async () => {
    api.deleteSwiped.mockResolvedValueOnce({ deleted: 1, bytes: MB, left: 3 });
    const { unmount } = summary({ state: swipeState({ set: [swipeCard('a', { delete: true })] }) });
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 item' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('3 items stay marked for tomorrow.');
    unmount();
    api.deleteSwiped.mockResolvedValueOnce({ deleted: 1, bytes: MB, left: 0 });
    summary({ state: swipeState({ set: [swipeCard('a', { delete: true }), swipeCard('b')] }) });
    expect(screen.getByRole('dialog')).toHaveTextContent('You reviewed 1 of 2');
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 item' }));
    expect(screen.getByRole('dialog')).toHaveTextContent('The rest of today’s set is still waiting.');
  });

  it('over the free removals left today, deletes the largest that fit, and Pro deletes them all', async () => {
    api.deleteSwiped.mockResolvedValue({ deleted: 1, bytes: 3 * MB, left: 1 });
    const { planGate } = summary({ left: 1 });
    expect(screen.getByRole('note')).toHaveTextContent('The free plan deletes 50 items a day and 1 is left today. The largest goes now. The other one stays marked for tomorrow.');
    expect(screen.getByRole('dialog')).toHaveTextContent('Items stay in Recently Deleted for 30 days.');
    await userEvent.click(screen.getByRole('button', { name: 'Delete all 2 with Pro' }));
    expect(planGate.remove).toHaveBeenCalledWith(2, 'items', expect.any(Function), true);
    // Bought: every one goes.
    expect(api.deleteSwiped).toHaveBeenLastCalledWith(undefined);
  });

  it('deletes as many as are left when they are chosen, from the summary or from the Pro sheet', async () => {
    api.deleteSwiped.mockResolvedValue({ deleted: 1, bytes: 3 * MB, left: 1 });
    const { unmount } = summary({ left: 1 });
    await userEvent.click(screen.getByRole('button', { name: 'Delete 1 now' }));
    expect(api.deleteSwiped).toHaveBeenLastCalledWith(1);
    unmount();
    // None left today: the Pro sheet, whose other button deletes what fits.
    summary({ left: 0, remove: vi.fn((_count: number, _label: string, run: (allowed: number) => void) => run(1)) });
    expect(screen.getByRole('note')).toHaveTextContent('You have used today’s 50 free removals. These stay marked for tomorrow.');
    await userEvent.click(screen.getByRole('button', { name: 'Get Pro' }));
    expect(api.deleteSwiped).toHaveBeenLastCalledWith(1);
  });

  it('says what went wrong, as when the person said no to the phone\'s question', async () => {
    api.deleteSwiped.mockRejectedValueOnce(new Error('You chose not to let PhotoSense delete these photos.'));
    const { notify } = summary({ platform: 'android' });
    expect(screen.getByRole('dialog')).toHaveTextContent('Items stay in the bin for 30 days.');
    await userEvent.click(screen.getByRole('button', { name: 'Delete 2 items' }));
    expect(notify).toHaveBeenCalledWith('You chose not to let PhotoSense delete these photos.', 'error');
    expect(screen.getByRole('button', { name: 'Delete 2 items' })).toBeEnabled();
  });

  it('with nothing marked, finishes; on Pro and before the plan is known it says nothing of the limit; Escape closes it', async () => {
    const { onClose, unmount } = summary({ state: swipeState({ set: [swipeCard('a', { delete: false })] }) });
    expect(screen.queryByText(/Items stay/)).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Finish' }));
    expect(onClose).toHaveBeenCalledOnce();
    unmount();
    const pro = summary({ left: null });
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
    expect(screen.getByRole('dialog')).toHaveTextContent('Items stay in Recently Deleted for 30 days.');
    pro.unmount();
    const unknown = summary({ left: 1, known: false });
    expect(screen.queryByRole('note')).not.toBeInTheDocument();
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(unknown.onClose).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: 'Close' }));
    expect(unknown.onClose).toHaveBeenCalledTimes(2);
  });
});
