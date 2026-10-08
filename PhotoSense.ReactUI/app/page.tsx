"use client";
import React, { useCallback, useEffect, useRef, useState } from 'react';
import { AppMenu } from '../components/AppMenu';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ErrorsPanel } from '../components/ErrorsPanel';
import { GroupList } from '../components/GroupList';
import { PhotoWindow } from '../components/PhotoWindow';
import { ReviewPanel } from '../components/ReviewPanel';
import { SetupScreen } from '../components/SetupScreen';
import { Toaster, useToasts } from '../components/Toaster';
import { TopBar } from '../components/TopBar';
import { clearResults, connectLogStream, fetchGroupTotals, openInViewer, removeDuplicates, removePhoto, retryNow, setKept, useGroups, useScanProgress, useScanStatus } from '../lib/apiClient';
import { formatBytes, linkedFiles } from '../lib/format';
import { loadLastScan } from '../lib/lastScan';
import { useTheme } from '../lib/theme';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode, PhotoDto } from '../types';

// Removed files are moved here, inside the scanned folder, rather than erased.
const REMOVED_FOLDER = '_PhotoSense_Removed';

/** What is waiting for a yes or no. */
type Pending =
  | { scope: 'all'; count: number; bytes: number }
  | { scope: 'group'; group: DuplicateGroupDto }
  | { scope: 'copy'; group: DuplicateGroupDto; member: GroupMemberDto }
  | { scope: 'clear' };

const files = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;
// As many names as a confirmation has room for.
const NAMED = 4;
const groupsOf = (n: number, kind: string) => `${n.toLocaleString()} ${kind} ${n === 1 ? 'group' : 'groups'}`;

export default function HomePage() {
  const [theme, setTheme] = useTheme();
  const { toasts, errors, push, remove, clearError, clearErrors } = useToasts();

  const [instanceId, setInstanceId] = useState<string>();
  // Where the person chose to be; until they choose, the screen follows whether there is anything to show.
  const [chosen, setChosen] = useState<'setup' | 'results'>();
  const [folders, setFolders] = useState<string[]>([]);
  const [mode, setMode] = useState<GroupMode>('duplicates');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [hideKept, setHideKept] = useState(false);
  const [selection, setSelection] = useState<{ key?: string; index: number }>({ index: 0 });
  const [copyIndex, setCopyIndex] = useState(0);
  const [comparing, setComparing] = useState<string>();
  const [pending, setPending] = useState<Pending>();
  const [busy, setBusy] = useState(false);
  const [menuOpen, setMenuOpen] = useState(false);
  const [errorsOpen, setErrorsOpen] = useState(false);
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
  const removable = data?.removableCount ?? 0;
  const reclaimable = data?.reclaimableBytes ?? 0;

  // Removing the last groups of the last page leaves that page empty: step back to one that exists.
  const lastPage = data?.totalPages;
  useEffect(() => {
    if (lastPage !== undefined && page > Math.max(1, lastPage)) setPage(Math.max(1, lastPage));
  }, [lastPage, page]);

  // When the scan that was started here finishes, say what it found and show it.
  const announced = useRef<string>();
  const finished = !!instanceId && !!progress.data?.completedUtc;
  useEffect(() => {
    if (!finished || announced.current === instanceId) return;
    announced.current = instanceId;
    setChosen('results');
    setSelection({ index: 0 });
    void retryNow();
    fetchGroupTotals().then(
      t => push(`Scan finished: ${groupsOf(t.duplicates, 'duplicate')} and ${groupsOf(t.similar, 'similar')}`, 'ok'),
      () => push('Scan finished.', 'ok'));
  }, [finished, instanceId, push]);

  const run = useCallback(async (action: () => Promise<void>) => {
    setBusy(true);
    try { await action(); }
    catch (e) { push(e instanceof Error ? e.message : String(e), 'error'); }
    finally { setBusy(false); }
  }, [push]);

  function select(key: string) {
    setSelection({ key, index: items.findIndex(g => g.key === key) });
    setCopyIndex(0);
  }
  function switchMode(next: GroupMode) { setMode(next); setPage(1); setSelection({ index: 0 }); setCopyIndex(0); }
  function turnTo(next: number) { setPage(next); setSelection({ index: 0 }); setCopyIndex(0); }

  const openExternally = (photo: PhotoDto) => run(() => openInViewer(photo.id));
  const toggleKeep = (photo: PhotoDto) => run(async () => {
    await setKept(photo.id, !photo.kept);
    push(photo.kept ? `Stopped keeping ${photo.fileName}.` : `Keeping ${photo.fileName}. Bulk deletion will skip it.`, 'ok');
  });
  // Every removal says which file went and which file stays, by name: the two can look like one.
  const removeOne = (photo: PhotoDto, stays: PhotoDto) => run(async () => {
    const result = await removePhoto(photo.id);
    setPending(undefined);
    setCopyIndex(0);
    push(`Moved ${photo.fileName}${linkedFiles(result.companions)} to ${REMOVED_FOLDER}. ${stays.fileName} stays where it is.`, 'ok');
  });
  const removeMany = (group?: DuplicateGroupDto) => run(async () => {
    const result = await removeDuplicates(group?.key);
    setPending(undefined);
    setCopyIndex(0);
    const going = group?.members.filter(m => !m.photo.kept) ?? [];
    const what = going.length === 1 ? going[0].photo.fileName : `${result.removed} ${result.removed === 1 ? 'duplicate' : 'duplicates'}`;
    const stays = group ? `. ${group.keeper.fileName} stays where it is.` : '';
    if (result.removed > 0) push(`Moved ${what} (${formatBytes(result.bytes)})${linkedFiles(result.companions)} to ${REMOVED_FOLDER}${stays}`, 'ok');
    if (result.skipped > 0) push(`${result.skipped} left alone. ${result.problems[0] ?? ''}`.trim(), 'error');
  });
  const clear = () => run(async () => {
    const result = await clearResults();
    setPending(undefined);
    setComparing(undefined);
    setSelection({ index: 0 });
    setPage(1);
    push(`Cleared the results: ${files(result.forgotten)} forgotten. Your photos were not touched.`, 'ok');
  });

  function started(id: string) {
    setInstanceId(id);
    setLogs([]);
    readFolders();
  }

  const offline = !!(groups.error || status.error);
  const cancel = () => setPending(undefined);
  const note = <>Files go to <span className="font-mono text-t1">{REMOVED_FOLDER}</span> inside the scanned folder. Moving a file back restores it.</>;
  const best = mode === 'similar' ? 'best shot' : 'original';

  return (
    <div className="flex h-screen min-w-[1100px] flex-col bg-bg text-t1">
      <TopBar showScan={hasResults && screen === 'results'} folders={folders} files={status.data?.totalPhotos} scanned={status.data?.completed}
        errorCount={errors.length} menuOpen={menuOpen} onChangeFolders={() => setChosen('setup')}
        onToggleErrors={() => { setErrorsOpen(o => !o); setMenuOpen(false); }} onToggleMenu={() => { setMenuOpen(o => !o); setErrorsOpen(false); }} />

      {offline && (
        <div role="alert" className="flex items-center gap-4 border-b border-rose-line bg-rose-bg px-6 py-2.5 text-[14px] text-rose-t">
          <span className="flex-1">The PhotoSense service is not answering, so what is shown here may be out of date.</span>
          <button type="button" onClick={() => void retryNow()} className="pill-rose-outline h-8 px-4 text-[13px]">Retry</button>
        </div>
      )}

      {screen === 'setup' ? (
        <SetupScreen scanning={scanning} progress={progress.data} log={logs} hasResults={hasResults} onStarted={started} onBack={() => setChosen('results')} notify={push} />
      ) : (
        <>
          <div className="flex min-h-[72px] shrink-0 items-stretch gap-5 border-b border-line px-6">
            <div className="flex gap-6" role="tablist">
              {(['duplicates', 'similar'] as const).map(m => (
                <button key={m} type="button" role="tab" aria-selected={mode === m} onClick={() => switchMode(m)}
                  className={`flex items-center gap-2 border-b-2 text-[16px] font-semibold ${mode === m ? 'border-t1 text-t1' : 'border-transparent text-t2'}`}>
                  {m === 'duplicates' ? 'Duplicates' : 'Similar'}
                  <span className="font-mono text-[12px] font-normal text-t3">{totals[m]?.toLocaleString()}</span>
                </button>
              ))}
            </div>
            <label className="my-auto flex h-[42px] w-[clamp(220px,22vw,380px)] items-center gap-2.5 rounded-full border border-line bg-s1 px-4">
              <span aria-hidden className="h-3 w-3 shrink-0 rounded-full border-2 border-t3" />
              <input value={filter} onChange={e => { setFilter(e.target.value); turnTo(1); }} placeholder="Search by file name or folder" aria-label="Search by file name or folder"
                className="min-w-0 flex-1 bg-transparent text-[14px] outline-none placeholder:text-t3" />
            </label>
            {mode === 'duplicates' && (
              <label className="my-auto flex cursor-pointer items-center gap-2.5 text-[13.5px] text-t2" title="Hide groups where every copy is marked keep">
                <input type="checkbox" role="switch" checked={hideKept} onChange={e => { setHideKept(e.target.checked); turnTo(1); }} className="peer sr-only" />
                <span aria-hidden className="relative h-[18px] w-8 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-3.5 after:w-3.5 after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-3.5 peer-checked:after:bg-on-brand" />
                Hide reviewed
              </label>
            )}
            <span className="flex-1" />
            {mode === 'duplicates' ? (
              <>
                <div className="my-auto text-right">
                  <p className="text-[14.5px]">
                    {removable > 0
                      ? <><span className="font-semibold">{removable.toLocaleString()}</span> duplicate {removable === 1 ? 'file' : 'files'} taking <span className="font-semibold">{formatBytes(reclaimable)}</span>. The best copy of each picture or video is kept.</>
                      : <span className="text-t2">{data ? 'No duplicates waiting to be removed.' : 'Loading…'}</span>}
                  </p>
                  <p className="text-[12.5px] text-t3">Deleted files move to {REMOVED_FOLDER} and can be moved back.</p>
                </div>
                <button type="button" disabled={busy || removable === 0} onClick={() => setPending({ scope: 'all', count: removable, bytes: reclaimable })}
                  className="pill-rose my-auto h-11 px-[22px] text-[15px]">Delete all duplicates</button>
              </>
            ) : (
              <p className="my-auto max-w-[520px] text-right text-[14px] text-t2">Similar shots are burst frames or edited versions. Nothing here is removed in bulk; review them one at a time.</p>
            )}
          </div>

          <div className="grid min-h-0 flex-1 grid-cols-[clamp(380px,32vw,620px)_1fr]">
            <GroupList groups={items} mode={mode} total={data?.total} page={page} totalPages={data?.totalPages ?? 1} query={filter}
              selectedKey={selected?.key} onSelect={select} onPage={turnTo} />
            <ReviewPanel group={selected} mode={mode} busy={busy} copyIndex={copyIndex} onSelectCopy={setCopyIndex}
              onCompare={() => selected && setComparing(selected.key)} onToggleKeep={toggleKeep} onOpenInViewer={openExternally}
              onDeleteCopy={member => selected && setPending({ scope: 'copy', group: selected, member })}
              onDeleteGroup={group => setPending({ scope: 'group', group })} />
          </div>
        </>
      )}

      {compared && (
        <PhotoWindow key={compared.key} original={compared.keeper} member={compared.members[Math.min(copyIndex, compared.members.length - 1)]} mode={mode} busy={busy}
          onClose={() => setComparing(undefined)} onToggleKeep={toggleKeep} onRemove={removeOne} onOpenInViewer={openExternally} />
      )}

      {pending?.scope === 'all' && (
        <ConfirmDialog busy={busy} title="Delete all duplicates?" confirmLabel={`Delete ${files(pending.count)}`} note={note} onCancel={cancel} onConfirm={() => removeMany()}>
          <p>{files(pending.count)} taking {formatBytes(pending.bytes)} will be removed.</p>
          <p>The best copy of each picture or video stays. Copies you marked keep are skipped.</p>
        </ConfirmDialog>
      )}
      {pending?.scope === 'group' && <GroupConfirm group={pending.group} busy={busy} note={note} onCancel={cancel} onConfirm={() => removeMany(pending.group)} />}
      {pending?.scope === 'copy' && (
        <ConfirmDialog busy={busy} title={`Delete ${pending.member.photo.fileName}?`} confirmLabel="Delete 1 file" note={note}
          onCancel={cancel} onConfirm={() => removeOne(pending.member.photo, pending.group.keeper)}>
          <p><Name>{pending.member.photo.fileName}</Name> ({formatBytes(pending.member.photo.fileSizeBytes)}) will be removed.</p>
          <Stays what={best} photo={pending.group.keeper} />
        </ConfirmDialog>
      )}
      {pending?.scope === 'clear' && (
        <ConfirmDialog busy={busy} tone="brand" title="Clear the scan results?" confirmLabel="Clear results" onCancel={cancel} onConfirm={clear}
          note={<>Only PhotoSense&apos;s own record goes. Your photos stay exactly where they are, and so does anything already moved to <span className="font-mono text-t1">{REMOVED_FOLDER}</span>.</>}>
          <p>PhotoSense forgets every file it has scanned, with the groups, the previews and any copies you marked keep.</p>
          <p>The next scan reads every file again.</p>
        </ConfirmDialog>
      )}

      {menuOpen && (
        <AppMenu theme={theme} errorCount={errors.length} onTheme={setTheme} onClose={() => setMenuOpen(false)} onErrors={() => setErrorsOpen(true)}
          onChangeFolders={() => setChosen('setup')} onClearResults={() => setPending({ scope: 'clear' })} />
      )}
      {errorsOpen && <ErrorsPanel errors={errors} onClear={clearError} onClearAll={clearErrors} onClose={() => setErrorsOpen(false)} />}

      <Toaster toasts={toasts} remove={remove} />
    </div>
  );
}

interface GroupConfirmProps { readonly group: DuplicateGroupDto; readonly busy: boolean; readonly note: React.ReactNode; onCancel(): void; onConfirm(): void; }

const Name = ({ children }: { readonly children: string }) => <span className="font-semibold text-t1 [overflow-wrap:anywhere]">{children}</span>;

/** The other half of every removal: the file that is not going anywhere, by name and by folder. */
function Stays({ what, photo }: { readonly what: string; readonly photo: PhotoDto }) {
  return <p>The {what}, <Name>{photo.fileName}</Name>, stays in <span className="font-mono text-[13.5px] [overflow-wrap:anywhere]">{photo.folder}</span>.</p>;
}

function GroupConfirm({ group, busy, note, onCancel, onConfirm }: GroupConfirmProps) {
  const going = group.members.filter(m => !m.photo.kept).map(m => m.photo.fileName);
  const kept = group.members.length - going.length;
  const count = going.length;
  const more = count - NAMED;
  return (
    <ConfirmDialog busy={busy} title={count === 1 ? `Delete ${going[0]}?` : `Delete the ${count} duplicates of ${group.keeper.fileName}?`}
      confirmLabel={`Delete ${files(count)}`} note={note} onCancel={onCancel} onConfirm={onConfirm}>
      {count === 1
        ? <p><Name>{going[0]}</Name> ({formatBytes(group.reclaimableBytes)}) will be removed.</p>
        : <p>{files(count)} taking {formatBytes(group.reclaimableBytes)} will be removed: <Name>{going.slice(0, NAMED).join(', ')}</Name>{more > 0 && ` and ${more} more`}.</p>}
      <Stays what="original" photo={group.keeper} />
      {kept > 0 && <p>{kept} {kept === 1 ? 'copy' : 'copies'} you marked keep {kept === 1 ? 'is' : 'are'} skipped.</p>}
    </ConfirmDialog>
  );
}
