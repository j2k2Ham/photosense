import React, { useEffect, useRef, useState } from 'react';
import { addPlaces, fixDates, renameOnComputer, thumbnailUrl, undoFix, useFixes } from '../../lib/apiClient';
import { datesLine, dayWritten, items, namesLine, placesNote } from '../../lib/fixes';
import { deviceWord, usePhone } from '../../lib/phone';
import type { FixesDto, PhotoDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { CalendarIcon, CheckIcon, ImageIcon, PinIcon, VideoIcon } from './Icons';
import { usePlanGate } from './ProSheet';
import { CollectionTile } from './CollectionTile';
import { LayerScreen, PushedHeader, useInPlace } from './Screens';

type Notify = (message: string, kind: ToastKind, action?: ToastAction) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** 26: Library's Needs fixing tile, with how many need a date or a place, opening Fix dates and places. */
export function FixesTile({ fixes, onOpen }: { readonly fixes: FixesDto | undefined; onOpen(): void }) {
  const count = fixes === undefined ? 'Looking…' : fixes.total === 0 ? 'All fixed' : items(fixes.total);
  return (
    <CollectionTile label="Needs fixing" line={count} onOpen={onOpen}>
      <span className="text-t2"><CalendarIcon /></span>
    </CollectionTile>
  );
}

/** Six of the photos a fix is for. */
function Thumbs({ photos }: { readonly photos: readonly PhotoDto[] }) {
  return (
    <div aria-hidden className="grid grid-cols-6 gap-[3px] overflow-hidden rounded-[10px]">
      {photos.map(p => (
        <span key={p.id} className="relative aspect-square bg-fill">
          {p.isVideo ? <span className="absolute inset-0 grid place-items-center text-t2"><VideoIcon size={16} /></span>
            : <img src={thumbnailUrl(p.id)} alt="" className="absolute inset-0 h-full w-full object-cover" />}
        </span>
      ))}
    </div>
  );
}

interface CardProps {
  readonly title: string;
  readonly line: string;
  readonly icon: React.ReactNode;
  /** The hue of the icon's tile on an iPhone, as the design gives it; Android shows the icon alone. */
  readonly hue: number;
  readonly ios: boolean;
  readonly children: React.ReactNode;
}

/** A kind of fix: its tile, title and line, then what it is about and its button. */
function Card({ title, line, icon, hue, ios, children }: CardProps) {
  return (
    <section aria-label={title} className="flex flex-col gap-3 rounded-[20px] bg-cell p-4">
      <div className="flex items-center gap-3">
        {ios
          ? <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] text-white" style={{ background: `oklch(0.62 0.13 ${hue})` }}>{icon}</span>
          : <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center text-t2">{icon}</span>}
        <div className="min-w-0">
          <h2 className="text-[16px] font-semibold">{title}</h2>
          <p className="text-[13.5px] text-t2">{line}</p>
        </div>
      </div>
      {children}
    </section>
  );
}

/** What a fix changes, from what it was to what it is to be. */
const Change = ({ from, to }: { readonly from: string; readonly to: React.ReactNode }) => (
  <div className="flex flex-wrap items-center gap-2 rounded-[10px] bg-fill px-3 py-2.5 text-[14px]">
    <span className="text-t2 line-through">{from}</span><span aria-hidden className="text-t2">→</span><span className="font-semibold">{to}</span>
  </div>
);

/** A fix's button, marked PRO on the free plan. */
function FixButton({ label, pro, disabled, onClick }: { readonly label: string; readonly pro: boolean; readonly disabled?: boolean; onClick(): void }) {
  return (
    <button type="button" disabled={disabled} onClick={onClick}
      className="flex h-10 items-center gap-2 self-start rounded-full bg-acc px-4 text-[14.5px] font-semibold text-on-acc disabled:opacity-40">
      {label}{pro && ' '}{pro && <span className="rounded-full border border-current px-1.5 text-[10.5px] font-bold leading-[16px]">PRO</span>}
    </button>
  );
}

/** What a fix did, with Undo. */
const Done = ({ text, onUndo }: { readonly text: string; onUndo?(): void }) => (
  <div className="flex items-center gap-2 text-[14.5px]">
    <span className="grid h-5 w-5 place-items-center rounded-full bg-keep text-on-keep"><CheckIcon size={12} /></span>
    <span className="min-w-0 flex-1 font-semibold">{text}</span>
    {onUndo && <button type="button" onClick={onUndo} className="text-[14.5px] font-semibold text-acc">Undo</button>}
  </div>
);

/**
 * 32: Fix dates and places, over every tab: photos a camera dated on its default or in the future, given the date the
 * person types; photos with no place, given that of the one taken nearest in time; and the copies on the computer
 * named by date. Each is Pro; the phone asks before anything changes, and Undo puts it back.
 */
export function FixesScreen({ notify, onClose }: { notify: Notify; onClose(): void }) {
  const { platform, tablet } = usePhone();
  const inPlace = useInPlace();
  const ios = platform === 'ios';
  const gate = usePlanGate();
  const fixes = useFixes(true);
  const data = fixes.data;
  const [day, setDay] = useState('');
  const [busy, setBusy] = useState(false);
  const free = gate.plan?.tier === 'free';
  useNamesEnded(data, notify);

  // Pro first; then the fix, said when done, with Undo where it can be undone.
  function run(fix: () => Promise<unknown>) {
    const go = async () => {
      setBusy(true);
      try { await fix(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
    };
    if (free) gate.offer('fix', () => void go());
    else void go();
  }

  const undo = (kind: 'dates' | 'places') => run(async () => {
    const n = await undoFix(kind);
    notify(kind === 'dates' ? `Put back ${n.toLocaleString()} ${n === 1 ? 'date' : 'dates'}.` : `Took back ${n.toLocaleString()} ${n === 1 ? 'location' : 'locations'}.`, 'ok');
  });
  const datesFix = () => run(async () => {
    const n = await fixDates(day);
    notify(`${n.toLocaleString()} ${n === 1 ? 'date' : 'dates'} fixed.`, 'ok', { label: 'Undo', run: () => undo('dates') });
  });
  const placesFix = () => run(async () => {
    const n = await addPlaces();
    notify(`${n.toLocaleString()} ${n === 1 ? 'location' : 'locations'} added.`, 'ok', { label: 'Undo', run: () => undo('places') });
  });

  const names = data?.names;
  return (
    <LayerScreen label="Fix dates and places" onBack={onClose}>
      <PushedHeader title="Fix dates and places" onBack={inPlace ? undefined : onClose} />
      <div className="mx-auto flex w-full max-w-[720px] flex-col gap-4 px-4 pb-8">
        {fixes.error !== undefined && <p role="alert" className="py-10 text-center text-[15px] text-t2">What needs fixing could not be found: {said(fixes.error)}</p>}
        {fixes.error === undefined && !data && <p className="py-10 text-center text-[15px] text-t2">Looking at dates and places…</p>}
        {data && <>
          <div>
            <p className="font-title text-[34px] font-bold leading-tight">{data.total === 0 ? 'All fixed' : items(data.total)}</p>
            <p className="text-[15px] text-t2">{data.total === 0 ? 'Dates and places look right.' : 'need a date or place'}</p>
          </div>
          {(data.dates.count > 0 || data.dates.undo > 0) && (
            <Card title="Camera clock not set" line={data.dates.count > 0 ? datesLine(data.dates.count, data.dates.date!) : 'Every date looks right.'} icon={<CalendarIcon />} hue={200} ios={ios}>
              {data.dates.count > 0 && <>
                <Thumbs photos={data.dates.photos} />
                <div className="flex flex-wrap items-center gap-2 rounded-[10px] bg-fill px-3 py-2 text-[14px]">
                  <span className="text-t2 line-through">{dayWritten(data.dates.date!)}</span><span aria-hidden className="text-t2">→</span>
                  <input type="date" aria-label="The date they were taken" value={day} onChange={e => setDay(e.target.value)} className="min-w-0 flex-1 bg-transparent font-semibold text-t1 outline-none" />
                </div>
                <p className="text-[13.5px] leading-[1.4] text-t2">Choose the day they were taken. Each keeps its time of day, and your {deviceWord(platform, tablet)} asks before they change.</p>
                <FixButton label={`Fix ${data.dates.count.toLocaleString()} ${data.dates.count === 1 ? 'date' : 'dates'}`} pro={free} disabled={!day || busy} onClick={datesFix} />
              </>}
              {data.dates.undo > 0 && <Done text={`${data.dates.undo.toLocaleString()} ${data.dates.undo === 1 ? 'date' : 'dates'} fixed`} onUndo={() => undo('dates')} />}
            </Card>
          )}
          {(data.places.count > 0 || data.places.undo > 0) && (
            <Card title="Missing location" line={data.places.count > 0 ? `${items(data.places.count)} with no place` : 'Every photo that can have a place has one.'} icon={<PinIcon />} hue={152} ios={ios}>
              {data.places.count > 0 && <>
                <Thumbs photos={data.places.photos} />
                <Change from="No location" to="Place of the nearest photo" />
                <p className="text-[13.5px] leading-[1.4] text-t2">{placesNote(data.places.count, data.places.place, data.places.placeCount)}</p>
                <FixButton label={`Add ${data.places.count.toLocaleString()} ${data.places.count === 1 ? 'location' : 'locations'}`} pro={free} disabled={busy} onClick={placesFix} />
              </>}
              {data.places.undo > 0 && <Done text={`${data.places.undo.toLocaleString()} ${data.places.undo === 1 ? 'location' : 'locations'} added`} onUndo={() => undo('places')} />}
            </Card>
          )}
          {names && (
            <Card title={`File names on ${names.computer}`} line={namesLine(names)} icon={<ImageIcon />} hue={250} ios={ios}>
              {names.style === 'asOnPhone' ? <>
                {names.now && names.after && <Change from={names.now} to={names.after} />}
                <p className="text-[13.5px] leading-[1.4] text-t2">Only the copies on the computer are renamed, and those backed up from now on. Files on this {deviceWord(platform, tablet)} keep their names.</p>
                <FixButton label={names.stage === 'running' ? 'Renaming…' : `Rename ${names.files.toLocaleString()} ${names.files === 1 ? 'file' : 'files'}`} pro={free}
                  disabled={busy || names.stage === 'running' || names.files === 0} onClick={() => run(() => renameOnComputer('date'))} />
              </> : <Done text={names.renamed != null ? `${names.renamed.toLocaleString()} ${names.renamed === 1 ? 'file' : 'files'} renamed` : 'Named by date'} />}
              {names.style !== 'asOnPhone' && <p className="text-[13.5px] leading-[1.4] text-t2">To undo it, open Organize on {names.computer} and choose Recently moved.</p>}
            </Card>
          )}
        </>}
      </div>
    </LayerScreen>
  );
}

/** Says when the computer has renamed the copies, or why not, once the phone has finished asking. */
function useNamesEnded(fixes: FixesDto | undefined, notify: Notify) {
  const was = useRef<string | null | undefined>(undefined);
  const names = fixes?.names;
  useEffect(() => {
    if (was.current === 'running' && names && names.stage !== 'running') {
      if (names.stage === 'ready') notify(`Renamed ${names.renamed!.toLocaleString()} ${names.renamed === 1 ? 'file' : 'files'} on ${names.computer}.`, 'ok');
      else notify(names.problem!, 'error');
    }
    was.current = names?.stage;
  }, [names, notify]);
}
