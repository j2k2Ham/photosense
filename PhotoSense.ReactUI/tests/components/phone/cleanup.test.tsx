import { act, fireEvent, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { CleanUpScreen } from '../../../components/phone/CleanUp';
import { useCleanUp } from '../../../lib/cleanup';
import type { Platform } from '../../../lib/phone';
import type { DuplicateGroupDto, GroupMode, ScanStatusDto } from '../../../types';
import { frame, inFrame } from './frame';
import { group, groupsPage, photo } from './groups';

const api = vi.hoisted(() => ({ useGroupsThrough: vi.fn(), removePhoto: vi.fn(), removeDuplicates: vi.fn(), setKept: vi.fn(), undoChange: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const ROOT = '/Photo Library';
// What the service holds, which the mocked calls change as the service would. Like SWR, the groups read are drawn
// again whenever they change.
let groups: Record<GroupMode, DuplicateGroupDto[]>;
let totalPages: number;
let version = 0;
const watching = new Set<() => void>();
function changed() { version++; watching.forEach(f => f()); }
function useVersion() {
  return React.useSyncExternalStore(f => { watching.add(f); return () => watching.delete(f); }, () => version);
}

const sameBytes = () => group(photo('DLWL2718.MP4', { isVideo: true, fileSizeBytes: 4.5 * 1024 ** 2, durationSeconds: 47 }),
  [photo('DLWL2718 (8E01B7C2).MP4', { isVideo: true, fileSizeBytes: 4.5 * 1024 ** 2, durationSeconds: 47 }), photo('DLWL2718 (1).MP4', { isVideo: true, fileSizeBytes: 4.5 * 1024 ** 2, durationSeconds: 47 })], 'identical');

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  groups = {
    duplicates: [
      group(photo('IMG_4299.JPG', { fileSizeBytes: 24 * 1024 ** 2 }), [photo('IMG_4299.HEIC', { folder: `${ROOT}/Butte` })]),
      sameBytes(),
      group(photo('IMG_0712.JPG'), [photo('IMG_0712 (1).JPG', { kept: true }), photo('IMG_0712 (2).JPG', { kept: true })], 'identical'),
    ],
    similar: [group(photo('IMG_5101.HEIC'), [photo('IMG_5102.HEIC'), photo('IMG_5103.HEIC'), photo('IMG_5104.HEIC')], 'similar')],
  };
  totalPages = 1;
  api.useGroupsThrough.mockImplementation((mode: GroupMode) => {
    useVersion();
    return { data: groupsPage(mode, groups[mode], { totalPages, removableCount: 3, reclaimableBytes: 21 * 1024 ** 2 }) };
  });
  // Deleting a copy takes it out of its group, and the group once it has none.
  api.removePhoto.mockImplementation(async (id: string) => {
    for (const mode of ['duplicates', 'similar'] as const)
      groups[mode] = groups[mode].map(g => ({ ...g, members: g.members.filter(m => m.photo.id !== id) })).filter(g => g.members.length > 0);
    changed();
    return { heldAt: 'x', companions: 0, batchId: 'b1' };
  });
  api.removeDuplicates.mockResolvedValue({ removed: 3, bytes: 21 * 1024 ** 2, skipped: 0, companions: 0, problems: [], batchId: 'b2' });
  api.setKept.mockResolvedValue(undefined);
  api.undoChange.mockResolvedValue({ restored: 1, skipped: 0, problems: [] });
});

interface ShowProps { platform?: Platform; tablet?: boolean; status?: ScanStatusDto; root?: string }

/** Clean up in a phone's or tablet's frame, holding its state as PhoneApp does. */
function show({ platform = 'ios', tablet = false, status, root = ROOT }: ShowProps = {}) {
  const notify = vi.fn(), onScanAgain = vi.fn();
  function Holder() {
    const cleanUp = useCleanUp(tablet);
    return <CleanUpScreen cleanUp={cleanUp} root={root} status={status} notify={notify} onScanAgain={onScanAgain} />;
  }
  const view = inFrame(<Holder />, frame(platform, { tablet, form: tablet ? 'tablet' : 'phone' }));
  return { ...view, notify, onScanAgain };
}

const rows = () => within(screen.getByRole('list', { name: 'Groups' })).getAllByRole('button');
const row = (name: string) => within(screen.getByRole('list', { name: 'Groups' })).getByRole('button', { name: new RegExp(`^${name.replace(/[().]/g, '\\$&')}`) });
const sheet = (name: string | RegExp) => screen.getByRole('dialog', { name });
const toastAction = (notify: ReturnType<typeof vi.fn>) => (notify.mock.calls.at(-1)![2] as { run(): void }).run;
const table = (name: string) => within(screen.getByRole('table', { name })).getAllByRole('row').map(r => r.textContent);

describe('Clean up\'s list', () => {
  it('says what deleting the duplicates would free, and lists each group: the one that stays, what there is to delete, and how many copies', async () => {
    const { onScanAgain } = show({ status: { instanceId: 's', completed: new Date().toISOString(), totalPhotos: 6941 } });
    expect(screen.getByRole('heading', { level: 1, name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByText('Copies of the same photo or video')).toBeInTheDocument();
    const card = screen.getByRole('region', { name: 'Duplicates to delete' });
    expect(card).toHaveTextContent('3 duplicates');
    expect(card).toHaveTextContent('They take 21.0 MB. The best copy of each picture or video is kept.');
    expect(rows().map(r => r.textContent)).toEqual([
      'IMG_4299.JPG1 duplicate · 12.0 MB+1', 'DLWL2718.MP42 duplicates · 9.0 MB+2', 'IMG_0712.JPG2 copies, all marked keep+2',
    ]);
    expect(within(row('IMG_0712.JPG')).getByText('2 copies, all marked keep')).toHaveClass('text-keep');
    // A video has no preview: a play mark in its place.
    expect(row('DLWL2718.MP4').querySelector('img')).toBeNull();
    expect(row('IMG_4299.JPG').querySelectorAll('img')[1]).toHaveAttribute('src', 'http://localhost:7071/api/photos/IMG_4299.JPG/thumbnail');
    expect(screen.getByText(/^Scanned 6,941 photos and videos today at/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Scan again' }));
    expect(onScanAgain).toHaveBeenCalledOnce();

    await userEvent.click(screen.getByRole('radio', { name: 'Similar' }));
    expect(screen.getByText('Shots that look alike')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Duplicates to delete' })).not.toBeInTheDocument();
    expect(rows().map(r => r.textContent)).toEqual(['IMG_5101.HEIC3 similar shots+3']);
  });

  it('says one duplicate as one, and nothing of a scan that has not finished', () => {
    api.useGroupsThrough.mockImplementation((mode: GroupMode) => ({ data: groupsPage(mode, groups[mode], { removableCount: 1, reclaimableBytes: 1024 }) }));
    show({ status: { instanceId: 's', totalPhotos: 0 } });
    expect(screen.getByRole('region', { name: 'Duplicates to delete' })).toHaveTextContent('1 duplicateIt takes 1 KB.');
    expect(screen.queryByText(/^Scanned/)).not.toBeInTheDocument();
  });

  it('says when there is nothing left, where what was deleted waits, in the platform\'s words, and nothing before the groups are read', async () => {
    groups = { duplicates: [], similar: [] };
    api.useGroupsThrough.mockImplementation((mode: GroupMode) => ({ data: groupsPage(mode, []) }));
    const { again } = show({ platform: 'android' });
    expect(screen.getByText('No duplicates left. What you deleted waits in Pictures/PhotoSense Removed until you delete it permanently.')).toBeInTheDocument();
    expect(screen.queryByRole('region', { name: 'Duplicates to delete' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Similar' }));
    expect(screen.getByText('No similar shots left to review.')).toBeInTheDocument();
    api.useGroupsThrough.mockReturnValue({});
    again(<CleanUpScreen cleanUp={{ ...stub(), data: undefined }} notify={vi.fn()} onScanAgain={vi.fn()} />);
    expect(screen.queryByText(/left/)).not.toBeInTheDocument();
  });

  it('reads more groups as the end of the list comes into view', () => {
    let seen: (entries: { isIntersecting: boolean }[]) => void = () => {};
    const disconnect = vi.fn();
    vi.stubGlobal('IntersectionObserver', class { constructor(f: typeof seen) { seen = f; } observe() {} disconnect = disconnect; });
    totalPages = 2;
    show();
    act(() => seen([{ isIntersecting: false }]));
    expect(api.useGroupsThrough).toHaveBeenLastCalledWith('duplicates', 1);
    act(() => seen([{ isIntersecting: true }]));
    expect(api.useGroupsThrough).toHaveBeenLastCalledWith('duplicates', 2);
    expect(disconnect).toHaveBeenCalled();
  });
});

/** A Clean up state with nothing in it, for drawing the screen as it is before anything is read. */
function stub() {
  return { mode: 'duplicates' as const, items: [], more: false, copy: 0, turnTo: vi.fn(), showMore: vi.fn(), review: vi.fn(), chooseCopy: vi.fn() };
}

describe('a group\'s review', () => {
  it('on a phone is pushed over the list: the one that stays beside the copy, what differs and what is the same, and Back', async () => {
    show();
    await userEvent.click(row('IMG_4299.JPG'));
    expect(screen.getByRole('region', { name: 'Review' })).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'Groups' })).not.toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toBeInTheDocument();
    expect(screen.getByText('1 duplicate · 12.0 MB to free')).toBeInTheDocument();
    expect(screen.getByText('Same picture')).toBeInTheDocument();
    expect(screen.getByText('The same picture, as JPG and as HEIC.')).toBeInTheDocument();
    const original = screen.getByRole('button', { name: 'See IMG_4299.JPG full screen' });
    expect(original).toHaveTextContent('OriginalJPG · 24.0 MB');
    expect(screen.getByRole('button', { name: 'See IMG_4299.HEIC full screen' })).toHaveTextContent('Copy 1 of 1HEIC · 12.0 MB');
    expect(screen.getByText('Stays in Photo Library')).toHaveClass('text-keep');
    expect(screen.getByText('In Photo Library › Butte')).toBeInTheDocument();
    // One copy: nothing to choose between.
    expect(screen.queryByRole('group', { name: 'Copies' })).not.toBeInTheDocument();
    expect(table('What differs')).toEqual(['OriginalThis copy', 'NameIMG_4299.JPGIMG_4299.HEIC', 'FormatJPGHEIC', 'File size24.0 MB12.0 MB', 'AlbumPhoto LibraryPhoto Library › Butte']);
    expect(table('The same in both')).toEqual(['TakenDec 27, 2023, 11:34 PM', 'PlaceAnaconda, Montana, US', 'Pixel size4032 × 3024']);
    expect(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Keep this copy too' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(screen.getByRole('list', { name: 'Groups' })).toBeInTheDocument();
  });

  it('chooses among several copies, and a video\'s pictures are wider', async () => {
    show();
    await userEvent.click(row('DLWL2718.MP4'));
    expect(screen.getByText('Identical file')).toBeInTheDocument();
    expect(screen.getByText('Same bytes, saved three times.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'See DLWL2718.MP4 full screen' })).toHaveClass('aspect-[16/11]');
    const copies = within(screen.getByRole('group', { name: 'Copies' }));
    expect(copies.getByRole('button', { name: 'Copy 1' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(copies.getByRole('button', { name: 'Copy 2' }));
    expect(screen.getByRole('button', { name: 'See DLWL2718 (1).MP4 full screen' })).toHaveTextContent('Copy 2 of 2');
    expect(table('The same in both').at(-1)).toBe('Video4032 × 3024 · 0:47');
  });

  it('for similar shots names the best shot and each shot, and offers to keep a shot', async () => {
    show();
    await userEvent.click(screen.getByRole('radio', { name: 'Similar' }));
    await userEvent.click(row('IMG_5101.HEIC'));
    expect(screen.getByText('3 similar shots')).toBeInTheDocument();
    expect(screen.getByText('A burst frame or an edited version.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'See IMG_5101.HEIC full screen' })).toHaveTextContent('Best shot');
    expect(screen.getByRole('button', { name: 'See IMG_5102.HEIC full screen' })).toHaveTextContent('Shot 1 of 3');
    expect(within(screen.getByRole('group', { name: 'Shots' })).getAllByRole('button').map(b => b.textContent)).toEqual(['Shot 1', 'Shot 2', 'Shot 3']);
    expect(table('What differs')[0]).toBe('OriginalThis shot');
    await userEvent.click(screen.getByRole('button', { name: 'Keep this shot too' }));
    expect(api.setKept).toHaveBeenCalledWith('IMG_5102.HEIC', true);
  });

  it('says when every copy is marked keep, and a copy marked keep cannot be deleted until it is unmarked', async () => {
    const { notify } = show();
    await userEvent.click(row('IMG_0712.JPG'));
    expect(screen.getByText('2 copies, all marked keep')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete IMG_0712 (1).JPG' })).toBeDisabled();
    await userEvent.click(screen.getByRole('button', { name: 'Marked keep. Tap to unmark' }));
    expect(api.setKept).toHaveBeenCalledWith('IMG_0712 (1).JPG', false);
    expect(notify).toHaveBeenCalledWith('IMG_0712 (1).JPG is no longer marked keep.', 'ok');
  });

  it('marks a copy keep, saying Delete all duplicates skips it, and says what went wrong when it cannot', async () => {
    const { notify } = show();
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'Keep this copy too' }));
    expect(notify).toHaveBeenLastCalledWith('IMG_4299.HEIC is marked keep. Delete all duplicates skips it.', 'ok');
    api.setKept.mockRejectedValueOnce(new Error('The service is busy.'));
    await userEvent.click(screen.getByRole('button', { name: 'Keep this copy too' }));
    expect(notify).toHaveBeenLastCalledWith('The service is busy.', 'error');
    api.setKept.mockRejectedValueOnce('no answer');
    await userEvent.click(screen.getByRole('button', { name: 'Keep this copy too' }));
    expect(notify).toHaveBeenLastCalledWith('no answer', 'error');
  });
});

describe('deleting on a phone', () => {
  it('asks first, naming the file that stays and the file that goes; then says both, goes back to the list, and Undo puts the copy back', async () => {
    const { notify } = show();
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    const asked = sheet('Delete IMG_4299.HEIC?');
    expect(asked).toHaveTextContent('IMG_4299.JPG stays, in Photo Library.');
    expect(asked).toHaveTextContent('IMG_4299.HEIC, in Photo Library › Butte, goes to PhotoSense Removed, where Undo can still bring it back.');
    await userEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
    expect(api.removePhoto).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    await userEvent.click(within(sheet('Delete IMG_4299.HEIC?')).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    expect(api.removePhoto).toHaveBeenCalledWith('IMG_4299.HEIC');
    expect(notify).toHaveBeenLastCalledWith('IMG_4299.HEIC deleted. IMG_4299.JPG stays.', 'ok', expect.objectContaining({ label: 'Undo' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('list', { name: 'Groups' })).toBeInTheDocument();

    const undo = toastAction(notify);
    api.undoChange.mockResolvedValueOnce({ restored: 1, skipped: 0, problems: [] })
      .mockResolvedValueOnce({ restored: 0, skipped: 1, problems: ['Moved since.'] })
      .mockResolvedValueOnce({ restored: 0, skipped: 1, problems: [] })
      .mockRejectedValueOnce(new Error('No such change.'));
    await act(async () => undo());
    expect(api.undoChange).toHaveBeenCalledWith('b1');
    expect(notify).toHaveBeenLastCalledWith('Put IMG_4299.HEIC back where it was.', 'ok');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('1 could not be put back. Moved since.', 'error');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('1 could not be put back.', 'error');
    await act(async () => undo());
    expect(notify).toHaveBeenLastCalledWith('No such change.', 'error');
  });

  it('stays on a group with copies left, on the copy that took the deleted one\'s place; a removal that moved nothing offers no Undo', async () => {
    api.removePhoto.mockImplementationOnce(async (id: string) => {
      groups.duplicates[1] = { ...groups.duplicates[1], members: groups.duplicates[1].members.filter(m => m.photo.id !== id) };
      changed();
      return { heldAt: null, companions: 0, batchId: null };
    });
    const { notify } = show();
    await userEvent.click(row('DLWL2718.MP4'));
    await userEvent.click(within(screen.getByRole('group', { name: 'Copies' })).getByRole('button', { name: 'Copy 2' }));
    await userEvent.click(screen.getByRole('button', { name: 'Delete DLWL2718 (1).MP4' }));
    await userEvent.click(within(sheet('Delete DLWL2718 (1).MP4?')).getByRole('button', { name: 'Delete DLWL2718 (1).MP4' }));
    expect(notify).toHaveBeenLastCalledWith('DLWL2718 (1).MP4 deleted. DLWL2718.MP4 stays.', 'ok', undefined);
    expect(screen.getByRole('button', { name: 'See DLWL2718 (8E01B7C2).MP4 full screen' })).toHaveTextContent('Copy 1 of 1');
  });

  it('says what went wrong when a copy cannot be deleted, and leaves the question up', async () => {
    api.removePhoto.mockRejectedValueOnce(new Error('The file has changed since it was scanned, so it was left alone. Scan again.'));
    const { notify } = show();
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    await userEvent.click(within(sheet('Delete IMG_4299.HEIC?')).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    expect(notify).toHaveBeenLastCalledWith('The file has changed since it was scanned, so it was left alone. Scan again.', 'error');
    expect(sheet('Delete IMG_4299.HEIC?')).toBeInTheDocument();
  });

  it('asks before deleting every duplicate, then says how many went, with Undo, and how many were left alone', async () => {
    const { notify } = show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete all duplicates' }));
    const asked = sheet('Delete all duplicates?');
    expect(asked).toHaveTextContent('3 copies go to PhotoSense Removed (21.0 MB).');
    expect(asked).toHaveTextContent('The best copy of each picture or video stays. Copies marked keep are skipped.');
    expect(asked).toHaveTextContent('Similar shots are not included.');
    api.removeDuplicates.mockResolvedValueOnce({ removed: 3, bytes: 1, skipped: 1, companions: 2, problems: ['IMG_1.JPG is open elsewhere.'], batchId: 'b2' });
    await userEvent.click(within(asked).getByRole('button', { name: 'Delete 3 duplicates' }));
    expect(api.removeDuplicates).toHaveBeenCalledWith(undefined, 'duplicates');
    expect(notify.mock.calls).toEqual([
      ['3 duplicates and 2 linked files deleted. The best copy of each stays.', 'ok', expect.objectContaining({ label: 'Undo' })],
      ['1 left alone. IMG_1.JPG is open elsewhere.', 'error'],
    ]);
    api.undoChange.mockResolvedValueOnce({ restored: 3, skipped: 0, problems: [] });
    await act(async () => (notify.mock.calls[0][2] as { run(): void }).run());
    expect(notify).toHaveBeenLastCalledWith('Put the 3 duplicates back where they were.', 'ok');

    notify.mockClear();
    api.removeDuplicates.mockResolvedValueOnce({ removed: 0, bytes: 0, skipped: 2, companions: 0, problems: [], batchId: null });
    await userEvent.click(screen.getByRole('button', { name: 'Delete all duplicates' }));
    await userEvent.click(within(sheet('Delete all duplicates?')).getByRole('button', { name: 'Delete 3 duplicates' }));
    expect(notify.mock.calls).toEqual([['2 left alone.', 'error']]);

    notify.mockClear();
    await userEvent.click(screen.getByRole('button', { name: 'Delete all duplicates' }));
    await userEvent.click(within(sheet('Delete all duplicates?')).getByRole('button', { name: 'Delete 3 duplicates' }));
    expect(notify.mock.calls).toEqual([['3 duplicates deleted. The best copy of each stays.', 'ok', expect.objectContaining({ label: 'Undo' })]]);
  });
});

describe('the picture full screen', () => {
  it('flips between the original and the copy by a tap or the switch, says where each is, and Escape puts it away', async () => {
    show();
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'See IMG_4299.HEIC full screen' }));
    let shown = sheet('IMG_4299.HEIC, full screen');
    expect(shown).toHaveTextContent('In Photo Library › Butte');
    expect(within(shown).getByRole('radio', { name: 'This copy' })).toHaveAttribute('aria-checked', 'true');
    expect(within(shown).getByText('Copy 1 of 1')).toHaveClass('bg-white/[.22]');
    // One copy: no stepping between copies.
    expect(within(shown).queryByRole('button', { name: 'Next copy' })).not.toBeInTheDocument();
    await userEvent.click(within(shown).getByRole('button', { name: 'Show the original' }));
    shown = sheet('IMG_4299.JPG, full screen');
    expect(shown).toHaveTextContent('Stays in Photo Library');
    expect(within(shown).getAllByText('Original')[0]).toHaveClass('bg-keep');
    await userEvent.click(within(shown).getByRole('button', { name: 'Show the copy' }));
    await userEvent.click(within(sheet('IMG_4299.HEIC, full screen')).getByRole('radio', { name: 'Original' }));
    expect(sheet('IMG_4299.JPG, full screen')).toBeInTheDocument();
    await userEvent.keyboard('a');
    expect(sheet('IMG_4299.JPG, full screen')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'See IMG_4299.JPG full screen' }));
    await userEvent.click(within(sheet('IMG_4299.JPG, full screen')).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('steps through several copies, round from the last to the first, showing each copy', async () => {
    show();
    await userEvent.click(row('DLWL2718.MP4'));
    await userEvent.click(screen.getByRole('button', { name: 'See DLWL2718.MP4 full screen' }));
    await userEvent.click(within(sheet('DLWL2718.MP4, full screen')).getByRole('button', { name: 'Next copy' }));
    const second = sheet('DLWL2718 (1).MP4, full screen');
    expect(second).toHaveTextContent('Copy 2 of 2');
    await userEvent.click(within(second).getByRole('button', { name: 'Next copy' }));
    await userEvent.click(within(sheet('DLWL2718 (8E01B7C2).MP4, full screen')).getByRole('button', { name: 'Previous copy' }));
    expect(sheet('DLWL2718 (1).MP4, full screen')).toBeInTheDocument();
  });

  it('names similar shots as shots', async () => {
    show();
    await userEvent.click(screen.getByRole('radio', { name: 'Similar' }));
    await userEvent.click(row('IMG_5101.HEIC'));
    await userEvent.click(screen.getByRole('button', { name: 'See IMG_5101.HEIC full screen' }));
    const shown = sheet('IMG_5101.HEIC, full screen');
    expect(within(shown).getAllByText('Best shot')).toHaveLength(2);
    expect(within(shown).getByRole('radio', { name: 'This shot' })).toBeInTheDocument();
    await userEvent.click(within(shown).getByRole('button', { name: 'Next shot' }));
    expect(sheet('IMG_5103.HEIC, full screen')).toHaveTextContent('Shot 2 of 3');
    expect(within(sheet('IMG_5103.HEIC, full screen')).getByRole('button', { name: 'Previous shot' })).toBeInTheDocument();
  });

  it('deletes the copy after the question, and is put away with it', async () => {
    const { notify } = show();
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'See IMG_4299.HEIC full screen' }));
    await userEvent.click(within(sheet('IMG_4299.HEIC, full screen')).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    await userEvent.click(within(sheet('Delete IMG_4299.HEIC?')).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    expect(notify).toHaveBeenLastCalledWith('IMG_4299.HEIC deleted. IMG_4299.JPG stays.', 'ok', expect.anything());
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('Clean up on a tablet', () => {
  it('shows the list and the first group\'s review side by side, the gear with the review, and the next group once one is done', async () => {
    show({ platform: 'android', tablet: true });
    expect(screen.getByRole('heading', { level: 1, name: 'Clean up' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'IMG_4299.JPG' })).toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: 'Settings' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(row('IMG_4299.JPG')).toHaveAttribute('aria-current', 'true');
    expect(table('What differs').at(-1)).toBe('FolderPhonePhone/Butte');
    await userEvent.click(row('IMG_0712.JPG'));
    expect(row('IMG_0712.JPG')).toHaveAttribute('aria-current', 'true');
    await userEvent.click(row('IMG_4299.JPG'));
    await userEvent.click(screen.getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    await userEvent.click(within(sheet('Delete IMG_4299.HEIC?')).getByRole('button', { name: 'Delete IMG_4299.HEIC' }));
    expect(screen.getByRole('heading', { name: 'DLWL2718.MP4' })).toBeInTheDocument();
  });

  it('says there is nothing left beside an empty list, and nothing before the groups are read', async () => {
    groups = { duplicates: [], similar: [] };
    const { again } = show({ tablet: true });
    expect(screen.getAllByRole('heading', { name: 'Clean up', level: 1 })).toHaveLength(2);
    expect(screen.getAllByText('No duplicates left. What you deleted waits in PhotoSense Removed until you delete it permanently.')).toHaveLength(2);
    await userEvent.click(screen.getByRole('radio', { name: 'Similar' }));
    expect(screen.getAllByText('No similar shots left to review.')).toHaveLength(2);
    again(<CleanUpScreen cleanUp={{ ...stub(), data: undefined }} notify={vi.fn()} onScanAgain={vi.fn()} />);
    expect(screen.queryByText(/left/)).not.toBeInTheDocument();
  });

  it('says "?" for a format that could not be told, beside the picture and full screen', async () => {
    groups.duplicates = [group(photo('IMG_1.JPG'), [photo('IMG_1', { format: undefined })])];
    show();
    await userEvent.click(row('IMG_1.JPG'));
    const copy = screen.getByRole('button', { name: 'See IMG_1 full screen' });
    expect(copy).toHaveTextContent('? · 12.0 MB');
    await userEvent.click(copy);
    expect(sheet('IMG_1, full screen')).toHaveTextContent('? · 12.0 MB');
  });

  it('names a file by its folder\'s name until the library\'s own folder is known', async () => {
    show({ root: '' });
    fireEvent.click(row('IMG_4299.JPG'));
    expect(await screen.findByText('In Butte')).toBeInTheDocument();
  });
});
