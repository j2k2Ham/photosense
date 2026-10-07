import React from 'react';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { SettingsPanel } from '../../components/SettingsPanel';
import { deferred } from '../fakeSignalR';

const api = vi.hoisted(() => ({ startScan: vi.fn(), browseFolders: vi.fn() }));
vi.mock('../../lib/apiClient', () => ({ startScan: api.startScan, browseFolders: api.browseFolders }));

const pictures = 'C:\\Users\\jamie\\Pictures';
const phone = pictures + "\\Jamie's Phone";

beforeEach(() => {
  api.startScan.mockReset();
  api.startScan.mockResolvedValue({ instanceId: 'scan-7' });
  // The service's machine has a Pictures folder with one folder in it.
  api.browseFolders.mockReset();
  api.browseFolders.mockImplementation(async (path?: string) => {
    if (!path) return { path: null, parent: null, folders: [{ name: 'Pictures', path: pictures }] };
    if (path === pictures) return { path: pictures, parent: 'C:\\Users\\jamie', folders: [{ name: "Jamie's Phone", path: phone }] };
    if (path === phone) return { path: phone, parent: pictures, folders: [] };
    throw new Error(`Folder not found: ${path}`);
  });
});

function show() {
  const onStarted = vi.fn();
  const view = render(<SettingsPanel onStarted={onStarted} />);
  return { onStarted, ...view };
}

const primary = () => screen.getByLabelText<HTMLInputElement>('Root folder path');
const secondary = () => screen.getByLabelText<HTMLInputElement>('Secondary folder path');
const scan = () => screen.getByRole('button', { name: /Scan|Starting/ });

describe('starting a scan', () => {
  it('needs a root folder first', async () => {
    show();
    expect(scan()).toBeDisabled();
    await userEvent.type(primary(), '   ');
    expect(scan()).toBeDisabled();
    await userEvent.type(primary(), 'C:\\photos');
    expect(scan()).toBeEnabled();
  });

  it('scans the folder as typed, subfolders included, and reports the scan it started', async () => {
    const { onStarted } = show();
    await userEvent.type(primary(), '  C:\\photos  ');
    await userEvent.type(secondary(), '   ');

    await userEvent.click(scan());

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true, startOver: false });
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('passes on a second folder and the choice to stay out of subfolders', async () => {
    show();
    await userEvent.type(primary(), 'C:\\photos');
    await userEvent.type(secondary(), ' D:\\backup ');
    await userEvent.click(screen.getByLabelText('Recursive'));

    await userEvent.click(scan());

    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: 'C:\\photos', secondaryLocation: 'D:\\backup', recursive: false, startOver: false });
  });

  it('starts over when asked to, once: the scan after it builds on its results again', async () => {
    show();
    await userEvent.type(primary(), 'C:\\photos');
    const startOver = screen.getByLabelText(/^Start over/);
    expect(startOver).not.toBeChecked();

    await userEvent.click(startOver);
    await userEvent.click(scan());
    expect(api.startScan).toHaveBeenLastCalledWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true, startOver: true });
    expect(await screen.findByRole('button', { name: 'Scan' })).toBeEnabled();
    expect(startOver).not.toBeChecked();

    await userEvent.click(scan());
    expect(api.startScan).toHaveBeenLastCalledWith({ primaryLocation: 'C:\\photos', secondaryLocation: undefined, recursive: true, startOver: false });
  });

  it('keeps the choice to start over when the scan could not be started', async () => {
    api.startScan.mockRejectedValueOnce(new Error('A scan is already running. Wait for it to finish.'));
    show();
    await userEvent.type(primary(), 'C:\\photos');
    await userEvent.click(screen.getByLabelText(/^Start over/));

    await userEvent.click(scan());
    await screen.findByRole('alert');
    expect(screen.getByLabelText(/^Start over/)).toBeChecked();

    await userEvent.click(screen.getByLabelText(/^Start over/));
    expect(screen.getByLabelText(/^Start over/)).not.toBeChecked();
  });

  it('cannot be started twice while the first is being started', async () => {
    const starting = deferred<{ instanceId: string }>();
    api.startScan.mockReturnValue(starting.promise);
    const { onStarted } = show();
    await userEvent.type(primary(), 'C:\\photos');

    await userEvent.click(scan());
    expect(scan()).toHaveTextContent('Starting...');
    expect(scan()).toBeDisabled();

    starting.resolve({ instanceId: 'scan-8' });
    expect(await screen.findByRole('button', { name: 'Scan' })).toBeEnabled();
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-8');
  });

  it.each([
    [new TypeError('Failed to fetch'), 'Cannot reach the PhotoSense server.'],
    [new Error('Folder not found: C:\\nowhere'), 'Folder not found: C:\\nowhere'],
    ['A scan is already running', 'A scan is already running'],
  ])('says why when it could not be started', async (failure, message) => {
    api.startScan.mockRejectedValueOnce(failure);
    const { onStarted } = show();
    await userEvent.type(primary(), 'C:\\nowhere');

    await userEvent.click(scan());
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(onStarted).not.toHaveBeenCalled();
    expect(scan()).toBeEnabled();

    // The complaint goes once the next attempt is made.
    await userEvent.click(scan());
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    expect(onStarted).toHaveBeenCalledExactlyOnceWith('scan-7');
  });
});

describe('browsing for a folder', () => {
  const browse = (which: 'root' | 'secondary') => userEvent.click(screen.getByRole('button', { name: `Browse for the ${which} folder` }));
  const picker = () => screen.getByRole('dialog');

  it('fills in the full path of the folder chosen, which typing the name alone would not give', async () => {
    show();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await browse('secondary');
    expect(screen.getByRole('dialog', { name: 'Choose the secondary folder' })).toBeInTheDocument();
    await userEvent.click(await within(picker()).findByRole('button', { name: 'Pictures' }));
    await userEvent.click(await within(picker()).findByRole('button', { name: "Jamie's Phone" }));
    await userEvent.click(within(picker()).getByRole('button', { name: 'Use this folder' }));

    expect(secondary()).toHaveValue(phone);
    expect(primary()).toHaveValue('');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('does the same for the root folder, and the scan is then started with that path', async () => {
    show();

    await browse('root');
    expect(screen.getByRole('dialog', { name: 'Choose the root folder' })).toBeInTheDocument();
    await userEvent.click(await within(picker()).findByRole('button', { name: 'Pictures' }));
    await userEvent.click(within(picker()).getByRole('button', { name: 'Use this folder' }));
    expect(primary()).toHaveValue(pictures);
    expect(secondary()).toHaveValue('');

    await userEvent.click(scan());
    expect(api.startScan).toHaveBeenCalledExactlyOnceWith({ primaryLocation: pictures, secondaryLocation: undefined, recursive: true, startOver: false });
  });

  it('opens at the folder already typed for that path', async () => {
    show();
    await userEvent.type(primary(), pictures);
    await userEvent.type(secondary(), phone);

    await browse('root');
    expect(await within(picker()).findByRole('button', { name: "Jamie's Phone" })).toBeInTheDocument();
    expect(api.browseFolders).toHaveBeenLastCalledWith(pictures);
    await userEvent.click(within(picker()).getByRole('button', { name: 'Cancel' }));

    await browse('secondary');
    expect(await within(picker()).findByText('No folders inside this one.')).toBeInTheDocument();
    expect(api.browseFolders).toHaveBeenLastCalledWith(phone);
  });

  it('leaves what was typed alone when the dialog is cancelled', async () => {
    show();
    await userEvent.type(secondary(), 'D:\\backup');

    await browse('secondary');
    await within(picker()).findByRole('button', { name: 'Pictures' });
    await userEvent.click(within(picker()).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(secondary()).toHaveValue('D:\\backup');
  });
});
