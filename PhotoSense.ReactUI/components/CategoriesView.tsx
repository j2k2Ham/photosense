import React, { useState } from 'react';
import { deleteChosen, undoChange, useBursts, useCategories, useCategoryPhotos } from '../lib/apiClient';
import { bytesOf, FIND_LABELS, findRows, largestIds, namesOf, resolution, rowLine, sizeOf, type FindId } from '../lib/categories';
import { binName, formatBytes } from '../lib/format';
import { shortDate } from '../lib/organize';
import { limitLine } from '../lib/plan';
import type { BurstDto, PhotoDto, PlanDto } from '../types';
import { ConfirmDialog } from './ConfirmDialog';
import { LimitDialog } from './LimitDialog';
import { PhotoThumb } from './PhotoThumb';
import type { ToastAction, ToastKind } from './Toaster';

/** A category the computer shows here: Clean up's own, less the duplicates, which have their tab. */
export type ComputerCategory = Exclude<FindId, 'duplicates' | 'similar'>;

const REMOVED_FOLDER = '_PhotoSense_Removed';

/** What each category says of itself under its title. */
const ABOUT: Record<ComputerCategory, string> = {
  largeVideos: 'Videos over 200 MB, largest first. Choose the ones to delete.',
  screenshots: 'Pictures of a screen. All are chosen: click one to keep it.',
  bursts: 'PhotoSense keeps the sharpest shot of each burst. Click another shot to keep it instead.',
  blurry: 'Pictures whose sharpest part is still soft. All are chosen: click one to keep it.',
  messaging: 'What WhatsApp and Telegram saved. All are chosen: click one to keep it.',
};

interface Props {
  /** The plan, for the free plan's removals left today; absent until the service has said. */
  readonly plan?: PlanDto;
  notify(message: string, kind: ToastKind, action?: ToastAction): void;
}

/** What is waiting for a yes: the files chosen, and for bursts the shots that stay. */
interface Asked { readonly going: readonly PhotoDto[]; readonly staying: readonly PhotoDto[] }

/**
 * Clean up's categories on a computer: large videos, screenshots, bursts, blurry shots and what messaging apps saved,
 * each with how many and what they take, and the chosen one's files to tick and delete into _PhotoSense_Removed, which
 * Undo puts back.
 */
export function CategoriesView({ plan, notify }: Props) {
  const report = useCategories(true).data;
  const rows = findRows(report, undefined, false).filter((r): r is typeof r & { id: ComputerCategory } => r.id !== 'duplicates');
  const [picked, setPicked] = useState<ComputerCategory>();
  const shown = picked ?? rows[0]?.id;
  return (
    <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[clamp(280px,24vw,380px)_1fr]">
      <nav aria-label="Categories" className="flex min-h-0 flex-col gap-1 overflow-y-auto border-line p-3 max-lg:border-b lg:border-r">
        {!report && <p role="status" className="px-3 py-2 text-[14px] text-t2">Loading…</p>}
        {rows.map(row => (
          <button key={row.id} type="button" aria-current={row.id === shown ? 'true' : undefined} onClick={() => setPicked(row.id)}
            className={`flex flex-col rounded-xl px-3.5 py-2.5 text-left ${row.id === shown ? 'bg-s2' : 'hover:bg-s1'}`}>
            <span className="text-[15px] font-semibold">{FIND_LABELS[row.id]}</span>
            <span className="text-[13px] text-t2">{rowLine(row, report!.unmeasured)}</span>
          </button>
        ))}
      </nav>
      {shown && (shown === 'bursts'
        ? <BurstsPane key="bursts" plan={plan} notify={notify} />
        : <FilesPane key={shown} category={shown} plan={plan} notify={notify} />)}
    </div>
  );
}

/** Asking before deleting, the free plan's limit, the deleting itself, and the message after with Undo. */
function useDeleting(plan: PlanDto | undefined, notify: Props['notify']) {
  const [asked, setAsked] = useState<Asked>();
  const [busy, setBusy] = useState(false);
  const left = plan?.leftToday;
  const over = asked && left != null && asked.going.length > left;

  const undo = (batchId: string) => void (async () => {
    try {
      const result = await undoChange(batchId);
      if (result.restored > 0) notify(`Put ${result.restored.toLocaleString()} ${result.restored === 1 ? 'file' : 'files'} back where ${result.restored === 1 ? 'it was' : 'they were'}.`, 'ok');
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    }
  })();

  const go = (going: readonly PhotoDto[], staying: readonly PhotoDto[], after: () => void) => void (async () => {
    setBusy(true);
    try {
      const done = await deleteChosen(going.map(p => p.id));
      setAsked(undefined);
      after();
      const stays = staying.length === 0 ? '' : staying.length === 1 ? ` ${staying[0].fileName} stays where it is.` : ` The shot kept of each burst stays where it is.`;
      if (done.deleted > 0) notify(`Moved ${done.deleted === 1 ? going[0].fileName : `${done.deleted.toLocaleString()} files`} (${formatBytes(done.bytes)}) to ${REMOVED_FOLDER}.${stays}`, 'ok', done.batchId ? { label: 'Undo', run: () => undo(done.batchId!) } : undefined);
      if (done.skipped > 0) notify(`${done.skipped.toLocaleString()} left alone. ${done.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    } finally {
      setBusy(false);
    }
  })();

  /** The question, or the limit's, for what was asked; nothing while nothing is. */
  const dialog = (after: () => void) => {
    if (!asked) return null;
    const cancel = () => setAsked(undefined);
    if (over) {
      const keep = new Set(largestIds(asked.going, left!));
      return <LimitDialog plan={plan!} count={asked.going.length} label="files" partial={left!} busy={busy} onCancel={cancel}
        onPartial={() => go(asked.going.filter(p => keep.has(p.id)), asked.staying, after)} />;
    }
    const n = asked.going.length;
    const limit = limitLine(plan);
    return (
      <ConfirmDialog busy={busy} title={`Delete ${n === 1 ? asked.going[0].fileName : `${n.toLocaleString()} files`}?`} confirmLabel={`Delete ${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`}
        onCancel={cancel} onConfirm={() => go(asked.going, asked.staying, after)}
        note={<>Files go to <span className="font-mono text-t1">{REMOVED_FOLDER}</span> inside the scanned folder, and Undo puts them back. Move to {binName()}, in the menu, empties it.{limit && ` ${limit}`}</>}>
        <p>{n === 1 ? `${asked.going[0].fileName} goes` : `${namesOf(asked.going)} go`}, {formatBytes(bytesOf(asked.going))} in all.</p>
        {asked.staying.length === 1 && <p>{asked.staying[0].fileName} stays.</p>}
        {asked.staying.length > 1 && <p>The shot kept of each of the {asked.staying.length.toLocaleString()} bursts stays.</p>}
      </ConfirmDialog>
    );
  };
  return { busy, ask: (going: readonly PhotoDto[], staying: readonly PhotoDto[] = []) => setAsked({ going, staying }), dialog };
}

/** The head of a category's pane: its name, what is chosen, what it is, and the buttons. */
function PaneHead({ title, chosen, of, about, children }: { readonly title: string; readonly chosen: readonly PhotoDto[]; readonly of: string; readonly about: string; readonly children: React.ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-x-5 gap-y-2 border-b border-line px-4 py-3 lg:px-6">
      <div className="min-w-0 flex-1">
        <h2 className="text-[19px] font-semibold">{title}</h2>
        <p className="text-[13.5px] text-t2"><span className="font-semibold text-t1">{chosen.length.toLocaleString()} chosen</span> of {of} · {sizeOf(chosen)}. {about}</p>
      </div>
      <div className="flex shrink-0 gap-2.5">{children}</div>
    </div>
  );
}

/**
 * A file's tile: its preview, chosen to delete or kept, and its name under it. A burst's shot kept is framed and marked
 * Keep, beside the button rather than in it, so that what the button is called holds all the words it shows.
 */
function FileTile({ photo, on, label, onClick, framed }: { readonly photo: PhotoDto; readonly on: boolean; readonly label: string; onClick(): void; readonly framed?: boolean }) {
  return (
    <div className="flex min-w-0 flex-col gap-1">
      <span className="relative block">
        <button type="button" aria-pressed={on} aria-label={label} title={photo.sourcePath} onClick={onClick}
          className={`relative block aspect-square w-full overflow-hidden rounded-xl ${framed ? 'ring-[3px] ring-keep' : ''}`}>
          <PhotoThumb photo={photo} className={`h-full w-full ${on || framed ? '' : 'opacity-40'}`} />
          {!framed && <span aria-hidden className={`absolute right-1.5 top-1.5 grid h-5 w-5 place-items-center rounded-md border-2 text-[12px] font-bold ${on ? 'border-rose bg-rose text-white' : 'border-white bg-black/30'}`}>{on ? '✓' : ''}</span>}
        </button>
        {framed && <span aria-hidden className="pointer-events-none absolute bottom-1.5 left-1/2 -translate-x-1/2 rounded-full bg-keep px-2 text-[11px] font-bold text-on-brand">Keep</span>}
      </span>
      <span className="truncate font-mono text-[12px] text-t2">{photo.fileName}</span>
    </div>
  );
}

/** Large videos, screenshots, blurry shots or what messaging apps saved: a grid of files to tick. */
function FilesPane({ category, plan, notify }: { readonly category: Exclude<ComputerCategory, 'bursts'> } & Props) {
  const photos = useCategoryPhotos(category).data;
  // Large videos start with none chosen, as each is worth a look; the rest start with all.
  const startChosen = category !== 'largeVideos';
  const [flipped, setFlipped] = useState<ReadonlySet<string>>(new Set());
  const { busy, ask, dialog } = useDeleting(plan, notify);
  const all = photos ?? [];
  const isOn = (p: PhotoDto) => flipped.has(p.id) !== startChosen;
  const chosen = all.filter(isOn);
  const flip = (id: string) => setFlipped(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  const every = chosen.length === all.length;
  const setAll = (on: boolean) => setFlipped(on === startChosen ? new Set() : new Set(all.map(p => p.id)));
  const of = `${all.length.toLocaleString()} ${all.length === 1 ? 'file' : 'files'}`;
  return (
    <section aria-label={FIND_LABELS[category]} className="flex min-h-0 flex-col">
      <PaneHead title={FIND_LABELS[category]} chosen={chosen} of={of} about={ABOUT[category]}>
        <button type="button" disabled={all.length === 0} onClick={() => setAll(!every)} className="pill-outline h-10 px-4 text-[14px]">{every && all.length > 0 ? 'Select none' : 'Select all'}</button>
        <button type="button" disabled={busy || chosen.length === 0} onClick={() => ask(chosen)} className="pill-rose h-10 px-4 text-[14px]">
          Delete {chosen.length.toLocaleString()} {chosen.length === 1 ? 'file' : 'files'}
        </button>
      </PaneHead>
      <div className="min-h-0 flex-1 overflow-y-auto p-4 lg:px-6">
        {!photos && <p role="status" className="text-[14px] text-t2">Loading…</p>}
        {photos && all.length === 0 && <p className="text-[14px] text-t2">Nothing left to review.</p>}
        <ul aria-label={`${FIND_LABELS[category]} files`} className="grid grid-cols-[repeat(auto-fill,minmax(132px,1fr))] gap-3">
          {all.map(p => (
            <li key={p.id} className="flex flex-col">
              <FileTile photo={p} on={isOn(p)} label={`${p.fileName}: ${isOn(p) ? 'to delete' : 'kept'}`} onClick={() => flip(p.id)} />
              {category === 'largeVideos' && <span className="text-[12px] text-t3">{[formatBytes(p.fileSizeBytes), resolution(p.width, p.height)].filter(Boolean).join(' · ')}</span>}
            </li>
          ))}
        </ul>
      </div>
      {dialog(() => setFlipped(new Set()))}
    </section>
  );
}

/** The bursts, each a row of its shots with the one kept framed; the rest are deleted. */
function BurstsPane({ plan, notify }: Props) {
  const bursts = useBursts(true).data;
  const [keepers, setKeepers] = useState<ReadonlyMap<string, string>>(new Map());
  const { busy, ask, dialog } = useDeleting(plan, notify);
  const keeperOf = (b: BurstDto) => keepers.get(b.shots[0].id) ?? b.keeper;
  const all = bursts ?? [];
  const extra = all.flatMap(b => b.shots.filter(s => s.id !== keeperOf(b)));
  const kept = all.map(b => b.shots.find(s => s.id === keeperOf(b))!);
  return (
    <section aria-label="Bursts" className="flex min-h-0 flex-col">
      <PaneHead title="Bursts" chosen={extra} of={`${all.length.toLocaleString()} ${all.length === 1 ? 'burst' : 'bursts'}`} about={ABOUT.bursts}>
        <button type="button" disabled={busy || extra.length === 0} onClick={() => ask(extra, kept)} className="pill-rose h-10 px-4 text-[14px]">
          Delete {extra.length.toLocaleString()} extra {extra.length === 1 ? 'shot' : 'shots'}
        </button>
      </PaneHead>
      <div className="flex min-h-0 flex-1 flex-col gap-5 overflow-y-auto p-4 lg:px-6">
        {!bursts && <p role="status" className="text-[14px] text-t2">Loading…</p>}
        {bursts && all.length === 0 && <p className="text-[14px] text-t2">Nothing left to review.</p>}
        {all.map(b => {
          const first = b.shots[0];
          const name = first.takenOn ? shortDate(first.takenOn) : first.fileName;
          return (
            <section key={first.id} aria-label={name} className="flex flex-col gap-2">
              <h3 className="text-[14.5px] font-semibold">{name}<span className="font-normal text-t2"> · {[first.placeName, `${b.shots.length.toLocaleString()} shots`].filter(Boolean).join(' · ')}</span></h3>
              <ul className="grid grid-cols-[repeat(auto-fill,minmax(112px,1fr))] gap-3">
                {b.shots.map(s => {
                  const keeps = s.id === keeperOf(b);
                  return (
                    <li key={s.id}>
                      <FileTile photo={s} on={!keeps} framed={keeps} label={`${s.fileName}: ${keeps ? 'kept' : 'to delete'}`} onClick={() => setKeepers(m => new Map(m).set(first.id, s.id))} />
                    </li>
                  );
                })}
              </ul>
            </section>
          );
        })}
      </div>
      {dialog(() => setKeepers(new Map()))}
    </section>
  );
}
