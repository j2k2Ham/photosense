import { act, cleanup, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ConnectServerScreen, LocationsCard, LocationScreen, LocationsGroup, LocationTile, SendToSheet, SentSheet } from '../../../components/phone/Locations';
import type { LocationDetailsDto, LocationsDto } from '../../../types';
import { deferred } from '../../deferred';
import { location, locations, plan } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({
  useLocations: vi.fn(), useLocationDetails: vi.fn(), turnOnLocation: vi.fn(), turnOffLocation: vi.fn(), locationInstead: vi.fn(), disconnectLocation: vi.fn(), removeLocation: vi.fn(),
  renameLocation: vi.fn(), setCountAt: vi.fn(), backUpToLocations: vi.fn(), checkLocations: vi.fn(), pauseLocations: vi.fn(), connectServer: vi.fn(), deleteSent: vi.fn(), forgetComputer: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

let listed: { data?: LocationsDto };
let details: { data?: LocationDetailsDto };
beforeEach(() => {
  for (const mock of Object.values(api)) mock.mockReset();
  listed = { data: locations() };
  details = {};
  api.useLocations.mockImplementation(() => listed);
  api.useLocationDetails.mockImplementation(() => details);
  for (const change of [api.turnOnLocation, api.turnOffLocation, api.locationInstead, api.disconnectLocation, api.removeLocation, api.renameLocation, api.setCountAt,
    api.backUpToLocations, api.checkLocations, api.pauseLocations]) change.mockResolvedValue(undefined);
  api.connectServer.mockResolvedValue('new');
  api.forgetComputer.mockResolvedValue({ computer: 'Studio iMac', told: true });
  api.deleteSent.mockResolvedValue({ removed: 2, bytes: 9, left: 0, storageTotal: 1, storageFreeBefore: 0 });
});

const refused = (message: string, status: number) => Object.assign(new Error(message), { status });
const S3 = location({ id: 's1', kind: 'amazonS3', name: 'Archive', on: true, connected: true, settings: { bucket: 'photos', region: 'us-west-2 (Oregon)', akid: 'AKIA1234567Q2F' }, copies: 1 });

function group(more: Partial<LocationsDto> = {}, tier: 'free' | 'pro' = 'free') {
  listed = { data: locations(more) };
  const notify = vi.fn(), onPair = vi.fn();
  const offered = gate({ plan: plan({ tier }), offer: vi.fn() });
  inFrame(<LocationsGroup notify={notify} onPair={onPair} />, frame(), offered);
  const row = (name: string) => within(screen.getByRole('region', { name: 'Locations' })).getByRole('switch', { name });
  return { notify, onPair, offered, row };
}

describe('the tiles', () => {
  it('draw a cloud service by its letter on its colour, and the computer and a location added by a glyph', () => {
    const { again, container } = inFrame(<LocationTile location={{ kind: 'dropbox' }} />);
    expect(container.textContent).toBe('D');
    again(<LocationTile location={{ kind: 'computer' }} />);
    expect(container.querySelector('svg')).not.toBeNull();
    again(<LocationTile location={{ kind: 'webDav' }} />);
    expect(container.querySelectorAll('rect')).toHaveLength(2);
  });
});

describe("Settings' Locations", () => {
  it('says it is looking until the phone has said', () => {
    listed = {};
    inFrame(<LocationsGroup notify={vi.fn()} onPair={vi.fn()} />);
    expect(screen.getByRole('status')).toHaveTextContent('Looking at where backups go…');
  });

  it('lists the computer and the cloud services each with a switch, and one not available yet cannot be turned on', () => {
    group({ locations: [...locations().locations.slice(0, 3), location({ id: 'dropbox', kind: 'dropbox', name: 'Dropbox', available: false })] });
    const list = within(screen.getByRole('region', { name: 'Locations' }));
    expect(list.getAllByRole('switch').map(s => s.getAttribute('aria-label'))).toEqual(['Studio iMac', 'Google Drive', 'OneDrive', 'Dropbox']);
    expect(list.getByRole('switch', { name: 'Dropbox' })).toBeDisabled();
    expect(list.getByText('Not available in this version yet')).toBeInTheDocument();
    expect(screen.getByText(/The free plan backs up to your computer and one other location\./)).toBeInTheDocument();
    expect(list.getByRole('button', { name: /^Connect to server…\s*PRO$/ })).toBeInTheDocument();
  });

  it('turns a location on, signing in as it does, and off, saying so', async () => {
    const { notify, row } = group();
    const signing = deferred<void>();
    api.turnOnLocation.mockReturnValueOnce(signing.promise);
    await userEvent.click(row('Dropbox'));
    expect(screen.getByText('Signing in…')).toBeInTheDocument();
    expect(row('OneDrive')).toBeDisabled();
    signing.resolve();
    await vi.waitFor(() => expect(notify).toHaveBeenCalledWith('Dropbox is on.', 'ok'));
    await userEvent.click(row('Studio iMac'));
    expect(api.turnOffLocation).toHaveBeenCalledWith('computer');
    expect(notify).toHaveBeenLastCalledWith('Studio iMac is off. Nothing there was deleted.', 'ok');
    api.turnOffLocation.mockRejectedValueOnce(new Error('No answer.'));
    await userEvent.click(row('Studio iMac'));
    expect(notify).toHaveBeenLastCalledWith('No answer.', 'error');
    api.turnOffLocation.mockRejectedValueOnce('no answer');
    await userEvent.click(row('Studio iMac'));
    expect(notify).toHaveBeenLastCalledWith('no answer', 'error');
  });

  it('says why a location stopped, in its row and in full, and Back puts the location away', async () => {
    group({ locations: [...locations().locations, { ...S3, problem: 'Down for the night.' }] });
    const archive = within(screen.getByRole('region', { name: 'Locations' })).getByText('Down for the night.');
    expect(archive).toHaveClass('text-danger-t');
    await userEvent.click(screen.getByRole('button', { name: /^Archive/ }));
    const layer = screen.getByRole('dialog', { name: 'Archive' });
    expect(within(layer).getByText('Down for the night.')).toHaveClass('text-danger-t');
    await userEvent.click(within(layer).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog', { name: 'Archive' })).not.toBeInTheDocument();
  });

  it('pairs a computer first, from its switch or its row, and says why a sign-in did not happen', async () => {
    const { notify, onPair, row } = group({ locations: [location({ id: 'computer', kind: 'computer', name: 'Computer' }), location()] });
    await userEvent.click(row('Computer'));
    expect(onPair).toHaveBeenCalledOnce();
    await userEvent.click(screen.getByRole('button', { name: /^Computer\s*Not paired yet\. Tap to connect your computer\.$/ }));
    expect(onPair).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    api.turnOnLocation.mockRejectedValueOnce(refused('Signing in was closed. Nothing changed.', 409));
    await userEvent.click(row('Google Drive'));
    expect(notify).toHaveBeenLastCalledWith('Signing in was closed. Nothing changed.', 'info');
    api.turnOnLocation.mockRejectedValueOnce(refused('Google Drive signed Trovolve out.', 502));
    await userEvent.click(row('Google Drive'));
    expect(notify).toHaveBeenLastCalledWith('Google Drive signed Trovolve out.', 'error');
  });

  it('on the free plan with another on besides the computer, opens the Pro sheet offering this one instead', async () => {
    const [computer, , ...rest] = locations().locations;
    const { notify, offered, row } = group({ locations: [computer, location({ on: true, connected: true }), ...rest] });
    api.turnOnLocation.mockRejectedValueOnce(refused('The free plan backs up to your computer and one other location, and Google Drive is on.', 402));
    await userEvent.click(row('Dropbox'));
    const [reason, then, instead] = (offered.offer as ReturnType<typeof vi.fn>).mock.lastCall!;
    expect([reason, instead.current, instead.wanted]).toEqual(['location', 'Google Drive', 'Dropbox']);
    instead.run();
    await vi.waitFor(() => expect(notify).toHaveBeenLastCalledWith('Dropbox is on in place of Google Drive.', 'ok'));
    expect(api.locationInstead).toHaveBeenCalledWith('dropbox');
    then();
    await vi.waitFor(() => expect(notify).toHaveBeenLastCalledWith('Dropbox is on.', 'ok'));
  });

  it('once backups have gone to the one that is on besides the computer, the free plan keeps it: it is not turned off, and another is offered only with Pro', async () => {
    const [computer, , ...rest] = locations().locations;
    const { notify, offered, row } = group({ locations: [computer, location({ on: true, connected: true }), ...rest], held: 'google-drive' });
    const held = 'Google Drive is the only location besides your computer the free plan can use, as your backups are there. Pro backs up to as many locations as you like.';
    await userEvent.click(row('Google Drive'));
    expect(notify).toHaveBeenLastCalledWith(held, 'info');
    expect(api.turnOffLocation).not.toHaveBeenCalled();
    // The computer goes off as ever.
    await userEvent.click(row('Studio iMac'));
    expect(api.turnOffLocation).toHaveBeenCalledWith('computer');
    api.turnOnLocation.mockRejectedValueOnce(refused('The free plan backs up to your computer and one other location, and your backups are on Google Drive.', 402));
    await userEvent.click(row('Dropbox'));
    const [reason, , instead] = (offered.offer as ReturnType<typeof vi.fn>).mock.lastCall!;
    expect([reason, instead.current, instead.run]).toEqual(['location', 'Google Drive', undefined]);

    // Its own page says the same of Turn off, Disconnect and Remove location, asking nothing.
    await userEvent.click(screen.getByRole('button', { name: /^Google Drive/ }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Google Drive' })).getByRole('button', { name: 'Turn off' }));
    expect(notify).toHaveBeenLastCalledWith(held, 'info');
    cleanup();
    const archive = { ...S3, account: null };
    const drive = location({ on: true, connected: true });
    for (const [l, button] of [[archive, 'Remove location'], [drive, 'Disconnect']] as const) {
      const shown = group({ locations: [...locations().locations.slice(0, 1).map(c => ({ ...c, on: false })), l], held: l.id });
      await userEvent.click(screen.getByRole('button', { name: new RegExp(`^${l.name}`) }));
      await userEvent.click(within(screen.getByRole('dialog', { name: l.name })).getByRole('button', { name: button }));
      expect(shown.notify).toHaveBeenLastCalledWith(`${l.name} is the only location besides your computer the free plan can use, as your backups are there. Pro backs up to as many locations as you like.`, 'info');
      expect(screen.queryByRole('dialog', { name: new RegExp(`^(Remove|Disconnect) `) })).not.toBeInTheDocument();
      cleanup();
    }
  });

  it('opens Connect to server on Pro, or the Pro sheet first on the free plan', async () => {
    const { offered } = group();
    await userEvent.click(screen.getByRole('button', { name: /^Connect to server…/ }));
    expect((offered.offer as ReturnType<typeof vi.fn>).mock.lastCall![0]).toBe('server');
    act(() => (offered.offer as ReturnType<typeof vi.fn>).mock.lastCall![1]());
    expect(screen.getByRole('dialog', { name: 'Connect to server' })).toBeInTheDocument();
  });

  it('on Pro opens Connect to server at once, counts a copy on any location or on two, and opens a location to see it in full', async () => {
    const { notify } = group({ pro: true, countAt: 2, locations: [...locations().locations, S3] }, 'pro');
    expect(screen.getByText(/Trovolve backs up to every one that is on/)).toBeInTheDocument();
    expect(screen.getByText('A photo counts as backed up once its copy is checked on 2 locations that are on.')).toBeInTheDocument();
    await userEvent.click(within(screen.getByRole('radiogroup', { name: 'Count a photo as backed up when its copy is checked on' })).getByRole('radio', { name: 'Any location' }));
    expect(api.setCountAt).toHaveBeenCalledWith(1);
    await userEvent.click(screen.getByRole('button', { name: 'Connect to server…' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Connect to server' })).getByRole('button', { name: 'Back' }));
    // A location opens in full; Edit opens Connect to server with its settings.
    await userEvent.click(screen.getByRole('button', { name: /^Archive/ }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Archive' })).getByRole('button', { name: 'Edit' }));
    expect(screen.getByRole('dialog', { name: 'Connect to server' })).toHaveTextContent('Edit Archive');
    expect(notify).not.toHaveBeenCalled();
  });
});

describe('a location in full', () => {
  function show(shown = S3, platform: 'ios' | 'android' = 'ios') {
    const notify = vi.fn(), onEdit = vi.fn(), onClose = vi.fn();
    inFrame(<LocationScreen location={shown} notify={notify} onEdit={onEdit} onClose={onClose} />, frame(platform));
    return { notify, onEdit, onClose, layer: () => screen.getByRole('dialog', { name: shown.name }) };
  }

  it('says how it stands, how full it is, its folder and copies, how it is reached with its key masked, and renames it', async () => {
    details = { data: { location: S3, where: 'photos/Trovolve', space: { total: 2e12, used: 641.8e9 }, problem: null } };
    const { notify, layer } = show();
    expect(layer()).toHaveTextContent('OnAmazon S3 · photos');
    expect(within(layer()).getByRole('progressbar', { name: 'Archive storage' })).toHaveAttribute('aria-valuenow', String(641.8e9));
    expect(layer()).toHaveTextContent('597.72 GB of 1.82 TB used');
    expect(within(layer()).getByText('photos/Trovolve')).toBeInTheDocument();
    expect(layer()).toHaveTextContent('Checked copies 1 file');
    expect(layer()).toHaveTextContent('Access key ID AKIA…7Q2F');
    expect(layer()).toHaveTextContent('Keys and passwords In the Keychain on this iPhone');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Rename' }));
    const naming = screen.getByRole('dialog', { name: 'Rename location' });
    await userEvent.clear(within(naming).getByRole('textbox', { name: 'Name' }));
    await userEvent.type(within(naming).getByRole('textbox', { name: 'Name' }), 'NAS{Enter}');
    expect(api.renameLocation).toHaveBeenCalledWith('s1', 'NAS');
    expect(notify).toHaveBeenLastCalledWith('Renamed NAS.', 'ok');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Rename' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Rename location' })).getByRole('button', { name: 'Save' }));
    await userEvent.click(within(layer()).getByRole('button', { name: 'Rename' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Rename location' })).getByRole('button', { name: 'Done' }));
    expect(screen.queryByRole('dialog', { name: 'Rename location' })).not.toBeInTheDocument();
  });

  it('removes a location added after a question saying nothing there is deleted, and says why it could not', async () => {
    const { notify, onClose, layer } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Remove location' }));
    const asked = screen.getByRole('dialog', { name: 'Remove Archive?' });
    expect(asked).toHaveTextContent('Files at Archive are not deleted.Photos backed up only here stop counting as safe to delete.');
    await userEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
    expect(api.removeLocation).not.toHaveBeenCalled();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Remove location' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Remove Archive?' })).getByRole('button', { name: 'Remove location' }));
    expect(notify).toHaveBeenLastCalledWith('Archive was removed. Nothing there was deleted.', 'ok');
    expect(onClose).toHaveBeenCalledOnce();
    api.turnOffLocation.mockRejectedValueOnce(new Error('No answer.'));
    await userEvent.click(within(layer()).getByRole('button', { name: 'Turn off' }));
    expect(notify).toHaveBeenLastCalledWith('No answer.', 'error');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Turn off' }));
    expect(notify).toHaveBeenLastCalledWith('Archive is off. Nothing there was deleted.', 'ok');
  });

  it('signs out of a cloud service, its folder said before the service answers, and the problem when it cannot be reached', async () => {
    details = { data: { location: location(), where: null, space: null, problem: 'No answer.' } };
    const drive = location({ connected: true, account: 'alex.m@gmail.com', lastBackup: new Date().toISOString(), lastChecked: new Date().toISOString() });
    const { notify, layer } = show(drive, 'android');
    expect(within(layer()).getByRole('alert')).toHaveTextContent('No answer.');
    expect(layer()).toHaveTextContent('Folder My Drive/Trovolve');
    expect(layer()).toHaveTextContent('Last backup today at');
    expect(within(layer()).queryByRole('button', { name: 'Turn off' })).not.toBeInTheDocument();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Disconnect' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Disconnect Google Drive?' })).getByRole('button', { name: 'Disconnect' }));
    expect(api.disconnectLocation).toHaveBeenCalledWith('google-drive');
    expect(notify).toHaveBeenLastCalledWith('Google Drive is disconnected. Nothing there was deleted.', 'ok');
  });

  it('for the computer says where its copies go, and is checked by the computer itself', () => {
    details = { data: { location: S3, where: 'Pictures/Trovolve', space: null, problem: null } };
    const { layer } = show(location({ id: 'computer', kind: 'computer', name: 'Studio iMac', connected: true, settings: { folder: 'Pictures/Trovolve' } }));
    expect(layer()).toHaveTextContent('Last backup Not yet');
    expect(layer()).not.toHaveTextContent('Last checked');
    expect(within(layer()).queryByRole('button', { name: 'Remove location' })).not.toBeInTheDocument();
    cleanup();
    // A computer not paired yet has nothing to forget.
    show(location({ id: 'computer', kind: 'computer', name: 'Computer' }));
    expect(screen.queryByRole('button', { name: 'Forget this computer' })).not.toBeInTheDocument();
  });

  it('forgets the computer after a question saying its backups stay there, and says whether the computer heard', async () => {
    const studio = location({ id: 'computer', kind: 'computer', name: 'Studio iMac', on: true, connected: true, settings: { folder: 'Pictures/Trovolve' } });
    const { notify, onClose, layer } = show(studio);
    await userEvent.click(within(layer()).getByRole('button', { name: 'Forget this computer' }));
    const asked = screen.getByRole('dialog', { name: 'Forget Studio iMac?' });
    expect(asked).toHaveTextContent('This iPhone stops backing up to Studio iMac, until you pair them again. The backups on Studio iMac stay there; nothing is deleted.');
    expect(asked).toHaveTextContent('Photos backed up only there stop counting as safe to delete.');
    await userEvent.click(within(asked).getByRole('button', { name: 'Cancel' }));
    expect(api.forgetComputer).not.toHaveBeenCalled();

    await userEvent.click(within(layer()).getByRole('button', { name: 'Forget this computer' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Forget Studio iMac?' })).getByRole('button', { name: 'Forget Studio iMac' }));
    expect(notify).toHaveBeenLastCalledWith('Forgot Studio iMac. It no longer lists this iPhone.', 'ok');
    expect(onClose).toHaveBeenCalledOnce();

    api.forgetComputer.mockResolvedValueOnce({ computer: 'Studio iMac', told: false });
    await userEvent.click(within(layer()).getByRole('button', { name: 'Forget this computer' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Forget Studio iMac?' })).getByRole('button', { name: 'Forget Studio iMac' }));
    expect(notify).toHaveBeenLastCalledWith('Forgot Studio iMac. It was not found on this Wi-Fi, so it still lists this iPhone: forget it in Phones there too.', 'ok');

    api.forgetComputer.mockRejectedValueOnce(new Error('This phone is not paired with a computer.'));
    await userEvent.click(within(layer()).getByRole('button', { name: 'Forget this computer' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'Forget Studio iMac?' })).getByRole('button', { name: 'Forget Studio iMac' }));
    expect(notify).toHaveBeenLastCalledWith('This phone is not paired with a computer.', 'error');
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('on Android says its keys are in the Keystore', () => {
    const { layer } = show(S3, 'android');
    expect(layer()).toHaveTextContent('In the Android Keystore on this phone');
    expect(layer()).toHaveTextContent('Last checked Not yet');
  });
});

describe('Connect to server', () => {
  function show(editing?: typeof S3, platform: 'ios' | 'android' = 'ios') {
    const notify = vi.fn(), onClose = vi.fn();
    inFrame(<ConnectServerScreen editing={editing} notify={notify} onClose={onClose} />, frame(platform));
    return { notify, onClose, page: () => screen.getByRole('dialog', { name: 'Connect to server' }) };
  }

  it('asks for each kind\'s fields, a name the person chooses first, and checks the location before adding it', async () => {
    const { notify, onClose, page } = show();
    expect(page()).toHaveTextContent('Use an access key that only reaches this bucket.');
    await userEvent.type(within(page()).getByRole('textbox', { name: 'Name' }), 'Archive');
    await userEvent.selectOptions(within(page()).getByRole('combobox', { name: 'Region' }), 'us-west-2 (Oregon)');
    await userEvent.type(within(page()).getByRole('textbox', { name: 'Bucket' }), 'photos');
    await userEvent.type(within(page()).getByRole('textbox', { name: 'Access key ID' }), 'AKIA');
    await userEvent.type(within(page()).getByLabelText('Secret key'), 's3cret');
    const checking = deferred<string>();
    api.connectServer.mockReturnValueOnce(checking.promise);
    await userEvent.click(within(page()).getByRole('button', { name: 'Connect' }));
    expect(within(page()).getByRole('button', { name: 'Checking…' })).toBeDisabled();
    checking.resolve('new');
    await vi.waitFor(() => expect(onClose).toHaveBeenCalledOnce());
    expect(api.connectServer).toHaveBeenCalledWith({
      id: null, kind: 'amazonS3', name: 'Archive', settings: { region: 'us-west-2 (Oregon)', cls: 'Standard', bucket: 'photos', akid: 'AKIA' }, secrets: { secret: 's3cret' },
    });
    expect(notify).toHaveBeenCalledWith('Archive is connected and on.', 'ok');
  });

  it('says what to change when the check fails, the page staying filled in, and names a location left unnamed by its kind', async () => {
    const { notify, page } = show(undefined, 'android');
    await userEvent.click(within(page()).getByRole('radio', { name: 'SFTP' }));
    expect(page()).toHaveTextContent('Keys and passwords are saved in the Android Keystore on this phone and are only sent to this location.');
    expect(within(page()).getByLabelText('Password')).toBeInTheDocument();
    await userEvent.click(within(within(page()).getByRole('radiogroup', { name: 'Sign in with' })).getByRole('radio', { name: 'SSH key' }));
    expect(within(page()).getByLabelText('Private key')).toBeInTheDocument();
    await userEvent.type(within(page()).getByRole('textbox', { name: 'Host' }), 'nas.local');
    api.connectServer.mockRejectedValueOnce(new Error('The user name, password or key was not accepted.'));
    await userEvent.click(within(page()).getByRole('button', { name: 'Connect' }));
    expect(within(page()).getByRole('alert')).toHaveTextContent('Could not connectThe user name, password or key was not accepted.');
    expect(within(page()).getByRole('textbox', { name: 'Host' })).toHaveValue('nas.local');
    await userEvent.click(within(page()).getByRole('button', { name: 'Connect' }));
    expect(notify).toHaveBeenCalledWith('SFTP is connected and on.', 'ok');
    // Another kind starts afresh.
    await userEvent.click(within(page()).getByRole('radio', { name: 'Azure Blob' }));
    expect(within(page()).getByRole('textbox', { name: 'Storage account' })).toHaveValue('');
  });

  it('changes a location added, from its settings, its keys typed again', async () => {
    const { onClose, page } = show(S3);
    expect(page()).toHaveTextContent('Edit Archive');
    expect(within(page()).queryByRole('radiogroup', { name: 'Kind' })).not.toBeInTheDocument();
    expect(within(page()).getByRole('textbox', { name: 'Bucket' })).toHaveValue('photos');
    await userEvent.click(within(page()).getByRole('button', { name: 'Save' }));
    expect(api.connectServer).toHaveBeenCalledWith(expect.objectContaining({ id: 's1', kind: 'amazonS3', name: 'Archive' }));
    await vi.waitFor(() => expect(onClose).toHaveBeenCalled());
  });
});

describe('Send to and what was sent', () => {
  it('lists the locations that are on, the one chosen last first, and sends there', async () => {
    const onSend = vi.fn();
    const on = [locations().locations[0], S3];
    inFrame(<SendToSheet locations={on} count={2} chosen="s1" onSend={onSend} onClose={vi.fn()} />);
    expect(screen.getByRole('radio', { name: /Archive/ })).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(screen.getByRole('radio', { name: /Studio iMac/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Send 2 items to Studio iMac' }));
    expect(onSend).toHaveBeenCalledWith(on[0]);
  });

  it('says each matches the copy there, warns of iCloud, and deletes what matched from the phone within the free removals', async () => {
    const notify = vi.fn(), onDone = vi.fn();
    const offered = gate({ plan: plan({ leftToday: 1 }), remove: vi.fn((_n: number, _l: string, run: (allowed: number) => void) => run(1)) });
    const { again } = inFrame(<SentSheet sent={{ locationId: 's1', items: 2, matched: 2, bytes: 3 * 1024 ** 2 }} location="Archive" cloudPhotos notify={notify} onDone={onDone} />, frame(), offered);
    const sheet = () => screen.getByRole('dialog', { name: 'Sent' });
    expect(sheet()).toHaveTextContent('2 items sent to ArchiveEach one matches the copy in Archive. What takes 3.0 MB here can be deleted from this iPhone.');
    expect(sheet()).toHaveTextContent('If iCloud Photos is on, deleting here also deletes them from iCloud');
    expect(sheet()).toHaveTextContent('Free plan: 1 removal left today.');
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Delete 2 items from this iPhone' }));
    expect(api.deleteSent).toHaveBeenCalledWith(1);
    expect(notify).toHaveBeenCalledWith('2 items deleted from this iPhone. They wait in Recently Deleted for 30 days. The copies in Archive stay.', 'ok');
    expect(onDone).toHaveBeenCalledOnce();
    api.deleteSent.mockRejectedValueOnce(new Error('You chose not to delete them.'));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Delete 2 items from this iPhone' }));
    expect(notify).toHaveBeenLastCalledWith('You chose not to delete them.', 'error');

    // Some did not match: they stay; with none matched there is nothing to delete.
    again(<SentSheet sent={{ locationId: 's1', items: 3, matched: 2, bytes: 1 }} location="Archive" cloudPhotos={false} notify={notify} onDone={onDone} />, frame('android'));
    expect(sheet()).toHaveTextContent('1 item did not match the copy in Archive, and stays on this phone.');
    again(<SentSheet sent={{ locationId: 's1', items: 2, matched: 0, bytes: 0 }} location="Archive" cloudPhotos notify={notify} onDone={onDone} />, frame('ios'));
    expect(sheet()).toHaveTextContent('2 items did not match the copy in Archive, and stay on this iPhone.');
    expect(within(sheet()).queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Done' }));
    expect(onDone).toHaveBeenCalledTimes(2);
  });
});

describe("the Backup tab's locations", () => {
  it('shows each location on other than the computer, how far it has got while backing up, and pauses', async () => {
    const notify = vi.fn(), onSettings = vi.fn();
    const busy = locations({
      locations: [...locations().locations, S3, { ...S3, id: 's2', name: 'NAS', problem: 'Down.' }, { ...S3, id: 's3', name: 'Cold', lastBackup: new Date().toISOString() }],
      activity: { task: 'backup', stage: 'running', locations: [{ id: 's1', done: 1, total: 3, bytes: 1024, totalBytes: 3072, problem: null }], sent: null },
    });
    const { again } = inFrame(<LocationsCard data={busy} notify={notify} onSettings={onSettings} />);
    const card = () => screen.getByRole('region', { name: 'Locations' });
    expect(card()).toHaveTextContent('Archive1 of 3 · 1 KB of 3 KB');
    expect(card()).toHaveTextContent('NASDown.');
    expect(card()).toHaveTextContent('3 on');
    await userEvent.click(screen.getByRole('button', { name: 'Pause' }));
    expect(notify).toHaveBeenLastCalledWith('Paused. What is there stays there.', 'info');
    expect(screen.getByRole('button', { name: 'Check all backups' })).toBeDisabled();
    await userEvent.click(within(card()).getByRole('button', { name: 'Choose locations' }));
    expect(onSettings).toHaveBeenCalledOnce();

    again(<LocationsCard data={{ ...busy, activity: null }} notify={notify} onSettings={onSettings} />);
    expect(card()).toHaveTextContent('ArchiveNot backed up yet');
    expect(card()).toHaveTextContent('Cold1 file · backed up today at');
    await userEvent.click(screen.getByRole('button', { name: 'Back up now' }));
    expect(notify).toHaveBeenLastCalledWith('Backing up to 3 locations. Every copy is checked.', 'info');
    api.checkLocations.mockRejectedValueOnce(new Error('Busy.'));
    await userEvent.click(screen.getByRole('button', { name: 'Check all backups' }));
    expect(notify).toHaveBeenLastCalledWith('Busy.', 'error');
    await userEvent.click(screen.getByRole('button', { name: 'Check all backups' }));
    expect(notify).toHaveBeenLastCalledWith('Checking every copy.', 'info');

    again(<LocationsCard data={locations({ locations: [S3] })} notify={notify} onSettings={onSettings} />);
    await userEvent.click(screen.getByRole('button', { name: 'Back up now' }));
    expect(notify).toHaveBeenLastCalledWith('Backing up to Archive. Every copy is checked.', 'info');
    again(<LocationsCard data={locations()} notify={notify} onSettings={onSettings} />);
    expect(within(card()).getByRole('button', { name: 'Back up to cloud storage or your own server' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Back up now' })).not.toBeInTheDocument();
  });
});
