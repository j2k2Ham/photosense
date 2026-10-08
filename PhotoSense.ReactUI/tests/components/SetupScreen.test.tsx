import React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SetupScreen } from '../../components/SetupScreen';
import { loadLastScan, saveLastScan } from '../../lib/lastScan';
import type { ScanProgressSnapshotDto } from '../../types';
import { deferred } from '../fakeSignalR';

const api = vi.hoisted(() => ({ startScan: vi.fn(), browseFolders: vi.fn() }));
vi.mock('../../lib/apiClient', () => ({ startScan: api.startScan, browseFolders: api.browseFolders }));

const pictures = 'C:\\Users\\jamie\\Pictures';

beforeEach(() => {
  api.startScan.mockReset();
  api.startScan.mockResolvedValue({ instanceId: 'scan-7' });
  api.browseFolders.mockReset();
  api.browseFolders.mockImplementation(async (path?: string) => (path
    ? { path, parent: 'C:\\Users\\jamie', folders: [] }
    : { path: null, parent: null, folders: [{ name: 'Pictures', path: pictures }] }));
});

const progress = (overrides: Partial<ScanProgressSnapshotDto> = {}): ScanProgressSnapshotDto => ({
  instanceId: 'scan-7', startedUtc: '2026-10-07T12:00:00Z', primaryTotal: 0, primaryProcessed: 0, secondaryTotal: 0, secondaryProcessed: 0,
  primaryPercent: 0, secondaryPercent: 0, overallPercent: 0, ...overrides,
});

function show(props: Partial<React.ComponentProps<typeof SetupScreen>> = {}) {
  const handlers = { onStarted: vi.fn(), onBack: vi.fn(), notify: vi.fn() };
  const view = render(<SetupScreen scanning={false} log={[]} hasResults={false} {...handlers} {...props} />);
  return { ...handlers, ...view };
}
const root = () => screen.getByLabelText<HTMLInputElement>('Root folder');
const second = () => screen.getByLabelText<HTMLInputElement>(/^Secondary folder/);
const scan = () => screen.getByRole('button', { name: /^(Scan|Starting…|Scanning…)$/ });

describe('choosing what to scan', () => {
  it('says what the app does and asks for a folder', () => {
    show();
    expect(screen.getByRole('heading', { name: 'Find duplicate photos and videos' })).toBeInTheDocument();
    expect(screen.getByText(/keeps the best copy of each, and lets you check the rest before anything moves/)).toBeInTheDocument();
    expect(root()).toHaveValue('');
    expect(screen.getByLabelText(/^Recursive/)).toBeChecked();
    expect(screen.getByLabelText(/^Start over/)).not.toBeChecked();
    expect(screen.queryByRole('button', { name: 'Back to results' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Scan progress')).not.toBeInTheDocument();
  });

  it('will not scan without a root folder, and says so', async () => {
    const { notify } = show();
    await userEvent.type(root(), '   ');
    await userEvent.click(scan());
    expect(notify).toHaveBeenCalledExactlyOnceWith('Choose a root folder to scan.', 'error');
    expect(api.startScan).not.toHaveBeenCalled();
  });

  it('scans the folders as typed, remembers them, and reports the scan it started', async () => {
    const { onStarted, notify } = show();
    await userEvent.type(root(), '  C:\\photos  ');
    await userEvent.type(second(), ' D:\\backup ');
    await userEvent.click(screen.getByLabelText(/^Recursive/));
    await userEvent.click(scan());

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: 'D:\\backup', recursive: false, startOver: false });
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
    expect(loadLastScan()).toEqual({ root: 'C:\\photos', second: 'D:\\backup', recursive: false });
    expect(notify).not.toHaveBeenCalled();
  });

  it('scans a folder once when it is given as both folders, and says so', async () => {
    const { onStarted } = show();
    await userEvent.type(root(), 'C:\\Users\\jamie\\Phone Pictures');
    expect(screen.getByText('· optional, for example a backup')).toBeInTheDocument();
    await userEvent.type(second(), ' c:\\users\\jamie\\phone pictures\\ ');
    expect(screen.getByText('· the same as the root folder, so it is scanned once. Copies inside it are still found.')).toBeInTheDocument();

    api.startScan.mockRejectedValueOnce(new Error('Folder not found on the server: C:\\Users\\jamie\\Phone Pictures'));
    await userEvent.click(scan());
    expect(root()).toHaveAttribute('aria-invalid', 'true');
    expect(second()).toHaveAttribute('aria-invalid', 'false');

    await userEvent.click(scan());
    expect(api.startScan).toHaveBeenLastCalledWith({ primaryLocation: 'C:\\Users\\jamie\\Phone Pictures', secondaryLocation: undefined, recursive: true, startOver: false });
    expect(loadLastScan()).toEqual({ root: 'C:\\Users\\jamie\\Phone Pictures', second: '', recursive: true });
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
  });

  it('comes back with the folders of the last scan filled in', () => {
    saveLastScan({ root: 'C:\\photos', second: '', recursive: false });
    show();
    expect(root()).toHaveValue('C:\\photos');
    expect(second()).toHaveValue('');
    expect(screen.getByLabelText(/^Recursive/)).not.toBeChecked();
  });

  it('starts over when asked to, once: the scan after it builds on its results again', async () => {
    show();
    await userEvent.type(root(), 'C:\\photos');
    await userEvent.click(screen.getByLabelText(/^Start over/));
    await userEvent.click(scan());
    expect(api.startScan).toHaveBeenLastCalledWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true, startOver: true });
    expect(await screen.findByRole('button', { name: 'Scan' })).toBeEnabled();
    expect(screen.getByLabelText(/^Start over/)).not.toBeChecked();
  });

  it('cannot be started twice while the first is being started', async () => {
    const starting = deferred<{ instanceId: string }>();
    api.startScan.mockReturnValue(starting.promise);
    show();
    await userEvent.type(root(), 'C:\\photos');
    await userEvent.click(scan());
    expect(scan()).toHaveTextContent('Starting…');
    expect(scan()).toBeDisabled();
    starting.resolve({ instanceId: 'scan-8' });
    expect(await screen.findByRole('button', { name: 'Scan' })).toBeEnabled();
  });

  it('marks the folder that is not there, says which, and clears the mark when it is retyped', async () => {
    const { notify } = show();
    await userEvent.type(root(), 'C:\\photos');
    await userEvent.type(second(), 'D:\\gone');
    api.startScan.mockRejectedValueOnce(new Error('Folder not found on the server: D:\\gone'));
    await userEvent.click(scan());
    expect(notify).toHaveBeenLastCalledWith('Folder not found: D:\\gone', 'error');
    expect(second()).toHaveAttribute('aria-invalid', 'true');
    expect(root()).toHaveAttribute('aria-invalid', 'false');

    await userEvent.type(root(), 'x');                      // the other field does not clear it
    expect(second()).toHaveAttribute('aria-invalid', 'true');
    await userEvent.type(second(), 'x');
    expect(second()).toHaveAttribute('aria-invalid', 'false');

    api.startScan.mockRejectedValueOnce(new Error('Folder not found on the server: C:\\photosx'));
    await userEvent.click(scan());
    expect(notify).toHaveBeenLastCalledWith('Folder not found: C:\\photosx', 'error');
    expect(root()).toHaveAttribute('aria-invalid', 'true');
    await userEvent.type(root(), 'y');
    expect(root()).toHaveAttribute('aria-invalid', 'false');
  });

  it.each([
    [new TypeError('Failed to fetch'), 'Cannot reach the PhotoSense server.'],
    [new Error('A scan is already running. Wait for it to finish.'), 'A scan is already running. Wait for it to finish.'],
    ['Something else', 'Something else'],
  ])('says why when the scan could not be started', async (failure, message) => {
    api.startScan.mockRejectedValueOnce(failure);
    const { notify, onStarted } = show();
    await userEvent.type(root(), 'C:\\photos');
    await userEvent.click(scan());
    expect(notify).toHaveBeenCalledExactlyOnceWith(message, 'error');
    expect(onStarted).not.toHaveBeenCalled();
    expect(scan()).toBeEnabled();
  });

  it('fills in the full path of a folder chosen by browsing, for either field', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Browse for the secondary folder' }));
    let picker = screen.getByRole('dialog', { name: 'Choose the secondary folder' });
    await userEvent.click(await within(picker).findByRole('button', { name: 'Pictures' }));
    await userEvent.click(within(picker).getByRole('button', { name: 'Use this folder' }));
    expect(second()).toHaveValue(pictures);
    expect(root()).toHaveValue('');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Browse for the root folder' }));
    picker = screen.getByRole('dialog', { name: 'Choose the root folder' });
    await userEvent.click(within(picker).getByRole('button', { name: 'Cancel' }));
    expect(root()).toHaveValue('');

    // Opening again starts at what is typed for that field.
    await userEvent.click(screen.getByRole('button', { name: 'Browse for the secondary folder' }));
    await within(screen.getByRole('dialog')).findByText('No folders inside this one.');
    expect(api.browseFolders).toHaveBeenLastCalledWith(pictures);
  });

  it('offers the way back when there are results to go back to', async () => {
    const { onBack } = show({ hasResults: true });
    await userEvent.click(screen.getByRole('button', { name: 'Back to results' }));
    expect(onBack).toHaveBeenCalledOnce();
  });
});

describe('while a scan runs', () => {
  const log = Array.from({ length: 250 }, (_, n) => `12:00:${String(n % 60).padStart(2, '0')} Info line ${n}`);

  it('shows how far it has come, folder by folder, and holds the Scan button', () => {
    saveLastScan({ root: 'C:\\Users\\jamie\\Phone Pictures', second: "C:\\Users\\jamie\\Pictures\\Jamie's Phone", recursive: true });
    show({ scanning: true, hasResults: true, log, progress: progress({ primaryTotal: 4212, primaryProcessed: 1737, secondaryTotal: 2729, secondaryProcessed: 1125, overallPercent: 41.2 }) });

    expect(scan()).toHaveTextContent('Scanning…');
    expect(scan()).toBeDisabled();
    const block = screen.getByLabelText('Scan progress');
    expect(block).toHaveTextContent('41%2,862 of 6,941 files');
    expect(block).toHaveTextContent('Phone Pictures 1,737 / 4,212');
    expect(block).toHaveTextContent("Jamie's Phone 1,125 / 2,729");
    expect(block).toHaveTextContent('About 13 minutes for 7,000 files');
    const mosaic = screen.getByRole('progressbar');
    expect(mosaic).toHaveAttribute('aria-valuenow', '41');
    expect(mosaic.children).toHaveLength(120);
    expect([...mosaic.children].filter(t => t.className.includes('bg-brand'))).toHaveLength(49);
    expect(screen.queryByRole('button', { name: 'Back to results' })).not.toBeInTheDocument();
  });

  it('shows the last three log lines, and the last two hundred on request', async () => {
    show({ scanning: true, log, progress: progress() });
    expect(within(screen.getByLabelText('Latest log lines')).getAllByRole('listitem').map(l => l.textContent)).toEqual(log.slice(-3));
    expect(screen.queryByLabelText('Full log')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Show full log' }));
    const full = within(screen.getByLabelText('Full log')).getAllByRole('listitem');
    expect(full).toHaveLength(200);
    expect(full[0]).toHaveTextContent('line 50');
    await userEvent.click(screen.getByRole('button', { name: 'Hide full log' }));
    expect(screen.queryByLabelText('Full log')).not.toBeInTheDocument();
  });

  it('names a folder once when it was given as both folders', () => {
    saveLastScan({ root: 'C:\\photos', second: 'C:\\photos', recursive: true });
    show({ scanning: true, progress: progress({ primaryTotal: 10, primaryProcessed: 4, overallPercent: 40 }) });
    expect(screen.getAllByText(/\d+ \/ \d+/)).toHaveLength(1);
    expect(screen.getByLabelText('Scan progress')).toHaveTextContent('photos 4 / 10');
  });

  it('stays at nothing until the files have been counted, and names only the folders being scanned', () => {
    saveLastScan({ root: 'C:\\photos', second: '', recursive: true });
    // A scan that has not counted anything yet reports itself complete: nothing out of nothing.
    const { rerender, onStarted, onBack, notify } = show({ scanning: true, progress: progress({ overallPercent: 100 }) });
    expect(screen.getByLabelText('Scan progress')).toHaveTextContent('0%0 of 0 files');
    expect(screen.getByLabelText('Scan progress')).toHaveTextContent('photos 0 / 0');
    expect(screen.getByLabelText('Scan progress')).not.toHaveTextContent(' + ');
    expect(screen.getAllByText(/\d+ \/ \d+/)).toHaveLength(1);

    // Before the first report of progress arrives there is only the bar.
    rerender(<SetupScreen scanning log={[]} hasResults={false} onStarted={onStarted} onBack={onBack} notify={notify} />);
    expect(screen.getByLabelText('Scan progress')).toHaveTextContent('0%0 of 0 files');
    expect(screen.queryByText(/\d+ \/ \d+/)).not.toBeInTheDocument();
  });
});
