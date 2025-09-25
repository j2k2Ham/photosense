import React, { useState } from 'react';
import { startScan } from '../lib/apiClient';

interface Props { onStarted(id: string): void; }

export function SettingsPanel({ onStarted }: Props) {
  const [primary, setPrimary] = useState('');
  const [secondary, setSecondary] = useState('');
  const [recursive, setRecursive] = useState(true);
  const [threshold, setThreshold] = useState(12);
  const [busy, setBusy] = useState(false);

  async function handleScan() {
    setBusy(true);
    try {
      const res = await startScan({ primaryLocation: primary, secondaryLocation: secondary || undefined, recursive, hammingThreshold: threshold });
      onStarted(res.instanceId);
    } catch (e) { console.error(e); alert('Failed to start scan'); }
    finally { setBusy(false); }
  }

  return (
    <div className="panel p-4 flex flex-col gap-4 w-72 shrink-0 overflow-y-auto">
      <div>
        <label className="block text-xs font-semibold mb-1">Root folder path</label>
        <input className="w-full rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={primary} onChange={e=>setPrimary(e.target.value)} placeholder="C:/photos" />
      </div>
      <div>
        <label className="block text-xs font-semibold mb-1">Secondary folder path</label>
        <input className="w-full rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={secondary} onChange={e=>setSecondary(e.target.value)} placeholder="D:/backup" />
      </div>
      <div>
        <label className="block text-xs font-semibold mb-1">Hamming threshold: {threshold}</label>
        <input type="range" min={0} max={32} value={threshold} onChange={e=>setThreshold(parseInt(e.target.value))} className="w-full" />
      </div>
      <div className="flex items-center gap-2 text-xs">
        <input id="recursive" type="checkbox" checked={recursive} onChange={e=>setRecursive(e.target.checked)} />
        <label htmlFor="recursive">Recursive</label>
      </div>
      <button disabled={!primary || busy} onClick={handleScan} className="btn-primary">{busy? 'Starting...' : 'Scan'}</button>
    </div>
  );
}
