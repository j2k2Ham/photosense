"use client";
import React, { useCallback, useState } from 'react';
import clsx from 'clsx';
import { SettingsPanel } from '../components/SettingsPanel';
import { GroupList } from '../components/GroupList';
import { ReviewPanel } from '../components/ReviewPanel';
import { PhotoWindow } from '../components/PhotoWindow';
import { ConfirmDialog } from '../components/ConfirmDialog';
import { ProgressPanel } from '../components/ProgressPanel';
import { LogsPanel } from '../components/LogsPanel';
import { useGroups, useScanProgress, connectLogStream, setKept, removePhoto, removeDuplicates, openInViewer, clearResults } from '../lib/apiClient';
import { useToasts, Toaster } from '../components/Toaster';
import { formatBytes, linkedFiles } from '../lib/format';
import type { DuplicateGroupDto, GroupMode, PhotoDto } from '../types';

// Removed files are moved here, inside the scanned folder, rather than erased.
const REMOVED_FOLDER = '_PhotoSense_Removed';

interface Opened { groupKey: string; memberId?: string; }
interface PendingRemoval { group?: DuplicateGroupDto; count: number; bytes: number; }

export default function HomePage() {
  const [instanceId, setInstanceId] = useState<string>();
  const [mode, setMode] = useState<GroupMode>('duplicates');
  const [filter, setFilter] = useState('');
  const [page, setPage] = useState(1);
  const [hideKept, setHideKept] = useState(false);
  const [selectedKey, setSelectedKey] = useState<string>();
  const [opened, setOpened] = useState<Opened>();
  const [pending, setPending] = useState<PendingRemoval>();
  // Whether the question "clear the results?" is being asked.
  const [clearing, setClearing] = useState(false);
  const [busy, setBusy] = useState(false);
  const groups = useGroups(mode, filter, page, hideKept);
  const progress = useScanProgress(instanceId);
  const [logs, setLogs] = useState<string[]>(["Ready."]);
  const { toasts, push, remove } = useToasts();
  React.useEffect(()=>{
    const dispose = connectLogStream(line => setLogs(l=>[...l.slice(-199), line]));
    return dispose;
  },[]);

  // While the other tab's groups are still loading, the previous tab's are not shown in its place.
  const data = groups.data?.mode === mode ? groups.data : undefined;
  const items = data?.items ?? [];
  // Falls back to the first group when the selected one is gone, as it is once its duplicates are removed.
  const selected = items.find(g => g.key === selectedKey) ?? items[0];
  const openedGroup = opened && items.find(g => g.key === opened.groupKey);
  const openedMember = openedGroup?.members.find(m => m.photo.id === opened?.memberId);
  const windowOpen = !!openedGroup && (!opened?.memberId || !!openedMember);

  function switchMode(next: GroupMode) { setMode(next); setPage(1); setSelectedKey(undefined); }

  // Removing the last groups of the last page leaves that page empty: step back to one that exists.
  const lastPage = data?.totalPages;
  React.useEffect(() => {
    if (lastPage !== undefined && page > Math.max(1, lastPage)) setPage(Math.max(1, lastPage));
  }, [lastPage, page]);

  const run = useCallback(async (action: () => Promise<void>) => {
    setBusy(true);
    try { await action(); }
    catch (e) { push(e instanceof Error ? e.message : String(e), 'error'); }
    finally { setBusy(false); }
  }, [push]);

  const toggleKeep = (photo: PhotoDto) => run(() => setKept(photo.id, !photo.kept));
  const openExternally = (photo: PhotoDto) => run(() => openInViewer(photo.id));

  const removeOne = (photo: PhotoDto) => run(async () => {
    const result = await removePhoto(photo.id);
    setOpened(undefined);
    push(`Moved ${photo.fileName}${linkedFiles(result.companions)} to ${REMOVED_FOLDER}`, 'success');
  });

  const confirmRemoval = () => run(async () => {
    const result = await removeDuplicates(pending?.group?.key);
    setPending(undefined);
    if (result.removed > 0) push(`Moved ${result.removed} ${result.removed === 1 ? 'duplicate' : 'duplicates'} (${formatBytes(result.bytes)})${linkedFiles(result.companions)} to ${REMOVED_FOLDER}`, 'success');
    if (result.skipped > 0) push(`${result.skipped} left alone. ${result.problems[0] ?? ''}`, 'error');
  });

  const confirmClear = () => run(async () => {
    const result = await clearResults();
    setClearing(false);
    setOpened(undefined);
    setSelectedKey(undefined);
    setPage(1);
    push(`Cleared the results: ${result.forgotten.toLocaleString()} scanned ${result.forgotten === 1 ? 'file' : 'files'} forgotten. Your photos were not touched.`, 'success');
  });

  return (
    <div className="flex flex-1 overflow-hidden">
      <div className="flex flex-col gap-3 p-3 pr-0 w-72 shrink-0 overflow-y-auto">
        <SettingsPanel onStarted={id => { setInstanceId(id); }} />
        <ProgressPanel progress={progress.data} />
        <LogsPanel lines={logs} />
      </div>

      <div className="flex flex-col flex-1 min-w-0 overflow-hidden gap-3 p-3">
        {/* What is on screen stays there when a request fails, so a service that has stopped must be said out loud. */}
        {groups.error && (
          <div role="alert" className="rounded-lg border border-rose-500/70 bg-rose-950/50 px-4 py-2 text-sm text-rose-100">
            The PhotoSense service is not answering, so what is shown here may be out of date. Look at the window it was started in; stopping it and starting it again does no harm.
          </div>
        )}
        <div className="flex items-center gap-2">
          <button className={clsx('btn-secondary', mode === 'duplicates' && 'ring-1 ring-emerald-500')} onClick={() => switchMode('duplicates')}>Duplicates</button>
          <button className={clsx('btn-secondary', mode === 'similar' && 'ring-1 ring-amber-500')} onClick={() => switchMode('similar')} title="Burst frames and edited versions. For review only; never removed in bulk.">Similar</button>
          <input value={filter} onChange={e => { setFilter(e.target.value); setPage(1); }} placeholder="Search by file name or folder" className="flex-1 rounded bg-neutral-800 border border-neutral-700 px-2 py-1.5 text-sm" />
          {mode === 'duplicates' && (
            <label className="flex items-center gap-1 text-xs text-neutral-400" title="Hide groups where every copy is marked keep">
              <input type="checkbox" checked={hideKept} onChange={e => { setHideKept(e.target.checked); setPage(1); }} /> Hide reviewed
            </label>
          )}
          <button type="button" disabled={busy} className="btn-secondary py-1.5 px-3 text-xs" onClick={() => setClearing(true)}
            title="Forget everything that has been scanned, so the next scan starts from nothing. Your photos are not touched.">
            Clear results
          </button>
        </div>

        {mode === 'duplicates' ? (
          <div className="panel px-4 py-2 flex items-center gap-4 text-sm">
            {data && data.removableCount > 0
              ? <span><span className="font-semibold">{data.removableCount.toLocaleString()}</span> duplicate {data.removableCount === 1 ? 'file' : 'files'} taking <span className="font-semibold">{formatBytes(data.reclaimableBytes)}</span>. The best copy of each picture or video is kept.</span>
              : <span className="text-neutral-400">{groups.error ? 'Cannot reach the PhotoSense server.' : 'No duplicates waiting to be removed.'}</span>}
            <button type="button" disabled={busy || !data || data.removableCount === 0} className="btn-danger ml-auto"
              onClick={() => data && setPending({ count: data.removableCount, bytes: data.reclaimableBytes })}>
              Delete all duplicates
            </button>
          </div>
        ) : (
          <div className="panel px-4 py-2 text-sm text-neutral-300">
            Look-alikes that are not the same file: burst frames and edited versions. Review them one at a time; nothing here is removed in bulk.
          </div>
        )}

        <div className="flex flex-1 gap-3 overflow-hidden">
          <div className="panel w-72 shrink-0 flex flex-col overflow-hidden">
            <div className="text-[11px] px-3 py-2 border-b border-neutral-700 text-neutral-400">
              {data ? `${data.total.toLocaleString()} ${data.total === 1 ? 'group' : 'groups'}` : 'Loading…'}
            </div>
            <GroupList groups={items} mode={mode} selectedKey={selected?.key} onSelect={setSelectedKey} />
            {data && data.totalPages > 1 && (
              <div className="px-3 py-2 border-t border-neutral-700 flex items-center gap-2 text-[11px] text-neutral-400">
                <button disabled={page <= 1} onClick={() => setPage(p => Math.max(1, p - 1))} className="rounded bg-neutral-700 hover:bg-neutral-600 px-3 py-1 text-neutral-100 disabled:opacity-50 disabled:cursor-not-allowed">Prev</button>
                <span className="mx-auto">Page {data.page} of {data.totalPages}</span>
                <button disabled={page >= data.totalPages} onClick={() => setPage(p => Math.min(data.totalPages, p + 1))} className="rounded bg-neutral-700 hover:bg-neutral-600 px-3 py-1 text-neutral-100 disabled:opacity-50 disabled:cursor-not-allowed">Next</button>
              </div>
            )}
          </div>

          <ReviewPanel group={selected} mode={mode} busy={busy} onOpenInViewer={openExternally}
            onOpen={member => selected && setOpened({ groupKey: selected.key, memberId: member?.photo.id })}
            onRemoveGroup={group => setPending({ group, count: group.members.filter(m => !m.photo.kept).length, bytes: group.reclaimableBytes })} />
        </div>
      </div>

      {windowOpen && openedGroup && (
        <PhotoWindow key={opened?.memberId ?? openedGroup.key} original={openedGroup.keeper} mode={mode} member={openedMember} busy={busy}
          onClose={() => setOpened(undefined)} onToggleKeep={toggleKeep} onRemove={removeOne} onOpenInViewer={openExternally} />
      )}

      {pending && (
        <ConfirmDialog busy={busy} onCancel={() => setPending(undefined)} onConfirm={confirmRemoval}
          title={pending.group ? `Delete the duplicates of ${pending.group.keeper.fileName}?` : 'Delete all duplicates?'}
          confirmLabel={`Delete ${pending.count.toLocaleString()} ${pending.count === 1 ? 'file' : 'files'}`}>
          <p><span className="font-semibold">{pending.count.toLocaleString()}</span> {pending.count === 1 ? 'file' : 'files'} ({formatBytes(pending.bytes)}) will be moved out of your photos. The best copy of each picture or video stays where it is, and copies you marked keep are not touched. An edit sidecar or Live Photo video goes with its picture only when no other picture of that shot stays in the folder.</p>
          <p className="text-neutral-400">The files go to a <span className="font-mono text-neutral-300">{REMOVED_FOLDER}</span> folder inside the scanned folder, so nothing is lost if a match was wrong. Delete that folder yourself to free the space.</p>
        </ConfirmDialog>
      )}

      {clearing && (
        <ConfirmDialog busy={busy} onCancel={() => setClearing(false)} onConfirm={confirmClear} title="Clear the scan results?" confirmLabel="Clear results">
          <p>PhotoSense forgets every file it has scanned, with the groups, the previews and any copies you marked keep. The next scan reads every file again.</p>
          <p className="text-neutral-400">Only PhotoSense's own record goes. Your photos stay exactly where they are, and so does anything already moved to <span className="font-mono text-neutral-300">{REMOVED_FOLDER}</span>.</p>
        </ConfirmDialog>
      )}

      <Toaster toasts={toasts} remove={remove} />
    </div>
  );
}
