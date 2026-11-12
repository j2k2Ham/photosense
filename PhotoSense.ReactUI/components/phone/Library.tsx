import React, { useEffect, useMemo, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { applyOrganize, chooseMorePhotos, organizeImageUrl, organizeThumbnailUrl, planOrganize, removeOrganize, undoChange, usePhotoAccess } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { formatBytes, formatDuration, leaf } from '../../lib/format';
import {
  albumsOf, byMonth, deleteQuestion, filterCounts, items, LIBRARY_FILTERS, monthName, newAlbumBase, shows, takenAt, whereIs,
  type Album, type Library, type LibraryFilter,
} from '../../lib/library';
import { checkFolderName, placeInFull, resolveNames } from '../../lib/organize';
import { usePhone } from '../../lib/phone';
import type { OrganizeDestination, OrganizeFileDto, OrganizePlaceDto } from '../../types';
import type { ToastAction, ToastKind } from '../Toaster';
import { Chip, ListGroup, ListRow, PhoneButton } from './Controls';
import { AddToFolderIcon, ChevronBackIcon, FolderIcon, SendIcon, TrashIcon } from './Icons';
import { MonthHeader, PhotoGrid, SelectionBar } from './PhotoGrid';
import { RootHeader, Screen } from './Screens';
import { ConfirmSheet, Sheet } from './Sheet';

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
  /** Takes these pictures and videos, by their ids, to Send, chosen to be sent. */
  onSend(ids: string[]): void;
}

/**
 * Library: every picture and video on the phone, latest first, by month, narrowed by a filter. Tapping one opens it;
 * Select chooses several to send, put in an album or folder, or delete.
 */
export function LibraryScreen({ library, selecting, onSelecting, filter, onFilter, made = NO_ALBUMS, notify, onSend }: Props) {
  const { platform, ios, words } = usePhone();
  const access = usePhotoAccess().data?.access;
  const { root, files, listing, reading } = library;
  const places = listing?.places ?? NO_PLACES;
  const [shown, setShown] = useState(LIBRARY_PAGE);
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  // The files a sheet is about: those chosen, or the one open in the viewer.
  const [acting, setActing] = useState<{ files: OrganizeFileDto[]; sheet: 'put' | 'new' | 'delete' }>();
  const [viewing, setViewing] = useState<string>();
  const [busy, setBusy] = useState(false);

  const counts = useMemo(() => (root ? filterCounts(files, root, platform) : undefined), [files, root, platform]);
  const filtered = useMemo(() => (root ? files.filter(f => shows(filter, f, root, platform)) : []), [files, filter, root, platform]);
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

  const remove = (going: OrganizeFileDto[]) => change(() => removeOrganize(root!, going.map(f => f.id)), done => `${items(done)} moved to ${words.removed}.`);

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
  const act = (sheet: 'put' | 'delete', going: OrganizeFileDto[]) => setActing({ files: going, sheet });

  return (
    <Screen label="Library">
      <RootHeader title={title} subtitle={subtitle} textAction={textAction} onStop={selecting && !ios ? stopSelecting : undefined} noGear={selecting} />
      {access === 'selected' && (
        <div role="note" className="mx-4 mb-3 flex items-center gap-3 rounded-cell bg-acc-soft px-4 py-3.5 text-on-acc-soft">
          <p className="min-w-0 flex-1 text-[14.5px] leading-[1.4]">PhotoSense can see only the {all.toLocaleString()} photos you chose. Copies among the rest are not found.</p>
          <button type="button" onClick={() => void chooseMore()} className="h-9 shrink-0 rounded-full bg-acc px-4 text-[14.5px] font-semibold text-on-acc">Choose more</button>
        </div>
      )}
      <div role="group" aria-label="Show" className="flex shrink-0 gap-2 overflow-x-auto px-4 pb-1">
        {LIBRARY_FILTERS.map(({ id, label }) => (
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
            items={m.files.map(f => ({ id: f.id, name: f.name, thumbnail: f.isVideo ? undefined : organizeThumbnailUrl(f.id), duration: f.durationSeconds == null ? undefined : formatDuration(f.durationSeconds) }))} />
        </section>
      ))}
      <div ref={end} />

      {selecting && <SelectionBar count={chosen.length} onSend={() => { onSend(chosen.map(f => f.id)); stopSelecting(); }} onPut={() => act('put', chosen)} onDelete={() => act('delete', chosen)} />}
      {/* Drawn over the whole page, tabs and all: inside the tab they would be under the shell's bars. */}
      {createPortal(<>
      {viewed && (
        <Viewer file={viewed} places={places} root={root!} onClose={() => setViewing(undefined)}
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
        return <ConfirmSheet title={q.title} lines={q.lines} confirmLabel={q.button} busy={busy} onConfirm={() => void remove(acting.files)} onCancel={() => setActing(undefined)} />;
      })()}
      </>, document.body)}
    </Screen>
  );
}

interface ViewerProps {
  readonly file: OrganizeFileDto;
  readonly places: readonly OrganizePlaceDto[];
  readonly root: string;
  onClose(): void;
  onSend(): void;
  onPut(): void;
  onDelete(): void;
}

/** One picture or video, whole, on black: when and where it was taken, where it is, its size, and what can be done with it. */
function Viewer({ file, places, root, onClose, onSend, onPut, onDelete }: ViewerProps) {
  const { words } = usePhone();
  useBackLayer(true, onClose);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  const place = file.place == null ? 'No location' : placeInFull(places[file.place]);
  const line = [formatBytes(file.sizeBytes), file.width > 0 ? `${file.width} × ${file.height}` : undefined, file.durationSeconds == null ? undefined : formatDuration(file.durationSeconds)];
  const action = 'flex flex-col items-center gap-[3px] py-2 text-[11.5px] font-semibold';
  return (
    <div role="dialog" aria-modal aria-label={file.name} className="fixed inset-0 z-40 flex animate-fade-in flex-col bg-stage font-ui text-white">
      <div className="flex items-center gap-3 px-3 pb-2 pt-[calc(var(--safe-t)+8px)]">
        <button type="button" aria-label="Back" onClick={onClose} className="grid h-11 w-11 shrink-0 place-items-center rounded-full bg-white/[.14]"><ChevronBackIcon /></button>
        <div className="min-w-0 flex-1 text-center">
          <p className="truncate font-code text-[14px] font-medium">{file.name}</p>
          <p className="text-[12.5px] text-white/[.72]">{takenAt(file.date)}</p>
        </div>
        <span className="w-11 shrink-0" />
      </div>
      <div className="flex min-h-0 flex-1 items-center justify-center">
        {file.isVideo
          ? <span className="text-[15px] text-white/[.78]">PhotoSense cannot play videos yet.</span>
          : (
            // The small preview shows at once, under the whole picture, which can take a moment to arrive.
            <span className="relative h-full w-full max-w-[680px]">
              <img src={organizeThumbnailUrl(file.id)} alt="" className="absolute inset-0 h-full w-full object-contain" />
              <img src={organizeImageUrl(file.id)} alt={file.name} className="absolute inset-0 h-full w-full object-contain" />
            </span>
          )}
      </div>
      <div className="mx-auto w-full max-w-[680px] px-5 pt-3 text-[13.5px] text-white/[.78]">
        <p className="text-[15px] text-white">{place}</p>
        <p>{whereIs(file.folder, root, words)}</p>
        <p>{line.filter(Boolean).join(' · ')}</p>
      </div>
      <div className="mx-auto grid w-full max-w-[680px] grid-cols-3 px-2 pb-[calc(var(--safe-b)+8px)] pt-2">
        <button type="button" onClick={onSend} className={action}><SendIcon size={22} />Send</button>
        <button type="button" onClick={onPut} className={action}><AddToFolderIcon size={22} />{words.put}</button>
        <button type="button" onClick={onDelete} className={`${action} text-[#FF6961]`}><TrashIcon size={22} />Delete</button>
      </div>
    </div>
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
