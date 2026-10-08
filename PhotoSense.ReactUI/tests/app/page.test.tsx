import React from 'react';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import HomePage from '../../app/page';
import { saveLastScan } from '../../lib/lastScan';
import type { GroupMode, GroupsPageDto, ScanProgressSnapshotDto, ScanStatusDto } from '../../types';
import { deferred } from '../fakeSignalR';
import { group, groupsPage, member, photo } from '../fixtures';

const api = vi.hoisted(() => ({
  useGroups: vi.fn(), useScanStatus: vi.fn(), useScanProgress: vi.fn(), connectLogStream: vi.fn(), startScan: vi.fn(), browseFolders: vi.fn(),
  setKept: vi.fn(), removePhoto: vi.fn(), removeDuplicates: vi.fn(), prefetchGroups: vi.fn(), openInViewer: vi.fn(), clearResults: vi.fn(), fetchGroupTotals: vi.fn(), retryNow: vi.fn(),
}));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

// Two groups: a picture with two copies, and another with one.
const copyA1 = member({ id: 'a1', fileName: 'IMG_4198 (1).JPG' }, 'identical', 'Same quality; kept the one with the plainer name');
const copyA2 = member({ id: 'a2', fileName: 'IMG_4198.HEIC', format: 'HEIC', fileSizeBytes: 3_046_000 }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');
const groupA = group({ key: 'gA', keeper: photo({ id: 'a', fileName: 'IMG_4198.JPG' }), members: [copyA1, copyA2], reclaimableBytes: 9_139_000 });
const groupB = group({ key: 'gB', keeper: photo({ id: 'b', fileName: 'IMG_5000.JPG' }), members: [member({ id: 'b1', fileName: 'IMG_5000 (1).JPG' })], reclaimableBytes: 6_093_000 });
const burst = group({ key: 'gS', keeper: photo({ id: 's', fileName: 'IMG_7001.JPG' }), members: [member({ id: 's1', fileName: 'IMG_7002.JPG' }, 'similar')], reclaimableBytes: 0 });

type Answer = { data?: GroupsPageDto; error?: Error };
/** What the service has for each request; a test replaces these, and may change them as it goes on. */
let groupsFor: (mode: GroupMode, filter: string, page: number, hideKept: boolean) => Answer;
let statusNow: { data?: ScanStatusDto; error?: Error };
let progressFor: (instanceId?: string) => { data?: ScanProgressSnapshotDto };
let emitLog: (line: string) => void;
const stopLogs = vi.fn();
const snapshot = (overrides: Partial<ScanProgressSnapshotDto> = {}): ScanProgressSnapshotDto => ({
  instanceId: 'scan-7', startedUtc: '2026-10-07T12:00:00Z', primaryTotal: 80, primaryProcessed: 34, secondaryTotal: 20, secondaryProcessed: 0,
  primaryPercent: 42.5, secondaryPercent: 0, overallPercent: 34, ...overrides,
});

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  stopLogs.mockReset();
  groupsFor = mode => ({ data: mode === 'similar' ? groupsPage([burst], {}, 'similar') : groupsPage([groupA, groupB]) });
  statusNow = { data: { instanceId: '', totalPhotos: 6941 } };
  progressFor = () => ({});
  api.useGroups.mockImplementation((mode, filter, page, hideKept) => groupsFor(mode, filter, page, hideKept));
  api.useScanStatus.mockImplementation(() => statusNow);
  api.useScanProgress.mockImplementation(id => progressFor(id));
  api.connectLogStream.mockImplementation(onLine => { emitLog = onLine; return stopLogs; });
  api.setKept.mockResolvedValue(undefined);
  api.openInViewer.mockResolvedValue(undefined);
  api.retryNow.mockResolvedValue(undefined);
  api.removePhoto.mockResolvedValue({ companions: 0 });
  api.removeDuplicates.mockResolvedValue({ removed: 0, bytes: 0, skipped: 0, companions: 0, problems: [] });
  api.clearResults.mockResolvedValue({ forgotten: 0 });
  api.startScan.mockResolvedValue({ instanceId: 'scan-7' });
  api.fetchGroupTotals.mockResolvedValue({ duplicates: 263, similar: 170 });
  api.browseFolders.mockResolvedValue({ path: null, parent: null, folders: [] });
});

function open() {
  const view = render(<HomePage />);
  // Stands in for the listings being fetched again: the page is drawn afresh from what the service now has.
  return { ...view, refresh: () => view.rerender(<HomePage />) };
}

const mainRequest = () => api.useGroups.mock.calls.filter(c => c[0] === currentMode).pop();
let currentMode: GroupMode = 'duplicates';
beforeEach(() => { currentMode = 'duplicates'; });
const button = (name: string | RegExp) => screen.getByRole('button', { name });
const click = (name: string | RegExp) => userEvent.click(button(name));
const tab = (name: RegExp) => screen.getByRole('tab', { name });
const desk = () => screen.getByLabelText('Review');
const gallery = () => screen.getByLabelText('Groups');
const tile = (fileName: string) => within(gallery()).getByRole('button', { name: new RegExp(fileName.replace(/[.()]/g, '\\$&')) });
const win = () => screen.getByRole('dialog', { name: /^Compare / });
const question = () => screen.getByRole('alertdialog');
const toast = (name: string | RegExp) => screen.findByText(name);
const setupHeading = () => screen.queryByRole('heading', { name: 'Find duplicate photos and videos' });

describe('which screen is shown', () => {
  it('is the setup screen when nothing has been scanned yet', () => {
    statusNow = { data: { instanceId: '', totalPhotos: 0 } };
    open();
    expect(setupHeading()).toBeInTheDocument();
    expect(screen.queryByRole('tab')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Scanned folders')).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back to results' })).not.toBeInTheDocument();
  });

  it('is the results when there are some, with the folders named from the last scan', async () => {
    saveLastScan({ root: 'C:\\Users\\jamie\\Phone Pictures', second: '', recursive: true });
    statusNow = { data: { instanceId: 'x', totalPhotos: 6941, completed: '2026-10-07T16:25:00' } };
    open();
    expect(setupHeading()).not.toBeInTheDocument();
    expect(screen.getByLabelText('Scanned folders')).toHaveTextContent(/^Phone Pictures · 6,941 files · scanned /);

    await click('Change or rescan');
    expect(setupHeading()).toBeInTheDocument();
    expect(screen.queryByLabelText('Scanned folders')).not.toBeInTheDocument();
    await click('Back to results');
    expect(tab(/Duplicates/)).toBeInTheDocument();
  });

  it('is the results, saying it is loading, until the service has said what it holds', () => {
    statusNow = {};
    groupsFor = () => ({});
    open();
    expect(setupHeading()).not.toBeInTheDocument();
    expect(gallery()).toHaveTextContent('Loading…');
    expect(screen.getAllByText('Loading…')).toHaveLength(2);
    expect(button('Delete all duplicates')).toBeDisabled();
    expect(screen.getByText('Select a group to review')).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});

describe('when the service stops answering', () => {
  it.each([
    ['the groups', () => { groupsFor = () => ({ data: groupsPage([groupA]), error: new TypeError('Failed to fetch') }); }],
    ['what it holds', () => { statusNow = { data: statusNow.data, error: new TypeError('Failed to fetch') }; }],
  ])('says so across the top when %s cannot be fetched, with a way to try again', async (_what, fail) => {
    fail();
    open();
    expect(screen.getByRole('alert')).toHaveTextContent('The PhotoSense service is not answering, so what is shown here may be out of date.');
    expect(tile('IMG_4198.JPG')).toBeInTheDocument();      // what was on screen stays
    await click('Retry');
    expect(api.retryNow).toHaveBeenCalledOnce();
  });
});

describe('the results', () => {
  it('count both kinds of group, sum up what can be removed, and show the first group', () => {
    const { container } = open();
    expect(tab(/Duplicates/)).toHaveTextContent('Duplicates2');
    expect(tab(/Similar/)).toHaveTextContent('Similar1');
    expect(tab(/Duplicates/)).toHaveAttribute('aria-selected', 'true');
    expect(container).toHaveTextContent('3 duplicate files taking 14.5 MB. The best copy of each picture or video is kept.');
    expect(container).toHaveTextContent('Deleted files move to _PhotoSense_Removed and can be moved back.');
    expect(tile('IMG_4198.JPG')).toHaveAttribute('aria-pressed', 'true');
    expect(desk()).toHaveTextContent('IMG_4198.JPG2 duplicates · 8.7 MB to free');
    expect(api.useGroups.mock.calls[0]).toEqual(['duplicates', '', 1, false]);
    expect(api.useGroups.mock.calls[1]).toEqual(['similar', '', 1, false]);
  });

  it('say so when there is one duplicate, or none', () => {
    groupsFor = mode => ({ data: mode === 'similar' ? undefined : groupsPage([groupB]) });
    const { container, refresh } = open();
    expect(container).toHaveTextContent('1 duplicate file taking 5.8 MB.');
    expect(tab(/Similar/)).toHaveTextContent(/^Similar$/);
    groupsFor = () => ({ data: groupsPage([]) });
    refresh();
    expect(screen.getByText('No duplicates waiting to be removed.')).toBeInTheDocument();
    expect(button('Delete all duplicates')).toBeDisabled();
  });

  it('show the group that is picked and its first copy; when it goes, the one that took its place', async () => {
    const third = group({ key: 'gC', keeper: photo({ id: 'c', fileName: 'IMG_6000.JPG' }) });
    let listed = [groupA, groupB, third];
    groupsFor = () => ({ data: groupsPage(listed) });
    const { refresh } = open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Show IMG_4198.HEIC' }));
    expect(desk()).toHaveTextContent('Copy 2 of 2 · HEIC');

    await userEvent.click(tile('IMG_5000.JPG'));
    expect(tile('IMG_5000.JPG')).toHaveAttribute('aria-pressed', 'true');
    expect(desk()).toHaveTextContent('Copy 1 of 1 · JPEG');

    listed = [groupA, third];
    refresh();
    expect(tile('IMG_6000.JPG')).toHaveAttribute('aria-pressed', 'true');
    listed = [groupA];
    refresh();
    expect(tile('IMG_4198.JPG')).toHaveAttribute('aria-pressed', 'true');
  });

  it('are searched, filtered and paged from the first page each time', async () => {
    groupsFor = (_m, _f, page) => ({ data: groupsPage([groupA], { page, totalPages: 3, total: 120 }) });
    open();
    await click('Next');
    expect(mainRequest()).toEqual(['duplicates', '', 2, false]);
    await userEvent.type(screen.getByRole('textbox', { name: 'Search by file name or folder' }), 'trip');
    expect(mainRequest()).toEqual(['duplicates', 'trip', 1, false]);
    await click('Next');
    await userEvent.click(screen.getByRole('switch', { name: /Hide reviewed/ }));
    expect(mainRequest()).toEqual(['duplicates', 'trip', 1, true]);
    await click('Next');
    await click('Prev');
    expect(mainRequest()).toEqual(['duplicates', 'trip', 1, true]);
  });

  it('have the pages either side fetched ahead, so that turning to one is immediate', async () => {
    // As the real listing does, a page that has not changed is the same answer each time it is read.
    const pages = [1, 2, 3].map(page => groupsPage([groupA], { page, totalPages: 3, total: 120 }));
    groupsFor = (_m, _f, page) => ({ data: pages[page - 1] });
    open();
    // The first page has no page before it.
    expect(api.prefetchGroups.mock.calls).toEqual([['duplicates', '', 2, false]]);
    await click('Next');
    expect(api.prefetchGroups.mock.calls.slice(1)).toEqual([['duplicates', '', 3, false], ['duplicates', '', 1, false]]);
    await click('Next');
    // Nor the last one a page after it.
    expect(api.prefetchGroups.mock.calls.slice(3)).toEqual([['duplicates', '', 2, false]]);
  });

  it('step back when the page they were on no longer exists', async () => {
    let totalPages = 3;
    groupsFor = (_m, _f, page) => ({ data: groupsPage(page <= totalPages ? [groupA] : [], { page, totalPages }) });
    const { refresh } = open();
    await click('Next');
    await click('Next');
    totalPages = 2;
    refresh();
    await waitFor(() => expect(mainRequest()).toEqual(['duplicates', '', 2, false]));
    totalPages = 0;
    refresh();
    await waitFor(() => expect(mainRequest()).toEqual(['duplicates', '', 1, false]));
  });
});

describe('similar shots', () => {
  it('are a listing of their own, and the one being left is not shown in place of the one being fetched', async () => {
    let fetched = false;
    const duplicates = groupsPage([groupA, groupB]);
    // As the real listing does, the last answer stays until the new one arrives.
    groupsFor = mode => ({ data: mode === 'similar' && fetched ? groupsPage([burst], {}, 'similar') : duplicates });
    const { refresh } = open();
    await userEvent.click(tile('IMG_5000.JPG'));

    currentMode = 'similar';
    await userEvent.click(tab(/Similar/));
    expect(mainRequest()).toEqual(['similar', '', 1, false]);
    expect(tab(/Similar/)).toHaveAttribute('aria-selected', 'true');
    expect(gallery()).toHaveTextContent('Loading…');
    expect(tab(/Duplicates/)).toHaveTextContent('Duplicates2');
    expect(tab(/Similar/)).toHaveTextContent(/^Similar$/);
    expect(screen.getByText(/^Similar shots are burst frames or edited versions\. They are never removed all at once/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Delete all duplicates' })).not.toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();

    fetched = true;
    refresh();
    expect(tab(/Similar/)).toHaveTextContent('Similar1');
    expect(tab(/Duplicates/)).toHaveTextContent('Duplicates2');
    expect(desk()).toHaveTextContent('IMG_7001.JPG1 similar shot');

    currentMode = 'duplicates';
    await userEvent.click(tab(/Duplicates/));
    expect(tile('IMG_4198.JPG')).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('scanning', () => {
  it('shows the scan running, then what it found and the results', async () => {
    statusNow = { data: { instanceId: '', totalPhotos: 0 } };
    let done = false;
    progressFor = id => (id === 'scan-7' ? { data: snapshot(done ? { completedUtc: '2026-10-07T12:01:00Z', overallPercent: 100 } : {}) } : {});
    const { refresh, unmount } = open();
    act(() => emitLog('11:59:00 Info left over from before'));

    await userEvent.type(screen.getByLabelText('Root folder'), 'C:\\photos');
    await click('Scan');
    expect(await screen.findByLabelText('Scan progress')).toHaveTextContent('34%34 of 100 files');
    expect(api.useScanProgress).toHaveBeenLastCalledWith('scan-7');
    act(() => emitLog('12:00:01 Info Scanning 100 files'));
    expect(within(screen.getByLabelText('Latest log lines')).getAllByRole('listitem').map(l => l.textContent)).toEqual(['12:00:01 Info Scanning 100 files']);
    act(() => { for (let n = 0; n < 250; n++) emitLog(`line ${n}`); });
    await click('Show full log');
    expect(within(screen.getByLabelText('Full log')).getAllByRole('listitem')).toHaveLength(200);

    done = true;
    statusNow = { data: { instanceId: 'scan-7', totalPhotos: 100 } };
    refresh();
    expect(await toast('Scan finished: 263 duplicate groups and 170 similar groups')).toBeInTheDocument();
    expect(setupHeading()).not.toBeInTheDocument();
    expect(screen.getByLabelText('Scanned folders')).toHaveTextContent('photos · 100 files');
    expect(api.retryNow).toHaveBeenCalledOnce();
    // Said once, however often the page is drawn again.
    refresh();
    expect(api.fetchGroupTotals).toHaveBeenCalledOnce();

    expect(stopLogs).not.toHaveBeenCalled();
    unmount();
    expect(stopLogs).toHaveBeenCalledOnce();
  });

  it.each([
    [{ duplicates: 1, similar: 1 }, 'Scan finished: 1 duplicate group and 1 similar group'],
    [undefined, 'Scan finished.'],
  ])('says what it can when the scan finishes', async (totals, message) => {
    if (totals) api.fetchGroupTotals.mockResolvedValue(totals); else api.fetchGroupTotals.mockRejectedValue(new Error('gone'));
    progressFor = id => (id ? { data: snapshot({ completedUtc: '2026-10-07T12:01:00Z' }) } : {});
    open();
    await click('Change or rescan');
    await userEvent.type(screen.getByLabelText('Root folder'), 'C:\\photos');
    await click('Scan');
    expect(await toast(message)).toBeInTheDocument();
  });

  it('keeps a failed start as an error, in the toast and in the list', async () => {
    statusNow = { data: { instanceId: '', totalPhotos: 0 } };
    open();
    await click('Scan');
    expect(await screen.findByRole('alert')).toHaveTextContent('Choose a root folder to scan.Kept in Errors');
    await click('1 error');
    expect(screen.getByRole('dialog', { name: 'Errors' })).toHaveTextContent('Choose a root folder to scan.');
  });
});

describe('the menu and the errors', () => {
  it('open one at a time, and the menu leads to the theme, the errors, the folders and clearing', async () => {
    api.openInViewer.mockRejectedValue('No application could be started');
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Compare IMG_4198.JPG' }));
    await userEvent.click(within(win()).getByRole('button', { name: 'Open in default viewer' }));
    await toast('No application could be started');
    fireEvent.keyDown(window, { key: 'Escape' });

    await click('Menu');
    expect(screen.getByRole('menu')).toBeInTheDocument();
    await click('Light');
    expect(document.documentElement).toHaveClass('light');
    await click('Dark');
    await click('1 error');
    expect(screen.queryByRole('menu')).not.toBeInTheDocument();
    const errors = screen.getByRole('dialog', { name: 'Errors' });
    await userEvent.click(within(errors).getByRole('button', { name: 'Clear' }));
    expect(errors).toHaveTextContent('No errors.');

    await click('Menu');
    expect(screen.queryByRole('dialog', { name: 'Errors' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('menuitem', { name: /^Errors/ }));
    expect(screen.getByRole('dialog', { name: 'Errors' })).toBeInTheDocument();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog', { name: 'Errors' })).not.toBeInTheDocument();

    await click('Menu');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Change folders or rescan' }));
    expect(setupHeading()).toBeInTheDocument();
  });

  it('clears every error at once', async () => {
    api.setKept.mockRejectedValue(new Error('The database is busy'));
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Keep this copy' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Dismiss' }));
    await click('1 error');
    await click('Clear all');
    expect(screen.queryByRole('button', { name: /error/ })).not.toBeInTheDocument();
  });
});

describe('keeping and viewing', () => {
  it('marks a copy to keep, or stops keeping it, and says which', async () => {
    let kept = false;
    groupsFor = () => ({ data: groupsPage([group({ ...groupA, members: [{ ...copyA1, photo: { ...copyA1.photo, kept } }, copyA2] })]) });
    const { refresh } = open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Keep this copy' }));
    expect(api.setKept).toHaveBeenLastCalledWith('a1', true);
    expect(await toast('Keeping IMG_4198 (1).JPG. Bulk deletion will skip it.')).toBeInTheDocument();
    kept = true;
    refresh();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Stop keeping this copy' }));
    expect(api.setKept).toHaveBeenLastCalledWith('a1', false);
    expect(await toast('Stopped keeping IMG_4198 (1).JPG.')).toBeInTheDocument();
  });

  it('lets nothing else be started while one change is being made', async () => {
    const saving = deferred();
    api.setKept.mockReturnValue(saving.promise);
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Keep this copy' }));
    expect(button('Delete all duplicates')).toBeDisabled();
    expect(within(desk()).getByRole('button', { name: /^Delete this copy/ })).toBeDisabled();
    await act(async () => saving.resolve());
    expect(button('Delete all duplicates')).toBeEnabled();
  });

  it('compares the copy beside the original in a window, which closes with its group', async () => {
    let listed = [groupA, groupB];
    groupsFor = () => ({ data: groupsPage(listed) });
    const { refresh } = open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Show IMG_4198.HEIC' }));
    await userEvent.click(within(desk()).getByRole('button', { name: 'Compare IMG_4198.HEIC' }));
    expect(screen.getByRole('dialog', { name: 'Compare IMG_4198.HEIC' })).toBeInTheDocument();
    await userEvent.click(within(win()).getByRole('button', { name: 'Keep this copy too' }));
    expect(api.setKept).toHaveBeenLastCalledWith('a2', true);

    // With that copy gone the window shows the one that is left.
    listed = [group({ ...groupA, members: [copyA1] }), groupB];
    refresh();
    expect(screen.getByRole('dialog', { name: 'Compare IMG_4198 (1).JPG' })).toBeInTheDocument();
    listed = [groupB];
    refresh();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('removes a copy from the window after its own second question, and closes on request', async () => {
    api.removePhoto.mockResolvedValue({ companions: 2 });
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Compare IMG_4198.JPG' }));
    await userEvent.click(within(win()).getByRole('button', { name: /^Delete this copy ·/ }));
    await userEvent.click(within(win()).getByRole('button', { name: 'Delete this copy' }));
    expect(api.removePhoto).toHaveBeenCalledExactlyOnceWith('a1');
    expect(await toast('Moved IMG_4198 (1).JPG and 2 linked files to _PhotoSense_Removed. IMG_4198.JPG stays where it is.')).toBeInTheDocument();
    await userEvent.click(within(win()).getByRole('button', { name: 'Close' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('deleting', () => {
  it('one copy from the desk: asks first, naming the copy and the original that stays', async () => {
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: /^Delete this copy/ }));
    expect(screen.getByRole('alertdialog', { name: 'Delete IMG_4198 (1).JPG?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('IMG_4198 (1).JPG (5.8 MB) will be removed.The original, IMG_4198.JPG, stays in C:\\photos\\2024.');
    expect(question()).toHaveTextContent('Files go to _PhotoSense_Removed inside the scanned folder. Moving a file back restores it.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    expect(api.removePhoto).toHaveBeenCalledExactlyOnceWith('a1');
    expect(await toast('Moved IMG_4198 (1).JPG to _PhotoSense_Removed. IMG_4198.JPG stays where it is.')).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('the one duplicate of a picture, in the same folder under another name: says which file goes and which stays, before and after', async () => {
    // Two files holding the same bytes, side by side in one folder. Only the names tell them apart.
    const folder = 'C:\\Users\\jamie\\Phone Pictures';
    const twin = group({
      key: 'g0377', reclaimableBytes: 15_521_465,
      keeper: photo({ id: 'k', fileName: 'IMG_0377.JPG', folder, fileSizeBytes: 15_521_465 }),
      members: [member({ id: 'c', fileName: 'IMG_0648.JPG', folder, fileSizeBytes: 15_521_465 })],
    });
    groupsFor = () => ({ data: groupsPage([twin]) });
    api.removeDuplicates.mockResolvedValue({ removed: 1, bytes: 15_521_465, skipped: 0, companions: 0, problems: [] });
    open();
    expect(desk().querySelector('[data-file="original"]')).toHaveTextContent(`IMG_0377.JPG${folder}`);
    expect(desk().querySelector('[data-file="copy"]')).toHaveTextContent('IMG_0648.JPGIn the same folder as the original');

    await userEvent.click(within(desk()).getByRole('button', { name: 'Delete this duplicate · 14.8 MB' }));
    expect(screen.getByRole('alertdialog', { name: 'Delete IMG_0648.JPG?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent(`IMG_0648.JPG (14.8 MB) will be removed.The original, IMG_0377.JPG, stays in ${folder}.`);
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith('g0377', 'duplicates');
    expect(await toast('Moved IMG_0648.JPG (14.8 MB) to _PhotoSense_Removed. IMG_0377.JPG stays where it is.')).toBeInTheDocument();
  });

  it('all the similar shots of one group: asks first, saying they are not copies, and takes only that group\'s', async () => {
    const shots = group({
      key: 'gS', reclaimableBytes: 18_279_000, keeper: photo({ id: 's', fileName: 'IMG_7001.JPG' }),
      members: [2, 3, 4].map(n => member({ id: `s${n}`, fileName: `IMG_700${n}.JPG` }, 'similar')),
    });
    groupsFor = mode => ({ data: mode === 'similar' ? groupsPage([shots], {}, 'similar') : groupsPage([groupA, groupB]) });
    api.removeDuplicates.mockResolvedValue({ removed: 3, bytes: 18_279_000, skipped: 0, companions: 0, problems: [] });
    open();
    currentMode = 'similar';
    await userEvent.click(tab(/Similar/));

    // One shot, or all of them: both are offered, and "this copy" asks about the one beside the best shot only.
    expect(within(desk()).getByRole('button', { name: 'Delete this copy · 5.8 MB' })).toBeEnabled();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Delete all 3 copies · 17.4 MB' }));
    expect(screen.getByRole('alertdialog', { name: 'Delete the 3 shots similar to IMG_7001.JPG?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('3 files taking 17.4 MB will be removed: IMG_7002.JPG, IMG_7003.JPG, IMG_7004.JPG.The best shot, IMG_7001.JPG, stays in C:\\photos\\2024.These are different shots or edited versions, not copies of one file.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith('gS', 'similar');
    expect(api.removePhoto).not.toHaveBeenCalled();
    expect(await toast('Moved 3 similar shots (17.4 MB) to _PhotoSense_Removed. IMG_7001.JPG stays where it is.')).toBeInTheDocument();
  });

  it('a similar shot: says that the best shot stays', async () => {
    open();
    currentMode = 'similar';
    await userEvent.click(tab(/Similar/));
    await userEvent.click(within(desk()).getByRole('button', { name: /^Delete this copy/ }));
    expect(question()).toHaveTextContent('IMG_7002.JPG (5.8 MB) will be removed.The best shot, IMG_7001.JPG, stays in C:\\photos\\2024.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 1 file' }));
    expect(api.removePhoto).toHaveBeenCalledExactlyOnceWith('s1');
    expect(await toast('Moved IMG_7002.JPG to _PhotoSense_Removed. IMG_7001.JPG stays where it is.')).toBeInTheDocument();
  });

  it('the duplicates of one group: counts what goes and what is kept', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 2, bytes: 9_139_000, skipped: 0, companions: 0, problems: [] });
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Delete all 2 copies · 8.7 MB' }));
    expect(screen.getByRole('alertdialog', { name: 'Delete the 2 duplicates of IMG_4198.JPG?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('2 files taking 8.7 MB will be removed: IMG_4198 (1).JPG, IMG_4198.HEIC.The original, IMG_4198.JPG, stays in C:\\photos\\2024.');
    expect(question()).not.toHaveTextContent('marked keep');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 2 files' }));
    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith('gA', 'duplicates');
    expect(await toast('Moved 2 duplicates (8.7 MB) to _PhotoSense_Removed. IMG_4198.JPG stays where it is.')).toBeInTheDocument();
  });

  it('the duplicates of one group: names the first few when there are many', async () => {
    const copies = [1, 2, 3, 4, 5, 6].map(n => member({ id: `a${n}`, fileName: `IMG_4198 (${n}).JPG` }));
    groupsFor = () => ({ data: groupsPage([group({ ...groupA, members: copies, reclaimableBytes: 36_558_000 })]) });
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: /^Delete all 6 copies/ }));
    expect(question()).toHaveTextContent('6 files taking 34.9 MB will be removed: IMG_4198 (1).JPG, IMG_4198 (2).JPG, IMG_4198 (3).JPG, IMG_4198 (4).JPG and 2 more.The original');
  });

  it.each([
    [1, '1 copy you marked keep is skipped.', 'Delete 2 files'],
    [2, '2 copies you marked keep are skipped.', 'Delete 1 file'],
  ])('says how many kept copies a group deletion skips (%d)', async (keptCount, line, label) => {
    const copies = [copyA1, copyA2, member({ id: 'a3', fileName: 'IMG_4198 (3).JPG' })].map((m, i) => ({ ...m, photo: { ...m.photo, kept: i < keptCount } }));
    groupsFor = () => ({ data: groupsPage([group({ ...groupA, members: copies })]) });
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: /^Delete (all 2 copies|this duplicate)/ }));
    expect(question()).toHaveTextContent(line);
    expect(within(question()).getByRole('button', { name: label })).toBeEnabled();
    await userEvent.click(within(question()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(api.removeDuplicates).not.toHaveBeenCalled();
  });

  it('all duplicates: reports what was moved and what was left alone', async () => {
    api.removeDuplicates.mockResolvedValue({ removed: 1, bytes: 6_093_000, skipped: 1, companions: 1, problems: ['Changed since the scan, left alone: C:\\photos\\x.jpg'] });
    open();
    await click('Delete all duplicates');
    expect(screen.getByRole('alertdialog', { name: 'Delete all duplicates?' })).toBeInTheDocument();
    expect(question()).toHaveTextContent('3 files taking 14.5 MB will be removed.The best copy of each picture or video stays. Copies you marked keep are skipped.');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(api.removeDuplicates).toHaveBeenCalledExactlyOnceWith(undefined, 'duplicates');
    expect(await toast('Moved 1 duplicate (5.8 MB) and 1 linked file to _PhotoSense_Removed')).toBeInTheDocument();
    expect(screen.getByText('1 left alone. Changed since the scan, left alone: C:\\photos\\x.jpg')).toBeInTheDocument();
  });

  it('all duplicates: says what was left alone even with no reason, and nothing when nothing happened', async () => {
    api.removeDuplicates.mockResolvedValueOnce({ removed: 0, bytes: 0, skipped: 3, companions: 0, problems: [] });
    open();
    await click('Delete all duplicates');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(await toast('3 left alone.')).toBeInTheDocument();
    await click('Delete all duplicates');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(screen.queryByText(/^Moved/)).not.toBeInTheDocument();
  });

  it('leaves the question open, and says why, when the removal failed', async () => {
    api.removeDuplicates.mockRejectedValue(new Error('The database is busy'));
    open();
    await click('Delete all duplicates');
    await userEvent.click(within(question()).getByRole('button', { name: 'Delete 3 files' }));
    expect(await toast('The database is busy')).toBeInTheDocument();
    expect(within(question()).getByRole('button', { name: 'Delete 3 files' })).toBeEnabled();
  });
});

describe('clearing the results', () => {
  const ask = async () => {
    await click('Menu');
    await userEvent.click(screen.getByRole('menuitem', { name: 'Clear results' }));
    return screen.getByRole('alertdialog', { name: 'Clear the scan results?' });
  };

  it.each([
    [6941, 'Cleared the results: 6,941 files forgotten. Your photos were not touched.'],
    [1, 'Cleared the results: 1 file forgotten. Your photos were not touched.'],
  ])('forgets everything once confirmed, closing whatever was open', async (forgotten, message) => {
    api.clearResults.mockResolvedValue({ forgotten });
    open();
    await userEvent.click(within(desk()).getByRole('button', { name: 'Compare IMG_4198.JPG' }));
    fireEvent.keyDown(window, { key: 'a' });
    const dialog = await ask();
    expect(dialog).toHaveTextContent('PhotoSense forgets every file it has scanned');
    expect(dialog).toHaveTextContent('Your photos stay exactly where they are');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Clear results' }));
    expect(api.clearResults).toHaveBeenCalledOnce();
    expect(await toast(message)).toBeInTheDocument();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('forgets nothing when the question is cancelled', async () => {
    open();
    await userEvent.click(within(await ask()).getByRole('button', { name: 'Cancel' }));
    expect(api.clearResults).not.toHaveBeenCalled();
  });
});
