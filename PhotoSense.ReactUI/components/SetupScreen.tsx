import React, { useState } from 'react';
import { startScan } from '../lib/apiClient';
import { leaf, sameFolder } from '../lib/format';
import { loadLastScan, saveLastScan } from '../lib/lastScan';
import type { ScanProgressSnapshotDto } from '../types';
import { FolderPicker } from './FolderPicker';
import type { ToastKind } from './Toaster';

interface Props {
  /** A scan is under way: its progress is shown and another cannot be started. */
  readonly scanning: boolean;
  readonly progress?: ScanProgressSnapshotDto;
  /** Lines the scan has logged, oldest first. */
  readonly log: string[];
  /** There are results to go back to. */
  readonly hasResults: boolean;
  onStarted(instanceId: string): void;
  onBack(): void;
  notify(message: string, kind: ToastKind): void;
}

type Which = 'root' | 'second';
const TILES = 120;
// What the service says of a path that is not there, with the path it could not find.
const NOT_FOUND = /^Folder not found on the server: (.*)$/;

/** The first screen, and the one for changing folders: choose what to scan, start it, and watch it run. */
export function SetupScreen({ scanning, progress, log, hasResults, onStarted, onBack, notify }: Props) {
  const [last] = useState(loadLastScan);
  const [root, setRoot] = useState(last.root);
  const [second, setSecond] = useState(last.second);
  const [recursive, setRecursive] = useState(last.recursive);
  const [startOver, setStartOver] = useState(false);
  const [starting, setStarting] = useState(false);
  const [missing, setMissing] = useState<Which>();
  const [browsing, setBrowsing] = useState<Which>();
  const [fullLog, setFullLog] = useState(false);

  function change(which: Which, value: string) {
    if (which === 'root') setRoot(value); else setSecond(value);
    // Whatever was wrong with the folder, it is being put right.
    if (missing === which) setMissing(undefined);
  }

  // The root folder given a second time adds nothing: it is scanned once, and copies inside it are found all the same.
  const twice = sameFolder(root, second);
  const other = twice ? '' : second.trim();

  async function scan() {
    if (!root.trim()) { notify('Choose a root folder to scan.', 'error'); return; }
    setStarting(true);
    setMissing(undefined);
    try {
      const res = await startScan({ primaryLocation: root.trim(), secondaryLocation: other || undefined, recursive, startOver });
      saveLastScan({ root: root.trim(), second: other, recursive });
      // Asked for once, done once: the scan after this one builds on its results again.
      setStartOver(false);
      onStarted(res.instanceId);
    } catch (e) {
      const message = e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e);
      const path = NOT_FOUND.exec(message)?.[1];
      if (path) setMissing(path === other ? 'second' : 'root');
      notify(path ? `Folder not found: ${path}` : message, 'error');
    } finally {
      setStarting(false);
    }
  }

  const running = starting || scanning;
  const field = (which: Which) => `flex h-[50px] items-center gap-2 rounded-xl border bg-bg pl-3.5 pr-1.5 ${missing === which ? 'border-rose' : 'border-line'}`;
  const input = 'min-w-0 flex-1 bg-transparent font-mono text-[14px] outline-none placeholder:text-t3';

  // Until the files have been counted there is nothing to be a percentage of.
  const total = progress ? progress.primaryTotal + progress.secondaryTotal : 0;
  const done = progress ? progress.primaryProcessed + progress.secondaryProcessed : 0;
  const percent = progress && total > 0 ? progress.overallPercent : 0;
  const filled = Math.round((percent / 100) * TILES);
  const lines = log.slice(-200);

  return (
    <div className="flex flex-1 flex-col items-center gap-10 overflow-y-auto px-6 pb-12 pt-[clamp(32px,8vh,96px)]">
      <div className="flex flex-col items-center gap-4 text-center">
        <h2 className="text-[clamp(40px,4.2vw,60px)] font-semibold leading-[1.05] tracking-[-0.03em]">Find duplicate photos and videos</h2>
        <p className="max-w-[680px] text-[18px] text-t2">PhotoSense compares the pictures and videos in your folders, keeps the best copy of each, and lets you check the rest before anything moves.</p>
      </div>

      <div className="grid w-[min(1240px,100%)] grid-cols-[1fr_1fr_auto] items-end gap-4 rounded-[20px] border border-line bg-s1 p-[22px] shadow-setup">
        <div className="flex min-w-0 flex-col gap-2">
          <label htmlFor="rootPath" className="text-[13px] text-t2">Root folder</label>
          <div className={field('root')}>
            <input id="rootPath" value={root} onChange={e => change('root', e.target.value)} aria-invalid={missing === 'root'} placeholder="C:\Users\you\Pictures" className={input} />
            <button type="button" onClick={() => setBrowsing('root')} aria-label="Browse for the root folder" className="pill-quiet h-[38px] shrink-0 px-4 text-[14px]">Browse</button>
          </div>
        </div>
        <div className="flex min-w-0 flex-col gap-2">
          <label htmlFor="secondPath" className="text-[13px] text-t2">Secondary folder <span className="text-t3">{twice ? '· the same as the root folder, so it is scanned once. Copies inside it are still found.' : '· optional, for example a backup'}</span></label>
          <div className={field('second')}>
            <input id="secondPath" value={second} onChange={e => change('second', e.target.value)} aria-invalid={missing === 'second'} placeholder="D:\Backup" className={input} />
            <button type="button" onClick={() => setBrowsing('second')} aria-label="Browse for the secondary folder" className="pill-quiet h-[38px] shrink-0 px-4 text-[14px]">Browse</button>
          </div>
        </div>
        <div className="flex flex-col gap-2">
          <label className="flex items-center gap-2 text-[13px]">
            <input type="checkbox" checked={recursive} onChange={e => setRecursive(e.target.checked)} className="h-[18px] w-[18px] rounded-[5px] accent-[var(--brand)]" />
            <span>Recursive <span className="text-t3">· include subfolders</span></span>
          </label>
          <label className="flex items-center gap-2 text-[13px]" title="Forget the results of earlier scans and read every file again. Without this, a scan skips files that have not changed.">
            <input type="checkbox" checked={startOver} onChange={e => setStartOver(e.target.checked)} className="h-[18px] w-[18px] rounded-[5px] accent-[var(--brand)]" />
            <span>Start over <span className="text-t3">· forget earlier results</span></span>
          </label>
          <button type="button" disabled={running} onClick={scan} className="pill-brand h-[50px] px-[34px] text-[15px]">
            {starting ? 'Starting…' : scanning ? 'Scanning…' : 'Scan'}
          </button>
        </div>
      </div>

      {scanning && (
        <div className="flex w-[min(1240px,100%)] flex-col gap-4" aria-label="Scan progress">
          <div className="flex flex-wrap items-baseline gap-x-5 gap-y-1">
            <span className="text-[40px] font-semibold tabular-nums leading-none">{Math.round(percent)}%</span>
            <span className="text-[14px] text-t2">{done.toLocaleString()} of {total.toLocaleString()} files</span>
            {progress && [[root, progress.primaryProcessed, progress.primaryTotal] as const, [other, progress.secondaryProcessed, progress.secondaryTotal] as const]
              .filter(([folder]) => folder.trim())
              .map(([folder, n, of]) => (
                <span key={folder} className="text-[13px] text-t3">{leaf(folder)} <span className="font-mono text-t2">{n.toLocaleString()} / {of.toLocaleString()}</span></span>
              ))}
            <span className="ml-auto text-[13px] text-t3">About 13 minutes for 7,000 files</span>
          </div>
          <div role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(percent)} className="grid grid-cols-[repeat(30,1fr)] gap-1.5">
            {Array.from({ length: TILES }, (_, i) => (
              <span key={i} className={`aspect-square rounded ${i < filled ? 'bg-brand' : 'bg-s3 opacity-60'}`} style={i < filled ? { opacity: 0.45 + ((i * 7) % 10) / 18 } : undefined} />
            ))}
          </div>
          <div className="flex items-start gap-4">
            <ul className="min-w-0 flex-1 font-mono text-[12.5px] text-t2" aria-label="Latest log lines">
              {lines.slice(-3).map((l, i) => <li key={i} className="truncate">{l}</li>)}
            </ul>
            <button type="button" onClick={() => setFullLog(v => !v)} className="shrink-0 text-[13px] text-t2 underline">{fullLog ? 'Hide full log' : 'Show full log'}</button>
          </div>
          {fullLog && (
            <ul aria-label="Full log" className="max-h-60 overflow-y-auto rounded-xl bg-stage p-3.5 font-mono text-[12.5px] text-t2">
              {lines.map((l, i) => <li key={i} className="[overflow-wrap:anywhere]">{l}</li>)}
            </ul>
          )}
        </div>
      )}

      {hasResults && !scanning && <button type="button" onClick={onBack} className="pill-outline h-11 px-5 text-[15px]">Back to results</button>}

      {browsing && (
        <FolderPicker title={browsing === 'root' ? 'Choose the root folder' : 'Choose the secondary folder'} startAt={browsing === 'root' ? root : second}
          onPick={path => { change(browsing, path); setBrowsing(undefined); }} onCancel={() => setBrowsing(undefined)} />
      )}
    </div>
  );
}
