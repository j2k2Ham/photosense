import React, { useState } from 'react';
import { moveBackups, organizeThumbnailUrl, pausePhone, tripsGoTo, useBackupPhones } from '../lib/apiClient';
import { checkedAt, gigabytes } from '../lib/backup';
import { formatBytes } from '../lib/format';
import { items } from '../lib/library';
import type { BackupPhoneDto } from '../types';
import { FolderPicker } from './FolderPicker';
import { RenameWindow } from './RenameWindow';
import { NAMING } from '../lib/fixes';
import type { ToastKind } from './Toaster';

interface Props {
  notify(message: string, kind: ToastKind): void;
  /** Opens Pair a phone. */
  onPair(): void;
}

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** A phone or tablet, drawn small, for its tile in the list. */
const DeviceIcon = ({ tablet }: { readonly tablet: boolean }) => (
  <svg aria-hidden width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" strokeLinecap="round" strokeLinejoin="round">
    {tablet ? <rect x="4" y="3" width="16" height="18" rx="2.5" /> : <rect x="6.5" y="2.5" width="11" height="19" rx="2.5" />}
    <path d="M10.5 18.5h3" />
  </svg>
);

/** What the list says under a phone's name: what is arriving, that it is paused, or when it last backed up. */
export function phoneStatus(phone: BackupPhoneDto, now: Date): { text: string; live: boolean } {
  if (phone.arriving) return { text: `Backing up ${phone.arriving.done.toLocaleString()} of ${phone.arriving.total.toLocaleString()}`, live: true };
  if (phone.paused) return { text: 'Paused on this computer', live: false };
  return { text: phone.lastBackup ? `Backed up ${checkedAt(phone.lastBackup, now)}` : 'Not backed up yet', live: !!phone.lastBackup };
}

/** "Now", "9:12 AM", "Yesterday" or "Nov 10": when something in a phone's activity happened. */
export function activityTime(iso: string, now: Date): string {
  const at = new Date(iso);
  if (now.getTime() - at.getTime() < 60_000) return 'Now';
  const day = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  const days = Math.round((day(now) - day(at)) / 86_400_000);
  if (days === 0) return at.toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' });
  return days === 1 ? 'Yesterday' : at.toLocaleDateString('en-US', { month: 'short', day: 'numeric' });
}

/**
 * Phones (D1): every phone and tablet that backs up to this computer, with Pair a phone under them, and the one chosen
 * beside: its backup arriving now, with the last files checked; what is on this computer, that every file matched its
 * checksum, and what the phone can delete; the folder its backups go to, which can be changed; and what happened.
 */
export function PhonesView({ notify, onPair }: Props) {
  const phones = useBackupPhones(true).data;
  const [chosenId, setChosenId] = useState<string>();
  const [picking, setPicking] = useState<'backups' | 'trips' | 'names'>();
  const [busy, setBusy] = useState(false);
  const chosen = phones?.find(p => p.id === chosenId) ?? phones?.[0];
  const now = new Date();

  async function act(run: () => Promise<unknown>, done: string) {
    setBusy(true);
    try {
      await run();
      notify(done, 'ok');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex min-h-0 flex-1">
      <aside aria-label="Phones and tablets" className="flex w-[320px] shrink-0 flex-col border-r border-line bg-s1 max-md:w-[260px]">
        <div className="px-5 pb-3 pt-5">
          <h2 className="text-[17px] font-semibold">Phones and tablets</h2>
          <p className="text-[12.5px] text-t2">Back up to this computer over Wi-Fi</p>
        </div>
        <ul className="flex min-h-0 flex-1 flex-col gap-1 overflow-y-auto px-3">
          {phones?.map(p => {
            const status = phoneStatus(p, now);
            return (
              <li key={p.id}>
                <button type="button" aria-current={p.id === chosen?.id} onClick={() => setChosenId(p.id)}
                  className={`flex w-full items-center gap-3 rounded-[12px] px-3 py-2.5 text-left ${p.id === chosen?.id ? 'bg-s3' : 'hover:bg-s2'}`}>
                  <span className="grid h-[38px] w-[38px] shrink-0 place-items-center rounded-[10px] bg-s2 text-t1"><DeviceIcon tablet={/iPad|tablet/.test(p.kind ?? '')} /></span>
                  <span className="flex min-w-0 flex-col">
                    <span className="truncate text-[14px] font-semibold">{p.name}</span>
                    <span className="flex items-center gap-1.5 truncate text-[12.5px] text-t2">
                      <span aria-hidden className={`h-[7px] w-[7px] shrink-0 rounded-full ${status.live ? 'bg-keep' : 'bg-t3'}`} />{status.text}
                    </span>
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
        <div className="p-4">
          <button type="button" onClick={onPair} className="pill-outline h-10 w-full text-[13.5px]">+ Pair a phone</button>
        </div>
      </aside>

      <section aria-label={chosen?.name ?? 'No phones yet'} className="flex min-w-0 flex-1 flex-col gap-5 overflow-y-auto px-8 py-6 max-md:px-4">
        {!phones ? <p role="status" className="text-[14px] text-t2">Looking for paired phones…</p>
          : !chosen ? (
            <div className="mx-auto flex max-w-[440px] flex-col items-center gap-3 py-16 text-center">
              <h2 className="text-[22px] font-bold">No phones yet</h2>
              <p className="text-[14px] leading-[1.5] text-t2">Pair a phone with PhotoSense on it, and it backs up its photos and videos to this computer over Wi-Fi. Each file is checked by its SHA-256 checksum as it arrives.</p>
              <button type="button" onClick={onPair} className="pill-brand h-10 px-5 text-[14px]">Pair a phone</button>
            </div>
          ) : <Detail phone={chosen} now={now} busy={busy}
            onPause={() => void act(() => pausePhone(chosen.id, !chosen.paused), chosen.paused ? `Backups from ${chosen.name} run again.` : `Backups from ${chosen.name} are paused.`)}
            onChangeFolder={() => setPicking('backups')} onChangeTrips={() => setPicking('trips')} onRename={() => setPicking('names')} />}
      </section>

      {picking === 'backups' && chosen && (
        <FolderPicker title={`Where backups from ${chosen.name} go`} startAt={chosen.path} pickLabel="Back up here"
          onPick={folder => { setPicking(undefined); void act(() => moveBackups(chosen.id, folder), `Backups from ${chosen.name} now go to ${folder}.`); }}
          onCancel={() => setPicking(undefined)} />
      )}
      {picking === 'names' && chosen && <RenameWindow phone={chosen} notify={notify} onClose={() => setPicking(undefined)} />}
      {picking === 'trips' && chosen && (
        <FolderPicker title={`Where trip folders from ${chosen.name} go`} startAt={chosen.tripsPath} pickLabel="Make them here"
          onPick={folder => { setPicking(undefined); void act(() => tripsGoTo(chosen.id, folder), `Trip folders from ${chosen.name} now go in ${folder}.`); }}
          onCancel={() => setPicking(undefined)} />
      )}
    </div>
  );
}

interface DetailProps {
  readonly phone: BackupPhoneDto;
  readonly now: Date;
  readonly busy: boolean;
  onPause(): void;
  onChangeFolder(): void;
  /** Chooses where its trip folders are made. */
  onChangeTrips(): void;
  /** Opens Rename files by date. */
  onRename(): void;
}

/** One phone, as Phones shows it beside the list. */
function Detail({ phone, now, busy, onPause, onChangeFolder, onChangeTrips, onRename }: DetailProps) {
  const arriving = phone.arriving;
  const percent = arriving && arriving.totalBytes > 0 ? arriving.bytes / arriving.totalBytes * 100 : 0;
  const stat = 'flex flex-col gap-1 rounded-[16px] border border-line bg-s1 p-4';
  return (
    <>
      <div className="flex flex-wrap items-start gap-3">
        <div className="min-w-0 flex-1">
          <h2 className="truncate text-[26px] font-bold">{phone.name}</h2>
          <p className="text-[13px] text-t2">{phone.kind ?? 'Phone'} · paired {new Date(phone.paired).toLocaleDateString('en-US', { month: 'short', day: 'numeric', year: 'numeric' })}</p>
        </div>
        <button type="button" disabled={busy} onClick={onPause} className="pill-quiet h-9 px-4 text-[13px]">{phone.paused ? 'Resume backups' : 'Pause backups'}</button>
      </div>

      <section aria-label="Backup" className={stat}>
        <h3 className="text-[16px] font-semibold">
          {arriving ? `Backing up ${arriving.done.toLocaleString()} of ${arriving.total.toLocaleString()}` : phone.paused ? 'Paused on this computer' : phone.lastBackup ? 'Up to date' : 'No backup yet'}
        </h3>
        <p className="text-[12.5px] text-t2">
          {arriving ? `${formatBytes(Math.max(0, arriving.totalBytes - arriving.bytes))} left · each file is checked after it arrives`
            : phone.paused ? `${phone.name} is told so when it tries to back up.`
              : phone.lastBackup ? `Last backup ${checkedAt(phone.lastBackup, now)}` : `${phone.name} backs up when PhotoSense is open on it.`}
        </p>
        {arriving && (
          <>
            <div role="progressbar" aria-label={`Backing up ${phone.name}`} aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(percent)} className="mt-2 h-1.5 overflow-hidden rounded-full bg-s3">
              <div className="h-full rounded-full bg-brand" style={{ width: `${percent}%` }} />
            </div>
            {arriving.arrived.length > 0 && (
              <ul aria-label="Arrived and checked" className="mt-3 grid grid-cols-10 gap-1.5">
                {arriving.arrived.map(id => (
                  <li key={id} className="relative aspect-[4/3] overflow-hidden rounded-[6px] bg-s3">
                    <img src={organizeThumbnailUrl(id)} alt="" className="absolute inset-0 h-full w-full object-cover" />
                    <span role="img" aria-label="Checked" className="absolute bottom-1 right-1 grid h-4 w-4 place-items-center rounded-full bg-keep text-[10px] font-bold text-bg">✓</span>
                  </li>
                ))}
              </ul>
            )}
          </>
        )}
      </section>

      <div className="grid grid-cols-3 gap-3 max-lg:grid-cols-1">
        <section aria-label="On this computer" className={stat}>
          <h3 className="label-caps">On this computer</h3>
          <p className="text-[20px] font-bold">{items(phone.held)}</p>
          <p className="text-[12.5px] text-t2">{gigabytes(phone.heldBytes)}</p>
        </section>
        <section aria-label="Checked" className={stat}>
          <h3 className="label-caps">Checked</h3>
          <p className="text-[20px] font-bold">{phone.lastChecked ? 'All match' : 'Not yet'}</p>
          <p className="text-[12.5px] text-t2">SHA-256 checksum of every file</p>
        </section>
        <section aria-label="Safe to delete on the phone" className={stat}>
          <h3 className="label-caps">Safe to delete on the phone</h3>
          <p className="text-[20px] font-bold">{items(phone.safe)}</p>
          <p className="text-[12.5px] text-t2">{gigabytes(phone.safeBytes)} after keep rules</p>
        </section>
      </div>

      <div className="grid grid-cols-2 gap-3 max-lg:grid-cols-1">
        <section aria-label="Files from this phone" className="rounded-[16px] border border-line bg-s1">
          <h3 className="border-b border-line px-4 py-3 text-[14px] font-semibold">Files from this phone</h3>
          <div className="flex items-center gap-3 px-4 py-3">
            <div className="min-w-0 flex-1">
              <p className="text-[13.5px]">Folder</p>
              <p className="truncate font-mono text-[12.5px] text-t2" title={phone.path}>{phone.folder}</p>
            </div>
            <button type="button" onClick={onChangeFolder} className="pill-quiet h-8 shrink-0 px-3 text-[12.5px]">Change…</button>
          </div>
          <div className="flex items-center gap-3 border-t border-line px-4 py-3">
            <div className="min-w-0 flex-1">
              <p className="text-[13.5px]">File names</p>
              <p className="text-[12.5px] text-t2">{NAMING[phone.names]}</p>
            </div>
            <button type="button" onClick={onRename} className="pill-quiet h-8 shrink-0 px-3 text-[12.5px]">{phone.names === 'asOnPhone' ? 'Rename by date…' : 'Change names…'}</button>
          </div>
          <div className="flex items-center gap-3 border-t border-line px-4 py-3">
            <div className="min-w-0 flex-1">
              <p className="text-[13.5px]">Trip folders</p>
              <p className="truncate font-mono text-[12.5px] text-t2" title={phone.tripsPath}>{phone.tripsFolder}</p>
              <p className="text-[12.5px] text-t2">Made when the phone asks. {phone.tripFolders === 0 ? 'None so far.' : `${phone.tripFolders.toLocaleString()} so far.`}</p>
            </div>
            <button type="button" onClick={onChangeTrips} aria-label="Change… where trip folders go" className="pill-quiet h-8 shrink-0 px-3 text-[12.5px]">Change…</button>
          </div>
        </section>
        <section aria-label="Activity" className="rounded-[16px] border border-line bg-s1">
          <h3 className="border-b border-line px-4 py-3 text-[14px] font-semibold">Activity</h3>
          {phone.events.length === 0 ? <p className="px-4 py-3 text-[13px] text-t2">Nothing yet.</p> : (
            <ul>
              {phone.events.map((e, i) => (
                <li key={`${e.at}-${i}`} className="flex gap-4 border-t border-line px-4 py-2.5 text-[13px] first:border-t-0">
                  <span className="w-[72px] shrink-0 text-t2">{activityTime(e.at, now)}</span>
                  <span className="min-w-0">{e.text}</span>
                </li>
              ))}
            </ul>
          )}
        </section>
      </div>
    </>
  );
}
