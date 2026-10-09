import React, { useEffect, useMemo, useRef, useState } from 'react';
import { applyOrganize, openOrganizeFile, removeOrganize, undoOrganize, useOrganizeBatches, useOrganizeFiles } from '../../lib/apiClient';
import { formatBytes, leaf } from '../../lib/format';
import {
  GALLERY_PAGE, checkFolderName, defaultSettings, fileTag, mapLayout, noChoices, pathOf, relativeFolder, searchText, suggest, totalBytes, unplaced,
  type FileRef, type OrganizeChoices, type OrganizeSettings, type OrganizedMark,
} from '../../lib/organize';
import { loadOrganizeRoot, saveOrganizeRoot } from '../../lib/lastScan';
import { useOrganizeReading } from '../../lib/useOrganizeReading';
import type { OrganizeApplyRequest, OrganizeFileDto } from '../../types';
import { ConfirmDialog } from '../ConfirmDialog';
import { FolderPicker } from '../FolderPicker';
import type { ToastAction, ToastKind } from '../Toaster';
import { FileGallery, type FileFilter } from './FileGallery';
import { MovePreview } from './MovePreview';
import { ReadingOverlay } from './ReadingOverlay';
import { SelectionBar } from './SelectionBar';
import { SuggestionsPanel, type Focus, type OwnFolder } from './SuggestionsPanel';
import { countFiles } from './shared';

interface Props {
  /** The folder to begin with when none was chosen here before: the one last scanned. */
  readonly startRoot?: string;
  notify(message: string, kind?: ToastKind, action?: ToastAction): void;
}

type Target = { kind: 'suggested' | 'own'; key: string };
/** What an undo has to put back in this page, beyond what the service puts back on disk. */
interface Undoable { paths: string[]; assigned: Record<string, string>; added: Record<string, string>; removed: string[]; }
/** The folder inside the root that holds what was deleted, as it does what Clean up removes. */
const REMOVED_FOLDER = '_PhotoSense_Removed';

const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));
const without = <T,>(map: Readonly<Record<string, T>>, keys: readonly string[]) => Object.fromEntries(Object.entries(map).filter(([k]) => !keys.includes(k)));
const only = <T,>(map: Readonly<Record<string, T>>, keys: readonly string[]) => Object.fromEntries(keys.filter(k => map[k]).map(k => [k, map[k]]));
const NO_FILES: OrganizeFileDto[] = [];

const Named = ({ children }: { readonly children: string }) => <span className="font-semibold text-t1 [overflow-wrap:anywhere]">{children}</span>;
const Where = ({ children }: { readonly children: string }) => <span className="font-mono text-[13.5px] [overflow-wrap:anywhere]">{children}</span>;

/** Organize: every file of a folder, the folders it could be sorted into, and the way to look before anything moves. */
export function OrganizeView({ startRoot, notify }: Props) {
  const [chosen, setChosen] = useState<string>();
  // What this browser remembers is read once the page is in the browser.
  useEffect(() => setChosen(c => c ?? (loadOrganizeRoot() || undefined)), []);
  const root = chosen ?? startRoot;
  const listing = useOrganizeFiles(root);
  const batches = useOrganizeBatches();

  const [settings, setSettings] = useState<OrganizeSettings>(defaultSettings);
  const [showMap, setShowMap] = useState(true);
  const [query, setQuery] = useState('');
  const [filter, setFilter] = useState<FileFilter>('all');
  const [page, setPage] = useState(1);
  const [picked, setPicked] = useState<ReadonlySet<string>>(new Set());
  const lastPick = useRef(-1);
  const [folders, setFolders] = useState<{ id: string; name: string }[]>([]);
  const folderSeq = useRef(0);
  const [choices, setChoices] = useState<OrganizeChoices>(noChoices);
  const [focus, setFocus] = useState<Focus>();
  const [naming, setNaming] = useState<'bar' | 'panel'>();
  const [newName, setNewName] = useState('');
  const [dragging, setDragging] = useState<string[]>([]);
  const [preview, setPreview] = useState<Target>();
  const [mode, setMode] = useState<'move' | 'copy'>('move');
  const [companions, setCompanions] = useState(true);
  const [busy, setBusy] = useState(false);
  const [browsing, setBrowsing] = useState(false);
  // Files deleted here, until the list that no longer has them arrives; and the ones a deletion is being asked about.
  const [gone, setGone] = useState<ReadonlySet<string>>(new Set());
  // For one of two files that share a name, the question also says which of them stays.
  const [deleting, setDeleting] = useState<{ files: FileRef[]; stays?: FileRef }>();
  const undoable = useRef(new Map<string, Undoable>());
  // The folder that was just chosen, until its files have been read and that has been said.
  const announce = useRef<string>();

  const reading = root !== undefined && !listing.data && !listing.error;
  const soFar = useOrganizeReading(root, reading);
  // Until the whole list has come, what has been read so far stands in for it, in the order it was read.
  const listed = listing.data ?? soFar?.listing;
  const data = useMemo(() => (listed && gone.size > 0 ? { ...listed, files: listed.files.filter(f => !gone.has(f.id)) } : listed), [listed, gone]);
  const files = data?.files ?? NO_FILES, places = data?.places ?? [];

  useEffect(() => {
    if (listing.error) notify(messageOf(listing.error), 'error');
  }, [listing.error, notify]);
  const whole = listing.data;
  // The whole list is in another order than the files came in: a range is counted from a click in the list as it now is.
  useEffect(() => { lastPick.current = -1; }, [whole]);
  useEffect(() => {
    if (!whole || announce.current === undefined) return;
    announce.current = undefined;
    notify(`Read dates and places for ${countFiles(whole.files.length)} in ${leaf(whole.root)}. No scan was needed.`, 'ok');
  }, [whole, notify]);

  const suggestions = useMemo(() => (data ? suggest(data, settings, choices) : []), [data, settings, choices]);
  const byKey = useMemo(() => new Map(suggestions.map(s => [s.key, s])), [suggestions]);
  const own: OwnFolder[] = useMemo(() => folders.map(f => ({ ...f, files: files.filter(x => choices.assigned[x.id] === f.id) })), [folders, files, choices.assigned]);
  const folderNames = useMemo(() => Object.fromEntries(folders.map(f => [f.id, f.name])), [folders]);
  const noPlace = useMemo(() => (data ? unplaced(data, choices, suggestions) : []), [data, choices, suggestions]);
  const searchable = useMemo(() => new Map(files.map(f => [f.id, searchText(f, places)])), [files, places]);
  const map = useMemo(() => (settings.group === 'place' && showMap ? mapLayout(suggestions, places, settings) : undefined), [suggestions, places, settings, showMap]);

  // The files of the folder picked in the panel alone, when one is.
  const focused = focus?.kind === 'suggested' ? byKey.get(focus.key) : undefined, focusedOwn = focus?.kind === 'own' ? own.find(f => f.id === focus.key) : undefined;
  const focusFiles = focused?.files ?? focusedOwn?.files ?? (focus?.kind === 'unplaced' ? noPlace : undefined);
  const focusLabel = focused ? `Suggested: ${focused.name}` : focusedOwn ? `Your folder: ${focusedOwn.name}` : focus?.kind === 'unplaced' ? 'No location' : undefined;
  const q = query.trim().toLowerCase();
  const shown = useMemo(() => (focusFiles ?? files)
    .filter(f => (filter === 'todo' ? !choices.organized[pathOf(f)] : filter === 'noloc' ? f.place == null : true))
    .filter(f => !q || searchable.get(f.id)!.includes(q)), [focusFiles, files, filter, choices.organized, q, searchable]);
  const pages = Math.max(1, Math.ceil(shown.length / GALLERY_PAGE));
  const counts = useMemo(() => ({ all: files.length, todo: files.filter(f => !choices.organized[pathOf(f)]).length, noloc: files.filter(f => f.place == null).length }), [files, choices.organized]);

  const clearPicks = () => { setPicked(new Set()); lastPick.current = -1; };
  // Everything that keeps some of the files from showing, put away at once.
  const showEverything = () => { setFocus(undefined); setQuery(''); setFilter('all'); setPage(1); };
  const turnTo = (focusNow?: Focus) => { setFocus(focusNow); setPage(1); };

  function chooseRoot(path: string) {
    setBrowsing(false);
    setChosen(path);
    saveOrganizeRoot(path);
    announce.current = path;
    // Everything arranged was arranged for the other folder's files.
    setFolders([]); setChoices(noChoices); setFocus(undefined); setPreview(undefined); setPage(1); setNaming(undefined); clearPicks(); setGone(new Set());
    undoable.current.clear();
  }

  function pick(file: OrganizeFileDto, index: number, range: boolean) {
    // Read now: by the time the selection is worked out, this click has become the last one.
    const last = lastPick.current;
    setPicked(before => {
      const next = new Set(before);
      if (range && last >= 0) for (let i = Math.min(last, index); i <= Math.max(last, index); i++) next.add(shown[i].id);
      else if (!next.delete(file.id)) next.add(file.id);
      return next;
    });
    lastPick.current = index;
  }

  function addToOwn(folderId: string, ids: readonly string[]) {
    setChoices(c => ({ ...c, assigned: { ...c.assigned, ...Object.fromEntries(ids.map(id => [id, folderId])) } }));
    clearPicks();
    notify(`Added ${countFiles(ids.length)} to ${folderNames[folderId]}. Preview it when you are ready.`, 'ok');
  }
  function addToSuggested(key: string, ids: readonly string[]) {
    // A file goes to one folder: put with a suggestion, it leaves any folder of the person's own.
    setChoices(c => ({ ...c, added: { ...c.added, ...Object.fromEntries(ids.map(id => [id, key])) }, assigned: without(c.assigned, ids) }));
    clearPicks();
    notify(`Added ${countFiles(ids.length)} to the suggested folder ${byKey.get(key)!.name}`, 'ok');
  }
  function drop(target: Target | 'new') {
    const ids = dragging;
    setDragging([]);
    if (target === 'new') { setPicked(new Set(ids)); setNaming('bar'); }
    else if (target.kind === 'own') addToOwn(target.key, ids);
    else addToSuggested(target.key, ids);
  }

  function createFolder() {
    const checked = checkFolderName(newName, folders.map(f => f.name));
    if (checked.error !== undefined) return notify(checked.error, 'error');
    const id = `own-${++folderSeq.current}`, ids = [...picked];
    setFolders(f => [...f, { id, name: checked.name }]);
    setChoices(c => ({ ...c, assigned: { ...c.assigned, ...Object.fromEntries(ids.map(x => [x, id])) } }));
    setNaming(undefined); setNewName(''); clearPicks();
    notify(ids.length > 0 ? `Made ${checked.name} with ${countFiles(ids.length)}. Preview it when you are ready.` : `Made ${checked.name}. Select or drag files onto it.`, 'ok');
  }
  function removeFolder(id: string) {
    setFolders(f => f.filter(x => x.id !== id));
    setChoices(c => ({ ...c, assigned: Object.fromEntries(Object.entries(c.assigned).filter(([, folder]) => folder !== id)) }));
    if (focus?.kind === 'own' && focus.key === id) setFocus(undefined);
    notify(`Removed ${folderNames[id]}. Its files go back into the suggestions.`, 'ok');
  }

  function changeSettings(change: Partial<OrganizeSettings>) {
    setSettings(s => ({ ...s, ...change }));
    // Another way of naming the folders starts their names afresh; another way of grouping makes other folders.
    if (change.nameFormat) setChoices(c => ({ ...c, renames: {} }));
    if (!('placeThen' in change) && !('dateThen' in change) && !change.nameFormat) turnTo(undefined);
  }

  // What is being previewed, as it is now: a suggestion's files change as its name does.
  const previewed = preview?.kind === 'suggested' ? byKey.get(preview.key) : undefined, previewedOwn = preview?.kind === 'own' ? own.find(f => f.id === preview.key) : undefined;
  // A folder whose last file was deleted from the preview has nothing left to preview.
  const nothingPreviewed = preview !== undefined && !(previewed ?? previewedOwn)?.files.length;
  useEffect(() => { if (nothingPreviewed) setPreview(undefined); }, [nothingPreviewed]);
  function renamePreviewed(name: string) {
    if (preview!.kind === 'suggested') setChoices(c => ({ ...c, renames: { ...c.renames, [preview!.key]: name } }));
    else setFolders(f => f.map(x => (x.id === preview!.key ? { ...x, name } : x)));
  }

  async function move(request: OrganizeApplyRequest, going: OrganizeFileDto[]) {
    setBusy(true);
    try {
      const result = await applyOrganize(request), copy = request.mode === 'copy';
      const at = new Map(result.items.map(i => [i.id, i.path])), gone = going.filter(f => at.has(f.id)), ids = gone.map(f => f.id);
      // A moved file is marked where it now is; a copied one where it still is.
      const marks = Object.fromEntries(gone.map(f => [copy ? pathOf(f) : at.get(f.id)!, { label: request.label, copied: copy } satisfies OrganizedMark]));
      if (result.batchId) {
        undoable.current.set(result.batchId, {
          paths: Object.keys(marks), assigned: only(choices.assigned, ids), added: only(choices.added, ids), removed: [],
        });
      }
      setChoices(c => ({ ...c, organized: { ...c.organized, ...marks }, assigned: without(c.assigned, ids), added: without(c.added, ids) }));
      setPicked(before => new Set([...before].filter(id => !ids.includes(id))));
      setPreview(undefined);
      turnTo(undefined);
      const batchId = result.batchId;
      if (result.done > 0) {
        notify(`${copy ? 'Copied' : 'Moved'} ${countFiles(result.done)} (${formatBytes(result.bytes)}) to ${request.label}`
          + `${result.companions > 0 ? `, with ${result.companions.toLocaleString()} Live Photo and edit files` : ''}`
          + `${result.renamed > 0 ? `. ${result.renamed.toLocaleString()} renamed so nothing was replaced` : ''}`, 'ok', batchId ? { label: 'Undo', run: () => void undo(batchId, request.label, copy) } : undefined);
      }
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} left where ${result.skipped === 1 ? 'it is' : 'they are'}. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(messageOf(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  async function remove(going: FileRef[]) {
    setBusy(true);
    try {
      const result = await removeOrganize(root!, going.map(f => f.id));
      const ids = result.items.map(i => i.id), went = new Set(ids);
      let undoIt: ToastAction | undefined;
      if (result.batchId) {
        const batchId = result.batchId;
        undoable.current.set(batchId, { paths: [], assigned: only(choices.assigned, ids), added: only(choices.added, ids), removed: ids });
        undoIt = { label: 'Undo', run: () => void undo(batchId, root!, false) };
      }
      // The files leave the page at once; the list is asked for again behind them.
      setGone(before => new Set([...before, ...ids]));
      setChoices(c => ({ ...c, assigned: without(c.assigned, ids), added: without(c.added, ids) }));
      setPicked(before => new Set([...before].filter(id => !went.has(id))));
      lastPick.current = -1;
      setDeleting(undefined);
      if (result.done > 0) {
        notify(`Moved ${countFiles(result.done)} (${formatBytes(result.bytes)}) to ${REMOVED_FOLDER} in ${leaf(root!)}`
          + `${result.companions > 0 ? `, with ${result.companions.toLocaleString()} Live Photo and edit files` : ''}`, 'ok', undoIt);
      }
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} left where ${result.skipped === 1 ? 'it is' : 'they are'}. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(messageOf(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  async function undo(batchId: string, label: string, copy: boolean) {
    setBusy(true);
    try {
      const result = await undoOrganize(batchId), was = undoable.current.get(batchId);
      undoable.current.delete(batchId);
      // The files are back where they were, and so is what had been arranged for them.
      if (was) {
        setChoices(c => ({ ...c, organized: without(c.organized, was.paths), assigned: { ...c.assigned, ...was.assigned }, added: { ...c.added, ...was.added } }));
        setGone(before => new Set([...before].filter(id => !was.removed.includes(id))));
      }
      if (result.restored > 0) notify(copy ? `Removed the ${result.restored.toLocaleString()} ${result.restored === 1 ? 'copy' : 'copies'} from ${label}` : `Moved ${countFiles(result.restored)} back to where ${result.restored === 1 ? 'it was' : 'they were'}`, 'ok');
      if (result.skipped > 0) notify(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
    } catch (e) {
      notify(messageOf(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  const open = (file: OrganizeFileDto) => { openOrganizeFile(file.id).catch(e => notify(messageOf(e), 'error')); };
  // A folder is previewed, and moved, whole: not while more of its files may still turn up.
  const showPreview = (target: Target) => (reading ? notify(`Still reading ${leaf(root!)}. Preview a folder once every file is in.`, 'info') : setPreview(target));
  // Nor are files deleted while the list they are in is still being made.
  const askToDelete = () => (reading ? notify(`Still reading ${leaf(root!)}. Delete files once every file is in.`, 'info') : setDeleting({ files: files.filter(f => picked.has(f.id)) }));
  const suggestedNames = byKey;

  return (
    <>
      <div className="flex min-h-[72px] shrink-0 items-center gap-4 border-b border-line px-6 py-3">
        <div className="flex h-10 shrink-0 items-center gap-3 rounded-full border border-line pl-4 pr-[5px] text-[13px] text-t2">
          <span className="whitespace-nowrap" aria-label="Folder being organized">
            {root
              ? <><span className="font-medium text-t1" title={root}>{leaf(root)}</span>{data && <> · {reading ? `${files.length.toLocaleString()} of ${soFar!.total.toLocaleString()} files so far` : `${countFiles(files.length)} · ${data.fromScan ? 'from your last scan' : 'read just now'}`}</>}</>
              : 'No folder chosen yet'}
          </span>
          <button type="button" onClick={() => setBrowsing(true)} className="pill-quiet h-[30px] shrink-0 px-3.5 text-[13px] font-medium">Choose root folder</button>
        </div>
        <label className="flex h-[42px] w-[clamp(200px,20vw,340px)] shrink-0 items-center gap-2.5 rounded-full border border-line bg-s1 px-3.5">
          <span aria-hidden className="h-[11px] w-[11px] shrink-0 rounded-full border-[1.5px] border-t3" />
          <input value={query} onChange={e => { setQuery(e.target.value); setPage(1); }} placeholder="Search by name, place or month" aria-label="Search by name, place or month"
            className="min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-t3" />
        </label>
        <p className="min-w-0 flex-1 text-right text-[13px] text-t3">Click to select, Shift-click for a range, drag onto your folders. Nothing moves until you preview and confirm.</p>
      </div>

      {root === undefined ? (
        <div className="flex flex-1 flex-col items-center justify-center gap-4 text-center">
          <p className="text-[18px] font-semibold">Choose a folder to organize</p>
          <p className="max-w-[520px] text-[14px] text-t2">PhotoSense reads when and where each picture and video was taken and suggests folders for them. Nothing moves until you preview a folder and confirm.</p>
          <button type="button" onClick={() => setBrowsing(true)} className="pill-brand h-11 px-6 text-[15px]">Choose a folder</button>
        </div>
      ) : (
        <div className="relative grid min-h-0 flex-1 grid-cols-[minmax(0,1fr)_clamp(400px,34vw,540px)]">
          {reading && !data && <ReadingOverlay folder={leaf(root)} progress={soFar} />}
          <section aria-label="All files" className="flex min-h-0 min-w-0 flex-col">
            {reading && data && <ReadingOverlay compact folder={leaf(root)} progress={soFar} />}
            <FileGallery files={shown} places={places} page={Math.min(page, pages)} filter={filter} counts={counts} focus={focusLabel} narrowed={focusLabel !== undefined || q !== '' || filter !== 'all'} picked={picked}
              reading={reading && !data} empty={listing.error ? 'This folder could not be read.' : q ? `No files match “${query.trim()}”` : 'No files here'}
              tagOf={f => fileTag(f, choices, folderNames, suggestedNames)} onPage={setPage} onFilter={f => { setFilter(f); setPage(1); }} onClearFocus={() => turnTo(undefined)} onClearAll={showEverything}
              onPick={pick} onOpen={open} onDragStart={f => setDragging(picked.has(f.id) ? [...picked] : [f.id])} onDragEnd={() => setDragging([])} />
            {picked.size > 0 && (
              <SelectionBar count={picked.size} shown={shown.length} hint={folders.length > 0 ? 'or add them to one of your folders' : ''} naming={naming === 'bar'} name={newName} onName={setNewName}
                onSelectAll={() => setPicked(new Set(shown.map(f => f.id)))} onStartNaming={() => setNaming('bar')} onCancelNaming={() => { setNaming(undefined); setNewName(''); }}
                onCreate={createFolder} onDelete={askToDelete} onClear={clearPicks} />
            )}
          </section>
          <SuggestionsPanel settings={settings} showMap={showMap} suggestions={suggestions} map={map} focus={focus} unplaced={noPlace.length} folders={own} picked={picked.size}
            dragging={dragging.length} naming={naming === 'panel'} name={newName} batches={batches.data ?? []} busy={busy} reading={reading} rootName={leaf(root)}
            onSettings={changeSettings} onShowMap={setShowMap} onFocus={turnTo} onPreview={showPreview} onDrop={drop} onGroupByDate={() => changeSettings({ group: 'date' })}
            onName={setNewName} onStartNaming={() => setNaming('panel')} onCancelNaming={() => { setNaming(undefined); setNewName(''); }} onCreate={createFolder}
            onAddPicked={id => addToOwn(id, [...picked])} onRemoveFolder={removeFolder} onUndo={b => void undo(b.id, b.label, b.mode === 'copy')} />
        </div>
      )}

      {root !== undefined && (previewed || previewedOwn) && (
        <MovePreview key={`${preview!.kind}:${preview!.key}`} kind={previewed ? 'Suggested folder' : 'Your folder'} root={root} files={previewed?.files ?? previewedOwn!.files}
          name={previewed?.name ?? previewedOwn!.name} split={previewed?.split} subfolderOf={previewed?.subfolderOf} merge={previewed?.merge ?? ''} mode={mode} companions={companions} busy={busy} asking={deleting !== undefined}
          placeOf={id => { const f = files.find(x => x.id === id); return f?.place == null ? undefined : `${places[f.place].town}, ${places[f.place].state}`; }}
          onName={renamePreviewed} onMode={setMode} onCompanions={setCompanions} onMove={(request, going) => void move(request, going)}
          onDelete={(file, stays) => setDeleting({ files: [file], stays })} onClose={() => setPreview(undefined)} />
      )}
      {deleting && (
        <ConfirmDialog busy={busy} title={deleting.files.length === 1 ? `Delete ${deleting.files[0].name}?` : `Delete ${countFiles(deleting.files.length)}?`} confirmLabel={`Delete ${countFiles(deleting.files.length)}`}
          note={<>Nothing is erased. The files are moved to <span className="font-mono">{REMOVED_FOLDER}</span> inside {leaf(root!)}, where Clean up puts what it removes. Undo this from Recently moved, or use Delete permanently in the menu to free the space.</>}
          onCancel={() => setDeleting(undefined)} onConfirm={() => void remove(deleting.files)}>
          {deleting.stays ? (
            <>
              {/* Two files with one name: which goes and which stays is told by the folder each is in. */}
              <p><Named>{deleting.files[0].name}</Named> ({formatBytes(deleting.files[0].sizeBytes)}) in <Where>{relativeFolder(root!, deleting.files[0].folder)}</Where> will be removed, along with any Live Photo video and edit files that belong only to it.</p>
              <p><Named>{deleting.stays.name}</Named> in <Where>{relativeFolder(root!, deleting.stays.folder)}</Where> stays.</p>
            </>
          ) : <p>{countFiles(deleting.files.length)} taking {formatBytes(totalBytes(deleting.files))} will be removed from {leaf(root!)}, along with any Live Photo videos and edit files that belong only to them.</p>}
        </ConfirmDialog>
      )}
      {browsing && <FolderPicker title="Choose a folder to organize" startAt={root} onPick={chooseRoot} onCancel={() => setBrowsing(false)} />}
    </>
  );
}
