import React, { useState } from 'react';
import { organizeThumbnailUrl, pauseBackup, removeSafe, saveBackupSettings, startBackup } from '../../lib/apiClient';
import { automaticNote, backupButton, backupLine, backupPercent, backupTitle, checkedAt, gigabytes, ofStorage, storageBar, whenSaid } from '../../lib/backup';
import { items } from '../../lib/library';
import { limitLine } from '../../lib/plan';
import { deviceWord, usePhone } from '../../lib/phone';
import type { ResultCard } from '../../lib/shareCard';
import type { BackupSettingsDto, BackupStateDto, SafeRemovalDto, SafeReviewDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { ListGroup, ListRow, PhoneButton, PhoneSwitch } from './Controls';
import { CheckIcon, DoneIcon, LaptopIcon, ShareIcon, TickIcon } from './Icons';
import { usePlanGate } from './ProSheet';
import { LayerScreen, PushedHeader, RootHeader, Screen } from './Screens';
import { ShareRow } from './Share';

type Notify = (message: string, kind: ToastKind) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** The small PRO badge beside what is part of Pro. */
export const ProBadge = () => <span className="flex h-[18px] items-center rounded-full bg-acc-soft px-[7px] text-[11px] font-bold tracking-[.04em] text-on-acc-soft">PRO</span>;

/** A section's header in the platform's way, with what stands beside it. */
function Header({ children, beside }: { readonly children: React.ReactNode; readonly beside?: React.ReactNode }) {
  const { ios } = usePhone();
  return (
    <div className="flex items-center gap-2 px-1">
      <h3 className={ios ? 'text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'text-[14px] font-medium text-acc'}>{children}</h3>
      {beside}
    </div>
  );
}

interface BackupProps {
  readonly state?: BackupStateDto;
  notify: Notify;
  /** Library choosing what to send now. */
  onChoose(): void;
  /** Pairing with a computer, when the phone has none yet. */
  onPair(): void;
}

/**
 * Backup (16, 17): the computer the phone backs up to, what waits or how far a backup has got with the last few
 * pictures, and the button that backs up, pauses or carries on; then automatic backup, which is part of Pro, and Send
 * now, which sends what the person chooses.
 */
export function BackupScreen({ state, notify, onChoose, onPair }: BackupProps) {
  const { platform, tablet } = usePhone();
  const gate = usePlanGate();
  const [busy, setBusy] = useState(false);
  const device = deviceWord(platform, tablet);
  const pro = gate.plan !== undefined && gate.plan.tier !== 'free';

  async function act(run: () => Promise<unknown>) {
    setBusy(true);
    try { await run(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }
  const save = (change: Partial<BackupSettingsDto>) => void act(() => saveBackupSettings({ ...state!.settings, ...change }));
  // Turning automatic backup on is part of Pro: the Pro sheet opens first, and turns it on once bought.
  const automatic = (on: boolean) => (on && !pro ? gate.offer('auto', () => save({ automatic: true })) : save({ automatic: on }));

  const button = state && backupButton(state);
  const press = () => void act(() => (button === 'Pause' ? pauseBackup() : startBackup()));
  const now = new Date();
  return (
    <Screen label="Backup">
      <RootHeader title="Backup" subtitle="Copies photos and videos to your computer over Wi-Fi" />
      <div className="mx-auto flex w-full max-w-[560px] flex-col gap-6 px-4 pb-4 pt-1">
        {!state ? <p role="status" className="py-10 text-center text-[15px] text-t2">Looking at what is backed up…</p>
          : state.stage === 'unpaired' ? (
            <section aria-label="No computer yet" className="flex flex-col gap-3 rounded-[24px] bg-cell p-4">
              <span className="flex items-center gap-3">
                <span aria-hidden className="grid h-10 w-10 place-items-center rounded-[12px] bg-acc-soft text-on-acc-soft"><LaptopIcon /></span>
                <span className="text-[16px] font-semibold">No computer yet</span>
              </span>
              <p className="text-[15px] leading-[1.4] text-t2">Pair this {device} with PhotoSense on your Mac or PC. {items(state.waiting)} can then be backed up over Wi-Fi.</p>
              <PhoneButton onClick={onPair}>Connect your computer</PhoneButton>
            </section>
          ) : (
            <section aria-label={state.computer!} className="flex flex-col gap-3 rounded-[24px] bg-cell p-4">
              <span className="flex items-center gap-3 border-b-[0.5px] border-sep pb-3">
                <span aria-hidden className="grid h-10 w-10 place-items-center rounded-[12px] bg-acc-soft text-on-acc-soft"><LaptopIcon /></span>
                <span className="flex min-w-0 flex-col">
                  <span className="truncate text-[16px] font-semibold">{state.computer}</span>
                  <span className="flex items-center gap-1.5 text-[13px] text-t2">
                    <span aria-hidden className={`h-2 w-2 rounded-full ${state.lastChecked ? 'bg-keep' : 'bg-t3'}`} />
                    {state.lastChecked ? `Checked ${checkedAt(state.lastChecked, now)}` : 'Not backed up yet'}
                  </span>
                </span>
              </span>
              <div className="flex flex-col gap-1">
                <p className="text-[18px] font-bold">{backupTitle(state)}</p>
                <p className={`text-[13.5px] [overflow-wrap:anywhere] ${state.stage === 'failed' ? 'text-danger-t' : 'text-t2'}`} role={state.stage === 'failed' ? 'alert' : undefined}>{backupLine(state, now, platform)}</p>
              </div>
              {(state.stage === 'running' || state.stage === 'paused') && (
                <div role="progressbar" aria-label={`Backing up to ${state.computer}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(backupPercent(state))} className="h-1.5 overflow-hidden rounded-full bg-fill">
                  <div className="h-full rounded-full bg-acc transition-[width]" style={{ width: `${backupPercent(state)}%` }} />
                </div>
              )}
              {state.strip.length > 0 && state.stage !== 'ready' && (
                <ul aria-label="Being backed up" className="grid grid-cols-6 gap-1">
                  {state.strip.map(tile => (
                    <li key={tile.id} className={`relative aspect-square overflow-hidden rounded-[6px] bg-fill ${tile.done ? '' : 'opacity-60'}`}>
                      <img src={organizeThumbnailUrl(tile.id)} alt="" className="absolute inset-0 h-full w-full object-cover" />
                      {tile.done && <span role="img" aria-label="On the computer" className="absolute bottom-1 left-1 grid h-4 w-4 place-items-center rounded-full bg-black/[.52] text-white"><TickIcon size={10} /></span>}
                    </li>
                  ))}
                </ul>
              )}
              {button && <PhoneButton kind={button === 'Resume' ? 'primary' : 'secondary'} disabled={busy} onClick={press}>{button}</PhoneButton>}
            </section>
          )}

        {state && state.stage !== 'unpaired' && (
          <section aria-label="Automatic backup" className="flex flex-col gap-2">
            <Header beside={!pro && <ProBadge />}>Automatic backup</Header>
            <ListGroup>
              <PhoneSwitch inList label="Back up automatically" on={state.settings.automatic} onChange={automatic} />
              <PhoneSwitch inList label="Only while charging" on={state.settings.onlyWhileCharging} onChange={on => save({ onlyWhileCharging: on })} />
              <PhoneSwitch inList label="Include videos" detail="A Live Photo's video always goes with it." on={state.settings.includeVideos} onChange={on => save({ includeVideos: on })} />
              <ListRow detail={state.folder} onClick={() => notify(`To change the folder, open Phones in PhotoSense on ${state.computer}.`, 'info')}>Folder</ListRow>
            </ListGroup>
            <p className="px-1 text-[13px] leading-[1.45] text-t2">
              {automaticNote(platform, state.computer!, state.settings.onlyWhileCharging)}
            </p>
          </section>
        )}

        <ListGroup header="Send now">
          <ListRow chevron onClick={onChoose}>Choose photos to send</ListRow>
        </ListGroup>
      </div>
    </Screen>
  );
}

interface SafeProps {
  readonly safe?: SafeReviewDto;
  notify: Notify;
  /** What was deleted, for Space freed. */
  onRemoved(removed: SafeRemovalDto, partial: boolean): void;
  onBack(): void;
}

/**
 * Safe to delete (09): how much could be freed from the phone, with a bar of its storage, that the computer checked
 * each copy, the keep rules, and on an iPhone signed in to iCloud the warning that deleting here deletes there too.
 * Remove asks the plan first: past what the free plan has left today, the largest go.
 */
export function SafeToDelete({ safe, notify, onRemoved, onBack }: SafeProps) {
  const { platform, tablet, ios, words } = usePhone();
  const gate = usePlanGate();
  const [busy, setBusy] = useState(false);
  const device = deviceWord(platform, tablet);
  const count = safe?.removable.count ?? 0;

  const keep = (change: Partial<BackupSettingsDto>) => void saveBackupSettings({ ...safe!.settings, ...change }).catch(e => notify(said(e), 'error'));
  function remove() {
    gate.remove(count, 'items', allowed => void (async () => {
      setBusy(true);
      try {
        const removed = await removeSafe(allowed < count ? allowed : undefined);
        onRemoved(removed, allowed < count);
      } catch (e) {
        notify(said(e), 'error');
      } finally {
        setBusy(false);
      }
    })(), true);
  }

  const limit = limitLine(gate.plan);
  const bar = safe && storageBar(safe);
  return (
    <LayerScreen label="Safe to delete" onBack={onBack} footer={<>
      <PhoneButton disabled={busy || count === 0} onClick={remove}>{count > 0 ? `Remove ${items(count)}` : 'Nothing to remove'}</PhoneButton>
      <p className="text-center text-[13px] leading-[1.45] text-t2">{limit ? `${limit} ` : ''}Removed items stay in {words.bin} for 30 days.</p>
    </>}>
      <PushedHeader title="Safe to delete" onBack={onBack} />
      {!safe ? <p role="status" className="py-10 text-center text-[15px] text-t2">Checking what is on the computer…</p> : (
        <div className="mx-auto flex w-full max-w-[560px] flex-col gap-6 px-4 pb-6">
          <div className="flex flex-col gap-1 pt-2">
            <p className="text-[52px] font-bold leading-none tabular-nums">{gigabytes(safe.removable.bytes)}</p>
            <p className="text-[15px] text-t2">can be freed from this {device}</p>
          </div>
          {bar && safe.storageTotal > 0 && (
            <div className="flex flex-col gap-2">
              <div aria-hidden className="flex h-2 overflow-hidden rounded-full bg-fill2">
                <span className="h-full bg-acc" style={{ width: `${bar.remove}%` }} />
                <span className="h-full bg-t3" style={{ width: `${bar.other}%` }} />
              </div>
              <p className="flex flex-wrap gap-x-4 gap-y-1 text-[12.5px] text-t2">
                <span className="flex items-center gap-1.5"><span aria-hidden className="h-2 w-2 rounded-full bg-acc" />To remove</span>
                <span className="flex items-center gap-1.5"><span aria-hidden className="h-2 w-2 rounded-full bg-t3" />Everything else {gigabytes(safe.storageTotal - safe.storageFree - Math.min(safe.removable.bytes, safe.storageTotal - safe.storageFree))}</span>
                <span className="flex items-center gap-1.5"><span aria-hidden className="h-2 w-2 rounded-full bg-fill2" />Free {gigabytes(safe.storageFree)}</span>
              </p>
            </div>
          )}
          <ListGroup header={`Checked on ${safe.computer ?? 'your computer'}`}>
            <li className="flex min-h-[56px] items-center gap-3 px-4 py-2.5">
              <span aria-hidden className="grid h-7 w-7 shrink-0 place-items-center rounded-full bg-keep-soft text-keep"><CheckIcon /></span>
              <span className="flex min-w-0 flex-col">
                <span className="text-[16px]">{safe.checked.count.toLocaleString()} {safe.checked.count === 1 ? 'photo or video' : 'photos and videos'}</span>
                <span className="text-[13px] text-t2">Each one matches the copy on the computer</span>
              </span>
            </li>
            <ListRow detail={safe.lastChecked ? whenSaid(safe.lastChecked, new Date()) : 'Not yet'}>Last checked</ListRow>
            <ListRow detail={safe.folder ?? '—'}>Folder</ListRow>
          </ListGroup>
          <ListGroup header={`Keep on this ${device}`}>
            <PhoneSwitch inList label="Favorites" detail={`${items(safe.favorites.count)} · ${gigabytes(safe.favorites.bytes)}`} on={safe.settings.keepFavorites} onChange={on => keep({ keepFavorites: on })} />
            <PhoneSwitch inList label="Last 30 days" detail={`${items(safe.recent.count)} · ${gigabytes(safe.recent.bytes)}`} on={safe.settings.keepRecent} onChange={on => keep({ keepRecent: on })} />
          </ListGroup>
          {ios && safe.cloudPhotos && (
            <p role="note" className="rounded-cell bg-amber-soft px-4 py-3.5 text-[14.5px] leading-[1.45] text-t1">
              If iCloud Photos is on, removing items here also removes them from iCloud and your other Apple devices. The copies on {safe.computer} are not affected.
            </p>
          )}
        </div>
      )}
    </LayerScreen>
  );
}

interface FreedProps {
  readonly removed: SafeRemovalDto;
  readonly partial: boolean;
  readonly computer: string;
  readonly folder: string;
  /** The free plan's limit stopped some: offers Pro. */
  onPro(): void;
  onDone(): void;
  /** Days in a row something was swiped, for the share card. */
  readonly streak?: number;
  /** Opens the share card. */
  onShare?(card: ResultCard): void;
}

/** Space freed (11): how much, what went and where its copies are, the phone's storage before and now, and what the limit left. */
export function SpaceFreed({ removed, partial, computer, folder, streak = 0, onPro, onShare, onDone }: FreedProps) {
  const { platform, tablet } = usePhone();
  const gate = usePlanGate();
  const device = deviceWord(platform, tablet);
  const before = removed.storageTotal - removed.storageFreeBefore;
  const after = Math.max(0, before - removed.bytes);
  const width = (used: number) => `${used / removed.storageTotal * 100}%`;
  const carry = partial && removed.left > 0 && gate.plan?.tier === 'free';
  const card: ResultCard = { amount: gigabytes(removed.bytes), device, streak, items: removed.removed };
  return (
    <LayerScreen label="Space freed" onBack={onDone} footer={(
      <div className="flex gap-3">
        {onShare && <PhoneButton kind="secondary" onClick={() => onShare(card)}><span className="flex items-center justify-center gap-2"><ShareIcon size={18} />Share</span></PhoneButton>}
        <PhoneButton onClick={onDone}>Done</PhoneButton>
      </div>
    )}>
      <div className="mx-auto flex w-full max-w-[560px] flex-col gap-7 px-6 pb-6 pt-[calc(var(--safe-t)+56px)]">
        <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-keep-soft text-keep"><DoneIcon /></span>
        <div className="flex flex-col gap-2">
          <h2 className="font-title text-[32px] font-bold leading-[1.15]">{gigabytes(removed.bytes)} freed</h2>
          <p className="text-[15px] leading-[1.45] text-t2">
            {partial ? `The ${removed.removed.toLocaleString()} largest items were removed from this ${device}.` : `${items(removed.removed)} removed from this ${device}.`} The copies are on {computer} in {folder}.
          </p>
        </div>
        {removed.storageTotal > 0 && (
          <div className="flex flex-col gap-3">
            {([['Before', before, 'bg-t3'], ['Now', after, 'bg-acc']] as const).map(([label, used, colour]) => (
              <div key={label} className="flex flex-col gap-1.5">
                <p className="flex justify-between text-[13px]"><span className="text-t2">{label}</span><span className="font-semibold">{ofStorage(used, removed.storageTotal)}</span></p>
                <div aria-hidden className="h-1.5 overflow-hidden rounded-full bg-fill"><div className={`h-full rounded-full ${colour}`} style={{ width: width(used) }} /></div>
              </div>
            ))}
          </div>
        )}
        {onShare && <ShareRow card={card} onOpen={() => onShare(card)} />}
        {carry && (
          <section aria-label="More are selected" className="flex flex-col gap-2 rounded-cell bg-cell p-4">
            <p className="text-[16px] font-semibold">{removed.left.toLocaleString()} more {removed.left === 1 ? 'is' : 'are'} selected</p>
            <p className="text-[14px] leading-[1.45] text-t2">The free plan deletes {gate.plan!.removalsPerDay.toLocaleString()} items a day. Come back tomorrow, or get Pro to remove the limit.</p>
            <PhoneButton kind="secondary" onClick={onPro}>Get Pro</PhoneButton>
          </section>
        )}
      </div>
    </LayerScreen>
  );
}
