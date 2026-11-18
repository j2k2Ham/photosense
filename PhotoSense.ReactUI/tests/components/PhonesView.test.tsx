import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { activityTime, phoneStatus, PhonesView } from '../../components/PhonesView';
import type { BackupPhoneDto } from '../../types';
import { backupPhone } from '../fixtures';

const api = vi.hoisted(() => ({ tripsGoTo: vi.fn(), useBackupPhones: vi.fn(), moveBackups: vi.fn(), pausePhone: vi.fn() }));
vi.mock('../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../lib/apiClient')>()), ...api }));
// The rename window is tested on its own; here it shows which phone it is for, and closes.
vi.mock('../../components/RenameWindow', () => ({
  RenameWindow: ({ phone, onClose }: { phone: BackupPhoneDto; onClose(): void }) => (
    <div role="dialog" aria-label="Rename files by date">{phone.name}<button type="button" onClick={onClose}>Close the window</button></div>
  ),
}));
// The folder picker is tested on its own; here it picks a folder or is put away.
vi.mock('../../components/FolderPicker', () => ({
  FolderPicker: ({ title, startAt, onPick, onCancel }: { title: string; startAt?: string; onPick(path: string): void; onCancel(): void }) => (
    <div role="dialog" aria-label={title}>
      From {startAt}
      <button type="button" onClick={() => onPick('D:\\Phones')}>Pick D:\Phones</button>
      <button type="button" onClick={onCancel}>Cancel</button>
    </div>
  ),
}));

let phones: BackupPhoneDto[] | undefined;
beforeEach(() => {
  for (const f of Object.values(api)) f.mockReset();
  phones = [backupPhone()];
  api.useBackupPhones.mockImplementation(() => ({ data: phones }));
  api.moveBackups.mockResolvedValue(backupPhone());
  api.pausePhone.mockResolvedValue(backupPhone());
  api.tripsGoTo.mockResolvedValue(backupPhone());
});

const now = new Date(2026, 10, 13, 15, 0);
const at = (day: number, hour: number, minute = 0) => new Date(2026, 10, day, hour, minute).toISOString();
const detail = (name = "Alex's iPhone") => screen.getByRole('region', { name });

describe('what the list and the activity say', () => {
  it('says what is arriving, that it is paused, or when it last backed up', () => {
    expect(phoneStatus(backupPhone({ arriving: { done: 186, total: 380, bytes: 1, totalBytes: 2, started: at(13, 9), arrived: [] } }), now)).toEqual({ text: 'Backing up 186 of 380', live: true });
    expect(phoneStatus(backupPhone({ paused: true }), now)).toEqual({ text: 'Paused on this computer', live: false });
    expect(phoneStatus(backupPhone({ lastBackup: at(13, 8, 2) }), now)).toEqual({ text: 'Backed up today at 8:02 AM', live: true });
    expect(phoneStatus(backupPhone(), now)).toEqual({ text: 'Not backed up yet', live: false });
  });

  it('says when each thing happened: now, the time today, yesterday, or the day', () => {
    expect([activityTime(now.toISOString(), now), activityTime(at(13, 9, 12), now), activityTime(at(12, 9), now), activityTime(at(10, 9), now)])
      .toEqual(['Now', '9:12 AM', 'Yesterday', 'Nov 10']);
  });
});

describe('Phones, on the computer', () => {
  it('says it is looking until the service has said, then that there are none, with Pair a phone', async () => {
    const onPair = vi.fn();
    phones = undefined;
    const { rerender } = render(<PhonesView notify={vi.fn()} onPair={onPair} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking for paired phones…');
    phones = [];
    rerender(<PhonesView notify={vi.fn()} onPair={onPair} />);
    expect(detail('No phones yet')).toHaveTextContent('Pair a phone with PhotoSense on it');
    for (const button of screen.getAllByRole('button', { name: /Pair a phone/ })) await userEvent.click(button);
    expect(onPair).toHaveBeenCalledTimes(2);
    expect(api.useBackupPhones).toHaveBeenCalledWith(true);
  });

  it('lists each phone, the first chosen, and shows the one chosen: what is on this computer, checked, safe to delete, and what happened', async () => {
    phones = [
      backupPhone({ lastChecked: at(13, 9), lastBackup: new Date().toISOString(), events: [{ at: at(13, 9, 41), text: '214 items backed up and checked' }, { at: at(10, 9), text: 'Paired with this computer' }] }),
      backupPhone({ id: 'pad', name: "Alex's iPad", kind: 'iPad', held: 1, heldBytes: 1, safe: 0, safeBytes: 0, tripFolders: 2 }),
      backupPhone({ id: 'pixel', name: "Sam's Pixel 9", kind: null }),
    ];
    render(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    const list = screen.getByRole('complementary', { name: 'Phones and tablets' });
    expect(within(list).getAllByRole('button').map(b => b.textContent)).toEqual([
      expect.stringMatching(/^Alex's iPhoneBacked up today at /), "Alex's iPadNot backed up yet", "Sam's Pixel 9Not backed up yet", '+ Pair a phone',
    ]);
    expect(within(list).getByRole('button', { name: /^Alex's iPhone/ })).toHaveAttribute('aria-current', 'true');
    expect(detail()).toHaveTextContent('iPhone · paired Nov 10, 2026');
    expect(detail()).toHaveTextContent(/Up to dateLast backup today at /);
    expect(within(detail()).getByRole('region', { name: 'On this computer' })).toHaveTextContent('6,747 items32.1 GB');
    expect(within(detail()).getByRole('region', { name: 'Checked' })).toHaveTextContent('All matchSHA-256 checksum of every file');
    expect(within(detail()).getByRole('region', { name: 'Safe to delete on the phone' })).toHaveTextContent('5,998 items28.1 GB after keep rules');
    expect(within(detail()).getByRole('region', { name: 'Files from this phone' })).toHaveTextContent("Pictures/PhotoSense/Alex's iPhone");
    expect(within(detail()).getByRole('region', { name: 'Files from this phone' })).toHaveTextContent('Trip foldersPictures/PhotoSenseMade when the phone asks. None so far.');
    expect(within(within(detail()).getByRole('region', { name: 'Activity' })).getAllByRole('listitem').map(li => li.textContent)).toEqual([
      'Nov 13214 items backed up and checked', 'Nov 10Paired with this computer',
    ]);

    await userEvent.click(within(list).getByRole('button', { name: /^Alex's iPad/ }));
    expect(within(detail("Alex's iPad")).getByRole('region', { name: 'Files from this phone' })).toHaveTextContent('Made when the phone asks. 2 so far.');
    await userEvent.click(within(list).getByRole('button', { name: /^Sam's Pixel 9/ }));
    expect(detail("Sam's Pixel 9")).toHaveTextContent('Phone · paired Nov 10, 2026');
    expect(detail("Sam's Pixel 9")).toHaveTextContent("No backup yetSam's Pixel 9 backs up when PhotoSense is open on it.");
    expect(within(detail("Sam's Pixel 9")).getByRole('region', { name: 'Checked' })).toHaveTextContent('Not yet');
    expect(within(detail("Sam's Pixel 9")).getByRole('region', { name: 'Activity' })).toHaveTextContent('Nothing yet.');
  });

  it('shows a backup arriving, with the files checked last, and a phone paused', async () => {
    phones = [backupPhone({ arriving: { done: 186, total: 380, bytes: 1e9, totalBytes: 2.1e9, started: at(13, 9), arrived: ['f1', 'f2'] } })];
    const { rerender } = render(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    expect(detail()).toHaveTextContent('Backing up 186 of 380');
    expect(detail()).toHaveTextContent('1.02 GB left · each file is checked after it arrives');
    expect(within(detail()).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '48');
    expect(within(within(detail()).getByRole('list', { name: 'Arrived and checked' })).getAllByRole('img', { name: 'Checked' })).toHaveLength(2);

    phones = [backupPhone({ arriving: { done: 0, total: 3, bytes: 0, totalBytes: 0, started: at(13, 9), arrived: [] } })];
    rerender(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    expect(within(detail()).getByRole('progressbar')).toHaveAttribute('aria-valuenow', '0');
    expect(within(detail()).queryByRole('list', { name: 'Arrived and checked' })).not.toBeInTheDocument();

    phones = [backupPhone({ paused: true })];
    rerender(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    expect(detail()).toHaveTextContent("Paused on this computerAlex's iPhone is told so when it tries to back up.");
    expect(screen.getByRole('button', { name: 'Resume backups' })).toBeInTheDocument();
  });

  it('pauses and resumes a phone\'s backups, and says what happened, or what went wrong', async () => {
    const notify = vi.fn();
    const { rerender } = render(<PhonesView notify={notify} onPair={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Pause backups' }));
    expect(api.pausePhone).toHaveBeenLastCalledWith('phone-1', true);
    expect(notify).toHaveBeenLastCalledWith("Backups from Alex's iPhone are paused.", 'ok');
    phones = [backupPhone({ paused: true })];
    rerender(<PhonesView notify={notify} onPair={vi.fn()} />);
    api.pausePhone.mockRejectedValueOnce('The service did not answer.');
    await userEvent.click(screen.getByRole('button', { name: 'Resume backups' }));
    expect(notify).toHaveBeenLastCalledWith('The service did not answer.', 'error');
    api.pausePhone.mockRejectedValueOnce(new Error('That phone is not paired with this computer.'));
    await userEvent.click(screen.getByRole('button', { name: 'Resume backups' }));
    expect(api.pausePhone).toHaveBeenLastCalledWith('phone-1', false);
    expect(notify).toHaveBeenLastCalledWith('That phone is not paired with this computer.', 'error');
    phones = [backupPhone({ paused: true })];
    await userEvent.click(screen.getByRole('button', { name: 'Resume backups' }));
    expect(notify).toHaveBeenLastCalledWith("Backups from Alex's iPhone run again.", 'ok');
  });

  it('changes where a phone\'s backups go, starting from where they go now, or is put away', async () => {
    const notify = vi.fn();
    render(<PhonesView notify={notify} onPair={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: 'Change…' }));
    expect(screen.getByRole('dialog', { name: "Where backups from Alex's iPhone go" })).toHaveTextContent("From C:\\Users\\alex\\Pictures\\PhotoSense\\Alex's iPhone");
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Change…' }));
    await userEvent.click(screen.getByRole('button', { name: 'Pick D:\\Phones' }));
    expect(api.moveBackups).toHaveBeenCalledExactlyOnceWith('phone-1', 'D:\\Phones');
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith("Backups from Alex's iPhone now go to D:\\Phones.", 'ok'));
  });
});

describe('Where trip folders go', () => {
  it('is Pictures/PhotoSense to start with, and Change… makes them in the folder chosen from then on', async () => {
    const notify = vi.fn();
    render(<PhonesView notify={notify} onPair={vi.fn()} />);
    const files = screen.getByRole('region', { name: 'Files from this phone' });
    await userEvent.click(within(files).getByRole('button', { name: 'Change… where trip folders go' }));
    expect(screen.getByRole('dialog', { name: "Where trip folders from Alex's iPhone go" })).toHaveTextContent('From C:\\Users\\alex\\Pictures\\PhotoSense');
    await userEvent.click(screen.getByRole('button', { name: 'Pick D:\\Phones' }));
    expect(api.tripsGoTo).toHaveBeenCalledWith('phone-1', 'D:\\Phones');
    expect(notify).toHaveBeenCalledWith("Trip folders from Alex's iPhone now go in D:\\Phones.", 'ok');

    await userEvent.click(within(files).getByRole('button', { name: 'Change… where trip folders go' }));
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});

describe('How the copies are named', () => {
  it("says the phone's names to start with, Rename by date… opens the window for the phone, and a name by date says so", async () => {
    const { rerender } = render(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    const files = screen.getByRole('region', { name: 'Files from this phone' });
    expect(files).toHaveTextContent('File namesAs on the phone');
    await userEvent.click(within(files).getByRole('button', { name: 'Rename by date…' }));
    expect(screen.getByRole('dialog', { name: 'Rename files by date' })).toHaveTextContent("Alex's iPhone");
    await userEvent.click(screen.getByRole('button', { name: 'Close the window' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    phones = [backupPhone({ names: 'datePlace' })];
    rerender(<PhonesView notify={vi.fn()} onPair={vi.fn()} />);
    expect(screen.getByRole('region', { name: 'Files from this phone' })).toHaveTextContent('File namesDate, time and place');
    expect(within(screen.getByRole('region', { name: 'Files from this phone' })).getByRole('button', { name: 'Change names…' })).toBeInTheDocument();
  });
});

