import React, { useState } from 'react';
import { startScan } from '../lib/apiClient';
import { FolderPicker } from './FolderPicker';

interface Props { onStarted(id: string): void; }

type Which = 'primary' | 'secondary';

export function SettingsPanel({ onStarted }: Props) {
  const [primary, setPrimary] = useState('');
  const [secondary, setSecondary] = useState('');
  const [recursive, setRecursive] = useState(true);
  const [startOver, setStartOver] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  // Which of the two paths a folder is being browsed for, while the folder dialog is open.
  const [browsing, setBrowsing] = useState<Which>();

  function picked(path: string) {
    if (browsing === 'primary') setPrimary(path); else setSecondary(path);
    setBrowsing(undefined);
  }

  async function handleScan() {
    setBusy(true);
    setError(undefined);
    try {
      const res = await startScan({ primaryLocation: primary.trim(), secondaryLocation: secondary.trim() || undefined, recursive, startOver });
      // Asked for once, done once: the scan after this one builds on its results again.
      setStartOver(false);
      onStarted(res.instanceId);
    } catch (e) { setError(e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e)); }
    finally { setBusy(false); }
  }

  return (
    <div className="panel p-4 flex flex-col gap-4">
      <div>
        <label htmlFor="primaryPath" className="block text-xs font-semibold mb-1">Root folder path</label>
        <div className="flex gap-2">
          <input id="primaryPath" className="flex-1 min-w-0 rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={primary} onChange={e=>setPrimary(e.target.value)} placeholder="C:/photos" />
          <button type="button" onClick={()=>setBrowsing('primary')} className="btn-secondary px-2 py-1 text-xs" aria-label="Browse for the root folder" title="Browse…">…</button>
        </div>
      </div>
      <div>
        <label htmlFor="secondaryPath" className="block text-xs font-semibold mb-1">Secondary folder path</label>
        <div className="flex gap-2">
          <input id="secondaryPath" className="flex-1 min-w-0 rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={secondary} onChange={e=>setSecondary(e.target.value)} placeholder="D:/backup" />
          <button type="button" onClick={()=>setBrowsing('secondary')} className="btn-secondary px-2 py-1 text-xs" aria-label="Browse for the secondary folder" title="Browse…">…</button>
        </div>
        <p className="mt-1 text-[10px] text-neutral-500 leading-snug">Browse for a folder, or type or paste its full path, for example {'C:\\Users\\you\\Pictures'}. The folders are those of the computer PhotoSense runs on.</p>
      </div>
      <div className="flex items-center gap-2 text-xs">
        <input id="recursive" type="checkbox" checked={recursive} onChange={e=>setRecursive(e.target.checked)} />
        <label htmlFor="recursive">Recursive</label>
      </div>
      <div className="flex items-start gap-2 text-xs">
        <input id="startOver" type="checkbox" className="mt-0.5" checked={startOver} onChange={e=>setStartOver(e.target.checked)} />
        <label htmlFor="startOver">
          Start over
          <span className="block text-[10px] text-neutral-500 leading-snug">Forget the results of earlier scans and read every file again. Without this, a scan skips files that have not changed.</span>
        </label>
      </div>
      <button disabled={!primary.trim() || busy} onClick={handleScan} className="btn-primary">{busy? 'Starting...' : 'Scan'}</button>
      {error && <p role="alert" className="text-xs text-rose-400 break-words">{error}</p>}

      {browsing && (
        <FolderPicker title={browsing === 'primary' ? 'Choose the root folder' : 'Choose the secondary folder'}
          startAt={browsing === 'primary' ? primary : secondary} onPick={picked} onCancel={() => setBrowsing(undefined)} />
      )}
    </div>
  );
}
