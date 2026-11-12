import React from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { FolderDialog } from '../../components/organize/FolderDialog';
import type { FolderListingDto } from '../../types';
import { deferred } from '../deferred';

const api = vi.hoisted(() => ({ browseFolders: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const ROOT = 'C:\\Users\\jamie\\Phone Pictures';
// The folders of a small disk: the root, what is in it, the folder above it and the places to start from.
const DISK: Record<string, FolderListingDto> = {
  [ROOT]: { path: ROOT, parent: 'C:\\Users\\jamie', folders: [{ name: 'Trips', path: `${ROOT}\\Trips` }, { name: 'Family', path: `${ROOT}\\Family` }] },
  [`${ROOT}\\Trips`]: { path: `${ROOT}\\Trips`, parent: ROOT, folders: [{ name: 'Glacier', path: `${ROOT}\\Trips\\Glacier` }] },
  [`${ROOT}\\Trips\\Glacier`]: { path: `${ROOT}\\Trips\\Glacier`, parent: `${ROOT}\\Trips`, folders: [] },
  'C:\\Users\\jamie': { path: 'C:\\Users\\jamie', parent: null, folders: [{ name: 'Phone Pictures', path: ROOT }] },
  '': { path: null, parent: null, folders: [{ name: 'Backup (D:)', path: 'D:\\' }] },
  'D:\\': { path: 'D:\\', parent: null, folders: [{ name: 'Archive', path: 'D:\\Archive' }] },
};

beforeEach(() => {
  api.browseFolders.mockReset().mockImplementation(async (path?: string) => DISK[path ?? '']);
});

function open(props: Partial<React.ComponentProps<typeof FolderDialog>> = {}) {
  const onDone = vi.fn(), onCancel = vi.fn();
  render(<FolderDialog count={3} root={ROOT} taken={[]} onDone={onDone} onCancel={onCancel} {...props} />);
  return { onDone, onCancel };
}

const dialog = () => screen.getByRole('dialog', { name: /in a folder$|^New folder$/ });
const button = (name: string | RegExp) => within(dialog()).getByRole('button', { name });
const name = () => within(dialog()).getByRole('textbox', { name: 'Name' });
const folders = () => within(dialog()).getByRole('list', { name: 'Folders' });
// Each folder listed, and whether it is the one picked.
const rows = () => within(folders()).getAllByRole('button').filter(b => b.hasAttribute('aria-pressed')).map(b => [b.textContent, b.getAttribute('aria-pressed')]);
const already = async () => { await userEvent.click(button('Folder already there')); await within(folders()).findByRole('button', { name: 'Trips' }); };

describe('a new folder', () => {
  it('is named, made in the folder being organized, with a hint on how to make one inside another', async () => {
    const { onDone } = open();
    expect(dialog()).toHaveAccessibleName('Put 3 files in a folder');
    expect(name()).toHaveFocus();
    expect(name()).toHaveAccessibleDescription('Use \\ to make a folder inside a folder.');
    expect(dialog()).toHaveTextContent(`Made inside${ROOT}Change`);
    expect(within(dialog()).queryByRole('button', { name: /instead$/ })).not.toBeInTheDocument();
    await userEvent.type(name(), 'Trips\\Glacier 2022');
    await userEvent.click(button('Create folder'));
    expect(onDone).toHaveBeenCalledExactlyOnceWith({ name: 'Trips\\Glacier 2022', base: ROOT, direct: false });
  });

  it('is said to be wrong only once it has been tried, and says so until it is put right', async () => {
    const { onDone } = open({ count: 0, taken: [`${ROOT}\\Trips`] });
    expect(dialog()).toHaveAccessibleName('New folder');
    await userEvent.type(name(), 'Trips');
    expect(within(dialog()).queryByRole('alert')).not.toBeInTheDocument();
    fireEvent.keyDown(name(), { key: 'a' });
    await userEvent.keyboard('{Enter}');
    expect(within(dialog()).getByRole('alert')).toHaveTextContent('You already have a folder called Trips there.');
    expect(name()).toHaveAttribute('aria-invalid', 'true');
    expect(name()).toHaveClass('border-rose');
    await userEvent.type(name(), ' 2022');
    expect(within(dialog()).queryByRole('alert')).not.toBeInTheDocument();
    expect(name()).toHaveAttribute('aria-invalid', 'false');
    await userEvent.keyboard('{Enter}');
    expect(onDone).toHaveBeenCalledExactlyOnceWith({ name: 'Trips 2022', base: ROOT, direct: false });
  });

  it('can be made somewhere else, chosen with the folder browser, and back in the folder being organized', async () => {
    const { onDone, onCancel } = open();
    await userEvent.click(button('Change'));
    const picker = screen.getByRole('dialog', { name: 'Choose where the new folder is made' });
    // Esc belongs to the folder browser while it is open, and leaves this window as it was.
    await userEvent.click(await within(picker).findByRole('button', { name: 'Backup (D:)' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
    expect(screen.queryByRole('dialog', { name: 'Choose where the new folder is made' })).not.toBeInTheDocument();

    await userEvent.click(button('Change'));
    const again = screen.getByRole('dialog', { name: 'Choose where the new folder is made' });
    await userEvent.click(await within(again).findByRole('button', { name: 'Backup (D:)' }));
    await waitFor(() => expect(within(again).getByRole('button', { name: 'Make it here' })).toBeEnabled());
    await userEvent.click(within(again).getByRole('button', { name: 'Make it here' }));
    expect(dialog()).toHaveTextContent('Made insideD:\\Change');
    await userEvent.type(name(), 'Glacier{Enter}');
    expect(onDone).toHaveBeenLastCalledWith({ name: 'Glacier', base: 'D:\\', direct: false });

    await userEvent.click(button('Make it in Phone Pictures instead'));
    expect(dialog()).toHaveTextContent(`Made inside${ROOT}Change`);
    await userEvent.click(button('Create folder'));
    expect(onDone).toHaveBeenLastCalledWith({ name: 'Glacier', base: ROOT, direct: false });
  });

  it('is given up on Cancel, Escape or a press outside the window', async () => {
    const { onCancel } = open();
    await userEvent.click(button('Cancel'));
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(dialog());
    fireEvent.mouseDown(dialog().parentElement!);
    expect(onCancel).toHaveBeenCalledTimes(3);
  });
});

describe('a folder already there', () => {
  it('is looked for only when asked, starting in the folder being organized, which is not taken for a choice', async () => {
    const { onDone } = open();
    expect(api.browseFolders).not.toHaveBeenCalled();
    await already();
    expect(api.browseFolders).toHaveBeenCalledExactlyOnceWith(ROOT);
    expect(dialog()).toHaveTextContent(`Up${ROOT}`);
    expect(rows()).toEqual([['Trips', 'false'], ['Family', 'false']]);
    expect(button('Use this folder')).toBeDisabled();
    expect(dialog()).toHaveTextContent('Pick a folder, or open one to see the folders inside it. Nothing in it is replaced: a name that is taken gets a number.');

    // Picked, and picked again to let it go; then another.
    await userEvent.click(button('Trips'));
    expect(rows()).toEqual([['Trips', 'true'], ['Family', 'false']]);
    expect(dialog()).toHaveTextContent('Click a folder to pick it; › shows the folders inside it.');
    await userEvent.click(button('Trips'));
    expect(button('Use this folder')).toBeDisabled();
    await userEvent.click(button('Family'));
    await userEvent.click(button('Use Family'));
    expect(onDone).toHaveBeenCalledExactlyOnceWith({ name: 'Family', base: `${ROOT}\\Family`, direct: true });
    // Back to a new folder and here again, the folders are not asked for twice.
    await userEvent.click(button('New folder'));
    await userEvent.click(button('Folder already there'));
    expect(api.browseFolders).toHaveBeenCalledOnce();
  });

  it('can be one inside another, opened with › or a double-click, which is then the choice until one inside it is picked', async () => {
    const { onDone } = open();
    await already();
    await userEvent.click(button('Open Trips'));
    expect(await within(folders()).findByRole('button', { name: 'Glacier' })).toBeInTheDocument();
    expect(dialog()).toHaveTextContent(`Up${ROOT}\\Trips`);
    await userEvent.click(button('Use Trips'));
    expect(onDone).toHaveBeenLastCalledWith({ name: 'Trips', base: `${ROOT}\\Trips`, direct: true });

    await userEvent.dblClick(button('Glacier'));
    expect(await within(folders()).findByText('No folders inside this one.')).toBeInTheDocument();
    await userEvent.click(button('Use Glacier'));
    expect(onDone).toHaveBeenLastCalledWith({ name: 'Trips\\Glacier', base: `${ROOT}\\Trips\\Glacier`, direct: true });
  });

  it('can be the folder being organized itself, picked from the folder above it, or one on another disk', async () => {
    const { onDone } = open();
    await already();
    await userEvent.click(button('Up'));
    await userEvent.click(await within(folders()).findByRole('button', { name: 'Phone Pictures' }));
    await userEvent.click(button('Use Phone Pictures'));
    expect(onDone).toHaveBeenLastCalledWith({ name: 'Phone Pictures', base: ROOT, direct: true });

    // From the top of a disk, Up shows the disks and places to start from, above which there is nothing.
    await userEvent.click(button('Up'));
    expect(await within(folders()).findByRole('button', { name: 'Backup (D:)' })).toBeInTheDocument();
    expect(dialog()).toHaveTextContent('UpThis computer');
    expect(button('Up')).toBeDisabled();
    expect(button('Use this folder')).toBeDisabled();
    await userEvent.click(button('Open Backup (D:)'));
    await userEvent.click(await within(folders()).findByRole('button', { name: 'Archive' }));
    await userEvent.click(button('Use Archive'));
    expect(onDone).toHaveBeenLastCalledWith({ name: 'D:\\Archive', base: 'D:\\Archive', direct: true });
  });

  it('is not set up twice', async () => {
    const { onDone } = open({ taken: [`${ROOT}\\Trips`] });
    await already();
    await userEvent.click(button('Trips'));
    expect(within(dialog()).getByRole('alert')).toHaveTextContent('Trips is already one of your folders. Use Add here on its card.');
    expect(button('Use Trips')).toBeDisabled();
    fireEvent.click(button('Use Trips'));
    expect(onDone).not.toHaveBeenCalled();
  });

  it('says it is loading, holds every button while it does, and says why the folders could not be listed', async () => {
    const waiting = deferred<FolderListingDto>();
    api.browseFolders.mockReturnValueOnce(waiting.promise);
    open();
    await userEvent.click(button('Folder already there'));
    expect(folders()).toHaveAttribute('aria-busy', 'true');
    expect(folders()).toHaveTextContent('Loading…');
    expect(button('Up')).toBeDisabled();
    waiting.resolve(DISK[ROOT]);
    await waitFor(() => expect(folders()).toHaveAttribute('aria-busy', 'false'));

    api.browseFolders.mockRejectedValueOnce(new Error('Access to the folder is denied.'));
    await userEvent.click(button('Open Family'));
    expect(await within(folders()).findByRole('alert')).toHaveTextContent('Access to the folder is denied.');
    api.browseFolders.mockRejectedValueOnce(new TypeError('Failed to fetch'));
    await userEvent.click(button('Open Trips'));
    expect(await within(folders()).findByRole('alert')).toHaveTextContent('Cannot reach the PhotoSense server.');
    api.browseFolders.mockRejectedValueOnce('gone');
    await userEvent.click(button('Open Trips'));
    expect(await within(folders()).findByRole('alert')).toHaveTextContent('gone');
  });
});
