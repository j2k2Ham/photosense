import React, { useEffect, useRef, useState } from 'react';
import { deleteHidden, hideItems, lockVault, organizeThumbnailUrl, recoverVault, turnOnVault, unhideItems, unlockVault, useVault, vaultImageUrl, vaultRecoveryKey, vaultThumbnailUrl } from '../../lib/apiClient';
import { items } from '../../lib/fixes';
import { deleteQuestion, hiddenTile, hideNote, infoLine, introLine, restoreLine, unlockLabel, warningLine } from '../../lib/hidden';
import { formatDuration } from '../../lib/format';
import { deviceWord, usePhone } from '../../lib/phone';
import { limitLine } from '../../lib/plan';
import type { OrganizeFileDto, VaultDto, VaultItemDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { CollectionTile } from './CollectionTile';
import { PhoneButton, StickyFooter } from './Controls';
import { LockIcon, VideoIcon } from './Icons';
import { PhotoGrid } from './PhotoGrid';
import { usePlanGate } from './ProSheet';
import { LayerScreen, PushedHeader } from './Screens';
import { ConfirmSheet, Sheet } from './Sheet';

type Notify = (message: string, kind: ToastKind, action?: ToastAction) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** 26: Library's Hidden tile, with a lock, and how many it holds, or "Pro" on the free plan before it is on. */
export function HiddenTile({ vault, free, onOpen }: { readonly vault: VaultDto | undefined; readonly free: boolean; onOpen(): void }) {
  const line = hiddenTile(vault, free);
  return (
    <CollectionTile label="Hidden" line={line} tone="fill2" onOpen={onOpen}>
      <LockIcon />
    </CollectionTile>
  );
}

/** The lock in its circle, a title and a line: the vault before it is on, and while it is locked (33). */
function Closed({ title, line }: { readonly title: string; readonly line: string }) {
  return (
    <div className="flex flex-1 flex-col items-center gap-3 px-8 pt-16 text-center">
      <span aria-hidden className="grid h-[72px] w-[72px] place-items-center rounded-full bg-fill2"><LockIcon size={28} /></span>
      <h2 className="font-title text-[24px] font-bold">{title}</h2>
      <p className="max-w-[320px] text-[15px] leading-[1.45] text-t2">{line}</p>
    </div>
  );
}

/**
 * 33–34: the hidden vault, over every tab. Before it is on, what it is, with Get Pro on the free plan, or Turn on
 * Hidden and Restore with a recovery key; locked, how many it holds and the phone's own question; open, its grid,
 * Lock, what hiding does, what deleting PhotoSense would take with it, the recovery key, and Add photos.
 */
export function HiddenScreen({ files, notify, onClose }: { readonly files: readonly OrganizeFileDto[]; notify: Notify; onClose(): void }) {
  const { platform, tablet, words } = usePhone();
  const gate = usePlanGate();
  const vault = useVault(true);
  const data = vault.data;
  const free = gate.plan?.tier === 'free';
  const [key, setKey] = useState<string>();
  const [restoring, setRestoring] = useState(false);
  const [picking, setPicking] = useState(false);
  const [viewing, setViewing] = useState<VaultItemDto>();
  const [busy, setBusy] = useState(false);
  useRestoreEnded(data, notify);

  async function act(run: () => Promise<void>) {
    setBusy(true);
    try { await run(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }

  const turnOn = () => act(async () => setKey(await turnOnVault()));
  const unlock = () => act(async () => { await unlockVault(); });
  const showKey = () => act(async () => setKey(await vaultRecoveryKey()));
  const addPhotos = () => (free ? gate.offer('vault', () => setPicking(true)) : setPicking(true));

  const stage = data?.stage;
  const header = <PushedHeader title="Hidden" onBack={onClose}
    trailing={stage === 'open' ? <button type="button" onClick={() => void act(lockVault)} className="h-9 rounded-full bg-fill px-4 text-[14.5px] font-semibold">Lock</button> : undefined} />;
  const footer = stage === 'off'
    ? (
      <StickyFooter>
        {free
          ? <PhoneButton disabled={busy} onClick={() => gate.offer('vault', () => void turnOn())}>Get Pro</PhoneButton>
          : <>
            <PhoneButton disabled={busy} onClick={() => void turnOn()}>Turn on Hidden</PhoneButton>
            <PhoneButton kind="secondary" onClick={() => setRestoring(true)}>Restore with a recovery key</PhoneButton>
          </>}
        {free && <p className="text-center text-[13px] text-t2">The hidden vault is part of Pro.</p>}
      </StickyFooter>
    )
    : stage === 'locked' ? <StickyFooter><PhoneButton disabled={busy} onClick={() => void unlock()}>{unlockLabel(platform)}</PhoneButton></StickyFooter>
      : stage === 'open' ? (
        <StickyFooter>
          <PhoneButton onClick={addPhotos}>Add photos{free && <> <span className="rounded-full border border-current px-1.5 text-[10.5px] font-bold leading-[16px]">PRO</span></>}</PhoneButton>
        </StickyFooter>
      ) : undefined;

  const restore = restoreLine(data);
  const warning = data ? warningLine(data.notBackedUp) : null;
  return (
    <LayerScreen label="Hidden" frozen={picking || viewing !== undefined || key !== undefined || restoring} onBack={onClose} footer={footer}>
      {header}
      <div className="mx-auto flex w-full max-w-[720px] flex-1 flex-col">
        {vault.error !== undefined && <p role="alert" className="px-4 py-10 text-center text-[15px] text-t2">The hidden vault could not be opened: {said(vault.error)}</p>}
        {stage === 'off' && <Closed title="Hidden vault" line={introLine(platform)} />}
        {stage === 'locked' && <Closed title="Hidden is locked" line={items(data!.count)} />}
        {restore && <p role="status" className="px-4 py-3 text-center text-[14px] text-t2">{restore}</p>}
        {stage === 'open' && <>
          {data!.items.length === 0
            ? <p className="px-4 py-12 text-center text-[15px] text-t2">Nothing is hidden yet. Add photos to keep them here.</p>
            : (
              <ul className="grid grid-cols-4 gap-[2px]">
                {data!.items.map(item => (
                  <li key={item.id}>
                    <button type="button" aria-label={item.name} onClick={() => setViewing(item)} className="relative block aspect-square w-full overflow-hidden bg-fill">
                      {item.isVideo
                        ? <span aria-hidden className="absolute inset-0 grid place-items-center text-t2"><VideoIcon size={20} /></span>
                        : <img src={vaultThumbnailUrl(item.id)} alt="" className="absolute inset-0 h-full w-full object-cover" />}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          <div className="flex flex-col gap-3 px-4 py-4">
            <p className="rounded-cell bg-cell px-4 py-3.5 text-[14px] leading-[1.45] text-t2">{infoLine(platform, data!.computer)}</p>
            {warning && <p role="note" className="rounded-cell bg-amber-soft px-4 py-3.5 text-[14px] leading-[1.45] text-amber">{warning}</p>}
            <button type="button" onClick={() => void showKey()} className="self-start text-[15px] font-semibold text-acc">Show recovery key</button>
          </div>
        </>}
      </div>
      {key !== undefined && <KeySheet recoveryKey={key} onClose={() => setKey(undefined)} />}
      {restoring && <RestoreSheet computer={data?.computer ?? null} notify={notify} onClose={() => setRestoring(false)} />}
      {picking && <HidePicker files={files} note={hideNote(platform, deviceWord(platform, tablet))} notify={notify} onClose={() => setPicking(false)} />}
      {viewing && <HiddenViewer item={viewing} bin={words.bin} notify={notify} onClose={() => setViewing(undefined)} />}
    </LayerScreen>
  );
}

/** The recovery key, to write down: shown once as the vault is turned on, and again while it is open. */
function KeySheet({ recoveryKey, onClose }: { readonly recoveryKey: string; onClose(): void }) {
  return (
    <Sheet title="Recovery key" bare onClose={onClose}>
      <div className="flex flex-col gap-3 px-5 pb-2 pt-4">
        <h2 className="font-title text-[21px] font-bold">Write down your recovery key</h2>
        <p className="text-[15px] leading-[1.45] text-t2">With it, a new phone paired with your computer brings your hidden items back from its backups. Without it, nobody can open them, PhotoSense included. Keep it somewhere safe, away from this phone.</p>
        <p aria-label="Your recovery key" className="rounded-cell bg-fill px-4 py-3 text-center font-code text-[17px] font-semibold tracking-wide [overflow-wrap:anywhere]">{recoveryKey}</p>
      </div>
      <div className="flex flex-col gap-2 px-4 pt-3"><PhoneButton onClick={onClose}>I wrote it down</PhoneButton></div>
    </Sheet>
  );
}

/** Takes a vault's recovery key, to bring its items back from the computer. */
function RestoreSheet({ computer, notify, onClose }: { readonly computer: string | null; notify: Notify; onClose(): void }) {
  const [typed, setTyped] = useState('');
  const [busy, setBusy] = useState(false);
  async function restore() {
    setBusy(true);
    try {
      await recoverVault(typed);
      onClose();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  return (
    <Sheet title="Restore hidden items" bare onClose={onClose}>
      <div className="flex flex-col gap-3 px-5 pb-2 pt-4">
        <h2 className="font-title text-[21px] font-bold">Restore hidden items</h2>
        <p className="text-[15px] leading-[1.45] text-t2">Type the recovery key you wrote down when you turned on Hidden. Your hidden items come back from {computer ?? 'your computer'}, still encrypted.</p>
        <input aria-label="Recovery key" value={typed} onChange={e => setTyped(e.target.value)} autoCapitalize="characters" autoComplete="off" spellCheck={false}
          placeholder="XXXX-XXXX-XXXX-XXXX-…" className="h-12 rounded-cell bg-fill px-4 font-code text-[16px] outline-none" />
      </div>
      <div className="flex flex-col gap-2 px-4 pt-3">
        <PhoneButton disabled={busy || typed.trim() === ''} onClick={() => void restore()}>Restore</PhoneButton>
        <PhoneButton kind="secondary" onClick={onClose}>Cancel</PhoneButton>
      </div>
    </Sheet>
  );
}

/** Chooses Library's pictures and videos to hide, saying what happens to them first. */
function HidePicker({ files, note, notify, onClose }: { readonly files: readonly OrganizeFileDto[]; readonly note: string; notify: Notify; onClose(): void }) {
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  const [busy, setBusy] = useState(false);
  const tap = (id: string) => setPicked(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  async function hide() {
    setBusy(true);
    try {
      const n = await hideItems([...picked]);
      notify(`${items(n)} hidden.`, 'ok');
      onClose();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  return (
    <LayerScreen label="Choose photos to hide" onBack={onClose} footer={(
      <StickyFooter>
        <p className="text-[13px] leading-[1.4] text-t2">{note}</p>
        <PhoneButton disabled={busy || picked.size === 0} onClick={() => void hide()}>{picked.size === 0 ? 'Choose photos to hide' : `Hide ${items(picked.size)}`}</PhoneButton>
      </StickyFooter>
    )}>
      <PushedHeader title="Choose photos to hide" onBack={onClose} />
      <PhotoGrid selecting selected={picked} onTap={tap}
        items={files.map(f => ({ id: f.id, name: f.name, thumbnail: f.isVideo ? undefined : organizeThumbnailUrl(f.id), duration: f.durationSeconds == null ? undefined : formatDuration(f.durationSeconds) }))} />
    </LayerScreen>
  );
}

/** An item of the vault, whole, with Unhide and Delete. */
function HiddenViewer({ item, bin, notify, onClose }: { readonly item: VaultItemDto; readonly bin: string; notify: Notify; onClose(): void }) {
  const gate = usePlanGate();
  const [asking, setAsking] = useState(false);
  const [busy, setBusy] = useState(false);
  async function act(run: () => Promise<void>) {
    setBusy(true);
    try {
      await run();
      onClose();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  const unhide = () => act(async () => {
    await unhideItems([item.id]);
    notify(`${item.name} is back in your library.`, 'ok');
  });
  const remove = () => act(async () => {
    await deleteHidden([item.id]);
    notify(`${item.name} deleted. It waits in ${bin} for 30 days.`, 'ok');
  });
  const question = deleteQuestion(item.name, bin);
  const limit = limitLine(gate.plan);
  const taken = item.taken ? new Date(item.taken).toLocaleString('en-US', { month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric', minute: '2-digit' }) : 'No date';
  return (
    <LayerScreen label={item.name} frozen={asking} onBack={onClose} footer={(
      <StickyFooter>
        <div className="flex gap-2">
          <PhoneButton kind="secondary" disabled={busy} onClick={() => void unhide()}>Unhide</PhoneButton>
          <PhoneButton kind="danger" disabled={busy} onClick={() => gate.remove(1, 'items', () => setAsking(true))}>Delete</PhoneButton>
        </div>
      </StickyFooter>
    )}>
      <PushedHeader title={item.name} subtitle={taken} onBack={onClose} />
      <div className="flex flex-1 items-center justify-center bg-black">
        {item.isVideo
          ? <p className="px-6 py-24 text-center text-[15px] text-white/70">PhotoSense cannot play videos yet.</p>
          : <img src={vaultImageUrl(item.id)} alt={item.name} className="max-h-full max-w-full object-contain" />}
      </div>
      {asking && <ConfirmSheet title={question.title} lines={limit ? [...question.lines, limit] : question.lines} confirmLabel="Delete" busy={busy}
        onConfirm={() => { setAsking(false); void remove(); }} onCancel={() => setAsking(false)} />}
    </LayerScreen>
  );
}

/** Says, wherever the person is in Hidden, when bringing its items back from the computer ends. */
function useRestoreEnded(vault: VaultDto | undefined, notify: Notify) {
  const was = useRef<string | undefined>(undefined);
  const restore = vault?.restore;
  useEffect(() => {
    if (was.current === 'running' && restore && restore.stage !== 'running') {
      if (restore.stage === 'ready') notify(`${items(restore.restored!)} brought back from ${vault!.computer}.`, 'ok');
      else notify(restore.problem!, 'error');
    }
    was.current = restore?.stage;
  }, [restore, vault, notify]);
}
