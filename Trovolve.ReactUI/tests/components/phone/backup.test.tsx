import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { BackupScreen, SafeToDelete, SpaceFreed } from '../../../components/phone/Backup';
import { SentSheet } from '../../../components/phone/Locations';
import type { LocationsDto } from '../../../types';
import { BACKUP_SETTINGS, backupState, location, locations, plan, safeReview } from '../../fixtures';
import { frame, gate, inFrame } from './frame';
import type { PlanGate } from '../../../components/phone/ProSheet';

const api = vi.hoisted(() => ({
  startBackup: vi.fn(), pauseBackup: vi.fn(), saveBackupSettings: vi.fn(), removeSafe: vi.fn(), backUpToLocations: vi.fn(), removeLocation: vi.fn(),
  useLocations: vi.fn((): { data?: unknown } => ({})), useLocationDetails: vi.fn((): { data?: unknown } => ({})),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

beforeEach(() => {
  for (const f of Object.values(api)) f.mockReset();
  for (const f of [api.startBackup, api.pauseBackup, api.saveBackupSettings]) f.mockResolvedValue(backupState());
  api.backUpToLocations.mockResolvedValue(undefined);
  api.removeLocation.mockResolvedValue(undefined);
  api.useLocations.mockReturnValue({});
  api.useLocationDetails.mockReturnValue({});
});

const card = (name = 'Studio iMac') => screen.getByRole('region', { name });

describe('Backup', () => {
  it('says the backup is being looked at until the phone has said', () => {
    inFrame(<BackupScreen notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking at what is backed up…');
    expect(screen.queryByRole('region', { name: 'Automatic backup' })).not.toBeInTheDocument();
  });

  it('with no computer, offers to connect one, and Send now still chooses', async () => {
    const onPair = vi.fn(), onChoose = vi.fn();
    inFrame(<BackupScreen state={backupState({ stage: 'unpaired', computer: null, waiting: 6941 })} notify={vi.fn()} onChoose={onChoose} onPair={onPair} />, frame('android'));
    expect(card('No computer yet')).toHaveTextContent('Pair this phone with Trovolve on your Mac or PC. 6,941 items can then be backed up over Wi-Fi.');
    await userEvent.click(screen.getByRole('button', { name: 'Connect your computer' }));
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    expect([onPair.mock.calls.length, onChoose.mock.calls.length]).toEqual([1, 1]);
  });

  it('says what waits on the computer named, never checked yet, and Back up now starts', async () => {
    inFrame(<BackupScreen state={backupState()} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()} />);
    expect(card()).toHaveTextContent('Not backed up yet');
    expect(card()).toHaveTextContent('380 new items waiting');
    expect(within(card()).queryByRole('progressbar')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Back up now' }));
    expect(api.startBackup).toHaveBeenCalledExactlyOnceWith();
  });

  it('while backing up, shows how far it has got and the last few pictures, ticked once on the computer; Pause pauses', async () => {
    const running = backupState({ stage: 'running', done: 184, total: 380, bytes: 1e9, totalBytes: 2.1e9, lastChecked: new Date().toISOString(), strip: [{ id: 'a', done: true }, { id: 'b', done: false }] });
    inFrame(<BackupScreen state={running} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()} />);
    expect(card()).toHaveTextContent(/Checked today at/);
    expect(within(card()).getByRole('progressbar', { name: 'Backing up to Studio iMac' })).toHaveAttribute('aria-valuenow', '48');
    const strip = within(card()).getByRole('list', { name: 'Being backed up' });
    expect(within(strip).getAllByRole('listitem')).toHaveLength(2);
    expect(within(strip).getAllByRole('img', { name: 'On the computer' })).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    expect(api.pauseBackup).toHaveBeenCalledOnce();
  });

  it('paused, Resume carries on; stopped, says why, and Try again; up to date, has no button; a failure is said', async () => {
    const notify = vi.fn();
    const { again } = inFrame(<BackupScreen state={backupState({ stage: 'paused', done: 1, total: 2 })} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} />);
    api.startBackup.mockRejectedValueOnce(new Error('Pair with a computer first.'));
    await userEvent.click(screen.getByRole('button', { name: 'Resume' }));
    expect(notify).toHaveBeenCalledExactlyOnceWith('Pair with a computer first.', 'error');
    again(<BackupScreen state={backupState({ stage: 'failed', problem: 'Studio iMac was not found on this Wi-Fi.' })} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} />);
    expect(within(card()).getByRole('alert')).toHaveTextContent('Studio iMac was not found on this Wi-Fi.');
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument();
    again(<BackupScreen state={backupState({ waiting: 0 })} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} />);
    expect(card()).toHaveTextContent('Up to date');
    expect(within(card()).queryByRole('button')).not.toBeInTheDocument();
  });

  it('turning automatic backup on opens the Pro sheet on the free plan, and turns it on once bought', async () => {
    const offer = vi.fn((_reason: string, then?: () => void) => then?.());
    inFrame(<BackupScreen state={backupState()} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()} />, frame(), gate({ plan: plan(), offer }));
    expect(within(screen.getByRole('region', { name: 'Automatic backup' })).getByText('PRO')).toBeInTheDocument();
    await userEvent.click(screen.getByText('Back up automatically'));
    expect(offer).toHaveBeenCalledWith('auto', expect.any(Function));
    expect(api.saveBackupSettings).toHaveBeenCalledExactlyOnceWith({ ...BACKUP_SETTINGS, automatic: true });
  });

  it('on Android, heads Automatic backup as Android heads a section', () => {
    inFrame(<BackupScreen state={backupState()} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()} />, frame('android'));
    expect(screen.getByRole('heading', { name: 'Automatic backup' })).toHaveClass('text-acc');
  });

  it('on Pro, has no badge, and each switch is kept; the folder is changed on the computer', async () => {
    const notify = vi.fn();
    const pro = backupState({ settings: { ...BACKUP_SETTINGS, automatic: true } });
    inFrame(<BackupScreen state={pro} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} />, frame(), gate({ plan: plan({ tier: 'pro', kind: 'lifetime', leftToday: null }) }));
    const section = screen.getByRole('region', { name: 'Automatic backup' });
    expect(within(section).queryByText('PRO')).not.toBeInTheDocument();
    await userEvent.click(screen.getByText('Back up automatically'));
    await userEvent.click(screen.getByText('Only while charging'));
    await userEvent.click(screen.getByText('Include videos'));
    expect(api.saveBackupSettings.mock.calls).toEqual([
      [{ ...BACKUP_SETTINGS, automatic: false }], [{ ...BACKUP_SETTINGS, automatic: true, onlyWhileCharging: false }], [{ ...BACKUP_SETTINGS, automatic: true, includeVideos: false }],
    ]);
    await userEvent.click(within(section).getByRole('button', { name: "Folder Pictures/Trovolve/Alex's iPhone" }));
    expect(notify).toHaveBeenCalledWith('To change the folder, open Phones in Trovolve on Studio iMac.', 'info');
    expect(section).toHaveTextContent('iOS decides when background backups run. They usually happen while this iPhone is charging on Wi-Fi. A backup also runs whenever Trovolve is open and Studio iMac is on the same Wi-Fi.');
  });
});

describe('Safe to delete', () => {
  const show = (safe = safeReview(), more: { platform?: 'ios' | 'android'; plan?: ReturnType<typeof plan>; remove?: PlanGate['remove'] } = {}) => {
    const notify = vi.fn(), onRemoved = vi.fn(), onBack = vi.fn();
    const view = inFrame(<SafeToDelete safe={safe} notify={notify} onRemoved={onRemoved} onBack={onBack} />, frame(more.platform),
      gate({ plan: more.plan, ...(more.remove ? { remove: more.remove } : {}) }));
    return { ...view, notify, onRemoved, onBack };
  };

  it('says how much can be freed, how the storage stands, that each copy matched, and what is kept', () => {
    show(safeReview({ lastChecked: new Date().toISOString(), cloudPhotos: true }));
    const page = screen.getByRole('dialog', { name: 'Safe to delete' });
    expect(page).toHaveTextContent('28.1 GBcan be freed from this iPhone');
    expect(page).toHaveTextContent('Everything else 83.9 GB');
    expect(page).toHaveTextContent('Free 16.0 GB');
    expect(within(page).getByRole('region', { name: 'Checked on Studio iMac' })).toHaveTextContent('6,561 photos and videosEach one matches the copy on the computer');
    expect(within(page).getByRole('region', { name: 'Checked on Studio iMac' })).toHaveTextContent(/Last checked Today, /);
    expect(within(page).getByRole('switch', { name: 'Favorites 214 items · 1.1 GB' })).toBeChecked();
    expect(within(page).getByRole('switch', { name: 'Last 30 days 349 items · 1.9 GB' })).toBeChecked();
    expect(within(page).getByRole('note')).toHaveTextContent('If iCloud Photos is on, removing items here also removes them from iCloud and your other Apple devices. The copies on Studio iMac are not affected.');
    expect(within(page).getByRole('button', { name: 'Remove 5,998 items' })).toBeEnabled();
    expect(page).toHaveTextContent('Removed items stay in Recently Deleted for 30 days.');
  });

  it('on Android: favorites as on an iPhone, no iCloud, the bin; and on the free plan, what is left today', () => {
    show(safeReview({ cloudPhotos: true, checked: { count: 1, bytes: 1 }, folder: null, computer: null }), { platform: 'android', plan: plan() });
    const page = screen.getByRole('dialog', { name: 'Safe to delete' });
    expect(within(page).getByRole('switch', { name: 'Favorites 214 items · 1.1 GB' })).toBeChecked();
    expect(within(page).queryByRole('note')).not.toBeInTheDocument();
    expect(page).toHaveTextContent('can be freed from this phone');
    expect(page).toHaveTextContent('1 photo or videoEach one matches');
    expect(within(page).getByRole('region', { name: 'Checked on your computer' })).toHaveTextContent('Last checked Not yet');
    expect(page).toHaveTextContent('Free plan: 38 removals left today. Removed items stay in the bin for 30 days.');
  });

  it('says it is checking until the phone has said, and that there is nothing to remove', () => {
    const { again } = inFrame(<SafeToDelete notify={vi.fn()} onRemoved={vi.fn()} onBack={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Checking what is on the computer…');
    expect(screen.getByRole('button', { name: 'Nothing to remove' })).toBeDisabled();
    again(<SafeToDelete safe={safeReview({ storageTotal: 0, removable: { count: 0, bytes: 0 } })} notify={vi.fn()} onRemoved={vi.fn()} onBack={vi.fn()} />);
    expect(screen.queryByText('To remove')).not.toBeInTheDocument();
  });

  it('keeps each keep rule as it is turned, and says when it could not', async () => {
    const { notify } = show();
    await userEvent.click(screen.getByText('Favorites'));
    await userEvent.click(screen.getByText('Last 30 days'));
    expect(api.saveBackupSettings.mock.calls).toEqual([[{ ...BACKUP_SETTINGS, keepFavorites: false }], [{ ...BACKUP_SETTINGS, keepRecent: false }]]);
    api.saveBackupSettings.mockRejectedValueOnce(new Error('Gone.'));
    await userEvent.click(screen.getByText('Last 30 days'));
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('Gone.', 'error'));
  });

  it('removes all of them, or the largest the plan lets go today, and says when the phone said no', async () => {
    const removed = { removed: 5998, bytes: 28.1e9, left: 0, storageTotal: 128e9, storageFreeBefore: 16e9 };
    api.removeSafe.mockResolvedValue(removed);
    const all = show();
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    await vi.waitFor(() => expect(all.onRemoved).toHaveBeenCalledWith(removed, false));
    expect(api.removeSafe).toHaveBeenLastCalledWith(undefined);
    all.unmount();

    const remove = vi.fn((_count: number, _label: string, run: (allowed: number) => void, largest?: boolean) => { expect(largest).toBe(true); run(50); });
    const some = show(safeReview(), { remove });
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    await vi.waitFor(() => expect(some.onRemoved).toHaveBeenCalledWith(removed, true));
    expect(api.removeSafe).toHaveBeenLastCalledWith(50);

    api.removeSafe.mockRejectedValueOnce('The phone did not answer.');
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    await vi.waitFor(() => expect(some.notify).toHaveBeenCalledWith('The phone did not answer.', 'error'));
    api.removeSafe.mockRejectedValueOnce(new Error('You chose not to let Trovolve delete these photos.'));
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998 items' }));
    await vi.waitFor(() => expect(some.notify).toHaveBeenCalledWith('You chose not to let Trovolve delete these photos.', 'error'));
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    expect(some.onBack).toHaveBeenCalled();
  });
});

describe('Space freed', () => {
  const removed = { removed: 5998, bytes: 28.1e9, left: 0, storageTotal: 128e9, storageFreeBefore: 16e9 };

  it('says how much was freed, what went and where the copies are, with the storage before and now', async () => {
    const onDone = vi.fn();
    inFrame(<SpaceFreed removed={removed} partial={false} computer="Studio iMac" folder="Pictures/Trovolve" onPro={vi.fn()} onDone={onDone} />);
    const page = screen.getByRole('dialog', { name: 'Space freed' });
    expect(within(page).getByRole('heading', { name: '28.1 GB freed' })).toBeInTheDocument();
    expect(page).toHaveTextContent('5,998 items removed from this iPhone. The copies are on Studio iMac in Pictures/Trovolve.');
    expect(page).toHaveTextContent('Before112 of 128 GB');
    expect(page).toHaveTextContent('Now83.9 of 128 GB');
    expect(within(page).queryByRole('region', { name: 'More are selected' })).not.toBeInTheDocument();
    await userEvent.click(within(page).getByRole('button', { name: 'Done' }));
    expect(onDone).toHaveBeenCalledOnce();
  });

  it('offers the share card of what was freed, by its row and by Share', async () => {
    const onShare = vi.fn();
    inFrame(<SpaceFreed removed={removed} partial={false} computer="Studio iMac" folder="Pictures/Trovolve" streak={6} onPro={vi.fn()} onShare={onShare} onDone={vi.fn()} />);
    await userEvent.click(screen.getByRole('button', { name: /^I freed 28.1 GB with Trovolve/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    expect(onShare.mock.calls).toEqual([[{ amount: '28.1 GB', device: 'iPhone', streak: 6, items: 5998 }], [{ amount: '28.1 GB', device: 'iPhone', streak: 6, items: 5998 }]]);
  });

  it('after the free plan let only some go, says how many more are chosen, and offers Pro', async () => {
    const onPro = vi.fn();
    inFrame(<SpaceFreed removed={{ ...removed, removed: 50, bytes: 3.6e9, left: 5948 }} partial computer="Studio iMac" folder="Pictures/Trovolve" onPro={onPro} onDone={vi.fn()} />, frame('android'), gate({ plan: plan() }));
    const page = screen.getByRole('dialog', { name: 'Space freed' });
    expect(page).toHaveTextContent('The 50 largest items were removed from this phone.');
    const more = within(page).getByRole('region', { name: 'More are selected' });
    expect(more).toHaveTextContent('5,948 more are selected');
    expect(more).toHaveTextContent('The free plan deletes 50 items a day. Come back tomorrow, or get Pro to remove the limit.');
    await userEvent.click(within(more).getByRole('button', { name: 'Get Pro' }));
    expect(onPro).toHaveBeenCalledOnce();
  });

  it('with one left, says it is chosen; with nothing known of the storage, draws no bars; on Pro, offers nothing', () => {
    const { again } = inFrame(<SpaceFreed removed={{ ...removed, left: 1, storageTotal: 0 }} partial computer="Studio iMac" folder="Pictures/Trovolve" onPro={vi.fn()} onDone={vi.fn()} />,
      frame(), gate({ plan: plan() }));
    expect(screen.getByRole('region', { name: 'More are selected' })).toHaveTextContent('1 more is selected');
    expect(screen.queryByText('Before')).not.toBeInTheDocument();
    again(<SpaceFreed removed={{ ...removed, left: 1, bytes: 200e9 }} partial computer="Studio iMac" folder="Pictures/Trovolve" onPro={vi.fn()} onDone={vi.fn()} />);
    expect(screen.getByRole('dialog')).toHaveTextContent('Now0 of 128 GB');
  });
});

describe('Backup with locations', () => {
  it('with no computer but another location on, lists the locations and offers automatic backup, the folder left to the computer', () => {
    const drive = location({ on: true, connected: true, account: 'alex.m@gmail.com' });
    inFrame(<BackupScreen state={backupState({ stage: 'unpaired', computer: null, folder: null })} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()}
      locations={locations({ locations: [location({ id: 'computer', kind: 'computer', name: 'Computer' }), drive] })} onSettings={vi.fn()} />);
    expect(within(screen.getByRole('region', { name: 'Locations' })).getByText('Google Drive')).toBeInTheDocument();
    expect(screen.getByRole('region', { name: 'Automatic backup' })).toHaveTextContent('A backup also runs whenever Trovolve is open and on Wi-Fi.');
    expect(screen.queryByRole('button', { name: /^Folder/ })).not.toBeInTheDocument();
  });

  it('with no computer and nothing else on, offers no automatic backup', () => {
    inFrame(<BackupScreen state={backupState({ stage: 'unpaired', computer: null, folder: null })} notify={vi.fn()} onChoose={vi.fn()} onPair={vi.fn()}
      locations={locations({ locations: [location({ id: 'computer', kind: 'computer', name: 'Computer' })] })} onSettings={vi.fn()} />);
    expect(screen.queryByRole('region', { name: 'Automatic backup' })).not.toBeInTheDocument();
  });
});

describe('Backup on a tablet (T6–T9)', () => {
  const tablet = frame('ios', { tablet: true, form: 'tablet' });
  const drive = location({ on: true, connected: true, account: 'alex.m@gmail.com', copies: 2, lastBackup: '2026-11-19T08:00:00Z' });
  const archive = location({ id: 's1', kind: 'webDav', name: 'Archive', on: false, connected: true, settings: { url: 'https://nas.local/dav' } });
  const files = [
    { id: 'a', name: 'IMG_1.JPG', sizeBytes: 1024 ** 2, isVideo: false }, { id: 'b', name: 'IMG_2.MOV', sizeBytes: 2 * 1024 ** 2, isVideo: true },
  ] as unknown as React.ComponentProps<typeof BackupScreen>['files'];
  function show(listed: LocationsDto, more: Partial<React.ComponentProps<typeof BackupScreen>> = {}) {
    api.useLocations.mockReturnValue({ data: listed });
    const notify = vi.fn(), onSend = vi.fn();
    const view = inFrame(<BackupScreen state={backupState()} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} locations={listed} onSettings={vi.fn()} files={files} onSend={onSend} {...more} />,
      tablet, gate({ plan: plan({ tier: 'pro' }) }));
    return { ...view, notify, onSend };
  }
  const pane = (name: string) => screen.getByRole('region', { name });

  it('lists every location with its switch, the first on beside it, lit, with how backing up there stands and Back up now', async () => {
    const running = locations({ pro: true, locations: [...locations().locations.slice(0, 1), drive, archive],
      activity: { task: 'backup', stage: 'running', locations: [{ id: 'google-drive', done: 1, total: 2, bytes: 0, totalBytes: 2, problem: null }], sent: null } });
    const { again, notify } = show(running);
    expect(within(screen.getByRole('region', { name: 'Locations' })).getAllByRole('switch')).toHaveLength(3);
    expect(screen.getByRole('button', { name: /^Google Drive/ })).toHaveAttribute('aria-current', 'true');
    expect(within(pane('Google Drive details')).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(within(pane('Google Drive details')).getByRole('status')).toHaveTextContent('1 of 2');
    expect(within(pane('Google Drive details')).getByRole('status')).not.toHaveClass('text-danger-t');
    expect(within(pane('Google Drive details')).getByRole('button', { name: 'Pause' })).toBeInTheDocument();
    const idle = { ...running, activity: null };
    api.useLocations.mockReturnValue({ data: idle });
    again(<BackupScreen state={backupState()} notify={notify} onChoose={vi.fn()} onPair={vi.fn()} locations={idle} onSettings={vi.fn()} />, tablet);
    await userEvent.click(within(pane('Google Drive details')).getByRole('button', { name: 'Back up now' }));
    expect(api.backUpToLocations).toHaveBeenCalledOnce();
    expect(notify).toHaveBeenLastCalledWith('Backing up to Google Drive. Every copy is checked.', 'info');
  });

  it('shows the location chosen, Connect to server with Cancel, and a server added to change, each beside the list', async () => {
    show(locations({ pro: true, locations: [...locations().locations.slice(0, 1), drive, archive] }));
    await userEvent.click(screen.getByRole('button', { name: /^Studio iMac/ }));
    expect(pane('Studio iMac details')).toBeInTheDocument();
    expect(within(pane('Studio iMac details')).queryByRole('button', { name: 'Back up now' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Connect to server…/ }));
    expect(within(pane('Connect to server')).getByRole('heading', { name: 'Connect to server' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /^Studio iMac/ })).not.toHaveAttribute('aria-current');
    await userEvent.click(within(pane('Connect to server')).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('region', { name: 'Connect to server' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /^Archive/ }));
    expect(within(pane('Archive details')).queryByRole('button', { name: 'Back up now' })).not.toBeInTheDocument();
    await userEvent.click(within(pane('Archive details')).getByRole('button', { name: 'Edit' }));
    expect(within(pane('Connect to server')).getByRole('heading', { name: 'Edit Archive' })).toBeInTheDocument();
  });

  it('says why backing up to a location stopped, and once a server is removed shows the first on again', async () => {
    const stopped = locations({ pro: true, locations: [...locations().locations.slice(0, 1), { ...archive, on: true }],
      activity: { task: 'backup', stage: 'running', locations: [{ id: 's1', done: 0, total: 2, bytes: 0, totalBytes: 2, problem: 'Archive is full.' }], sent: null } });
    show(stopped);
    expect(within(pane('Archive details')).getByRole('status')).toHaveClass('text-danger-t');
    await userEvent.click(screen.getByRole('button', { name: /^Studio iMac/ }));
    await userEvent.click(screen.getByRole('button', { name: /^Archive/ }));
    await userEvent.click(within(pane('Archive details')).getByRole('button', { name: 'Remove location' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Remove Archive?' })).getByRole('button', { name: 'Remove location' }));
    await vi.waitFor(() => expect(screen.getByRole('button', { name: /^Archive/ })).toHaveAttribute('aria-current', 'true'));
  });

  it('with no way to send given, choosing photos and Send to… only puts them away', async () => {
    show(locations(), { onSend: undefined });
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    await userEvent.click(within(pane('Choose photos')).getByRole('button', { name: /IMG_2\.MOV/ }));
    await userEvent.click(within(pane('Choose photos')).getByRole('button', { name: 'Send to…' }));
    expect(screen.queryByRole('region', { name: 'Choose photos' })).not.toBeInTheDocument();
  });

  it('with only the computer on shows it, and with none on the first there is', () => {
    const { unmount } = show(locations());
    expect(pane('Studio iMac details')).toBeInTheDocument();
    unmount();
    show(locations({ locations: [location({ id: 'computer', kind: 'computer', name: 'Computer' }), location()] }));
    expect(pane('Computer details')).toBeInTheDocument();
  });

  it('chooses photos to send beside the list, all or some, and Send to… sends those; Cancel puts them away', async () => {
    const { onSend } = show(locations({ pro: true, locations: [...locations().locations.slice(0, 1), drive] }));
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    const picker = () => pane('Choose photos');
    expect(screen.getByRole('button', { name: 'Choose photos to send' })).toHaveAttribute('aria-current', 'true');
    expect(within(picker()).getByRole('button', { name: 'Send to…' })).toBeDisabled();
    await userEvent.click(within(picker()).getByRole('button', { name: 'Select all' }));
    expect(picker()).toHaveTextContent('2 selected');
    expect(picker()).toHaveTextContent('3.0 MB');
    await userEvent.click(within(picker()).getByRole('button', { name: 'Deselect all' }));
    await userEvent.click(within(picker()).getByRole('button', { name: /IMG_1\.JPG/ }));
    await userEvent.click(within(picker()).getByRole('button', { name: 'Send to…' }));
    expect(onSend).toHaveBeenCalledWith(['a']);
    expect(screen.queryByRole('region', { name: 'Choose photos' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    await userEvent.click(within(picker()).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('region', { name: 'Choose photos' })).not.toBeInTheDocument();
  });

  it('with nothing in the library, Select all chooses nothing', async () => {
    show(locations(), { files: undefined });
    await userEvent.click(screen.getByRole('button', { name: 'Choose photos to send' }));
    await userEvent.click(within(pane('Choose photos')).getByRole('button', { name: 'Select all' }));
    expect(pane('Choose photos')).toHaveTextContent('0 selected');
  });

  it('shows what a send did beside the list, in place of the location', async () => {
    const onDone = vi.fn();
    show(locations({ pro: true, locations: [...locations().locations.slice(0, 1), drive] }), {
      sent: <SentSheet sent={{ locationId: 'google-drive', items: 2, matched: 2, bytes: 5 }} location="Google Drive" cloudPhotos={false} notify={vi.fn()} onDone={onDone} />,
    });
    expect(pane('Sent')).toHaveTextContent('2 items sent to Google Drive');
    expect(screen.queryByRole('dialog', { name: 'Sent' })).not.toBeInTheDocument();
    await userEvent.click(within(pane('Sent')).getByRole('button', { name: 'Done' }));
    expect(onDone).toHaveBeenCalledOnce();
  });
});
