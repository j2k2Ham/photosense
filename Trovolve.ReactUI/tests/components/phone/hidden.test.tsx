import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { HiddenScreen, HiddenTile } from '../../../components/phone/Hidden';
import type { Platform } from '../../../lib/phone';
import type { VaultDto, VaultItemDto } from '../../../types';
import { organizeFile, plan } from '../../fixtures';
import { frame, gate, inFrame } from './frame';

const api = vi.hoisted(() => ({
  useVault: vi.fn(), turnOnVault: vi.fn(), recoverVault: vi.fn(), unlockVault: vi.fn(), lockVault: vi.fn(), vaultRecoveryKey: vi.fn(),
  hideItems: vi.fn(), unhideItems: vi.fn(), deleteHidden: vi.fn(),
}));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const KEY = 'K7QP-2M9X-AB12-CD34-EF56-GH78-JK90-MN12';
const item = (more: Partial<VaultItemDto> = {}): VaultItemDto => ({ id: 'h1', name: 'IMG_0001.JPG', taken: '2024-04-07T17:35:59', isVideo: false, bytes: 1000, onComputer: false, ...more });
const vault = (more: Partial<VaultDto> = {}): VaultDto => ({
  stage: 'open', count: 2, notBackedUp: 0, computer: 'Studio iMac', items: [item(), item({ id: 'h2', name: 'IMG_0003.MOV', isVideo: true, taken: null })], restore: null, ...more,
});

let answer: { data?: VaultDto; error?: unknown };
beforeEach(() => {
  answer = { data: vault() };
  for (const mock of Object.values(api)) mock.mockReset();
  api.useVault.mockImplementation(() => answer);
  api.turnOnVault.mockResolvedValue(KEY);
  api.recoverVault.mockResolvedValue(undefined);
  api.unlockVault.mockResolvedValue(true);
  api.lockVault.mockResolvedValue(undefined);
  api.vaultRecoveryKey.mockResolvedValue(KEY);
  api.hideItems.mockResolvedValue(2);
  api.unhideItems.mockResolvedValue(1);
  api.deleteHidden.mockResolvedValue(1);
});

const FILES = [organizeFile({ id: 'o1', name: 'IMG_1.JPG' }), organizeFile({ id: 'o2', name: 'IMG_2.MOV', isVideo: true, durationSeconds: 42 }), organizeFile({ id: 'o3', name: 'IMG_3.MOV', isVideo: true })];

function show(platform: Platform = 'ios', tier: 'free' | 'pro' = 'pro', leftToday: number | null = null) {
  const notify = vi.fn(), onClose = vi.fn();
  const offered = gate({ plan: plan({ tier, leftToday }), offer: vi.fn((_reason: string, then?: () => void) => then?.()) });
  const view = inFrame(<HiddenScreen files={FILES} notify={notify} onClose={onClose} />, frame(platform), offered);
  return { notify, onClose, offered, again: () => view.again(<HiddenScreen files={FILES} notify={notify} onClose={onClose} />), layer: () => screen.getByRole('dialog', { name: 'Hidden' }) };
}

describe("Library's Hidden tile", () => {
  it('says how many it holds, Pro before it is on on the free plan, or Turn on, and opens it', async () => {
    const onOpen = vi.fn();
    const { again } = inFrame(<HiddenTile vault={undefined} free onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Hidden, Looking…' })).toBeInTheDocument();
    again(<HiddenTile vault={vault({ stage: 'off', count: 0 })} free onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Hidden, Pro' })).toBeInTheDocument();
    again(<HiddenTile vault={vault({ stage: 'off', count: 0 })} free={false} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Hidden, Turn on' })).toBeInTheDocument();
    again(<HiddenTile vault={vault({ stage: 'locked', count: 0 })} free={false} onOpen={onOpen} />);
    expect(screen.getByRole('button', { name: 'Hidden, Nothing hidden' })).toBeInTheDocument();
    again(<HiddenTile vault={vault({ stage: 'locked', count: 24 })} free onOpen={onOpen} />);
    await userEvent.click(screen.getByRole('button', { name: 'Hidden, 24 items' }));
    expect(onOpen).toHaveBeenCalledOnce();
  });
});

describe('the hidden vault (33–34)', () => {
  it('says what went wrong opening it, and Back closes it', async () => {
    answer = { error: new Error('No.') };
    const { layer, again, onClose } = show();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('The hidden vault could not be opened: No.');
    answer = { error: 'gone' };
    again();
    expect(within(layer()).getByRole('alert')).toHaveTextContent('could not be opened: gone');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Back' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('on the free plan says what it is and offers Pro, which turns it on and shows the recovery key to write down', async () => {
    answer = { data: vault({ stage: 'off', count: 0, items: [] }) };
    const { layer, offered } = show('ios', 'free');
    expect(layer()).toHaveTextContent('Hidden vaultKeep private photos behind Face ID. They are encrypted inside Trovolve and removed from your Photos library.');
    expect(layer()).toHaveTextContent('The hidden vault is part of Pro.');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Get Pro' }));
    expect(offered.offer).toHaveBeenCalledWith('vault', expect.any(Function));
    expect(api.turnOnVault).toHaveBeenCalledOnce();
    const sheet = await screen.findByRole('dialog', { name: 'Recovery key' });
    expect(within(sheet).getByLabelText('Your recovery key')).toHaveTextContent(KEY);
    expect(sheet).toHaveTextContent('Without it, nobody can open them, Trovolve included.');
    await userEvent.click(within(sheet).getByRole('button', { name: 'I wrote it down' }));
    expect(screen.queryByRole('dialog', { name: 'Recovery key' })).not.toBeInTheDocument();
  });

  it('on Pro, on Android, turns it on, or says why not', async () => {
    answer = { data: vault({ stage: 'off', count: 0, items: [] }) };
    api.turnOnVault.mockRejectedValueOnce(new Error('The hidden vault is on already.'));
    const { layer, notify } = show('android');
    expect(layer()).toHaveTextContent('Keep private photos behind your fingerprint. They are encrypted inside Trovolve and removed from your gallery.');
    expect(layer()).not.toHaveTextContent('part of Pro');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Turn on Hidden' }));
    expect(notify).toHaveBeenCalledWith('The hidden vault is on already.', 'error');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Turn on Hidden' }));
    expect(await screen.findByRole('dialog', { name: 'Recovery key' })).toBeInTheDocument();
  });

  it('restores with a recovery key typed, saying where from, or why not, and Cancel puts it away', async () => {
    answer = { data: vault({ stage: 'off', count: 0, items: [], computer: null }) };
    api.recoverVault.mockRejectedValueOnce('That is not a recovery key.');
    const { layer, notify, again } = show();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Restore with a recovery key' }));
    let sheet = screen.getByRole('dialog', { name: 'Restore hidden items' });
    expect(sheet).toHaveTextContent('Your hidden items come back from your computer, still encrypted.');
    expect(within(sheet).getByRole('button', { name: 'Restore' })).toBeDisabled();
    await userEvent.type(within(sheet).getByLabelText('Recovery key'), 'nonsense');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Restore' }));
    expect(notify).toHaveBeenCalledWith('That is not a recovery key.', 'error');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog', { name: 'Restore hidden items' })).not.toBeInTheDocument();

    answer = { data: vault({ stage: 'off', count: 0, items: [] }) };
    again();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Restore with a recovery key' }));
    sheet = screen.getByRole('dialog', { name: 'Restore hidden items' });
    expect(sheet).toHaveTextContent('come back from Studio iMac');
    await userEvent.type(within(sheet).getByLabelText('Recovery key'), KEY);
    await userEvent.click(within(sheet).getByRole('button', { name: 'Restore' }));
    expect(api.recoverVault).toHaveBeenLastCalledWith(KEY);
    expect(screen.queryByRole('dialog', { name: 'Restore hidden items' })).not.toBeInTheDocument();
  });

  it('says while items come back from the computer, and when they have, or why not', () => {
    answer = { data: vault({ restore: { stage: 'running', problem: null, restored: null } }) };
    const { layer, again, notify } = show();
    expect(within(layer()).getByRole('status')).toHaveTextContent('Bringing your hidden items back from Studio iMac…');
    answer = { data: vault({ restore: { stage: 'ready', problem: null, restored: 3 } }) };
    again();
    expect(notify).toHaveBeenCalledWith('3 items brought back from Studio iMac.', 'ok');
    expect(within(layer()).queryByRole('status')).not.toBeInTheDocument();
    answer = { data: vault({ computer: null, restore: { stage: 'running', problem: null, restored: null } }) };
    again();
    expect(within(layer()).getByRole('status')).toHaveTextContent('Bringing your hidden items back from your computer…');
    answer = { data: vault({ restore: { stage: 'failed', problem: 'A hidden item arrived damaged.', restored: null } }) };
    again();
    expect(notify).toHaveBeenCalledWith('A hidden item arrived damaged.', 'error');
  });

  it('locked, says how many it holds and asks the phone before it opens', async () => {
    answer = { data: vault({ stage: 'locked', count: 1, items: [] }) };
    const { layer } = show();
    expect(layer()).toHaveTextContent('Hidden is locked1 item');
    expect(within(layer()).queryByRole('button', { name: 'Lock' })).not.toBeInTheDocument();
    await userEvent.click(within(layer()).getByRole('button', { name: 'Unlock with Face ID' }));
    expect(api.unlockVault).toHaveBeenCalledOnce();
  });

  it('locked on Android, asks for the fingerprint', () => {
    answer = { data: vault({ stage: 'locked' }) };
    const { layer } = show('android');
    expect(within(layer()).getByRole('button', { name: 'Unlock with fingerprint' })).toBeInTheDocument();
  });

  it('open, shows what it holds, what hiding does, what deleting Trovolve would take, the recovery key, and locks', async () => {
    answer = { data: vault({ notBackedUp: 1 }) };
    const { layer, again } = show();
    const grid = within(layer()).getByRole('list');
    expect(within(grid).getAllByRole('button').map(b => b.getAttribute('aria-label'))).toEqual(['IMG_0001.JPG', 'IMG_0003.MOV']);
    expect(grid.querySelector('img')!.getAttribute('src')).toContain('/vault/h1/thumbnail');
    expect(layer()).toHaveTextContent('Hidden items are encrypted inside Trovolve and removed from your Photos library. Backups to Studio iMac stay encrypted.');
    expect(within(layer()).getByRole('note')).toHaveTextContent('If you delete Trovolve, hidden items that are not backed up are deleted too. 1 item is not backed up yet.');

    answer = { data: vault({ notBackedUp: 2, computer: null }) };
    again();
    expect(within(layer()).getByRole('note')).toHaveTextContent('2 items are not backed up yet.');
    expect(layer()).toHaveTextContent('Pair your computer to back them up, still encrypted.');

    answer = { data: vault({ items: [] }) };
    again();
    expect(layer()).toHaveTextContent('Nothing is hidden yet. Add photos to keep them here.');
    expect(within(layer()).queryByRole('note')).not.toBeInTheDocument();

    await userEvent.click(within(layer()).getByRole('button', { name: 'Show recovery key' }));
    expect(within(await screen.findByRole('dialog', { name: 'Recovery key' })).getByLabelText('Your recovery key')).toHaveTextContent(KEY);
    await userEvent.click(screen.getByRole('button', { name: 'I wrote it down' }));
    await userEvent.click(within(layer()).getByRole('button', { name: 'Lock' }));
    expect(api.lockVault).toHaveBeenCalledOnce();
  });

  it('hides the photos chosen, saying first what becomes of them; on the free plan Pro comes first', async () => {
    api.hideItems.mockRejectedValueOnce(new Error('Nothing was hidden.'));
    const { layer, notify, offered, again } = show('ios', 'free');
    await userEvent.click(within(layer()).getByRole('button', { name: 'Add photos PRO' }));
    expect(offered.offer).toHaveBeenCalledWith('vault', expect.any(Function));
    let picker = screen.getByRole('dialog', { name: 'Choose photos to hide' });
    expect(picker).toHaveTextContent('They are encrypted into Hidden first. Then your iPhone asks before they leave Photos, and they wait in Recently Deleted for 30 days.');
    expect(within(picker).getByRole('button', { name: 'Choose photos to hide' })).toBeDisabled();
    expect(picker).toHaveTextContent('0:42');
    await userEvent.click(within(picker).getByRole('button', { name: 'IMG_1.JPG' }));
    await userEvent.click(within(picker).getByRole('button', { name: 'IMG_2.MOV' }));
    await userEvent.click(within(picker).getByRole('button', { name: 'IMG_2.MOV' }));
    await userEvent.click(within(picker).getByRole('button', { name: 'IMG_3.MOV' }));
    await userEvent.click(within(picker).getByRole('button', { name: 'Hide 2 items' }));
    expect(notify).toHaveBeenCalledWith('Nothing was hidden.', 'error');
    await userEvent.click(within(picker).getByRole('button', { name: 'Hide 2 items' }));
    expect(api.hideItems).toHaveBeenLastCalledWith(['o1', 'o3']);
    expect(notify).toHaveBeenCalledWith('2 items hidden.', 'ok');
    expect(screen.queryByRole('dialog', { name: 'Choose photos to hide' })).not.toBeInTheDocument();

    // On Pro, and on Android, straight to the picker; Back puts it away.
    inFrame(<HiddenScreen files={FILES} notify={notify} onClose={vi.fn()} />, frame('android'), gate({ plan: plan({ tier: 'pro' }) }));
    await userEvent.click(screen.getAllByRole('button', { name: 'Add photos' }).at(-1)!);
    picker = screen.getByRole('dialog', { name: 'Choose photos to hide' });
    expect(picker).toHaveTextContent('Then Android asks before they leave the gallery, and they wait in the bin for 30 days.');
    await userEvent.click(within(picker).getByRole('button', { name: 'Back' }));
    expect(screen.queryByRole('dialog', { name: 'Choose photos to hide' })).not.toBeInTheDocument();
    again();
  });

  it('opens an item whole, and puts it back, or deletes it after a question naming it, within the free removals', async () => {
    api.unhideItems.mockRejectedValueOnce(new Error('Photos would not take it back.'));
    const { layer, notify } = show('ios', 'free', 38);
    await userEvent.click(within(layer()).getByRole('button', { name: 'IMG_0001.JPG' }));
    let viewer = screen.getByRole('dialog', { name: 'IMG_0001.JPG' });
    expect(within(viewer).getByRole('img', { name: 'IMG_0001.JPG' }).getAttribute('src')).toContain('/vault/h1/image');
    expect(viewer).toHaveTextContent('Apr 7, 2024');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Unhide' }));
    expect(notify).toHaveBeenCalledWith('Photos would not take it back.', 'error');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Unhide' }));
    expect(api.unhideItems).toHaveBeenLastCalledWith(['h1']);
    expect(notify).toHaveBeenCalledWith('IMG_0001.JPG is back in your library.', 'ok');
    expect(screen.queryByRole('dialog', { name: 'IMG_0001.JPG' })).not.toBeInTheDocument();

    await userEvent.click(within(layer()).getByRole('button', { name: 'IMG_0003.MOV' }));
    viewer = screen.getByRole('dialog', { name: 'IMG_0003.MOV' });
    expect(viewer).toHaveTextContent('No date');
    expect(viewer).toHaveTextContent('Trovolve cannot play videos yet.');
    await userEvent.click(within(viewer).getByRole('button', { name: 'Delete' }));
    let question = screen.getByRole('dialog', { name: 'Delete IMG_0003.MOV?' });
    expect(question).toHaveTextContent('IMG_0003.MOV goes back to your library, then to Recently Deleted, where it can be found for 30 days.');
    expect(question).toHaveTextContent('Free plan: 38 removals left today.');
    await userEvent.click(within(question).getByRole('button', { name: 'Cancel' }));
    await userEvent.click(within(viewer).getByRole('button', { name: 'Delete' }));
    question = screen.getByRole('dialog', { name: 'Delete IMG_0003.MOV?' });
    await userEvent.click(within(question).getByRole('button', { name: 'Delete' }));
    expect(api.deleteHidden).toHaveBeenLastCalledWith(['h2']);
    expect(notify).toHaveBeenCalledWith('IMG_0003.MOV deleted. It waits in Recently Deleted for 30 days.', 'ok');
  });

  it('on Pro, on Android, asks before deleting with no daily limit, and says the bin', async () => {
    const { layer, notify } = show('android', 'pro');
    await userEvent.click(within(layer()).getByRole('button', { name: 'IMG_0001.JPG' }));
    await userEvent.click(within(screen.getByRole('dialog', { name: 'IMG_0001.JPG' })).getByRole('button', { name: 'Delete' }));
    const question = screen.getByRole('dialog', { name: 'Delete IMG_0001.JPG?' });
    expect(question).not.toHaveTextContent('Free plan');
    expect(question).toHaveTextContent('then to the bin');
    await userEvent.click(within(question).getByRole('button', { name: 'Delete' }));
    expect(notify).toHaveBeenCalledWith('IMG_0001.JPG deleted. It waits in the bin for 30 days.', 'ok');
  });
});
