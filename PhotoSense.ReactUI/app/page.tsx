"use client";
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AppMenu } from '../components/AppMenu';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorsPanel } from '../components/ErrorsPanel';
import { GroupList } from '../components/GroupList';
import { AboutWindow } from '../components/AboutWindow';
import { HelpWindow } from '../components/HelpWindow';
import type { Area } from '../components/ModeSwitch';
import { OrganizeView } from '../components/organize/OrganizeView';
import { PhotoWindow } from '../components/PhotoWindow';
import { ReceiveWindow } from '../components/ReceiveWindow';
import { ReviewPanel } from '../components/ReviewPanel';
import { SettingsWindow } from '../components/SettingsWindow';
import { SetupScreen } from '../components/SetupScreen';
import { Toaster, useToasts, type ToastAction } from '../components/Toaster';
import { TopBar } from '../components/TopBar';
import {
  clearResults, connectLogStream, eraseRemoved, fetchGroupTotals, fetchRemoved, openInViewer, prefetchGroups, removeDuplicates, removeGroups, removePhoto, retryNow, setKept, undoChange, useGroups,
  useAbout, useScanProgress, useScanStatus,
} from '../lib/apiClient';
import { formatBytes, linkedFiles } from '../lib/format';
import type { GuidePlace } from '../lib/guide';
import { loadLastScan, loadOrganizeRoot } from '../lib/lastScan';
import { useAppSettings } from '../lib/settings';
import { useTheme } from '../lib/theme';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode, PhotoDto, RemovedFilesDto } from '../types';

// Removed files are moved here, inside the scanned folder, rather than erased.
const REMOVED_FOLDER = '_PhotoSense_Removed';

/** "the duplicate", "the 3 duplicates". */
const these = (count: number, kind: string) => (count === 1 ? `the ${kind}` : `the ${count.toLocaleString()} ${kind}s`);

/** What is waiting for a yes or no. */
type Pending =
  | { scope: 'all'; count: number; bytes: number }
  | { scope: 'group'; group: DuplicateGroupDto }
  | { scope: 'copy'; group: DuplicateGroupDto; member: GroupMemberDto }
  | { scope: 'clear' }
  // The copies of every group that is ticked.
  | { scope: 'ticked' }
  // Erasing what was removed: the warning first, then the question that makes sure.
  | { scope: 'erase'; sure: boolean };

const files = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;
// As many names as a confirmation has room for.
const NAMED = 4;
const groupsOf = (n: number, kind: string) => `${n.toLocaleString()} ${kind} ${n === 1 ? 'group' : 'groups'}`;
const messageOf = (e: unknown) => (e instanceof Error ? e.message : String(e));
/** The programs that run on a phone. */
const PHONES = new Set(['iphone', 'android']);

export default function HomePage() {
  const [theme, setTheme] = useTheme();
  const [settings, changeSettings] = useAppSettings();
  const { toasts, errors, push, remove, clearError, clearErrors } = useToasts();

  // Clean up or Organize. Organize is made the first time it is turned to and then kept, with all that was arranged in it.
  const [area, setArea] = useState<Area>('clean');
  const [organizing, setOrganizing] = useState(false);
  const turnTo = (next: Area) => { setArea(next); if (next === 'organize') setOrganizing(true); };
  const [instanceId, setInstanceId] = useState<string>();
  // Where the person chose to be; until they choose, the screen follows whether there is anything to show.
  const [chosen, setChosen] = useState<'setup' | 'results'>();
  const [folders, setFolders] = useState<string[]>([]);
  const [mode, setMode] = useState<GroupMode>('duplicates');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [hideKept, setHideKept] = useState(false);
  const [selection, setSelection] = useState<{ key?: string; index: number }>({ index: 0 });
  // On a narrow screen the groups and the review take turns: a group that was tapped is reviewed, with the way back.
  const [reviewing, setReviewing] = useState(false);
  const [copyIndex, setCopyIndex] = useState(0);
  const [comparing, setComparing] = useState<string>();
  const [pending, setPending] = useState<Pending>();
  const [busy, setBusy] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [errorsOpen, setErrorsOpen] = useState(false);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [aboutOpen, setAboutOpen] = useState(false);
  const [receiveOpen, setReceiveOpen] = useState(false);
  // A computer receives photos from a phone; a phone sends them, from Organize.
  const onPhone = PHONES.has(useAbout().data?.program ?? '');
  // The groups that are ticked, on whichever page each was: as it was when ticked, for when its page is not the one showing.
  const [ticked, setTicked] = useState<ReadonlyMap<string, DuplicateGroupDto>>(new Map());
  // What is waiting to be erased, once the service has said; and the wish, made while being asked, not to be asked twice again.
  const [held, setHeld] = useState<RemovedFilesDto>();
  const [askOnce, setAskOnce] = useState(false);
  const [logs, setLogs] = useState<string[]>([]);

  const groups = useGroups(mode, filter, page, hideKept);
  const others = useGroups(mode === 'duplicates' ? 'similar' : 'duplicates', '', 1, false);
  const status = useScanStatus();
  const progress = useScanProgress(instanceId);

  useEffect(() => connectLogStream(line => setLogs(l => [...l.slice(-199), line])), []);

  const readFolders = useCallback(() => {
    const last = loadLastScan();
    setFolders([last.root, last.second].filter(Boolean));
  }, []);
  useEffect(readFolders, [readFolders]);

  // A scan the service is running that this page did not start, as after the page was loaded again, is followed all
  // the same: without that the results would show half made, with nothing to say a scan was still at work.
  const running = status.data?.instanceId && !status.data.completed ? status.data.instanceId : undefined;
  useEffect(() => { if (running) setInstanceId(running); }, [running]);

  const scanning = !!instanceId && !progress.data?.completedUtc;
  const hasResults = (status.data?.totalPhotos ?? 0) > 0;
  const screen = scanning ? 'setup' : chosen ?? (status.data && !hasResults ? 'setup' : 'results');

  // While the other tab's groups are still loading, the previous tab's are not shown in its place.
  const data = groups.data?.mode === mode ? groups.data : undefined;
  const otherTotal = others.data?.mode === mode ? undefined : others.data?.total;
  const totals = { duplicates: mode === 'duplicates' ? data?.total : otherTotal, similar: mode === 'similar' ? data?.total : otherTotal };
  const items = data?.items ?? [];
  // When the selected group is gone, as it is once its duplicates are removed, the one that took its place is shown.
  const selected = items.find(g => g.key === selection.key) ?? items[Math.min(selection.index, items.length - 1)];
  const compared = comparing ? items.find(g => g.key === comparing) : undefined;
  const comparedIndex = compared ? Math.min(copyIndex, compared.members.length - 1) : 0;
  const removable = data?.removableCount ?? 0;
  const reclaimable = data?.reclaimableBytes ?? 0;

  // Removing the last groups of the last page leaves that page empty: step back to one that exists.
  const lastPage = data?.totalPages;
  useEffect(() => {
    if (lastPage !== undefined && page > Math.max(1, lastPage)) setPage(Math.max(1, lastPage));
  }, [lastPage, page]);

  // The pages either side of this one are fetched ahead, pictures and all, so that turning to one is immediate.
  // Done again whenever this page's own contents change, which is when theirs may have too.
  useEffect(() => {
    if (lastPage === undefined) return;
    for (const near of [page + 1, page - 1]) if (near >= 1 && near <= lastPage) void prefetchGroups(mode, filter, near, hideKept);
  }, [data, lastPage, mode, filter, page, hideKept]);

  // When the scan that was started here finishes, say what it found and show it.
  const announced = useRef<string | undefined>(undefined);
  const finished = !!instanceId && !!progress.data?.completedUtc;
  useEffect(() => {
    if (!finished || announced.current === instanceId) return;
    announced.current = instanceId;
    setChosen('results');
    setSelection({ index: 0 });
    setTicked(new Map());
    void retryNow();
    fetchGroupTotals().then(
      t => push(`Scan finished: ${groupsOf(t.duplicates, 'duplicate')} and ${groupsOf(t.similar, 'similar')}`, 'ok'),
      () => push('Scan finished.', 'ok'));
  }, [finished, instanceId, push]);

  const run = useCallback(async (action: () => Promise<void>) => {
    setBusy(true);
    try { await action(); }
    catch (e) { push(messageOf(e), 'error'); }
    finally { setBusy(false); }
  }, [push]);

  function select(key: string) {
    setSelection({ key, index: items.findIndex(g => g.key === key) });
    setCopyIndex(0);
    setReviewing(true);
  }
  // A tick is on a group of one kind: the other tab starts with none.
  function switchMode(next: GroupMode) { setMode(next); setPage(1); setSelection({ index: 0 }); setCopyIndex(0); setTicked(new Map()); setReviewing(false); }
  const untick = (key?: string) => setTicked(before => new Map([...before].filter(([k]) => k !== key)));
  const tick = (group: DuplicateGroupDto) => (ticked.has(group.key) ? untick(group.key) : setTicked(before => new Map(before).set(group.key, group)));
  function tickAll(all: boolean) {
    setTicked(before => {
      const next = new Map(before);
      for (const g of items) { if (all) next.set(g.key, g); else next.delete(g.key); }
      return next;
    });
  }
  // Each ticked group as it is now, where this page shows it; otherwise as it was when it was ticked.
  const tickedGroups = [...ticked.values()].map(g => items.find(x => x.key === g.key) ?? g);
  const tickedFiles = tickedGroups.reduce((n, g) => n + g.members.filter(m => !m.photo.kept).length, 0);
  const tickedBytes = tickedGroups.reduce((n, g) => n + g.reclaimableBytes, 0);
  function turnPage(next: number) { setPage(next); setSelection({ index: 0 }); setCopyIndex(0); }

  const openExternally = (photo: PhotoDto) => run(() => openInViewer(photo.id));
  const toggleKeep = (photo: PhotoDto) => run(async () => {
    await setKept(photo.id, !photo.kept);
    push(photo.kept ? `Stopped keeping ${photo.fileName}.` : `Keeping ${photo.fileName}. Bulk deletion will skip it.`, 'ok');
  });
  // A removal is taken back whole: every file it moved goes back where it was, and into its group again.
  const undoRemoval = (batchId: string, kind: string, name?: string) => run(async () => {
    const result = await undoChange(batchId);
    // Said by what came back, which is less than what went when a file has been taken away since.
    if (result.restored > 0) push(`Put ${result.restored === 1 && name ? name : these(result.restored, kind)} back where ${result.restored === 1 ? 'it was' : 'they were'}.`, 'ok');
    if (result.skipped > 0) push(`${result.skipped.toLocaleString()} could not be put back. ${result.problems[0] ?? ''}`.trim(), 'error');
  });
  /** The Undo on a message that says what a removal moved: none when it moved nothing. */
  const undoAction = (batchId: string | null | undefined, kind: string, name?: string): ToastAction | undefined =>
    batchId ? { label: 'Undo', run: () => void undoRemoval(batchId, kind, name) } : undefined;
  const kindOf = (m: GroupMode) => (m === 'similar' ? 'similar shot' : 'duplicate');
  // Every removal says which file went and which file stays, by name: the two can look like one.
  const removeOne = (photo: PhotoDto, stays: PhotoDto) => run(async () => {
    // Where the file stood among its group's copies. The copy after it takes its place, so the place is
    // kept: going through a group's copies does not start again from the first after each one.
    const from = items.find(g => g.members.some(m => m.photo.id === photo.id));
    const result = await removePhoto(photo.id);
    setPending(undefined);
    untick(from?.key);
    setCopyIndex(from && from.members.length > 1 ? Math.min(from.members.findIndex(m => m.photo.id === photo.id), from.members.length - 2) : 0);
    push(`Moved ${photo.fileName}${linkedFiles(result.companions)} to ${REMOVED_FOLDER}. ${stays.fileName} stays where it is.`, 'ok', undoAction(result.batchId, kindOf(mode), photo.fileName));
  });
  const removeMany = (group?: DuplicateGroupDto) => run(async () => {
    const result = await removeDuplicates(group?.key, mode);
    setPending(undefined);
    setCopyIndex(0);
    // The group's copies have gone, or every group's have: there is nothing left there to be ticked.
    if (group) untick(group.key); else setTicked(new Map());
    const going = group?.members.filter(m => !m.photo.kept) ?? [];
    const kind = kindOf(mode);
    const what = going.length === 1 ? going[0].photo.fileName : `${result.removed} ${kind}${result.removed === 1 ? '' : 's'}`;
    const stays = group ? `. ${group.keeper.fileName} stays where it is.` : '';
    if (result.removed > 0) push(`Moved ${what} (${formatBytes(result.bytes)})${linkedFiles(result.companions)} to ${REMOVED_FOLDER}${stays}`, 'ok', undoAction(result.batchId, kind, going.length === 1 ? going[0].photo.fileName : undefined));
    if (result.skipped > 0) push(`${result.skipped} left alone. ${result.problems[0] ?? ''}`.trim(), 'error');
  });
  const removeTicked = () => run(async () => {
    const result = await removeGroups([...ticked.keys()], mode);
    setPending(undefined);
    setCopyIndex(0);
    setTicked(new Map());
    const kind = kindOf(mode);
    if (result.removed > 0) {
      push(`Moved ${result.removed.toLocaleString()} ${kind}${result.removed === 1 ? '' : 's'} (${formatBytes(result.bytes)})${linkedFiles(result.companions)} to ${REMOVED_FOLDER}. The ${mode === 'similar' ? 'best shot' : 'original'} of each group stays where it is.`,
        'ok', undoAction(result.batchId, kind));
    }
    if (result.skipped > 0) push(`${result.skipped} left alone. ${result.problems[0] ?? ''}`.trim(), 'error');
  });
  const clear = () => run(async () => {
    const result = await clearResults();
    setPending(undefined);
    setTicked(new Map());
    setComparing(undefined);
    setSelection({ index: 0 });
    setPage(1);
    push(`Cleared the results: ${files(result.forgotten)} forgotten. Your photos were not touched.`, 'ok');
  });

  function askToErase() {
    setHeld(undefined);
    setAskOnce(false);
    setPending({ scope: 'erase', sure: false });
    const last = loadLastScan();
    // The folders this browser knows of; the service adds the ones it has on record.
    fetchRemoved([...new Set([last.root, last.second, loadOrganizeRoot()].filter(Boolean))]).then(setHeld, e => {
      setPending(now => (now?.scope === 'erase' ? undefined : now));
      push(messageOf(e), 'error');
    });
  }
  const erase = () => run(async () => {
    if (askOnce) changeSettings({ eraseAskTwice: false });
    const result = await eraseRemoved(held!.folders.map(f => f.path));
    setPending(undefined);
    if (result.erased > 0) push(`Deleted ${files(result.erased)} (${formatBytes(result.bytes)}) permanently.`, 'ok');
    if (result.skipped > 0) push(`${files(result.skipped)} could not be deleted. ${result.problems[0] ?? ''}`.trim(), 'error');
    if (result.erased + result.skipped === 0) push('There was nothing left to delete.', 'info');
  });

  function started(id: string) {
    setInstanceId(id);
    setLogs([]);
    readFolders();
  }

  // Scanning is part of Clean up, wherever it is asked for from.
  const changeFolders = () => { setArea('clean'); setChosen('setup'); };
  // The guide is put away, and what it was describing is shown.
  function goTo(place: GuidePlace) {
    setHelpOpen(false);
    if (place === 'scan') changeFolders();
    else if (place === 'organize') turnTo('organize');
    else if (place === 'settings') setSettingsOpen(true);
    else { setArea('clean'); setChosen('results'); if (mode !== place) switchMode(place); }
  }
  const offline = !!(groups.error || status.error);
  const cancel = () => setPending(undefined);
  const note = <>Files go to <span className="font-mono text-t1">{REMOVED_FOLDER}</span> inside the scanned folder, and Undo puts them back. Delete permanently, in the menu, erases them.</>;
  const best = mode === 'similar' ? 'best shot' : 'original';
  // Once there is no group left to review, a narrow screen goes back to the list.
  const showReview = reviewing && selected !== undefined;

  return (
    // The height the browser shows, which on a phone leaves out its own bars.
    <div className="flex h-dvh flex-col bg-bg text-t1">
      <TopBar area={area} onArea={turnTo} showScan={area === 'clean' && hasResults && screen === 'results'} folders={folders} files={status.data?.totalPhotos} scanned={status.data?.completed}
        errorCount={errors.length} menuOpen={menuOpen} onChangeFolders={changeFolders}
        onToggleErrors={() => { setErrorsOpen(o => !o); setMenuOpen(false); }} onToggleMenu={() => { setMenuOpen(o => !o); setErrorsOpen(false); }}>
        {/* The header holds its own popovers, so that they belong to it for a screen reader. */}
        {menuOpen && (
          <AppMenu theme={theme} errorCount={errors.length} onTheme={setTheme} onClose={() => setMenuOpen(false)} onErrors={() => setErrorsOpen(true)}
            onChangeFolders={changeFolders} onClearResults={() => setPending({ scope: 'clear' })} onSettings={() => setSettingsOpen(true)} onHelp={() => setHelpOpen(true)} onAbout={() => setAboutOpen(true)} onDeletePermanently={askToErase}
            onReceive={onPhone ? undefined : () => setReceiveOpen(true)} />
        )}
        {errorsOpen && <ErrorsPanel errors={errors} onClear={clearError} onClearAll={clearErrors} onClose={() => setErrorsOpen(false)} />}       </TopBar>

      {offline && (
        <div role="alert" className="flex items-center gap-4 border-b border-rose-line bg-rose-bg px-4 py-2.5 text-[14px] text-rose-t sm:px-6">
          <span className="flex-1">The PhotoSense service is not answering, so what is shown here may be out of date.</span>
          <button type="button" onClick={() => void retryNow()} className="pill-rose-outline h-8 px-4 text-[13px]">Retry</button>
        </div>
      )}

      <main className="flex min-h-0 flex-1 flex-col">
        {organizing && (
          <div className={area === 'organize' ? 'flex min-h-0 flex-1 flex-col' : 'hidden'}><OrganizeView startRoot={folders[0]} notify={push} /></div>
        )}

        {area === 'organize' ? null : screen === 'setup' ? (
          <SetupScreen scanning={scanning} progress={progress.data} log={logs} hasResults={hasResults} onStarted={started} onBack={() => setChosen('results')} notify={push} />
        ) : (
          <>
            {/* On a narrow screen the bar wraps: the tabs and the switch, then the search, then what deleting would free. */}
            <div className={`min-h-[72px] shrink-0 flex-wrap items-stretch gap-x-5 gap-y-2.5 border-b border-line px-4 pb-3 lg:flex-nowrap lg:px-6 lg:pb-0 ${showReview ? 'hidden lg:flex' : 'flex'}`}>
              <div className="flex h-12 gap-6 lg:h-auto" role="tablist">
                {(['duplicates', 'similar'] as const).map(m => (
                  <button key={m} type="button" role="tab" aria-selected={mode === m} onClick={() => switchMode(m)}
                    className={`flex items-center gap-2 border-b-2 text-[16px] font-semibold ${mode === m ? 'border-t1 text-t1' : 'border-transparent text-t2'}`}>
                    {m === 'duplicates' ? 'Duplicates' : 'Similar'}
                    <span className="font-mono text-[12px] font-normal text-t3">{totals[m]?.toLocaleString()}</span>
                  </button>
                ))}
              </div>
              <label className="my-auto flex h-[42px] w-full items-center gap-2.5 rounded-full border border-line bg-s1 px-4 max-lg:order-1 lg:w-[clamp(220px,22vw,380px)]">
                <span aria-hidden className="h-3 w-3 shrink-0 rounded-full border-2 border-t3" />
                <input value={filter} onChange={e => { setFilter(e.target.value); turnPage(1); }} placeholder="Search by file name or folder" aria-label="Search by file name or folder"
                  className="min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-t3" />
              </label>
              {mode === 'duplicates' && (
                <label className="my-auto flex cursor-pointer items-center gap-2.5 whitespace-nowrap text-[13.5px] text-t2" title="Hide groups where every copy is marked keep">
                  <input type="checkbox" role="switch" checked={hideKept} onChange={e => { setHideKept(e.target.checked); turnPage(1); }} className="peer sr-only" />
                  <span aria-hidden className="relative h-[18px] w-8 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-3.5 after:w-3.5 after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-3.5 peer-checked:after:bg-on-brand" />
                  Hide reviewed
                </label>
              )}
              <span className="hidden flex-1 lg:block" />
              {mode === 'duplicates' ? (
                <>
                  {/* Between the narrow layout and a wide screen the bar is tight: only the count and the size are said there. */}
                  <div className="my-auto min-w-0 flex-1 max-lg:order-2 lg:flex-initial lg:text-right">
                    <p className="text-[13.5px] lg:text-[14.5px]">
                      {removable > 0
                        ? <><span className="font-semibold">{removable.toLocaleString()}</span> duplicate {removable === 1 ? 'file' : 'files'} taking <span className="font-semibold">{formatBytes(reclaimable)}</span><span className="lg:hidden xl:inline">. The best copy of each picture or video is kept.</span></>
                        : <span className="text-t2">{data ? 'No duplicates waiting to be removed.' : 'Loading…'}</span>}
                    </p>
                    <p className="hidden text-[12.5px] text-t3 sm:block lg:hidden xl:block">Deleted files move to {REMOVED_FOLDER}, and Undo puts them back.</p>
                  </div>
                  <button type="button" disabled={busy || removable === 0} onClick={() => setPending({ scope: 'all', count: removable, bytes: reclaimable })}
                    className="pill-rose my-auto h-11 shrink-0 px-[22px] text-[15px] max-lg:order-2">Delete all duplicates</button>
                </>
              ) : (
                <p className="my-auto text-[13px] text-t2 max-lg:order-2 lg:max-w-[520px] lg:text-right lg:text-[14px]">Similar shots are burst frames or edited versions, not copies. Go through them a group at a time, or tick the groups you have looked at to delete their similar shots together.</p>
              )}
            </div>

            <div className="grid min-h-0 flex-1 grid-cols-1 lg:grid-cols-[clamp(380px,32vw,620px)_1fr]">
              <GroupList className={showReview ? 'hidden lg:flex' : 'flex'} groups={items} mode={mode} total={data?.total} page={page} totalPages={data?.totalPages ?? 1} query={filter}
                selectedKey={selected?.key} otherTotal={mode === 'duplicates' ? totals.similar : totals.duplicates} onSelect={select} onPage={turnPage}
                onOther={() => switchMode(mode === 'duplicates' ? 'similar' : 'duplicates')} chosen={ticked} onChoose={tick} onChooseAll={tickAll}
                footer={ticked.size > 0 && (
                  <div role="region" aria-label="Selected groups" className="m-3 flex shrink-0 flex-wrap items-center gap-2.5 rounded-2xl border border-line bg-pop px-4 py-2.5 shadow-toast">
                    <span className="min-w-0 flex-1 text-[13.5px]">
                      <span className="font-semibold">{ticked.size.toLocaleString()} {ticked.size === 1 ? 'group' : 'groups'} selected</span>
                      <span className="text-t2"> · {tickedFiles > 0 ? `${files(tickedFiles)}, ${formatBytes(tickedBytes)}` : 'every copy is marked keep'}</span>
                    </span>
                    <button type="button" disabled={busy || tickedFiles === 0} onClick={() => setPending({ scope: 'ticked' })} className="pill-rose h-9 px-4 text-[13.5px]">Delete their {mode === 'similar' ? 'similar shots' : 'duplicates'}</button>
                    <button type="button" onClick={() => setTicked(new Map())} className="pill-outline h-9 px-3.5 text-[13.5px] font-medium">Clear</button>
                  </div>
                )} />
              <ReviewPanel className={showReview ? 'flex' : 'hidden lg:flex'} onBack={() => setReviewing(false)} group={selected} mode={mode} busy={busy} copyIndex={copyIndex} onSelectCopy={setCopyIndex}
                onCompare={() => selected && setComparing(selected.key)} onToggleKeep={toggleKeep} onOpenInViewer={openExternally}
                onDeleteCopy={member => selected && setPending({ scope: 'copy', group: selected, member })}
                onDeleteGroup={group => setPending({ scope: 'group', group })} />
            </div>
          </>
        )}
      </main>

      {compared && (
        <PhotoWindow key={compared.key} original={compared.keeper} member={compared.members[comparedIndex]} index={comparedIndex} count={compared.members.length} mode={mode} busy={busy}
          onClose={() => setComparing(undefined)} onSelectCopy={setCopyIndex} onToggleKeep={toggleKeep} onRemove={removeOne} onOpenInViewer={openExternally} />
      )}

      {pending?.scope === 'all' && (
        <ConfirmDialog busy={busy} title="Delete all duplicates?" confirmLabel={`Delete ${files(pending.count)}`} note={note} onCancel={cancel} onConfirm={() => removeMany()}>
          <p>{files(pending.count)} taking {formatBytes(pending.bytes)} will be removed.</p>
          <p>The best copy of each picture or video stays. Copies you marked keep are skipped.</p>
        </ConfirmDialog>
      )}
      {pending?.scope === 'group' && <GroupConfirm group={pending.group} similar={mode === 'similar'} busy={busy} note={note} onCancel={cancel} onConfirm={() => removeMany(pending.group)} />}
      {pending?.scope === 'copy' && (
        <ConfirmDialog busy={busy} title={`Delete ${pending.member.photo.fileName}?`} confirmLabel="Delete 1 file" note={note}
          onCancel={cancel} onConfirm={() => removeOne(pending.member.photo, pending.group.keeper)}>
          <p><Name>{pending.member.photo.fileName}</Name> ({formatBytes(pending.member.photo.fileSizeBytes)}) will be removed.</p>
          <Stays what={best} photo={pending.group.keeper} />
        </ConfirmDialog>
      )}
      {pending?.scope === 'ticked' && (
        <ConfirmDialog busy={busy} title={`Delete the ${mode === 'similar' ? 'similar shots' : 'duplicates'} of ${ticked.size.toLocaleString()} ${ticked.size === 1 ? 'group' : 'groups'}?`}
          confirmLabel={`Delete ${files(tickedFiles)}`} note={note} onCancel={cancel} onConfirm={removeTicked}>
          <p>{files(tickedFiles)} taking {formatBytes(tickedBytes)} will be removed.</p>
          <p>The {best} of each group stays. Copies you marked keep are skipped.</p>
          {mode === 'similar' && <p>These are different shots or edited versions, not copies of one file.</p>}
        </ConfirmDialog>
      )}
      {pending?.scope === 'clear' && (
        <ConfirmDialog busy={busy} tone="brand" title="Clear the scan results?" confirmLabel="Clear results" onCancel={cancel} onConfirm={clear}
          note={<>Only PhotoSense&apos;s own record goes. Your photos stay exactly where they are, and so does anything already moved to <span className="font-mono text-t1">{REMOVED_FOLDER}</span>.</>}>
          <p>PhotoSense forgets every file it has scanned, with the groups, the previews and any copies you marked keep.</p>
          <p>The next scan reads every file again.</p>
        </ConfirmDialog>
      )}

      {pending?.scope === 'erase' && !pending.sure && (
        <ConfirmDialog busy={busy} ready={!!held && held.files > 0} title="Delete removed files permanently?" confirmLabel="Delete permanently" onCancel={cancel}
          onConfirm={() => (settings.eraseAskTwice ? setPending({ scope: 'erase', sure: true }) : erase())}
          note={<>This cannot be undone. The files are erased, not moved: they do not go to the Recycle Bin, and Undo can no longer bring them back.</>}>
          {!held ? <p role="status">Looking for removed files…</p> : held.files === 0 ? (
            <p>There is nothing to delete: no <span className="font-mono text-t1">{REMOVED_FOLDER}</span> folder holds any files.</p>
          ) : (
            <>
              <p><Name>{files(held.files)}</Name> taking {formatBytes(held.bytes)} will be erased: everything Clean up and Organize have removed that is still waiting in {held.folders.length === 1 ? 'this folder' : `these ${held.folders.length} folders`}.</p>
              <ul aria-label="Folders that will be emptied" className="space-y-1 pt-1">
                {held.folders.slice(0, NAMED).map(f => (
                  <li key={f.path} className="flex gap-3 text-[13px]"><span className="min-w-0 flex-1 font-mono text-t1 [overflow-wrap:anywhere]">{f.path}</span><span className="shrink-0">{files(f.files)}</span></li>
                ))}
              </ul>
              {held.folders.length > NAMED && <p className="text-[13.5px]">and {held.folders.length - NAMED} more</p>}
            </>
          )}
        </ConfirmDialog>
      )}
      {pending?.scope === 'erase' && pending.sure && (
        <ConfirmDialog small busy={busy} title="Are you sure?" confirmLabel="Delete permanently" onCancel={cancel} onConfirm={erase}>
          <p>{files(held!.files)} ({formatBytes(held!.bytes)}) will be erased for good.</p>
          <label className="flex cursor-pointer select-none items-center gap-2.5 pt-2 text-[13.5px] text-t1">
            <input type="checkbox" role="switch" checked={askOnce} onChange={e => setAskOnce(e.target.checked)} className="peer sr-only" />
            <span aria-hidden className="relative h-[18px] w-8 shrink-0 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-3.5 after:w-3.5 after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-3.5 peer-checked:after:bg-on-brand peer-focus-visible:ring-2 peer-focus-visible:ring-brand" />
            Do not ask me this second time again
          </label>
          <p className="text-[12.5px] text-t3">It can be turned back on under Settings in the menu.</p>
        </ConfirmDialog>
      )}

      {helpOpen && <HelpWindow onGo={goTo} onClose={() => setHelpOpen(false)} />}
      {aboutOpen && <AboutWindow onClose={() => setAboutOpen(false)} />}
      {settingsOpen && <SettingsWindow settings={settings} onChange={changeSettings} onClose={() => setSettingsOpen(false)} />}
      {receiveOpen && <ReceiveWindow folder={loadOrganizeRoot() || loadLastScan().root} notify={push} onClose={() => setReceiveOpen(false)} />}

      <Toaster toasts={toasts} remove={remove} />
    </div>
  );
}

interface GroupConfirmProps { readonly group: DuplicateGroupDto; readonly similar: boolean; readonly busy: boolean; readonly note: React.ReactNode; onCancel(): void; onConfirm(): void; }

const Name = ({ children }: { readonly children: string }) => <span className="font-semibold text-t1 [overflow-wrap:anywhere]">{children}</span>;

/** The other half of every removal: the file that is not going anywhere, by name and by folder. */
function Stays({ what, photo }: { readonly what: string; readonly photo: PhotoDto }) {
  return <p>The {what}, <Name>{photo.fileName}</Name>, stays in <span className="font-mono text-[13.5px] [overflow-wrap:anywhere]">{photo.folder}</span>.</p>;
}

function GroupConfirm({ group, similar, busy, note, onCancel, onConfirm }: GroupConfirmProps) {
  const going = group.members.filter(m => !m.photo.kept).map(m => m.photo.fileName);
  const kept = group.members.length - going.length;
  const count = going.length;
  const more = count - NAMED;
  return (
    <ConfirmDialog busy={busy} title={count === 1 ? `Delete ${going[0]}?` : `Delete the ${count} ${similar ? 'shots similar to' : 'duplicates of'} ${group.keeper.fileName}?`}
      confirmLabel={`Delete ${files(count)}`} note={note} onCancel={onCancel} onConfirm={onConfirm}>
      {count === 1
        ? <p><Name>{going[0]}</Name> ({formatBytes(group.reclaimableBytes)}) will be removed.</p>
        : <p>{files(count)} taking {formatBytes(group.reclaimableBytes)} will be removed: <Name>{going.slice(0, NAMED).join(', ')}</Name>{more > 0 && ` and ${more} more`}.</p>}
      <Stays what={similar ? 'best shot' : 'original'} photo={group.keeper} />
      {similar && <p>These are different shots or edited versions, not copies of one file.</p>}
      {kept > 0 && <p>{kept} {kept === 1 ? 'copy' : 'copies'} you marked keep {kept === 1 ? 'is' : 'are'} skipped.</p>}
    </ConfirmDialog>
  );
}
