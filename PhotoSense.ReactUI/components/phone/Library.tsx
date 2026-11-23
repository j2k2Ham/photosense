import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { applyOrganize, chooseMorePhotos, organizeThumbnailUrl, planOrganize, removeOrganize, undoChange, useFixes, usePhotoAccess, useTrips, useVault } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { formatDuration, leaf } from '../../lib/format';
import {
  albumsOf, byMonth, deleteQuestion, filterCounts, items, libraryFilters, monthName, newAlbumBase, shows,
  type Album, type BackedUp, type Library, type LibraryFilter,
} from '../../lib/library';
import { checkedAt, gigabytes } from '../../lib/backup';
import { deletedSaid } from '../../lib/categories';
import { checkFolderName, resolveNames } from '../../lib/organize';
import { deviceWord, usePhone, type Collection } from '../../lib/phone';
import type { LibraryBackupDto, OrganizeDestination, OrganizeFileDto, OrganizePlaceDto, SafeReviewDto, YearReviewDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { Chip, ListGroup, ListRow, PhoneButton } from './Controls';
import { CheckIcon, CloseIcon, FolderIcon, LaptopIcon } from './Icons';
import { MonthHeader, PhotoGrid, SelectionBar } from './PhotoGrid';
import { PlaceContext, RootHeader, Screen } from './Screens';
import { SearchField, SearchScreen } from './Search';
import { Viewer } from './Viewer';
import { FixesScreen, FixesTile } from './Fixes';
import { HiddenScreen, HiddenTile } from './Hidden';
import { TripsCard, TripsScreen, TripsTile } from './Trips';
import { YearCard } from './YearInReview';
import { usePlanGate } from './ProSheet';
import { ConfirmSheet, Sheet } from './Sheet';
import { largest, limitLine } from '../../lib/plan';

type Notify = (message: string, kind: ToastKind, action?: ToastAction) => void;

/** How many files are drawn at first, and how many more each time the end of them comes into view. */
export const LIBRARY_PAGE = 240;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));
const NO_PLACES: OrganizePlaceDto[] = [];
const NO_ALBUMS: readonly Album[] = [];

interface Props {
  readonly library: Library;
  /** Choosing files rather than opening them. */
  readonly selecting: boolean;
  onSelecting(on: boolean): void;
  /** Which files are shown, which Organize can set too. */
  readonly filter: LibraryFilter;
  onFilter(filter: LibraryFilter): void;
  /** Albums or folders named in Organize and not made yet, offered beside those there are. */
  readonly made?: readonly Album[];
  notify: Notify;
  /** Sends these pictures and videos, by their ids, to the computer now. */
  onSend(ids: string[]): void;
  /** Which are on the computer and which wait, once the phone backs up to one. */
  readonly backup?: LibraryBackupDto;
  /** What could be freed, for the banner that leads to Safe to delete. */
  readonly safe?: SafeReviewDto;
  /** Opens Safe to delete. */
  onReview?(): void;
  /** Year in review, in December and January; its card shows while it is due. */
  readonly year?: YearReviewDto;
  /** Opens Year in review. */
  onYear?(): void;
  /** Whether Share photo starts with Remove location on, as Settings has it. */
  readonly removeLocation?: boolean;
  /** Raised each time Search is asked for from outside, as from the app icon's menu: Search opens. */
  readonly searchAsked?: number;
  /** The collection open, if any: over every tab on a phone, in place of the tab on a tablet. */
  readonly collection?: Collection;
  /** Opens a collection, or, with none, closes the one open; without it, Library keeps which is open itself. */
  onCollection?(collection?: Collection): void;
}

/**
 * Library: every picture and video on the phone, latest first, by month, narrowed by a filter, with the search field
 * above them (26). Tapping one opens it; Select chooses several to send, put in an album or folder, or delete.
 */
export function LibraryScreen({ library, selecting, onSelecting, filter, onFilter, made = NO_ALBUMS, notify, onSend, backup, safe, onReview, year, onYear, removeLocation = true, searchAsked = 0, collection: given, onCollection }: Props) {
  const { platform, ios, tablet, words, placeSlot } = usePhone();
  const backedUp = useMemo<BackedUp | undefined>(() => (backup?.computer ? { on: new Set(backup.onComputer), waiting: new Set(backup.waiting) } : undefined), [backup]);
  const [bannerHidden, setBannerHidden] = useState(false);
  const access = usePhotoAccess().data?.access;
  const { root, files, listing, reading } = library;
  const places = listing?.places ?? NO_PLACES;
  const [shown, setShown] = useState(LIBRARY_PAGE);
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  // The files a sheet is about: those chosen, or the one open in the viewer.
  const [acting, setActing] = useState<{ files: OrganizeFileDto[]; sheet: 'put' | 'new' | 'delete' }>();
  const gate = usePlanGate();
  const [viewing, setViewing] = useState<string>();
  const [searching, setSearching] = useState(false);
  // Search asked for from outside opens it, adjusted while drawing rather than after.
  const [searchSeen, setSearchSeen] = useState(searchAsked);
  if (searchAsked !== searchSeen) {
    setSearchSeen(searchAsked);
    setSearching(true);
  }
  const trips = useTrips(!selecting).data;
  const fixes = useFixes(!selecting).data;
  const vault = useVault(!selecting).data;
  const [busy, setBusy] = useState(false);

  const counts = useMemo(() => (root ? filterCounts(files, root, platform, backedUp) : undefined), [files, root, platform, backedUp]);
  const filtered = useMemo(() => (root ? files.filter(f => shows(filter, f, root, platform, backedUp)) : []), [files, filter, root, platform, backedUp]);
  const months = useMemo(() => byMonth(filtered.slice(0, shown)), [filtered, shown]);
  const albums = useMemo(() => {
    if (!root) return [];
    const there = albumsOf(files, root, words);
    return [...there, ...made.filter(m => !there.some(a => a.path === m.path))];
  }, [files, root, words, made]);
  const chosen = files.filter(f => picked.has(f.id));
  const viewed = files.find(f => f.id === viewing);

  const stopSelecting = () => { onSelecting(false); setPicked(new Set()); };
  useBackLayer(selecting, stopSelecting);

  // More files are drawn as the end of those drawn comes into view: a library of thousands is not drawn at once.
  const end = useRef<HTMLDivElement>(null);
  const more = shown < filtered.length;
  useEffect(() => {
    if (!more) return undefined;
    const watch = new IntersectionObserver(seen => { if (seen.some(e => e.isIntersecting)) setShown(s => s + LIBRARY_PAGE); }, { rootMargin: '800px' });
    watch.observe(end.current!);
    return () => watch.disconnect();
  }, [more]);

  function tap(id: string) {
    if (!selecting) { setViewing(id); return; }
    setPicked(before => { const next = new Set(before); if (!next.delete(id)) next.add(id); return next; });
  }

  // When a change is done, the files it was about are let go of, and the sheet and viewer put away.
  function finish() {
    setActing(undefined);
    setViewing(undefined);
    stopSelecting();
  }

  async function undo(batchId: string) {
    try {
      const result = await undoChange(batchId);
      if (result.restored > 0) notify(`Moved ${items(result.restored)} back.`, 'ok');
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(said(e), 'error');
    }
  }

  async function change(run: () => Promise<{ batchId: string; done: number; skipped: number; problems: string[] }>, message: (done: number) => string) {
    setBusy(true);
    try {
      const result = await run();
      finish();
      if (result.done > 0) notify(message(result.done), 'ok', result.batchId ? { label: 'Undo', run: () => void undo(result.batchId) } : undefined);
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} left where ${result.skipped === 1 ? 'it is' : 'they are'}. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  // Each file arrives under its own name, or, where that is taken, the name with a number: nothing is replaced.
  const put = (going: OrganizeFileDto[], destination: OrganizeDestination, label: string) => change(async () => {
    const plan = await planOrganize(destination, going.map(f => ({ id: f.id })));
    const { names } = resolveNames(going, plan, {}, {});
    return applyOrganize({ ...destination, mode: 'move', companions: true, label, files: Object.entries(names).map(([id, name]) => ({ id, name })) });
  }, done => `Moved ${items(done)} to ${label}.`);

  // To the phone's own bin, which has no Undo of PhotoSense's (Jamie, 2026-11-20).
  const remove = (going: OrganizeFileDto[]) => change(() => removeOrganize(root!, going.map(f => f.id)), done => deletedSaid(done, words.bin));

  async function chooseMore() {
    try {
      const after = await chooseMorePhotos();
      notify(after.access === 'everything' ? 'PhotoSense can now see all your photos.' : 'PhotoSense can see the photos you selected.', 'ok');
    } catch (e) {
      notify(`The photo picker could not be shown: ${said(e)}`, 'error');
    }
  }

  const all = counts?.all ?? 0;
  const title = !selecting ? 'Library' : picked.size === 0 ? 'Select items' : `${picked.size.toLocaleString()} selected`;
  const subtitle = selecting ? undefined
    : reading ? (reading.total > 0 ? `Reading… ${reading.done.toLocaleString()} of ${reading.total.toLocaleString()}` : 'Reading your photos…')
      : access === 'selected' ? `The ${all.toLocaleString()} photos you chose`
        : `${all.toLocaleString()} ${all === 1 ? 'photo or video' : 'photos and videos'}`;
  // Nothing is chosen while the library is still being read: what is chosen may not have its place yet.
  const textAction = selecting ? (ios ? { label: 'Cancel', run: stopSelecting } : undefined) : reading ? undefined : { label: 'Select', run: () => onSelecting(true) };
  // A deletion asks the plan first: when the free plan has fewer left today, the largest of those chosen go.
  const act = (sheet: 'put' | 'delete', going: OrganizeFileDto[]) => (sheet === 'put'
    ? setActing({ files: going, sheet })
    : gate.remove(going.length, 'items', allowed => setActing({ files: largest(going, allowed), sheet })));

  const [own, setOwn] = useState<Collection>();
  const collection = onCollection ? given : own;
  const openCollection = onCollection ?? setOwn;
  const close = () => openCollection(undefined);
  // A collection: over every tab on a phone; on a tablet where the shell keeps room for it beside the sidebar or rail.
  const collections = <>
    {collection === 'fixes' && <FixesScreen notify={notify} onClose={close} />}
    {collection === 'hidden' && <HiddenScreen files={files} notify={notify} onClose={close} />}
    {collection === 'trips' && <TripsScreen files={files} frozen={viewed !== undefined} notify={notify} onOpen={f => setViewing(f.id)} onClose={close} />}
  </>;
  const banner = backup?.computer && safe && safe.checked.count > 0 && !bannerHidden && !selecting && (
    <section aria-label={`On ${backup.computer}`} className={`flex gap-3 rounded-[20px] bg-cell p-4 ${tablet ? 'min-w-0 flex-[1.4]' : 'mx-4 mb-3'}`}>
      <span aria-hidden className="grid h-9 w-9 shrink-0 place-items-center rounded-[10px] bg-acc-soft text-on-acc-soft"><LaptopIcon /></span>
      <div className="flex min-w-0 flex-1 flex-col gap-2">
        <div>
          <p className="text-[15px] font-semibold">{items(safe.checked.count)} {safe.checked.count === 1 ? 'is' : 'are'} on {backup.computer}</p>
          <p className="text-[13.5px] leading-[1.4] text-t2">Remove them from this {deviceWord(platform, tablet)} to free up to {gigabytes(safe.checked.bytes)}.</p>
        </div>
        <button type="button" onClick={onReview} className="h-9 self-start rounded-full bg-acc px-4 text-[14.5px] font-semibold text-on-acc">Review</button>
      </div>
      <button type="button" aria-label="Hide" onClick={() => setBannerHidden(true)} className="grid h-7 w-7 shrink-0 place-items-center rounded-full text-t2"><CloseIcon size={16} /></button>
    </section>
  );
  const yearCard = year?.year != null && !selecting && onYear && <YearCard review={year} onWatch={onYear} inRow={tablet} />;

  return (
    <Screen label="Library">
      <RootHeader title={title} subtitle={subtitle} textAction={textAction} onStop={selecting && !ios ? stopSelecting : undefined} noGear={selecting}
        side={tablet && !selecting ? <SearchField side onOpen={() => setSearching(true)} /> : undefined} />
      {!selecting && !tablet && <SearchField onOpen={() => setSearching(true)} />}
      {!selecting && !tablet && (
        <div className="grid grid-cols-3 gap-3 px-4 pb-3">
          <TripsTile trips={trips} onOpen={() => openCollection('trips')} />
          <FixesTile fixes={fixes} onOpen={() => openCollection('fixes')} />
          <HiddenTile vault={vault} free={gate.plan?.tier === 'free'} onOpen={() => openCollection('hidden')} />
        </div>
      )}
      {/* T1's row on a tablet: what is on the computer, Trips, and Year in review while it is due. */}
      {!selecting && tablet && (
        <div className="flex gap-3 px-4 pb-4">
          {banner}
          <TripsCard trips={trips} onOpen={() => openCollection('trips')} />
          {yearCard}
        </div>
      )}
      {access === 'selected' && (
        <div role="note" className="mx-4 mb-3 flex items-center gap-3 rounded-cell bg-acc-soft px-4 py-3.5 text-on-acc-soft">
          <p className="min-w-0 flex-1 text-[14.5px] leading-[1.4]">PhotoSense can see only the {all.toLocaleString()} photos you chose. Copies among the rest are not found.</p>
          <button type="button" onClick={() => void chooseMore()} className="h-9 shrink-0 rounded-full bg-acc px-4 text-[14.5px] font-semibold text-on-acc">Choose more</button>
        </div>
      )}
      {!tablet && banner}
      {!tablet && yearCard}
      <div role="group" aria-label="Show" className="flex shrink-0 gap-2 overflow-x-auto px-4 pb-1">
        {libraryFilters(backup?.computer).map(({ id, label }) => (
          <Chip key={id} label={label} count={counts?.[id].toLocaleString()} on={filter === id} onClick={() => { onFilter(id); setShown(LIBRARY_PAGE); }} />
        ))}
      </div>
      {library.error !== undefined && <p role="alert" className="px-4 py-10 text-center text-[15px] text-t2">PhotoSense could not read your photos: {said(library.error)}</p>}
      {library.error === undefined && !reading && filtered.length === 0 && (
        <p className="px-4 py-16 text-center text-[15px] text-t2">{filter === 'all' ? 'No photos or videos yet.' : 'Nothing here with this filter.'}</p>
      )}
      {months.map(m => (
        <section key={m.month} aria-label={monthName(m.month)}>
          <MonthHeader month={monthName(m.month)} count={items(m.files.length)} />
          <PhotoGrid selecting={selecting} selected={picked} onTap={tap}
            items={m.files.map(f => ({ id: f.id, name: f.name, thumbnail: f.isVideo ? undefined : organizeThumbnailUrl(f.id), duration: f.durationSeconds == null ? undefined : formatDuration(f.durationSeconds), backedUp: backedUp?.on.has(f.id) }))} />
        </section>
      ))}
      <div ref={end} />
      {backup?.lastChecked && !more && filtered.length > 0 && (
        <p className="flex items-center gap-2 px-4 py-4 text-[13px] text-t2">
          <span aria-hidden className="grid h-[18px] w-[18px] place-items-center rounded-full bg-fill2 text-t1"><CheckIcon size={11} /></span>
          On {backup.computer}, checked {checkedAt(backup.lastChecked, new Date())}
        </p>
      )}

      {selecting && <SelectionBar count={chosen.length} onSend={() => { onSend(chosen.map(f => f.id)); stopSelecting(); }} onPut={() => act('put', chosen)} onDelete={() => act('delete', chosen)} />}
      {/* Drawn over the whole page, tabs and all: inside the tab they would be under the shell's bars. */}
      {placeSlot && createPortal(<PlaceContext.Provider value>{collections}</PlaceContext.Provider>, placeSlot)}
      {createPortal(<>
      {searching && <SearchScreen files={files} notify={notify} onOpen={f => setViewing(f.id)} onClose={() => setSearching(false)} />}
      {!tablet && collections}
      {viewed && (
        <Viewer file={viewed} places={places} root={root!} removeLocation={removeLocation} notify={notify} onClose={() => setViewing(undefined)}
          onSend={() => { onSend([viewed.id]); setViewing(undefined); }} onPut={() => act('put', [viewed])} onDelete={() => act('delete', [viewed])} />
      )}
      {acting?.sheet === 'put' && (
        <PutSheet count={acting.files.length} albums={albums} busy={busy} onClose={() => setActing(undefined)}
          onAlbum={album => void put(acting.files, album.base ? { basePath: album.base, folderName: leaf(album.path), direct: false } : { basePath: album.path, folderName: '', direct: true }, leaf(album.path))}
          onNew={() => setActing({ ...acting, sheet: 'new' })} />
      )}
      {acting?.sheet === 'new' && (
        <NewAlbumSheet base={newAlbumBase(root!, platform)} albums={albums} busy={busy} onClose={() => setActing(undefined)}
          onCreate={(base, name) => void put(acting.files, { basePath: base, folderName: name, direct: false }, name)} />
      )}
      {acting?.sheet === 'delete' && (() => {
        const q = deleteQuestion(acting.files.map(f => f.name), words);
        const limit = limitLine(gate.plan);
        return <ConfirmSheet title={q.title} lines={limit ? [...q.lines, limit] : q.lines} confirmLabel={q.button} busy={busy} onConfirm={() => void remove(acting.files)} onCancel={() => setActing(undefined)} />;
      })()}
      </>, document.body)}
    </Screen>
  );
}

interface PutSheetProps {
  readonly count: number;
  readonly albums: readonly Album[];
  readonly busy: boolean;
  onAlbum(album: Album): void;
  onNew(): void;
  onClose(): void;
}

/** Which album or folder the files go in: those there are, or a new one. */
function PutSheet({ count, albums, busy, onAlbum, onNew, onClose }: PutSheetProps) {
  const { ios, words } = usePhone();
  return (
    <Sheet title={ios ? `Add ${count.toLocaleString()} to an album` : `Move ${count.toLocaleString()} to a folder`} onClose={onClose}>
      <div className="px-4 pb-3 pt-2">
        <ListGroup inSheet>
          {albums.map(a => (
            <ListRow key={a.path} disabled={busy} onClick={() => onAlbum(a)}>
              <span className="flex items-center gap-3"><span className="text-t2"><FolderIcon size={20} /></span>{a.label}</span>
            </ListRow>
          ))}
          <ListRow disabled={busy} onClick={onNew}><span className="font-semibold text-acc">+ New {words.album}…</span></ListRow>
        </ListGroup>
      </div>
    </Sheet>
  );
}

interface NewAlbumProps {
  /** Where the new album or folder is made. */
  readonly base: string;
  readonly albums: readonly Album[];
  readonly busy: boolean;
  onCreate(base: string, name: string): void;
  onClose(): void;
}

/** A new album or folder: its name, and Create, which moves the files into it, or in Organize makes it ready for them. */
export function NewAlbumSheet({ base, albums, busy, onCreate, onClose }: NewAlbumProps) {
  const { ios, words } = usePhone();
  const [typed, setTyped] = useState('');
  const [problem, setProblem] = useState<string>();
  // The names of the albums already made where this one would go.
  const here = albums.filter(a => a.path.startsWith(base)).map(a => leaf(a.path));
  function create() {
    const checked = checkFolderName(typed, here);
    // Organize's words are a computer's; an iPhone has albums.
    if (checked.error !== undefined) { setProblem(ios ? checked.error.replace('a folder', 'an album').replace('new folder', 'new album').replace('Folder names', 'Album names') : checked.error); return; }
    onCreate(base, checked.name);
  }
  return (
    <Sheet title={`New ${words.album}`} onClose={onClose}>
      <form className="flex flex-col gap-3.5 px-4 pb-3 pt-2" onSubmit={e => { e.preventDefault(); create(); }}>
        <input value={typed} onChange={e => { setTyped(e.target.value); setProblem(undefined); }} aria-label={`${words.Album} name`} placeholder={`${words.Album} name`}
          aria-invalid={problem !== undefined} className="h-[54px] rounded-[14px] border border-sep bg-input px-4 text-[17px] text-t1 outline-none placeholder:text-t3" />
        {problem && <p role="alert" className="px-1 text-[14px] text-danger-t">{problem}</p>}
        <PhoneButton disabled={busy || !typed.trim()} onClick={create}>Create</PhoneButton>
      </form>
    </Sheet>
  );
}
