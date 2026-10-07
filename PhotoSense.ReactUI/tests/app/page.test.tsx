import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import HomePage from '../../app/page';
import type { GroupMode, GroupsPageDto, ScanProgressSnapshotDto } from '../../types';
import { deferred } from '../fakeSignalR';
import { group, groupsPage, member, photo } from '../fixtures';

const api = vi.hoisted(() => ({
  useGroups: vi.fn(), useScanProgress: vi.fn(), connectLogStream: vi.fn(), startScan: vi.fn(),
  setKept: vi.fn(), removePhoto: vi.fn(), removeDuplicates: vi.fn(), openInViewer: vi.fn(), clearResults: vi.fn(),
}));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

// Two groups: a picture with two copies, and another with one.
const copyA1 = member({ id: 'a1', fileName: 'IMG_4198 (1).JPG' }, 'identical', 'Same quality; kept the one with the plainer name');
const copyA2 = member({ id: 'a2', fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');
const groupA = group({ key: 'gA', keeper: photo({ id: 'a', fileName: 'IMG_4198.JPG' }), members: [copyA1, copyA2], reclaimableBytes: 9_139_000 });
const groupB = group({ key: 'gB', keeper: photo({ id: 'b', fileName: 'IMG_5000.JPG' }), members: [member({ id: 'b1', fileName: 'IMG_5000 (1).JPG' })], reclaimableBytes: 6_093_000 });

type Answer = { data?: GroupsPageDto; error?: Error };
/** What the service has for each request for groups; a test replaces it, and may change it as the test goes on. */
let groupsFor: (mode: GroupMode, filter: string, page: number, hideKept: boolean) => Answer;
let progressFor: (instanceId?: string) => { data?: ScanProgressSnapshotDto };
let emitLog: (line: string) => void;
const stopLogs = vi.fn();

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  stopLogs.mockReset();
  groupsFor = () => ({ data: groupsPage([groupA, groupB]) });
  progressFor = () => ({});
  api.useGroups.mockImplementation((mode, filter, page, hideKept) => groupsFor(mode, filter, page, hideKept));
  api.useScanProgress.mockImplementation(id => progressFor(id));
  api.connectLogStream.mockImplementation(onLine => { emitLog = onLine; return stopLogs; });
  api.setKept.mockResolvedValue(undefined);
  api.openInViewer.mockResolvedValue(undefined);
  api.removePhoto.mockResolvedValue({ companions: 0 });
  api.removeDuplicates.mockResolvedValue({ removed: 0, bytes: 0, skipped: 0, companions: 0, problems: [] });
  api.startScan.mockResolvedValue({ instanceId: 'scan-7' });
  api.clearResults.mockResolvedValue({ forgotten: 0 });
});

function open() {
  const view = render(<HomePage />);
  // Stands in for the listing being fetched again: the page is drawn afresh from what the service now has.
  return { ...view, refresh: () => view.rerender(<HomePage />) };
}

const lastGroupsRequest = () => api.useGroups.mock.calls[api.useGroups.mock.calls.length - 1];
const button = (name: string | RegExp) => screen.getByRole('button', { name });
const click = (name: string | RegExp) => userEvent.click(button(name));
const photoWindow = () => screen.getByRole('dialog');
const question = () => screen.getByRole('alertdialog');
const inWindow = (name: string | RegExp) => within(photoWindow()).getByRole('button', { name });
const inList = (fileName: string) => button(new RegExp(`^${fileName.replace('.', '\\.')}\\d+ (duplicates?|similar shots?|cop)`));

describe('before there is anything to show', () => {
  it('says the groups are loading', () => {
    groupsFor = () => ({});
    open();

    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.getByText('No duplicates waiting to be removed.')).toBeInTheDocument();
    expect(screen.getByText('No duplicates to show.')).toBeInTheDocument();
    expect(screen.getByText('Select a group to review')).toBeInTheDocument();
    expect(button('Delete all duplicates')).toBeDisabled();
    expect(screen.getByText('Ready.')).toBeInTheDocument();
    expect(screen.getByText('0.0%')).toBeInTheDocument();
    expect(lastGroupsRequest()).toEqual(['duplicates', '', 1, false]);
    expect(api.useScanProgress).toHaveBeenLastCalledWith(undefined);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says so when the service cannot be reached', () => {
    groupsFor = () => ({ error: new TypeError('Failed to fetch') });
    open();
    expect(screen.getByText('Cannot reach the PhotoSense server.')).toBeInTheDocument();
    expect(screen.getByRole('alert')).toHaveTextContent('The PhotoSense service is not answering');
    expect(button('Delete all duplicates')).toBeDisabled();
  });

  it('says so too when the service stops answering while groups are on screen, and stops saying it once it answers again', () => {
    let failing = true;
    // As the real listing does, the last answer stays on screen when a later request fails.
    groupsFor = () => ({ data: groupsPage([groupA, groupB]), error: failing ? new TypeError('Failed to fetch') : undefined });
    const { refresh } = open();

    expect(screen.getByRole('alert')).toHaveTextContent('The PhotoSense service is not answering, so what is shown here may be out of date.');
    expect(screen.getByText('2 groups')).toBeInTheDocument();

    failing = false;
    refresh();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('says there is nothing to remove when no duplicates were found', () => {
    groupsFor = () => ({ data: groupsPage([]) });
    open();
    expect(screen.getByText('0 groups')).toBeInTheDocument();
    expect(screen.getByText('No duplicates waiting to be removed.')).toBeInTheDocument();
    expect(button('Delete all duplicates')).toBeDisabled();
  });
});

describe('the groups', () => {
  it('are summed up, listed, and the first is shown for review', () => {
    const { container } = open();

    expect(container).toHaveTextContent('3 duplicate files taking 14.5 MB. The best copy of each picture or video is kept.');
    expect(screen.getByText('2 groups')).toBeInTheDocument();
    expect(inList('IMG_4198.JPG')).toHaveClass('border-emerald-400');
    expect(inList('IMG_5000.JPG')).toHaveClass('border-transparent');
    expect(screen.getByRole('heading', { name: 'Duplicates of this picture (2)' })).toBeInTheDocument();
    expect(button('Delete all duplicates')).toBeEnabled();
    expect(screen.queryByText(/^Page /)).not.toBeInTheDocument();
  });

  it('are counted in the singular when there is one of each', () => {
    groupsFor = () => ({ data: groupsPage([groupB]) });
    const { container } = open();
    expect(container).toHaveTextContent('1 duplicate file taking 5.8 MB.');
    expect(screen.getByText('1 group')).toBeInTheDocument();
  });

  it('show the one that is picked, and the first again once that one is gone', async () => {
    let listed = [groupA, groupB];
    groupsFor = () => ({ data: groupsPage(listed) });
    const { refresh } = open();

    await userEvent.click(inList('IMG_5000.JPG'));
    expect(inList('IMG_5000.JPG')).toHaveClass('border-emerald-400');
    expect(screen.getByRole('heading', { name: 'Duplicates of this picture (1)' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'IMG_5000.JPG' })).toBeInTheDocument();

    listed = [groupA];
    refresh();
    expect(screen.getByRole('img', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
    expect(inList('IMG_4198.JPG')).toHaveClass('border-emerald-400');
  });
});

describe('duplicates and similar shots', () => {
  const burst = group({ key: 'gS', keeper: photo({ id: 's', fileName: 'IMG_7001.JPG' }), members: [member({ id: 's1', fileName: 'IMG_7002.JPG' }, 'similar')], reclaimableBytes: 0 });

  it('are two listings, and the one being left is not shown in place of the one being fetched', async () => {
    let similarFetched = false;
    const duplicates = groupsPage([groupA, groupB], { totalPages: 2 });
    // As the real listing does, the last answer stays until the new one arrives.
    groupsFor = mode => ({ data: mode === 'similar' && similarFetched ? groupsPage([burst], {}, 'similar') : duplicates });
    const { refresh } = open();
    expect(button('Duplicates')).toHaveClass('ring-emerald-500');
    expect(button('Similar')).not.toHaveClass('ring-amber-500');
    await click('Next');
    await userEvent.click(inList('IMG_5000.JPG'));

    await click('Similar');
    expect(lastGroupsRequest()).toEqual(['similar', '', 1, false]);
    expect(button('Similar')).toHaveClass('ring-amber-500');
    expect(button('Duplicates')).not.toHaveClass('ring-emerald-500');
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.getByText('No similar shots found.')).toBeInTheDocument();
    expect(screen.getByText(/^Look-alikes that are not the same file/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete all duplicates' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText(/Hide reviewed/)).not.toBeInTheDocument();

    similarFetched = true;
    refresh();
    expect(screen.getByText('1 group')).toBeInTheDocument();
    expect(inList('IMG_7001.JPG')).toHaveTextContent('1 similar shot');
    expect(screen.getByRole('heading', { name: 'Similar shots (1)' })).toBeInTheDocument();
    expect(screen.getByText('The best of these similar shots.')).toBeInTheDocument();

    await click('Duplicates');
    expect(lastGroupsRequest()).toEqual(['duplicates', '', 1, false]);
    expect(screen.getByLabelText(/Hide reviewed/)).toBeInTheDocument();
    // The selection made before does not carry over: the first group is shown.
    expect(inList('IMG_4198.JPG')).toHaveClass('border-emerald-400');
  });

  it('are searched, and reviewed ones hidden, from the first page each time', async () => {
    groupsFor = (_mode, _filter, page) => ({ data: groupsPage([groupA], { page, totalPages: 3 }) });
    open();

    await click('Next');
    expect(lastGroupsRequest()).toEqual(['duplicates', '', 2, false]);
    await userEvent.type(screen.getByPlaceholderText('Search by file name or folder'), 'trip');
    expect(lastGroupsRequest()).toEqual(['duplicates', 'trip', 1, false]);

    await click('Next');
    await userEvent.click(screen.getByLabelText(/Hide reviewed/));
    expect(lastGroupsRequest()).toEqual(['duplicates', 'trip', 1, true]);
    await userEvent.click(screen.getByLabelText(/Hide reviewed/));
    expect(lastGroupsRequest()).toEqual(['duplicates', 'trip', 1, false]);
  });
});

describe('paging', () => {
  it('goes forward and back between the first page and the last', async () => {
    groupsFor = (_mode, _filter, page) => ({ data: groupsPage([groupA], { page, totalPages: 3, total: 120 }) });
    open();
    expect(screen.getByText('120 groups')).toBeInTheDocument();
    expect(screen.getByText('Page 1 of 3')).toBeInTheDocument();
    expect(button('Prev')).toBeDisabled();

    await click('Next');
    expect(screen.getByText('Page 2 of 3')).toBeInTheDocument();
    expect(button('Prev')).toBeEnabled();
    await click('Next');
    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();
    expect(button('Next')).toBeDisabled();

    await click('Prev');
    expect(lastGroupsRequest()).toEqual(['duplicates', '', 2, false]);
  });

  it('steps back when the page it was on no longer exists', async () => {
    // Removing the last groups of the last page leaves fewer pages than before.
    let totalPages = 3;
    groupsFor = (_mode, _filter, page) => ({ data: groupsPage(page <= totalPages ? [groupA] : [], { page: Math.min(page, Math.max(1, totalPages)), totalPages }) });
    const { refresh } = open();
    await click('Next');
    await click('Next');
    expect(screen.getByText('Page 3 of 3')).toBeInTheDocument();

    totalPages = 2;
    refresh();
    await waitFor(() => expect(lastGroupsRequest()).toEqual(['duplicates', '', 2, false]));
    expect(screen.getByText('Page 2 of 2')).toBeInTheDocument();

    totalPages = 0;
    refresh();
    await waitFor(() => expect(lastGroupsRequest()).toEqual(['duplicates', '', 1, false]));
    expect(screen.queryByText(/^Page /)).not.toBeInTheDocument();
  });
});

describe('scanning', () => {
  it('follows the scan that was started', async () => {
    progressFor = id => (id === 'scan-7'
      ? { data: { instanceId: 'scan-7', startedUtc: '2026-10-06T12:00:00Z', primaryTotal: 80, primaryProcessed: 34, secondaryTotal: 20, secondaryProcessed: 0, primaryPercent: 42.5, secondaryPercent: 0, overallPercent: 34 } }
      : {});
    open();

    await userEvent.type(screen.getByLabelText('Root folder path'), 'C:\\photos');
    await click('Scan');

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true, startOver: false });
    expect(await screen.findByText('34.0%')).toBeInTheDocument();
    expect(api.useScanProgress).toHaveBeenLastCalledWith('scan-7');
    expect(screen.getByText('Primary 34/80 • Secondary 0/20')).toBeInTheDocument();
  });

  it('shows the log as it is written, the newest two hundred lines of it', () => {
    const { unmount } = open();
    expect(api.connectLogStream).toHaveBeenCalledOnce();

    act(() => emitLog('12:00:01 Info Scanning 12 files'));
    expect(screen.getByText('Ready.')).toBeInTheDocument();
    expect(screen.getByText('12:00:01 Info Scanning 12 files')).toBeInTheDocument();

    act(() => { for (let n = 0; n < 250; n++) emitLog(`line ${n}`); });
    expect(screen.queryByText('Ready.')).not.toBeInTheDocument();
    expect(screen.queryByText('line 49')).not.toBeInTheDocument();
    expect(screen.getByText('line 50')).toBeInTheDocument();
    expect(screen.getByText('line 249')).toBeInTheDocument();

    expect(stopLogs).not.toHaveBeenCalled();
    unmount();
    expect(stopLogs).toHaveBeenCalledOnce();
  });
});

describe('looking at a file', () => {
  it('opens the original in a window of its own, and closes it again', async () => {
    open();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(screen.getByTitle('Open this file'));
    expect(screen.getByRole('dialog', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
    expect(within(photoWindow()).getByText('ORIGINAL — THE BEST COPY, KEPT')).toBeInTheDocument();
    expect(within(photoWindow()).queryByRole('button', { name: 'This copy' })).not.toBeInTheDocument();

    await userEvent.click(inWindow('Close'));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens a copy beside the original it was matched with', async () => {
    open();
    await click('Open IMG_4198.HEIC');

    expect(screen.getByRole('dialog', { name: 'IMG_4198.HEIC' })).toBeInTheDocument();
    expect(within(photoWindow()).getByText('SAME PICTURE')).toBeInTheDocument();
    expect(within(photoWindow()).getByText('Original preferred: Opens everywhere: JPEG rather than HEIC')).toBeInTheDocument();

    await userEvent.click(inWindow('Original'));
    expect(screen.getByRole('dialog', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
  });

  it('opens the copy of whichever group is being reviewed', async () => {
    open();
    await userEvent.click(inList('IMG_5000.JPG'));
    await click('Open IMG_5000 (1).JPG');
    expect(screen.getByRole('dialog', { name: 'IMG_5000 (1).JPG' })).toBeInTheDocument();
  });

  it('marks a copy to keep, and stops keeping one that is marked', async () => {
    let kept = false;
    groupsFor = () => ({ data: groupsPage([group({ ...groupA, members: [{ ...copyA1, photo: { ...copyA1.photo, kept } }, copyA2] })]) });
    const { refresh } = open();
    await click('Open IMG_4198 (1).JPG');

    await userEvent.click(inWindow('Keep this copy too'));
    expect(api.setKept).toHaveBeenLastCalledWith('a1', true);

    kept = true;
    refresh();
    expect(button('Open IMG_4198 (1).JPG')).toHaveTextContent('KEEPING');
    await userEvent.click(inWindow('Stop keeping this copy'));
    expect(api.setKept).toHaveBeenLastCalledWith('a1', false);
    expect(api.setKept).toHaveBeenCalledTimes(2);
  });

  it('lets nothing else be started while one change is being made', async () => {
    const saving = deferred();
    api.setKept.mockReturnValue(saving.promise);
    open();
    await click('Open IMG_4198 (1).JPG');

    await userEvent.click(inWindow('Keep this copy too'));
    expect(inWindow('Keep this copy too')).toBeDisabled();
    expect(inWindow('Delete this copy')).toBeDisabled();
    expect(button('Delete all duplicates')).toBeDisabled();

    await act(async () => saving.resolve());
    expect(inWindow('Keep this copy too')).toBeEnabled();
    expect(button('Delete all duplicates')).toBeEnabled();
  });

  it('hands the file to the system\'s own viewer', async () => {
    open();
    await click('Open IMG_4198 (1).JPG');
    await userEvent.click(inWindow('Open in default viewer'));
    expect(api.openInViewer).toHaveBeenCalledExactlyOnceWith('a1');
    expect(screen.queryByText(/no longer there/)).not.toBeInTheDocument();
  });

  it.each([
    [new Error('The file is no longer there. Scan again.'), 'The file is no longer there. Scan again.'],
    ['No application could be started for IMG_4198 (1).JPG', 'No application could be started for IMG_4198 (1).JPG'],
  ])('says why when the viewer could not be opened, until the message is dismissed', async (failure, message) => {
    api.openInViewer.mockRejectedValue(failure);
    open();
    await click('Open IMG_4198 (1).JPG');

    await userEvent.click(inWindow('Open in default viewer'));
    const toast = await screen.findByRole('button', { name: message });
    expect(toast).toHaveClass('bg-red-900');
    expect(inWindow('Open in default viewer')).toBeEnabled();

    await userEvent.click(toast);
    expect(screen.queryByRole('button', { name: message })).not.toBeInTheDocument();
  });

  it.each([
    [0, 'Moved IMG_4198 (1).JPG to _PhotoSense_Removed'],
    [2, 'Moved IMG_4198 (1).JPG and 2 linked files to _PhotoSense_Removed'],
  ])('removes the one copy, closes the window and says where it went', async (companions, message) => {
    api.removePhoto.mockResolvedValue({ companions });
    open();
    await click('Open IMG_4198 (1).JPG');

    await userEvent.click(inWindow('Delete this copy'));
    expect(api.removePhoto).not.toHaveBeenCalled();
    await userEvent.click(inWindow('Delete'));

    expect(api.removePhoto).toHaveBeenCalledExactlyOnceWith('a1');
    expect(await screen.findByRole('button', { name: message })).toHaveClass('bg-emerald-900');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('keeps the window open, and says why, when the copy could not be removed', async () => {
    api.removePhoto.mockRejectedValue(new Error('Changed since the scan, left alone'));
    open();
    await click('Open IMG_4198 (1).JPG');
    await userEvent.click(inWindow('Delete this copy'));
    await userEvent.click(inWindow('Delete'));

    expect(await screen.findByRole('button', { name: 'Changed since the scan, left alone' })).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'IMG_4198 (1).JPG' })).toBeInTheDocument();
  });

  it('closes the window once the copy in it is no longer among the duplicates', async () => {
    let members = [copyA1, copyA2];
    groupsFor = () => ({ data: groupsPage([group({ ...groupA, members }), groupB]) });
    const { refresh } = open();
    await click('Open IMG_4198 (1).JPG');

    members = [copyA2];
    refresh();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('closes the window once the group it belongs to is gone, and not before', async () => {
    let listed = [groupA, groupB];
    groupsFor = () => ({ data: groupsPage(listed) });
    const { refresh } = open();
    await userEvent.click(screen.getByTitle('Open this file'));

    listed = [groupA];
    refresh();
    expect(screen.getByRole('dialog', { name: 'IMG_4198.JPG' })).toBeInTheDocument();

    listed = [groupB];
    refresh();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('deleting the duplicates of one group', () => {
  it('asks first, saying how much goes and where', async () => {
    open();
    await click('Delete these 2 duplicates · 8.7 MB');

    expect(screen.getByRole('alertdialog', { name: 'Delete the duplicates of IMG_4198.JPG?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('2 files (8.7 MB) will be moved out of your photos.');
    expect(question()).toHaveTextContent('The files go to a _PhotoSense_Removed folder inside the scanned folder');
    expect(within(question()).getByRole('button', { name: 'Delete 2 files' })).toBeEnabled();
    expect(api.removeDuplicates).not.toHaveBeenCalled();
  });

  it('removes them once confirmed and says what was moved', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 2, bytes: 9_139_000, skipped: 0, companions: 0, problems: [] });
    open();
    await click('Delete these 2 duplicates · 8.7 MB');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 files' }));

    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith('gA');
    expect(await screen.findByRole('button', { name: 'Moved 2 duplicates (8.7 MB) to _PhotoSense_Removed' })).toHaveClass('bg-emerald-900');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('counts only the copies that are not marked keep', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 1, bytes: 6_093_000, skipped: 0, companions: 1, problems: [] });
    const partlyKept = group({ ...groupA, members: [copyA1, { ...copyA2, photo: { ...copyA2.photo, kept: true } }], reclaimableBytes: 6_093_000 });
    groupsFor = () => ({ data: groupsPage([partlyKept]) });
    open();

    await click('Delete this duplicate · 5.8 MB');
    expect(question()).toHaveTextContent('1 file (5.8 MB) will be moved out of your photos.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));

    expect(await screen.findByRole('button', { name: 'Moved 1 duplicate (5.8 MB) and 1 linked file to _PhotoSense_Removed' })).toBeInTheDocument();
  });

  it('removes nothing when the question is cancelled, by the button or by Escape', async () => {
    open();
    await click('Delete these 2 duplicates · 8.7 MB');
    await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await click('Delete these 2 duplicates · 8.7 MB');
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(api.removeDuplicates).not.toHaveBeenCalled();
  });
});

describe('clearing the results', () => {
  const ask = async () => {
    await click('Clear results');
    return screen.getByRole('alertdialog', { name: 'Clear the scan results?' });
  };

  it('asks first, saying that only the record goes and the photos stay', async () => {
    open();
    const dialog = await ask();
    expect(dialog).toHaveTextContent('PhotoSense forgets every file it has scanned');
    expect(dialog).toHaveTextContent('Your photos stay exactly where they are');
    expect(api.clearResults).not.toHaveBeenCalled();
  });

  it.each([
    [6941, 'Cleared the results: 6,941 scanned files forgotten. Your photos were not touched.'],
    [1, 'Cleared the results: 1 scanned file forgotten. Your photos were not touched.'],
  ])('forgets everything once confirmed, and goes back to the first page with nothing chosen', async (forgotten, message) => {
    let cleared = false;
    api.clearResults.mockImplementation(async () => { cleared = true; return { forgotten }; });
    groupsFor = (_mode, _filter, page) => ({ data: cleared ? groupsPage([]) : groupsPage([groupA, groupB], { page, totalPages: 2 }) });
    open();
    await click('Next');
    await userEvent.click(inList('IMG_5000.JPG'));
    await click('Open IMG_5000 (1).JPG');
    await userEvent.click(inWindow('Close'));

    await userEvent.click(within(await ask()).getByRole('button', { name: 'Clear results' }));

    expect(api.clearResults).toHaveBeenCalledOnce();
    expect(await screen.findByRole('button', { name: new RegExp('^' + message.replace(/[.,]/g, '.')) })).toHaveClass('bg-emerald-900');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(lastGroupsRequest()).toEqual(['duplicates', '', 1, false]);
    expect(screen.getByText('0 groups')).toBeInTheDocument();
    expect(screen.getByText('Select a group to review')).toBeInTheDocument();
  });

  it('closes an open photo window along with the results it came from', async () => {
    open();
    await click('Open IMG_4198 (1).JPG');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    // The question sits over the window; answering it puts both away.
    fireEvent.click(button('Clear results'));
    await userEvent.click(within(question()).getByRole('button', { name: 'Clear results' }));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('forgets nothing when the question is cancelled', async () => {
    open();
    await userEvent.click(within(await ask()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(api.clearResults).not.toHaveBeenCalled();
    expect(screen.getByText('2 groups')).toBeInTheDocument();
  });

  it('says why, and leaves the question open, when a scan is still running', async () => {
    api.clearResults.mockRejectedValue(new Error('A scan is running. Wait for it to finish before clearing its results.'));
    open();
    await userEvent.click(within(await ask()).getByRole('button', { name: 'Clear results' }));

    expect(await screen.findByRole('button', { name: /^A scan is running/ })).toHaveClass('bg-red-900');
    expect(question()).toBeInTheDocument();
    expect(screen.getByText('2 groups')).toBeInTheDocument();
  });

  it('is offered on the Similar tab too, and before anything has been found', async () => {
    groupsFor = () => ({});
    open();
    expect(button('Clear results')).toBeEnabled();
    await click('Similar');
    expect(button('Clear results')).toBeEnabled();
  });
});

describe('deleting all duplicates', () => {
  const confirmAll = async () => {
    await click('Delete all duplicates');
    expect(screen.getByRole('alertdialog', { name: 'Delete all duplicates?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('3 files (14.5 MB) will be moved out of your photos.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
  };

  it('removes every group\'s duplicates, and reports what was moved and what was left alone', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 2, bytes: 9_139_000, skipped: 1, companions: 1, problems: ['Changed since the scan, left alone: C:\\photos\\IMG_5000 (1).JPG', 'another'] });
    open();
    await confirmAll();

    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith(undefined);
    expect(await screen.findByRole('button', { name: 'Moved 2 duplicates (8.7 MB) and 1 linked file to _PhotoSense_Removed' })).toHaveClass('bg-emerald-900');
    expect(screen.getByRole('button', { name: '1 left alone. Changed since the scan, left alone: C:\\photos\\IMG_5000 (1).JPG' })).toHaveClass('bg-red-900');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('reports what was left alone even when no reason came with it', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 0, bytes: 0, skipped: 3, companions: 0, problems: [] });
    open();
    await confirmAll();

    expect(await screen.findByRole('button', { name: '3 left alone.' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Moved/ })).not.toBeInTheDocument();
  });

  it('says nothing when there turned out to be nothing to do', async () => {
    open();
    await confirmAll();

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(screen.queryByRole('button', { name: /^Moved|left alone/ })).not.toBeInTheDocument();
  });

  it('shows that it is working, and cannot be answered twice', async () => {
    const removing = deferred<{ removed: number; bytes: number; skipped: number; companions: number; problems: string[] }>();
    api.removeDuplicates.mockReturnValue(removing.promise);
    open();
    await confirmAll();

    expect(within(question()).getByRole('button', { name: 'Working…' })).toBeDisabled();
    expect(within(question()).getByRole('button', { name: 'Cancel' })).toBeDisabled();

    await act(async () => removing.resolve({ removed: 1, bytes: 6_093_000, skipped: 0, companions: 0, problems: [] }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(api.removeDuplicates).toHaveBeenCalledOnce();
  });

  it('leaves the question open, and says why, when the removal failed', async () => {
    api.removeDuplicates.mockRejectedValue(new Error('The database is busy'));
    open();
    await confirmAll();

    expect(await screen.findByRole('button', { name: 'The database is busy' })).toHaveClass('bg-red-900');
    expect(within(question()).getByRole('button', { name: 'Delete 3 files' })).toBeEnabled();
  });
});
