import React, { useState } from 'react';
import { deleteChosen, thumbnailUrl, useBursts, useCategories, useCategoryPhotos } from '../../lib/apiClient';
import { ofStorage } from '../../lib/backup';
import { bytesOf, counted, deletedSaid, largestIds, percentFull, resolution, sizeOf } from '../../lib/categories';
import { formatBytes, formatDuration } from '../../lib/format';
import { shortDate } from '../../lib/organize';
import { limitLine } from '../../lib/plan';
import { deviceWord, usePhone } from '../../lib/phone';
import type { BurstDto, PhotoDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { PhoneButton, StickyFooter } from './Controls';
import { TickIcon } from './Icons';
import { usePlanGate } from './ProSheet';
import { PushedHeader } from './Screens';

type Notify = (message: string, kind: ToastKind) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * Deleting what a category screen chose: through the plan's gate, which lets the largest go when the free plan has fewer
 * left today, then to the phone's own bin after its question, saying so after.
 */
function useDeleting(notify: Notify) {
  const gate = usePlanGate();
  const { words } = usePhone();
  const [busy, setBusy] = useState(false);
  const run = (photos: readonly PhotoDto[], after?: () => void) => gate.remove(photos.length, 'items', allowed => void (async () => {
    setBusy(true);
    try {
      const done = await deleteChosen(largestIds(photos, allowed));
      notify(deletedSaid(done.deleted, words.bin), 'ok');
      after?.();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  })(), true);
  return { busy, run, limit: limitLine(gate.plan) };
}

/** A category screen's foot: its Delete button, and the free plan's removals left today under it. */
function DeleteFoot({ disabled, limit, onClick, children }: { readonly disabled: boolean; readonly limit?: string; onClick(): void; readonly children: React.ReactNode }) {
  return (
    <StickyFooter>
      <PhoneButton disabled={disabled} onClick={onClick}>{children}</PhoneButton>
      {limit && <p className="text-center text-[13px] text-t2">{limit}</p>}
    </StickyFooter>
  );
}

/** The figure at the top of a category's screen, what it is of, and a note. */
function Total({ photos, of, note }: { readonly photos: readonly PhotoDto[]; readonly of: string; readonly note?: string }) {
  return (
    <div className="flex flex-col gap-1 px-4 pb-3">
      <p className="font-title text-[40px] font-bold leading-tight tabular-nums">{sizeOf(photos)}</p>
      <p className="text-[15px] text-t2">{of}</p>
      {note && <p className="pt-1 text-[13.5px] leading-[1.4] text-t2">{note}</p>}
    </div>
  );
}

/** A small preview, or a dark tile with its length for a video, which has none. */
function Tile({ photo, className }: { readonly photo: PhotoDto; readonly className: string }) {
  if (photo.isVideo) {
    return (
      <span aria-hidden className={`relative grid place-items-center bg-[#1c2124] ${className}`}>
        {photo.durationSeconds != null && <span className="absolute bottom-1 right-1.5 text-[11px] font-semibold text-white">{formatDuration(photo.durationSeconds)}</span>}
      </span>
    );
  }
  return <img src={thumbnailUrl(photo.id)} alt="" loading="lazy" className={`bg-fill object-cover ${className}`} />;
}

interface GridProps {
  readonly category: 'screenshots' | 'blurry' | 'messaging';
  readonly title: string;
  notify: Notify;
  onBack(): void;
}

/**
 * 19: a category's pictures in a grid of four, all chosen at first: the size chosen, from how many, and where they were
 * found; tap one to keep it. Select all or Deselect all in the header, and Delete N items at the foot.
 */
export function CategoryGrid({ category, title, notify, onBack }: GridProps) {
  const { ios, platform, tablet } = usePhone();
  const photos = useCategoryPhotos(category).data;
  const [kept, setKept] = useState<ReadonlySet<string>>(new Set());
  const { busy, run, limit } = useDeleting(notify);
  const all = photos ?? [];
  const chosen = all.filter(p => !kept.has(p.id));
  const toggle = (id: string) => setKept(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  const everything = chosen.length === all.length;
  return (
    <>
      <PushedHeader title={title} onBack={onBack} trailing={all.length > 0 && (
        <button type="button" onClick={() => setKept(everything ? new Set(all.map(p => p.id)) : new Set())}
          className="h-9 shrink-0 rounded-full bg-fill px-3.5 text-[14px] font-semibold">{everything ? 'Deselect all' : 'Select all'}</button>
      )} />
      {!photos ? <p role="status" className="px-4 py-10 text-center text-[15px] text-t2">Looking for them…</p> : (
        <>
          <Total photos={chosen} of={`selected from ${counted(category, all.length)}`}
            note={`Found on this ${deviceWord(platform, tablet)}. Nothing was uploaded. Tap a photo to keep it.`} />
          {all.length === 0 && <p className="px-4 py-6 text-[15px] text-t2">Nothing left to review.</p>}
          <ul aria-label={title} className="grid grid-cols-4 gap-[2px] pb-4">
            {all.map(p => {
              const on = !kept.has(p.id);
              return (
                <li key={p.id}>
                  <button type="button" aria-pressed={on} aria-label={`${p.fileName}: ${on ? 'to delete' : 'kept'}`} onClick={() => toggle(p.id)} className="relative block aspect-square w-full">
                    <Tile photo={p} className={`h-full w-full ${on ? '' : 'opacity-40'}`} />
                    <span aria-hidden className={`absolute grid h-6 w-6 place-items-center rounded-full border-2 border-white ${ios ? 'bottom-1.5 right-1.5' : 'left-1.5 top-1.5'} ${on ? 'bg-acc' : 'bg-black/30'}`}>
                      {on && <TickIcon size={12} />}
                    </span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
      <DeleteFoot disabled={busy || chosen.length === 0} limit={limit} onClick={() => run(chosen, () => setKept(new Set()))}>
        {chosen.length > 0 ? `Delete ${chosen.length.toLocaleString()} ${chosen.length === 1 ? 'item' : 'items'}` : 'Nothing selected'}
      </DeleteFoot>
    </>
  );
}

/** 20: each burst on a card of its own, the shot kept framed; tap another to keep it instead. Delete N extra shots at the foot. */
export function BurstsScreen({ notify, onBack }: { notify: Notify; onBack(): void }) {
  const bursts = useBursts(true).data;
  const [keepers, setKeepers] = useState<ReadonlyMap<string, string>>(new Map());
  const { busy, run, limit } = useDeleting(notify);
  const keeperOf = (b: BurstDto) => keepers.get(b.shots[0].id) ?? b.keeper;
  const extra = (bursts ?? []).flatMap(b => b.shots.filter(s => s.id !== keeperOf(b)));
  return (
    <>
      <PushedHeader title="Bursts" onBack={onBack} />
      {!bursts ? <p role="status" className="px-4 py-10 text-center text-[15px] text-t2">Looking for them…</p> : (
        <>
          <Total photos={extra} of={`from ${extra.length.toLocaleString()} extra ${extra.length === 1 ? 'shot' : 'shots'} in ${bursts.length.toLocaleString()} ${bursts.length === 1 ? 'burst' : 'bursts'}`}
            note="PhotoSense keeps the sharpest shot in each burst. Tap another shot to keep it instead." />
          {bursts.length === 0 && <p className="px-4 py-6 text-[15px] text-t2">Nothing left to review.</p>}
          <div className="flex flex-col gap-3 px-4 pb-4">
            {bursts.map(b => {
              const first = b.shots[0];
              return (
                <section key={first.id} aria-label={first.takenOn ? shortDate(first.takenOn) : first.fileName} className="flex flex-col gap-2 rounded-[20px] bg-cell p-3">
                  <div>
                    <p className="text-[15px] font-semibold">{first.takenOn ? shortDate(first.takenOn) : first.fileName}</p>
                    <p className="text-[13px] text-t2">{[first.placeName, `${b.shots.length.toLocaleString()} shots`].filter(Boolean).join(' · ')}</p>
                  </div>
                  <ul className="grid grid-cols-6 gap-1">
                    {b.shots.map(s => {
                      const keeps = s.id === keeperOf(b);
                      return (
                        <li key={s.id} className="relative">
                          <button type="button" aria-pressed={keeps} aria-label={`${s.fileName}: ${keeps ? 'kept' : 'to delete'}`} onClick={() => setKeepers(m => new Map(m).set(first.id, s.id))}
                            className={`relative block aspect-[3/4] w-full overflow-hidden rounded-[8px] ${keeps ? 'ring-[2.5px] ring-keep' : ''}`}>
                            <Tile photo={s} className={`h-full w-full ${keeps ? '' : 'opacity-50'}`} />
                          </button>
                          {/* Beside the button, not in it: what the button is called holds every word it shows. */}
                          {keeps && <span aria-hidden className="pointer-events-none absolute inset-x-0 bottom-1 mx-auto w-fit rounded-full bg-keep px-1.5 text-[10px] font-bold text-on-keep">Keep</span>}
                        </li>
                      );
                    })}
                  </ul>
                </section>
              );
            })}
          </div>
        </>
      )}
      <DeleteFoot disabled={busy || extra.length === 0} limit={limit} onClick={() => run(extra, () => setKeepers(new Map()))}>
        {extra.length > 0 ? `Delete ${extra.length.toLocaleString()} extra ${extra.length === 1 ? 'shot' : 'shots'}` : 'Nothing to delete'}
      </DeleteFoot>
    </>
  );
}

/** 21: the videos over 200 MB, largest first: where and when each was taken, its frame and its size. Tap videos to choose them to delete. */
export function LargeVideosScreen({ notify, onBack }: { notify: Notify; onBack(): void }) {
  const videos = useCategoryPhotos('largeVideos').data;
  const [chosen, setChosen] = useState<ReadonlySet<string>>(new Set());
  const { busy, run, limit } = useDeleting(notify);
  const all = videos ?? [];
  const going = all.filter(v => chosen.has(v.id));
  const toggle = (id: string) => setChosen(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  return (
    <>
      <PushedHeader title="Large videos" onBack={onBack} />
      {!videos ? <p role="status" className="px-4 py-10 text-center text-[15px] text-t2">Looking for them…</p> : (
        <>
          <Total photos={all} of={`in ${all.length.toLocaleString()} ${all.length === 1 ? 'video' : 'videos'} over 200 MB`} note={all.length > 0 ? 'Tap videos to choose the ones to delete.' : undefined} />
          {all.length === 0 && <p className="px-4 py-6 text-[15px] text-t2">Nothing left to review.</p>}
          <ul aria-label="Large videos" className="mx-4 mb-4 overflow-hidden rounded-cell bg-cell [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep">
            {all.map(v => {
              const on = chosen.has(v.id);
              return (
                <li key={v.id}>
                  <button type="button" aria-pressed={on} onClick={() => toggle(v.id)} className={`flex w-full items-center gap-3 px-3 py-2.5 text-left ${on ? 'bg-sel' : ''}`}>
                    <Tile photo={v} className="h-14 w-14 shrink-0 rounded-[10px]" />
                    <span className="min-w-0 flex-1">
                      <span className="block truncate text-[15.5px] font-semibold">{v.placeName ?? v.fileName}</span>
                      <span className="block truncate text-[13px] text-t2">{[v.takenOn ? shortDate(v.takenOn) : undefined, resolution(v.width, v.height)].filter(Boolean).join(' · ')}</span>
                    </span>
                    <span className="shrink-0 text-[15px] font-semibold tabular-nums">{formatBytes(v.fileSizeBytes)}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        </>
      )}
      <DeleteFoot disabled={busy || going.length === 0} limit={limit} onClick={() => run(going, () => setChosen(new Set()))}>
        {going.length > 0 ? `Delete ${going.length.toLocaleString()} ${going.length === 1 ? 'video' : 'videos'}` : 'Choose videos to delete'}
      </DeleteFoot>
    </>
  );
}

/** What Free up space offers, in its order: the quickest to check. */
const QUICK = [['duplicates', 'Duplicates'], ['screenshots', 'Screenshots'], ['blurry', 'Blurry']] as const;

/**
 * 24: how full the phone is, what the quickest kinds to check would free (duplicates' copies, screenshots and blurry
 * shots, each photo counted once), each to tick or untick, and Delete N items.
 */
export function FreeUpSpace({ notify, onBack }: { notify: Notify; onBack(): void }) {
  const { ios, platform, tablet } = usePhone();
  const report = useCategories(true).data;
  const lists = { duplicates: useCategoryPhotos('duplicates').data, screenshots: useCategoryPhotos('screenshots').data, blurry: useCategoryPhotos('blurry').data };
  const [off, setOff] = useState<ReadonlySet<string>>(new Set());
  const { busy, run, limit } = useDeleting(notify);
  // Each photo once, though it be both a duplicate's copy and a screenshot.
  const chosen = [...new Map(QUICK.filter(([id]) => !off.has(id)).flatMap(([id]) => lists[id] ?? []).map(p => [p.id, p] as const)).values()];
  const total = report?.storageTotal ?? 0, free = report?.storageFree ?? 0;
  // "iPhone" and "iPad" as Apple writes them; an Android "phone" or "tablet" starts the sentence.
  const word = deviceWord(platform, tablet);
  const device = word.startsWith('i') ? word : word[0].toUpperCase() + word.slice(1);
  const used = total - free;
  return (
    <>
      <PushedHeader title="Free up space" onBack={onBack} />
      <div className="flex flex-col gap-1 px-4 pb-3">
        {total > 0 && <p className="text-[14px] font-semibold text-amber">{`${device} storage is ${percentFull(total, free)}% full`}</p>}
        <p className="font-title text-[46px] font-bold leading-tight tabular-nums">{sizeOf(chosen)}</p>
        <p className="text-[15px] text-t2">can be freed from {chosen.length.toLocaleString()} {chosen.length === 1 ? 'item' : 'items'}</p>
        {total > 0 && (
          <>
            <div aria-hidden className="mt-3 flex h-2 overflow-hidden rounded-full bg-fill">
              <span className="bg-acc" style={{ width: `${(bytesOf(chosen) / total) * 100}%` }} />
              <span className="bg-t3" style={{ width: `${(Math.max(0, used - bytesOf(chosen)) / total) * 100}%` }} />
            </div>
            <p className="flex justify-between text-[12.5px] text-t2"><span>{sizeOf(chosen)} selected</span><span>{ofStorage(used, total)} used</span></p>
          </>
        )}
      </div>
      <ul aria-label="What to delete" className="mx-4 overflow-hidden rounded-cell bg-cell [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep">
        {QUICK.map(([id, label]) => {
          const on = !off.has(id), found = lists[id];
          return (
            <li key={id}>
              <button type="button" role="checkbox" aria-checked={on} onClick={() => setOff(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; })}
                className="flex w-full items-center gap-3 px-4 py-3 text-left">
                <span aria-hidden className={`grid h-6 w-6 shrink-0 place-items-center ${ios ? 'rounded-full' : 'rounded-[4px]'} ${on ? 'bg-acc' : 'border-2 border-t3'}`}>{on && <TickIcon size={12} />}</span>
                <span className="min-w-0 flex-1">
                  <span className="block text-[16px] font-semibold">{label}</span>
                  <span className="block text-[13px] text-t2">{!found ? '…' : found.length === 0 ? 'Nothing found' : `${found.length.toLocaleString()} ${found.length === 1 ? 'item' : 'items'} · ${sizeOf(found)}`}</span>
                </span>
              </button>
            </li>
          );
        })}
      </ul>
      <p className="px-6 pb-4 pt-3 text-[13px] leading-[1.45] text-t2">These three are the quickest to check. Large videos and similar shots are in Clean up.</p>
      <DeleteFoot disabled={busy || chosen.length === 0} limit={limit} onClick={() => run(chosen)}>
        {chosen.length > 0 ? `Delete ${chosen.length.toLocaleString()} ${chosen.length === 1 ? 'item' : 'items'}` : 'Nothing selected'}
      </DeleteFoot>
    </>
  );
}
