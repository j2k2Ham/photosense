import React, { useEffect, useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { applyOrganize, organizeThumbnailUrl, planOrganize, removeOrganize, undoChange, useOrganizeBatches } from '../../lib/apiClient';
import { formatBytes, formatDuration } from '../../lib/format';
import { newAlbumBase, whereIs, type Album, type Library } from '../../lib/library';
import {
  checkFolderName, dateRange, joinPath, mapLayout, monthOf, RADII, relativeFolder, resolveNames, shortDate, type Clash, type ClashChoice, type Suggestion,
} from '../../lib/organize';
import { usePhone } from '../../lib/phone';
import {
  ago, batchLine, files, goesTo, insideLine, optionsLine, placesFor, rowFacts, rowNote, sections, type FileKinds, type OrganizeRow, type PhoneOrganize,
} from '../../lib/phoneOrganize';
import type { OrganizeBatchDto, OrganizeFileDto, OrganizePlanDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { Chip, IconButton, ListGroup, ListRow, PhoneButton, Segmented, StickyFooter } from './Controls';
import { ChevronIcon, DoneIcon, ListIcon, MapIcon, PlusIcon, SlidersIcon, TrashIcon } from './Icons';
import { NewAlbumSheet } from './Library';
import { PushedHeader, PushedScreen, RootHeader, Screen, SplitView } from './Screens';
import { Sheet } from './Sheet';

type Notify = (message: string, kind: ToastKind, action?: ToastAction) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));
/** Organize's words are a computer's; an iPhone has albums. */
const albumWords = (text: string, ios: boolean) => (ios ? text.replace('a folder', 'an album').replace('new folder', 'new album').replace('Folder names', 'Album names') : text);

interface Props {
  readonly organize: PhoneOrganize;
  readonly library: Library;
  /** Albums or folders named here and not made yet. */
  readonly made: readonly Album[];
  onMade(album: Album): void;
  /** Shows Library's files with no location. */
  onNoLocation(): void;
  notify: Notify;
}

/**
 * Organize on a phone or tablet: the albums or folders suggested for what is in none yet, by place or date, as a list or
 * on a plain map; a suggestion's preview, pushed on a phone and beside the list on a tablet, where nothing moves until
 * Move; and what was moved lately, with Undo.
 */
export function OrganizeScreen({ organize, library, made, onMade, onNoLocation, notify }: Props) {
  const { tablet, ios, words, platform } = usePhone();
  const { settings, rows, open } = organize;
  const root = library.root;
  const batches = useOrganizeBatches().data ?? [];
  const [sheet, setSheet] = useState<'options' | 'recent' | 'new'>();
  const [busy, setBusy] = useState(false);
  const [picked, setPicked] = useState<string>();
  const byPlace = settings.group === 'place';
  const map = useMemo(() => {
    const live = rows.flatMap(r => (r.suggestion ? [r.suggestion] : []));
    return byPlace && organize.view === 'map' && library.listing ? mapLayout(live, library.listing.places, settings) : undefined;
  }, [rows, byPlace, organize.view, library.listing, settings]);

  async function undo(batchId: string) {
    setBusy(true);
    try {
      const result = await undoChange(batchId);
      organize.undone(batchId);
      if (result.restored > 0 && result.skipped === 0) notify('Put back. Every file is where it was.', 'ok');
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  const undoAction = (batchId: string | undefined): ToastAction | undefined => (batchId ? { label: 'Undo', run: () => void undo(batchId) } : undefined);

  // A suggestion is previewed, and moved, whole: not while more of its files may still turn up.
  const look = (key: string) => (library.reading ? notify(`Still reading your photos. Preview ${ios ? 'an album' : 'a folder'} once every file is in.`, 'info') : organize.look(key));
  const heading = byPlace ? `Suggested ${words.albums}` : `${words.Album}s by year`;
  const list = (
    <>
      <RootHeader title="Organize" subtitle={`Sort ${words.library} into ${words.albums} by place and date`} noGear={tablet}
        buttons={byPlace && (organize.view === 'list'
          ? <IconButton label="Show the map" onClick={() => organize.setView('map')}><MapIcon /></IconButton>
          : <IconButton label="Show the list" onClick={() => organize.setView('list')}><ListIcon /></IconButton>)} />
      <div className="flex flex-col gap-4 px-4 pb-4">
        <div className="flex flex-col gap-2">
          <div className="flex items-center gap-2.5">
            <Segmented label="Group by" className="min-w-0 flex-1" options={[{ id: 'place', label: 'Place' }, { id: 'date', label: 'Date' }]} value={settings.group} onChange={group => organize.change({ group })} />
            <button type="button" onClick={() => setSheet('options')} className="flex h-[var(--seg-h)] shrink-0 items-center gap-2 rounded-full bg-btn2-bg px-4 text-[14.5px] font-semibold text-btn2-fg"><SlidersIcon />Options</button>
          </div>
          <p className="px-1 text-[13px] text-t2">{optionsLine(settings, organize.kinds, words)}</p>
        </div>
        {library.reading && (
          <p role="status" className="px-1 text-[14px] text-t2">
            {library.reading.total > 0 ? `Reading your photos… ${library.reading.done.toLocaleString()} of ${library.reading.total.toLocaleString()}` : 'Reading your photos…'}
          </p>
        )}
        {map && map.dots.length > 0 && (
          <PhoneMap map={map} rows={rows} picked={picked ?? map.dots[0].key} onPick={setPicked} onPreview={look} />
        )}
        {organize.view === 'list' || !byPlace ? (
          rows.length > 0 ? (
            <section aria-label={heading} className="flex flex-col gap-2">
              <div className="flex items-baseline justify-between px-1">
                <h2 className={ios ? 'text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'text-[14px] font-medium text-acc'}>{heading}</h2>
                <span className="text-[13px] tabular-nums text-t2">{rows.length.toLocaleString()}</span>
              </div>
              <Rows rows={rows} selected={tablet ? open?.key : undefined} onOpen={look} />
            </section>
          ) : !library.reading && library.listing && (
            <p className="px-1 py-4 text-[15px] leading-[1.45] text-t2">{byPlace ? `Every photo with a location is in ${ios ? 'an album' : 'a folder'}.` : `Every photo is in ${ios ? 'an album' : 'a folder'}.`}</p>
          )
        ) : null}

        <ListGroup header={`Your ${words.albums}`}>
          {made.map(a => {
            const n = library.files.filter(f => f.folder === a.path).length;
            return <ListRow key={a.path} detail={n > 0 ? files(n) : undefined}>{a.label}{n === 0 && <span className="block text-[13px] text-t2">Select files in Library, then {words.put}.</span>}</ListRow>;
          })}
          <li>
            <button type="button" disabled={!root} onClick={() => setSheet('new')} className="flex min-h-[56px] w-full items-center gap-3.5 px-4 py-2.5 text-left text-[17px] font-semibold text-acc disabled:opacity-40">
              <span aria-hidden className="grid h-9 w-9 place-items-center rounded-[10px] bg-acc-soft"><PlusIcon /></span>New {words.album}
            </button>
          </li>
        </ListGroup>
        <ListGroup>
          {organize.unplaced > 0 && <ListRow chevron onClick={onNoLocation}>{files(organize.unplaced)} {organize.unplaced === 1 ? 'has' : 'have'} no location. {organize.unplaced === 1 ? 'It stays where it is.' : 'They stay where they are.'}</ListRow>}
          <ListRow chevron detail={batches.length.toLocaleString()} onClick={() => setSheet('recent')}>Recently moved</ListRow>
        </ListGroup>
      </div>
    </>
  );

  const detail = open && (open.suggestion
    ? <Preview key={open.key} row={open} suggestion={open.suggestion} organize={organize} library={library} busy={busy} onBusy={setBusy} notify={notify} undoAction={undoAction} onBack={tablet ? undefined : () => organize.look(undefined)} />
    : <Done row={open} root={root!} busy={busy} onUndo={undo} onBack={tablet ? undefined : () => organize.look(undefined)} />);

  return (
    <>
      {tablet ? (
        <Screen label="Organize" fixed>
          <SplitView list={list} detail={detail ?? (
            <>
              <PushedHeader title="Organize" gear />
              {library.listing && !library.reading && <p className="px-6 py-16 text-center text-[15px] text-t2">Nothing to sort: every photo is in {ios ? 'an album' : 'a folder'}, or has no location.</p>}
            </>
          )} />
        </Screen>
      ) : detail ? <PushedScreen label="Preview">{detail}</PushedScreen> : <Screen label="Organize">{list}</Screen>}
      {createPortal(<>
        {sheet === 'options' && <OptionsSheet organize={organize} onClose={() => setSheet(undefined)} />}
        {sheet === 'recent' && <RecentSheet batches={batches} root={root} busy={busy} onUndo={b => void undo(b.id)} onClose={() => setSheet(undefined)} />}
        {sheet === 'new' && (
          <NewAlbumSheet base={newAlbumBase(root!, platform)} albums={made} busy={false} onClose={() => setSheet(undefined)}
            onCreate={(base, name) => {
              onMade({ path: joinPath(base, name), label: name, base });
              setSheet(undefined);
              notify(`${name} is ready. Select files in Library, then ${words.put}.`, 'ok');
            }} />
        )}
      </>, document.body)}
    </>
  );
}

/** Up to four of a suggestion's pictures, two by two; a dark tile with a play mark for a video. */
function Mosaic({ of, size = 60 }: { readonly of: readonly OrganizeFileDto[]; readonly size?: number }) {
  return (
    <span aria-hidden className="grid shrink-0 grid-cols-2 gap-[2px] overflow-hidden rounded-xl bg-fill" style={{ width: size, height: size }}>
      {of.slice(0, 4).map(f => (f.isVideo
        ? <span key={f.id} className="grid place-items-center bg-[#1c2124]"><span className="ml-[2px] h-0 w-0 border-y-[4px] border-l-[6px] border-y-transparent border-l-white" /></span>
        : <img key={f.id} src={organizeThumbnailUrl(f.id)} alt="" loading="lazy" className="h-full w-full object-cover" />))}
    </span>
  );
}

/** The suggestions as rows: four of their pictures, the name, how many and how large, when from, and a note; moved ones say so. */
function Rows({ rows, selected, onOpen }: { readonly rows: readonly OrganizeRow[]; readonly selected?: string; onOpen(key: string): void }) {
  const { ios, tablet } = usePhone();
  return (
    <ul aria-label="Suggestions" className={ios
      ? 'overflow-hidden rounded-cell bg-cell [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep'
      : 'flex flex-col gap-[2px] overflow-hidden rounded-[20px] [&>li]:rounded-[4px] [&>li]:bg-cell'}>
      {rows.map(r => {
        const facts = rowFacts(r), note = r.suggestion && rowNote(r.suggestion), on = r.key === selected;
        return (
          <li key={r.key} className={on ? '!bg-sel' : ''}>
            <button type="button" aria-current={on ? 'true' : undefined} onClick={() => onOpen(r.key)} className="flex w-full items-center gap-3.5 px-3.5 py-3 text-left">
              <Mosaic of={r.suggestion?.files ?? []} />
              <span className="flex min-w-0 flex-1 flex-col gap-px">
                <span className="flex items-center gap-2">
                  <span className="truncate text-[16px] font-semibold">{r.suggestion?.name ?? r.moved!.name}</span>
                  {r.moved && <span className="flex h-5 shrink-0 items-center rounded-md bg-keep-soft px-2 text-[12px] font-semibold text-keep">Moved</span>}
                </span>
                <span className="text-[14px] text-t2">{files(facts.count)} · {formatBytes(facts.bytes)}</span>
                <span className="text-[14px] text-t2">{facts.range}</span>
                {note && <span className="text-[12.5px] leading-[1.35] text-t2">{note}</span>}
              </span>
              {!tablet && <span aria-hidden className="text-t3"><ChevronIcon /></span>}
            </button>
          </li>
        );
      })}
    </ul>
  );
}

interface MapProps {
  readonly map: NonNullable<ReturnType<typeof mapLayout>>;
  readonly rows: readonly OrganizeRow[];
  readonly picked: string;
  onPick(key: string): void;
  onPreview(key: string): void;
}

/** The suggestions as dots on a plain map, drawn here from their positions; the one picked as a card under it, with Preview. */
function PhoneMap({ map, rows, picked, onPick, onPreview }: MapProps) {
  const { tablet } = usePhone();
  // The dot picked, or the first should it have gone, as it does once its files have moved.
  const row = rows.find(r => r.key === picked && r.suggestion) ?? rows.find(r => r.key === map.dots[0].key)!;
  const shown = row.suggestion!;
  const facts = rowFacts(row);
  return (
    <>
      <div role="group" aria-label="Map" className={`relative overflow-hidden bg-map [background-image:linear-gradient(var(--map-line)_1px,transparent_1px),linear-gradient(90deg,var(--map-line)_1px,transparent_1px)] [background-size:40px_40px] ${tablet ? 'h-[420px] rounded-[20px]' : 'h-[360px] rounded-[24px]'}`}>
        {map.dots.map(d => {
          const n = rows.find(r => r.key === d.key)!.suggestion!.files.length, on = d.key === row.key, size = 26 + Math.sqrt(n) * 1.05;
          return (
            <button key={d.key} type="button" aria-label={`${d.tip}`} aria-pressed={on} onClick={() => onPick(d.key)}
              className={`absolute flex -translate-x-1/2 -translate-y-1/2 flex-col items-center ${on ? 'z-10' : ''}`} style={{ left: `${d.x}%`, top: `${d.y}%` }}>
              <span className={`grid place-items-center rounded-full text-[12px] font-bold tabular-nums ${on ? 'bg-acc text-on-acc shadow-[0_0_0_3px_var(--cell)]' : 'bg-cell text-t1 shadow-[inset_0_0_0_2px_var(--acc)]'}`} style={{ width: size, height: size }}>{n.toLocaleString()}</span>
              {on && <span className="mt-1 whitespace-nowrap rounded-md bg-cell px-1.5 py-0.5 text-[12px] font-semibold">{shown.name}</span>}
            </button>
          );
        })}
        <p className="absolute inset-x-3 bottom-2.5 text-[12px] text-t2">Drawn on this phone. Positions are never sent anywhere.</p>
      </div>
      <div className="flex items-center gap-3.5 rounded-cell bg-cell p-3.5">
        <Mosaic of={shown.files} />
        <span className="flex min-w-0 flex-1 flex-col">
          <span className="truncate text-[17px] font-semibold">{shown.name}</span>
          <span className="text-[14px] text-t2">{files(facts.count)} · {formatBytes(facts.bytes)}</span>
          <span className="text-[14px] text-t2">{facts.range}</span>
        </span>
        <button type="button" onClick={() => onPreview(row.key)} className="h-10 shrink-0 rounded-full bg-acc px-5 text-[15px] font-semibold text-on-acc">Preview</button>
      </div>
    </>
  );
}

/** A choice of a few, under its label, in the options sheet. */
function Choice({ label, children, note }: { readonly label: string; readonly children: React.ReactNode; readonly note?: string }) {
  return (
    <div className="flex flex-col gap-2">
      <span className="text-[15px] font-semibold text-t2">{label}</span>
      {children}
      {note && <p className="text-[13.5px] leading-[1.4] text-t2">{note}</p>}
    </div>
  );
}

/** How Organize sorts: by place or date, near places together or apart, how albums are named, what is inside each, and which files. */
function OptionsSheet({ organize, onClose }: { readonly organize: PhoneOrganize; onClose(): void }) {
  const { ios, words } = usePhone();
  const { settings: s, change } = organize;
  const KINDS: { id: FileKinds; label: string }[] = [{ id: 'both', label: 'Photos & videos' }, { id: 'photos', label: 'Photos' }, { id: 'videos', label: 'Videos' }];
  return (
    <Sheet title={`${words.Album} options`} onClose={onClose}>
      <div className="flex flex-col gap-5 px-5 pb-3 pt-2">
        <Choice label="Group by"><Segmented label="Group by" options={[{ id: 'place', label: 'Place' }, { id: 'date', label: 'Date' }]} value={s.group} onChange={group => change({ group })} /></Choice>
        {s.group === 'place' ? (
          <>
            <Choice label="Nearby places"><Segmented label="Nearby places" options={[{ id: 'combine', label: 'Combine' }, { id: 'separate', label: 'Keep separate' }]} value={s.near} onChange={near => change({ near })} /></Choice>
            {s.near === 'combine' && (
              <Choice label="Within">
                <div role="group" aria-label="Within" className="flex flex-wrap gap-2">
                  {RADII.map(r => <Chip key={r} label={`${r} mi`} on={s.radiusMiles === r} onClick={() => change({ radiusMiles: r })} />)}
                </div>
              </Choice>
            )}
            <Choice label={`${words.Album} names`} note={s.nameFormat === 'town' ? undefined : 'A landmark or area is used where a photo was taken at one. Otherwise the town is used.'}>
              <Segmented label={`${words.Album} names`} options={[{ id: 'area', label: 'Landmark or area' }, { id: 'town', label: 'Town' }]} value={s.nameFormat === 'town' ? 'town' : 'area'} onChange={nameFormat => change({ nameFormat })} />
            </Choice>
            <Choice label={`Inside each ${words.album}`}>
              <Segmented label={`Inside each ${words.album}`} options={[{ id: 'year', label: 'Year' }, { id: 'month', label: 'Month' }, { id: 'none', label: 'Nothing' }]} value={s.placeThen} onChange={placeThen => change({ placeThen })} />
            </Choice>
          </>
        ) : (
          <Choice label="Inside each year">
            <Segmented label="Inside each year" options={[{ id: 'place', label: 'Place' }, { id: 'none', label: 'Nothing' }]} value={s.dateThen} onChange={dateThen => change({ dateThen })} />
          </Choice>
        )}
        <Choice label="Show" note={organize.kinds === 'both' ? undefined : 'Only the kind shown is sorted. A Live Photo\'s video always goes with its picture.'}>
          <Segmented label="Show" fit options={KINDS} value={organize.kinds} onChange={organize.setKinds} />
        </Choice>
        <p className="rounded-cell bg-sheet-cell px-4 py-3 text-[14px] leading-[1.4] text-t2">
          {ios ? 'Albums are made in Photo Library. Files already in one of your albums are left out.' : 'Folders are made in Pictures. Files already in one of your folders are left out.'}
        </p>
      </div>
    </Sheet>
  );
}

/** What was moved or deleted lately, each with Undo. */
function RecentSheet({ batches, root, busy, onUndo, onClose }: { readonly batches: readonly OrganizeBatchDto[]; readonly root?: string; readonly busy: boolean; onUndo(b: OrganizeBatchDto): void; onClose(): void }) {
  const { words } = usePhone();
  const now = new Date();
  return (
    <Sheet title="Recently moved" onClose={onClose}>
      <div className="flex flex-col gap-3 px-4 pb-3 pt-2">
        {batches.length === 0 ? <p className="px-1 py-6 text-center text-[15px] text-t2">Nothing to undo.</p> : (
          <ListGroup inSheet>
            {batches.map(b => (
              <li key={b.id} className="flex min-h-[56px] items-center gap-3 px-4 py-2.5">
                <span className="flex min-w-0 flex-1 flex-col">
                  <span className="text-[15px] [overflow-wrap:anywhere]">{batchLine(b, root, words)}</span>
                  <span className="text-[13px] text-t2">{ago(b.utc, now)}</span>
                </span>
                <button type="button" disabled={busy} onClick={() => onUndo(b)} className="h-9 shrink-0 rounded-full bg-btn2-bg px-4 text-[15px] font-semibold text-btn2-fg disabled:opacity-40">Undo</button>
              </li>
            ))}
          </ListGroup>
        )}
        <p className="px-1 text-[13.5px] leading-[1.4] text-t2">Undo puts files back under the names they had. What was deleted permanently can&apos;t be put back.</p>
      </div>
    </Sheet>
  );
}

/** A suggestion moved in this sitting: where its files are now, and Undo. */
function Done({ row, root, busy, onUndo, onBack }: { readonly row: OrganizeRow; readonly root: string; readonly busy: boolean; onUndo(batchId: string): void; onBack?(): void }) {
  const { words, tablet, platform } = usePhone();
  const m = row.moved!;
  return (
    <>
      <PushedHeader title={m.name} subtitle="Moved" onBack={onBack} gear={tablet} />
      <div className="flex flex-col items-center gap-3 px-6 py-12 text-center">
        <span className="grid h-16 w-16 place-items-center rounded-full bg-keep-soft text-keep"><DoneIcon /></span>
        <p className="text-[19px] font-semibold">{files(m.count)} {m.count === 1 ? 'is' : 'are'} in {m.name}</p>
        <p className="font-code text-[13px] text-t2 [overflow-wrap:anywhere]">{goesTo(m.path, root, words, platform)}</p>
        {m.batchId && <button type="button" disabled={busy} onClick={() => onUndo(m.batchId!)} className="mt-2 h-10 rounded-full bg-btn2-bg px-6 text-[15px] font-semibold text-btn2-fg disabled:opacity-40">Undo</button>}
      </div>
    </>
  );
}

interface PreviewProps {
  readonly row: OrganizeRow;
  readonly suggestion: Suggestion;
  readonly organize: PhoneOrganize;
  readonly library: Library;
  readonly busy: boolean;
  onBusy(busy: boolean): void;
  notify: Notify;
  undoAction(batchId: string | undefined): ToastAction | undefined;
  /** Goes back to the list, on a phone; a tablet shows both. */
  onBack?(): void;
}

/** Rows of tiles a section shows at first, and how many more each Show more adds. */
const FIRST_ROWS = 2, MORE_ROWS = 5;

/**
 * A suggestion before anything moves: its name, where it is made and what it holds inside, any names already taken
 * there, and its files by year, month or place, any of which can be left where it is. Move moves the rest.
 */
function Preview({ row, suggestion: s, organize, library, busy, onBusy, notify, undoAction, onBack }: PreviewProps) {
  const { ios, tablet, words, platform } = usePhone();
  const root = library.root!;
  const [base, setBase] = useState(() => newAlbumBase(root, platform));
  const [excluded, setExcluded] = useState<Readonly<Record<string, boolean>>>({});
  const [choices, setChoices] = useState<Readonly<Record<string, ClashChoice>>>({});
  const [answer, setAnswer] = useState<{ asked: string; plan?: OrganizePlanDto; problem?: string }>();
  const [sheet, setSheet] = useState<'where' | 'names'>();
  const [reviewed, setReviewed] = useState(false);
  const [shownRows, setShownRows] = useState<Readonly<Record<string, number>>>({});

  const named = checkFolderName(s.name);
  const folderName = named.error === undefined ? named.name : undefined;
  const asked = useMemo(() => s.files.map(f => ({ id: f.id, subfolder: s.subfolderOf(f) })), [s]);
  const request = folderName === undefined ? undefined : JSON.stringify([base, folderName, asked]);
  // Asked afresh whenever where the files go changes; an answer to an earlier question is not used.
  useEffect(() => {
    if (request === undefined) return undefined;
    let latest = true;
    const [basePath, name, files_] = JSON.parse(request) as [string, string, { id: string; subfolder?: string }[]];
    planOrganize({ basePath, folderName: name, direct: false }, files_).then(
      plan => { if (latest) setAnswer({ asked: request, plan }); },
      e => { if (latest) setAnswer({ asked: request, problem: said(e) }); });
    return () => { latest = false; };
  }, [request]);
  const current = answer?.asked === request ? answer : undefined;
  const plan = current?.plan;
  const planned = useMemo(() => (plan ? new Set(plan.items.map(i => i.id)) : undefined), [plan]);
  const movable = s.files.filter(f => planned?.has(f.id) ?? true);
  const going = movable.filter(f => !excluded[f.id]);
  const resolved = useMemo(() => (plan ? resolveNames(s.files, plan, choices, excluded) : { clashes: [] as Clash[], names: {} as Record<string, string> }), [s.files, plan, choices, excluded]);
  const clashes = resolved.clashes;
  const destination = joinPath(base, folderName);
  const columns = tablet ? 6 : 4;

  async function move() {
    onBusy(true);
    try {
      const result = await applyOrganize({
        basePath: base, folderName: folderName!, direct: false, mode: 'move', companions: true, label: folderName!,
        files: going.map(f => ({ id: f.id, name: resolved.names[f.id], subfolder: s.subfolderOf(f) })),
      });
      if (result.done > 0) {
        const batchId = result.batchId || undefined;
        organize.moved(row.key, { name: folderName!, count: result.done, bytes: result.bytes, range: dateRange(going), path: plan!.destination, batchId });
        notify(`Moved ${files(result.done)} to ${folderName}.`, 'ok', undoAction(batchId));
      }
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} left where ${result.skipped === 1 ? 'it is' : 'they are'}. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      onBusy(false);
    }
  }

  const clashFolders = [...new Set(clashes.map(c => c.folder))];
  const takenIn = clashFolders.length === 1 ? relativeFolder(root, clashFolders[0]).split(/[\\/]/).join(words.separator) : folderName;
  const label = 'flex min-h-[52px] items-center gap-3 px-4 py-2.5';
  return (
    <>
      <PushedHeader title={s.name || `New ${words.album}`} subtitle="Preview. Nothing moves until you tap Move." onBack={onBack} gear={tablet} />
      <div className="flex flex-col gap-4 px-4">
        <p className="text-[15px] text-t2">{going.length.toLocaleString()} of {files(movable.length)} will move. Tap a file to leave it where it is.</p>
        <div className={ios ? 'overflow-hidden rounded-cell bg-cell [&>*+*]:border-t-[0.5px] [&>*+*]:border-sep' : 'flex flex-col gap-[2px] overflow-hidden rounded-[20px] [&>*]:rounded-[4px] [&>*]:bg-cell'}>
          <label className={label}>
            <span className="w-[100px] shrink-0 text-[15px] text-t2">{words.Album} name</span>
            <input value={s.name} onChange={e => organize.rename(row.key, e.target.value)} aria-invalid={named.error !== undefined}
              className="min-w-0 flex-1 bg-transparent text-[16px] font-semibold text-t1 outline-none" />
          </label>
          {named.error !== undefined && <p role="alert" className="px-4 py-2 text-[14px] text-danger-t">{albumWords(named.error, ios)}</p>}
          <div className={label}>
            <span className="w-[100px] shrink-0 text-[15px] text-t2">Made inside</span>
            <span className="min-w-0 flex-1 truncate text-[16px]">{goesTo(base, root, words, platform)}</span>
            <button type="button" onClick={() => setSheet('where')} className="shrink-0 text-[15px] font-semibold text-acc">Change</button>
          </div>
          <div className={label}>
            <span className="w-[100px] shrink-0 text-[15px] text-t2">Inside it</span>
            <span className="min-w-0 flex-1 text-[16px]">{insideLine(organize.settings, words)}</span>
          </div>
        </div>
        {current?.problem && <p role="alert" className="text-[14px] text-danger-t">{current.problem}</p>}
        {request !== undefined && !current && <p role="status" className="text-[14px] text-t2">Checking what is already there…</p>}
        {clashes.length > 0 && (reviewed ? (
          <p className="rounded-cell bg-keep-soft px-4 py-3.5 text-[15px] text-keep">{clashes.length === 1 ? '1 name' : `${clashes.length.toLocaleString()} names`} reviewed.</p>
        ) : (
          <div className="flex flex-col items-start gap-3 rounded-cell bg-amber-soft px-4 py-3.5">
            <p className="text-[15px] leading-[1.4]">
              {clashes.length === 1 ? '1 name is' : `${clashes.length.toLocaleString()} names are`} already taken in {takenIn}. {clashes.length === 1 ? 'It gets' : 'Each gets'} a number, as in {clashes[0].auto}.
            </p>
            <button type="button" onClick={() => setSheet('names')} className="h-10 rounded-full bg-t1 px-4 text-[15px] font-semibold text-bg">Review each name</button>
          </div>
        ))}
      </div>
      {sections(movable, s.subfolderOf, s.split).map(section => {
        const rowsNow = shownRows[section.title] ?? FIRST_ROWS, all = Math.ceil(section.files.length / columns);
        return (
          <section key={section.title} aria-label={section.title} className="pt-5">
            <div className="flex items-baseline justify-between px-4 pb-2">
              <h3 className="text-[17px] font-semibold">{section.title}</h3>
              <span className="text-[13px] text-t2">{files(section.files.length)}</span>
            </div>
            <ul className={`grid gap-[2px] ${tablet ? 'grid-cols-6' : 'grid-cols-4'}`}>
              {section.files.slice(0, rowsNow * columns).map(f => (
                <li key={f.id}>
                  <button type="button" aria-label={f.name} aria-pressed={!excluded[f.id]} onClick={() => setExcluded(x => ({ ...x, [f.id]: !x[f.id] }))} className="relative block aspect-square w-full overflow-hidden bg-fill">
                    {f.isVideo
                      ? <span aria-hidden className="absolute inset-0 bg-[#1c2124]" />
                      : <img src={organizeThumbnailUrl(f.id)} alt="" loading="lazy" className="absolute inset-0 h-full w-full object-cover" />}
                    {f.durationSeconds != null && <span className="absolute bottom-1 right-1.5 text-[12px] font-semibold text-white [text-shadow:0_1px_3px_rgba(0,0,0,.6)]">{formatDuration(f.durationSeconds)}</span>}
                    {excluded[f.id] && (
                      <span className="absolute inset-0 grid place-items-center bg-[color-mix(in_srgb,var(--bg)_74%,transparent)]">
                        <span className="rounded-full bg-t1 px-2.5 py-0.5 text-[12px] font-semibold text-bg">Stays</span>
                      </span>
                    )}
                  </button>
                </li>
              ))}
            </ul>
            {all > FIRST_ROWS && (
              <button type="button" onClick={() => setShownRows(r => ({ ...r, [section.title]: rowsNow >= all ? FIRST_ROWS : rowsNow === FIRST_ROWS ? MORE_ROWS : rowsNow + MORE_ROWS }))}
                className="px-4 pt-2.5 text-[15px] font-semibold text-acc">{rowsNow >= all ? 'Show fewer' : 'Show more'}</button>
            )}
          </section>
        );
      })}
      <StickyFooter>
        <PhoneButton disabled={busy || !plan || going.length === 0 || folderName === undefined} onClick={() => void move()}>Move {files(going.length)}</PhoneButton>
        <p className="text-center text-[13px] text-t2">Into {goesTo(destination, root, words, platform)}. Undo puts them back.</p>
      </StickyFooter>

      {createPortal(<>
        {sheet === 'where' && (
          <WhereSheet places={placesFor(root, library.files.map(f => f.folder), platform)} value={base} onPick={setBase} onClose={() => setSheet(undefined)} />
        )}
        {sheet === 'names' && clashes.length > 0 && (
          <NamesSheet clashes={clashes} root={root} busy={busy} onBusy={onBusy} notify={notify} undoAction={undoAction}
            onChoice={(clash, how, name) => {
              setExcluded(x => ({ ...x, [clash.file.id]: how === 'skip' }));
              // A name already typed for it is kept; otherwise one is offered that says when the file is from.
              const typed = name ?? (clash.mode === 'rename' ? clash.wanted : `${clash.base} ${monthOf(clash.file)}`);
              if (how !== 'skip') setChoices(c => ({ ...c, [clash.file.id]: how === 'auto' ? { mode: 'auto' } : { mode: 'rename', name: typed } }));
            }}
            onDone={() => { setSheet(undefined); setReviewed(true); notify(`${clashes.length === 1 ? '1 name' : `${clashes.length.toLocaleString()} names`} reviewed. Nothing moves until you tap Move.`, 'ok'); }}
            onClose={() => setSheet(undefined)} />
        )}
      </>, document.body)}
    </>
  );
}

/** Where the new album or folder is made: a round mark beside each place it can go. */
function WhereSheet({ places, value, onPick, onClose }: { readonly places: ReturnType<typeof placesFor>; readonly value: string; onPick(path: string): void; onClose(): void }) {
  const { ios, words } = usePhone();
  return (
    <Sheet title={`Where the ${words.album} goes`} onClose={onClose}>
      <div className="flex flex-col gap-3 px-4 pb-3 pt-2">
        <ListGroup inSheet>
          {places.map(p => (
            <li key={p.path}>
              <button type="button" role="radio" aria-checked={p.path === value} onClick={() => onPick(p.path)} className="flex min-h-[56px] w-full items-center gap-3.5 px-4 py-2.5 text-left">
                <Radio on={p.path === value} />
                <span className="flex min-w-0 flex-col"><span className="text-[16px]">{p.title}</span><span className="text-[13px] text-t2">{p.detail}</span></span>
              </button>
            </li>
          ))}
        </ListGroup>
        <p className="px-1 text-[13.5px] text-t2">{ios ? 'A folder here is a folder of albums in the Photos app.' : 'Android lets apps make folders only inside DCIM, Pictures or Movies.'}</p>
      </div>
    </Sheet>
  );
}

const Radio = ({ on }: { readonly on: boolean }) => (
  <span aria-hidden className={`grid h-[22px] w-[22px] shrink-0 place-items-center rounded-full border-2 ${on ? 'border-acc' : 'border-t3'}`}>{on && <span className="h-3 w-3 rounded-full bg-acc" />}</span>
);

interface NamesProps {
  readonly clashes: readonly Clash[];
  readonly root: string;
  readonly busy: boolean;
  onBusy(busy: boolean): void;
  notify: Notify;
  undoAction(batchId: string | undefined): ToastAction | undefined;
  /** What should happen to the file whose name is taken, with the name typed for it. */
  onChoice(clash: Clash, how: 'auto' | 'rename' | 'skip', name?: string): void;
  onDone(): void;
  onClose(): void;
}

/** One of the two files that share a name, as the names sheet shows it. */
interface Side { readonly id: string; readonly name: string; readonly where: string; readonly date: string; readonly sizeBytes: number; readonly isVideo: boolean; readonly folder: string }

/**
 * The names already taken, one at a time: the file going in beside the one already there, either of which can be
 * deleted after a question naming the one that stays, and what happens to the file going in. Nothing is ever replaced.
 */
function NamesSheet({ clashes, root, busy, onBusy, notify, undoAction, onChoice, onDone, onClose }: NamesProps) {
  const { words } = usePhone();
  const [index, setIndex] = useState(0);
  const [deleting, setDeleting] = useState<{ goes: Side; stays: Side }>();
  const at = Math.min(index, clashes.length - 1), clash = clashes[at], there = clash.takenBy[0], last = at === clashes.length - 1;
  const where = (folder: string) => whereIs(folder, root, words);
  const going: Side = { id: clash.file.id, name: clash.file.name, where: `From ${where(clash.file.folder)}`, date: clash.file.date, sizeBytes: clash.file.sizeBytes, isVideo: clash.file.isVideo, folder: clash.file.folder };
  const other: Side = { id: there.id, name: there.ownName, where: there.incoming ? `Also from ${where(there.folder)}` : `In ${where(clash.folder)}`, date: there.date, sizeBytes: there.sizeBytes, isVideo: there.isVideo, folder: there.incoming ? there.folder : clash.folder };

  async function remove(goes: Side, stays: Side) {
    onBusy(true);
    try {
      const result = await removeOrganize(root, [goes.id]);
      setDeleting(undefined);
      if (result.done > 0) notify(`${goes.name} deleted from ${where(goes.folder)}. ${stays.name} in ${where(stays.folder)} stays.`, 'ok', undoAction(result.batchId || undefined));
      if (result.skipped > 0) notify(`${goes.name} was left where it is. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      onBusy(false);
    }
  }

  const column = (side: Side, heading: string, stays: Side) => (
    <div className="flex min-w-0 flex-col gap-1">
      <span className="text-[13px] font-semibold uppercase tracking-[.02em] text-t2">{heading}</span>
      <span className="relative block aspect-square overflow-hidden rounded-[14px] bg-fill">
        {side.isVideo ? <span aria-hidden className="absolute inset-0 bg-[#1c2124]" /> : <img src={organizeThumbnailUrl(side.id)} alt="" className="absolute inset-0 h-full w-full object-cover" />}
      </span>
      <span className="truncate pt-1 font-code text-[12.5px]">{side.name}</span>
      <span className="truncate text-[13px] text-t2">{side.where}</span>
      <span className="text-[13px] text-t2">{shortDate(side.date)} · {formatBytes(side.sizeBytes)}</span>
      <button type="button" disabled={busy} onClick={() => setDeleting({ goes: side, stays })} aria-label={`Delete ${side.name}, ${heading.toLowerCase()}`}
        className="mt-1 flex h-8 items-center gap-1.5 self-start rounded-full bg-danger-soft px-3 text-[13.5px] font-semibold text-danger-t disabled:opacity-40"><TrashIcon size={15} />Delete</button>
    </div>
  );
  const option = (how: 'auto' | 'rename' | 'skip', title: string, detail: string) => (
    <button key={how} type="button" role="radio" aria-checked={clash.mode === how} onClick={() => onChoice(clash, how)} className="flex min-h-[56px] w-full items-center gap-3.5 px-4 py-2.5 text-left">
      <Radio on={clash.mode === how} />
      <span className="flex min-w-0 flex-col"><span className="text-[16px]">{title}</span><span className="truncate font-code text-[12.5px] text-t2">{detail}</span></span>
    </button>
  );
  return (
    <Sheet title="Name taken" done="Close" onClose={onClose}>
      <div className="flex flex-col gap-3.5 px-5 pb-3 pt-1">
        <p className="text-[14px] text-t2">{at + 1} of {clashes.length} · The file going in and the one already there</p>
        <div className="grid grid-cols-2 gap-3">
          {column(going, 'Going in', other)}
          {column(other, there.incoming ? 'Also going in' : 'Already there', going)}
        </div>
        {deleting ? (
          <div role="alertdialog" aria-label={`Delete ${deleting.goes.name}?`} className="flex flex-col gap-3 rounded-cell bg-danger-soft px-4 py-3.5">
            <p className="text-[15px] leading-[1.4]">Delete {deleting.goes.name} from {where(deleting.goes.folder)}? The one in {where(deleting.stays.folder)} stays.</p>
            <div className="flex gap-2">
              <button type="button" onClick={() => setDeleting(undefined)} className="h-10 flex-1 rounded-full bg-btn2-bg text-[15px] font-semibold text-btn2-fg">Cancel</button>
              <button type="button" disabled={busy} onClick={() => void remove(deleting.goes, deleting.stays)} className="h-10 flex-1 rounded-full bg-danger text-[15px] font-semibold text-on-danger disabled:opacity-40">Delete</button>
            </div>
          </div>
        ) : there.identical
          ? <p className="rounded-xl bg-ident-bg px-3.5 py-2.5 text-[14px] font-semibold text-ident">Byte for byte the same file.</p>
          : <p className="rounded-xl bg-amber-soft px-3.5 py-2.5 text-[14px] font-semibold text-amber">Two different files with the same name.</p>}
        <div role="radiogroup" aria-label="What happens to it" className="overflow-hidden rounded-cell bg-sheet-cell [&>*+*]:border-t-[0.5px] [&>*+*]:border-sep">
          {option('auto', 'Add a number', clash.auto)}
          {option('rename', 'Give it another name', 'Type the new name below')}
          {option('skip', 'Leave it where it is', `It stays in ${where(clash.file.folder)}`)}
        </div>
        {clash.mode === 'rename' && (
          <div className="flex flex-col gap-1">
            <span className="flex h-12 items-center gap-2 rounded-[14px] border border-sep bg-input pl-4 pr-2">
              <input value={clash.wanted} onChange={e => onChoice(clash, 'rename', e.target.value)} aria-label="New name" aria-invalid={clash.bad}
                className="min-w-0 flex-1 bg-transparent font-code text-[15px] text-t1 outline-none" />
              <span className="font-code text-[14px] text-t2">{clash.ext}</span>
            </span>
            {clash.bad && <p role="alert" className="px-1 text-[13.5px] text-danger-t">That name is taken or has characters file names cannot use. It gets a number instead.</p>}
          </div>
        )}
        <PhoneButton onClick={() => (last ? onDone() : setIndex(at + 1))}>{last ? 'Done' : 'Next name'}</PhoneButton>
      </div>
    </Sheet>
  );
}
