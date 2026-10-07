import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FolderPicker } from '../../components/FolderPicker';
import type { FolderListingDto } from '../../types';
import { deferred } from '../fakeSignalR';

const api = vi.hoisted(() => ({ browseFolders: vi.fn() }));
vi.mock('../../lib/apiClient', () => ({ browseFolders: api.browseFolders }));

// A small disk as the service would describe it.
const places: FolderListingDto = { path: null, parent: null, folders: [{ name: 'Pictures', path: 'C:\\Users\\jamie\\Pictures' }, { name: 'C:\\', path: 'C:\\' }] };
const disk: Record<string, FolderListingDto> = {
  'C:\\': { path: 'C:\\', parent: null, folders: [{ name: 'Users', path: 'C:\\Users' }] },
  'C:\\Users\\jamie\\Pictures': {
    path: 'C:\\Users\\jamie\\Pictures', parent: 'C:\\Users\\jamie',
    folders: [{ name: "Jamie's Phone", path: "C:\\Users\\jamie\\Pictures\\Jamie's Phone" }, { name: 'Trips', path: 'C:\\Users\\jamie\\Pictures\\Trips' }],
  },
  "C:\\Users\\jamie\\Pictures\\Jamie's Phone": { path: "C:\\Users\\jamie\\Pictures\\Jamie's Phone", parent: 'C:\\Users\\jamie\\Pictures', folders: [] },
  'C:\\Users\\jamie': { path: 'C:\\Users\\jamie', parent: 'C:\\Users', folders: [{ name: 'Pictures', path: 'C:\\Users\\jamie\\Pictures' }] },
};

beforeEach(() => {
  api.browseFolders.mockReset();
  api.browseFolders.mockImplementation(async (path?: string) => {
    if (!path) return places;
    if (disk[path]) return disk[path];
    throw new Error(`Folder not found: ${path}`);
  });
});

function open(startAt?: string) {
  const onPick = vi.fn(), onCancel = vi.fn();
  const view = render(<FolderPicker title="Choose the secondary folder" startAt={startAt} onPick={onPick} onCancel={onCancel} />);
  return { onPick, onCancel, ...view };
}

const dialog = () => screen.getByRole('dialog', { name: 'Choose the secondary folder' });
const here = () => screen.getByLabelText('Current folder');
const folder = (name: string) => within(dialog()).getByRole('button', { name });
const use = () => within(dialog()).getByRole('button', { name: 'Use this folder' });
const up = () => within(dialog()).getByRole('button', { name: 'Up' });

describe('FolderPicker', () => {
  it('starts with the places to browse from, none of which is a folder to pick yet', async () => {
    open();
    expect(screen.getByText('Loading…')).toBeInTheDocument();

    expect(await screen.findByRole('button', { name: 'Pictures' })).toBeInTheDocument();
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument();
    expect(here()).toHaveTextContent('This computer');
    expect(use()).toBeDisabled();
    expect(up()).toBeDisabled();
    expect(api.browseFolders).toHaveBeenCalledExactlyOnceWith(undefined);
    expect(dialog().parentElement?.parentElement).toBe(document.body);    // over the whole page, not inside the panel it was opened from
  });

  it('opens a folder that is clicked and hands back its full path when it is chosen', async () => {
    const { onPick } = open();

    await userEvent.click(await screen.findByRole('button', { name: 'Pictures' }));
    expect(here()).toHaveTextContent('C:\\Users\\jamie\\Pictures');
    expect(folder('Trips')).toHaveAttribute('title', 'C:\\Users\\jamie\\Pictures\\Trips');

    await userEvent.click(folder("Jamie's Phone"));
    expect(here()).toHaveTextContent("C:\\Users\\jamie\\Pictures\\Jamie's Phone");
    expect(screen.getByText('No folders inside this one.')).toBeInTheDocument();

    await userEvent.click(use());
    expect(onPick).toHaveBeenCalledExactlyOnceWith("C:\\Users\\jamie\\Pictures\\Jamie's Phone");
  });

  it('goes up a folder at a time, and from the top of a disk back to the starting places', async () => {
    open('C:\\Users\\jamie\\Pictures');
    expect(await screen.findByRole('button', { name: 'Trips' })).toBeInTheDocument();

    await userEvent.click(up());
    expect(here()).toHaveTextContent(/^C:\\Users\\jamie$/);

    await userEvent.click(up());       // C:\Users is not on this small disk
    expect(await screen.findByRole('alert')).toHaveTextContent('Folder not found: C:\\Users');
    expect(here()).toHaveTextContent(/^C:\\Users\\jamie$/);      // stays where it was

    await userEvent.click(folder('Pictures'));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();  // the complaint goes with the next move
  });

  it('goes from the top of a disk back to the starting places', async () => {
    open('C:\\');
    expect(await screen.findByRole('button', { name: 'Users' })).toBeInTheDocument();
    expect(up()).toBeEnabled();

    await userEvent.click(up());

    expect(here()).toHaveTextContent('This computer');
    expect(api.browseFolders).toHaveBeenLastCalledWith(undefined);
  });

  it('opens where the typed path points, spaces around it aside', async () => {
    open("  C:\\Users\\jamie\\Pictures\\Jamie's Phone  ");
    expect(await screen.findByText('No folders inside this one.')).toBeInTheDocument();
    expect(api.browseFolders).toHaveBeenCalledExactlyOnceWith("C:\\Users\\jamie\\Pictures\\Jamie's Phone");
    expect(use()).toBeEnabled();
  });

  it.each(['Pictures', '   '])('starts from the top, without complaint, when what was typed ("%s") is not a folder', async typed => {
    open(typed);
    expect(await screen.findByRole('button', { name: 'C:\\' })).toBeInTheDocument();
    expect(here()).toHaveTextContent('This computer');
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it.each([
    [new TypeError('Failed to fetch'), 'Cannot reach the PhotoSense server.'],
    [new Error('Folder not found: E:\\'), 'Folder not found: E:\\'],
    ['The service is shutting down', 'The service is shutting down'],
  ])('says why when the folders cannot be listed', async (failure, message) => {
    api.browseFolders.mockRejectedValue(failure);
    open();
    expect(await screen.findByRole('alert')).toHaveTextContent(message);
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument();
    expect(use()).toBeDisabled();
  });

  it('lets nothing be clicked twice while a folder is being opened', async () => {
    open();
    await screen.findByRole('button', { name: 'Pictures' });
    const opening = deferred<FolderListingDto>();
    api.browseFolders.mockReturnValueOnce(opening.promise);

    await userEvent.click(folder('C:\\'));
    expect(folder('Pictures')).toBeDisabled();
    expect(folder('C:\\')).toBeDisabled();

    opening.resolve(disk['C:\\']);
    expect(await screen.findByRole('button', { name: 'Users' })).toBeEnabled();
    expect(use()).toBeEnabled();
  });

  it('is dismissed by Cancel, the close button, Escape and a click outside it, and by nothing else', async () => {
    const { onCancel, onPick, unmount } = open();
    await screen.findByRole('button', { name: 'Pictures' });

    await userEvent.click(within(dialog()).getByRole('button', { name: 'Cancel' }));
    await userEvent.click(within(dialog()).getByRole('button', { name: 'Close' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(dialog().parentElement!);
    expect(onCancel).toHaveBeenCalledTimes(4);

    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(dialog());
    expect(onCancel).toHaveBeenCalledTimes(4);
    expect(onPick).not.toHaveBeenCalled();

    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(4);
  });
});
