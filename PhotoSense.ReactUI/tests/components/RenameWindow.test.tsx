import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { RenameWindow } from '../../components/RenameWindow';
import type { RenamingDto } from '../../types';
import { backupPhone } from '../fixtures';

const api = vi.hoisted(() => ({ useRenaming: vi.fn(), renameCopies: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));

const rows: RenamingDto = { count: 6941, rows: [{ now: 'IMG_4821.HEIC', after: '2026-03-02 14.31.07.HEIC' }, { now: 'IMG_4822.HEIC', after: '2026-03-02 14.31.07-2.HEIC' }] };

beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  api.useRenaming.mockImplementation((id?: string) => ({ data: id ? rows : undefined }));
  api.renameCopies.mockResolvedValue(6941);
});

const window = () => screen.getByRole('dialog', { name: 'Rename files by date' });

describe('Rename files by date, on the computer (D3)', () => {
  it('shows the first names now and after, renames them all, and says how to undo it', async () => {
    const notify = vi.fn(), onClose = vi.fn();
    render(<RenameWindow phone={backupPhone()} notify={notify} onClose={onClose} />);
    expect(window()).toHaveTextContent("6,747 files from Alex's iPhone in Pictures/PhotoSense/Alex's iPhone");
    expect(within(window()).getAllByRole('row').map(r => r.textContent)).toEqual(['NowAfter', 'IMG_4821.HEIC2026-03-02 14.31.07.HEIC', 'IMG_4822.HEIC2026-03-02 14.31.07-2.HEIC']);
    expect(api.useRenaming).toHaveBeenLastCalledWith('phone-1', 'date');
    expect(window()).toHaveTextContent('Files with the same date and time get -2, -3 at the end');
    expect(within(window()).queryByRole('button', { name: 'As on the phone' })).not.toBeInTheDocument();
    await userEvent.click(within(window()).getByRole('button', { name: 'Date, time and place' }));
    expect(api.useRenaming).toHaveBeenLastCalledWith('phone-1', 'datePlace');
    await userEvent.click(within(window()).getByRole('button', { name: 'Rename 6,941 files' }));
    expect(api.renameCopies).toHaveBeenCalledWith('phone-1', 'datePlace');
    expect(notify).toHaveBeenCalledWith('Renamed 6,941 files by date. Recently moved, in Organize, can undo it.', 'ok');
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('says while it reads, when nothing would change, and one file in the singular', async () => {
    api.useRenaming.mockReturnValue({ data: undefined });
    const notify = vi.fn();
    const { rerender, unmount } = render(<RenameWindow phone={backupPhone()} notify={notify} onClose={vi.fn()} />);
    expect(window()).toHaveTextContent('Reading the files…');
    expect(within(window()).getByRole('button', { name: 'Name new backups this way' })).toBeDisabled();
    api.useRenaming.mockReturnValue({ data: { count: 0, rows: [] } });
    rerender(<RenameWindow phone={backupPhone()} notify={notify} onClose={vi.fn()} />);
    expect(window()).toHaveTextContent('Every file is named this way already, or does not say when it was taken.');
    api.renameCopies.mockResolvedValueOnce(0);
    await userEvent.click(within(window()).getByRole('button', { name: 'Name new backups this way' }));
    expect(notify).toHaveBeenLastCalledWith("New backups from Alex's iPhone are named by date.", 'ok');
    // Done, the window closes; opened again, one file.
    unmount();
    api.useRenaming.mockReturnValue({ data: { count: 1, rows: [{ now: 'A.JPG', after: 'B.JPG' }] } });
    render(<RenameWindow phone={backupPhone()} notify={notify} onClose={vi.fn()} />);
    api.renameCopies.mockResolvedValueOnce(1);
    await userEvent.click(within(window()).getByRole('button', { name: 'Rename 1 file' }));
    expect(notify).toHaveBeenLastCalledWith('Renamed 1 file by date. Recently moved, in Organize, can undo it.', 'ok');
  });

  it("once named by date, goes back to the phone's names for new backups, and says why not", async () => {
    const notify = vi.fn(), onClose = vi.fn();
    render(<RenameWindow phone={backupPhone({ names: 'datePlace' })} notify={notify} onClose={onClose} />);
    expect(within(window()).getByRole('button', { name: 'Date, time and place' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(within(window()).getByRole('button', { name: 'As on the phone' }));
    expect(within(window()).queryByRole('table')).not.toBeInTheDocument();
    expect(window()).toHaveTextContent('Files backed up from now on keep the names they have on the phone.');
    api.renameCopies.mockRejectedValueOnce(new Error('The computer could not keep it.'));
    await userEvent.click(within(window()).getByRole('button', { name: 'Keep the phone’s names for new backups' }));
    expect(notify).toHaveBeenLastCalledWith('The computer could not keep it.', 'error');
    api.renameCopies.mockRejectedValueOnce('gone');
    await userEvent.click(within(window()).getByRole('button', { name: 'Keep the phone’s names for new backups' }));
    expect(notify).toHaveBeenLastCalledWith('gone', 'error');
    expect(onClose).not.toHaveBeenCalled();
    await userEvent.click(within(window()).getByRole('button', { name: 'Keep the phone’s names for new backups' }));
    expect(notify).toHaveBeenLastCalledWith("New backups from Alex's iPhone keep the phone’s names.", 'ok');
  });

  it('closes on Cancel, on Escape, and on a press outside it', async () => {
    const onClose = vi.fn();
    render(<RenameWindow phone={backupPhone()} notify={vi.fn()} onClose={onClose} />);
    await userEvent.click(within(window()).getByRole('button', { name: 'Cancel' }));
    fireEvent.keyDown(globalThis.window, { key: 'a' });
    fireEvent.keyDown(globalThis.window, { key: 'Escape' });
    // A press outside it.
    fireEvent.mouseDown(window().parentElement!);
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});
