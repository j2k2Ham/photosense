import React, { useEffect, useRef, useState } from 'react';
import { startScan } from '../lib/apiClient';

interface Props { onStarted(id: string): void; }

export function SettingsPanel({ onStarted }: Props) {
  const [primary, setPrimary] = useState('');
  const [secondary, setSecondary] = useState('');
  const [recursive, setRecursive] = useState(true);
  const [threshold, setThreshold] = useState(12);
  const [busy, setBusy] = useState(false);
  const primaryInputRef = useRef<HTMLInputElement>(null);
  const secondaryInputRef = useRef<HTMLInputElement>(null);

  useEffect(()=>{
    // Add non-standard directory selection attributes after mount to satisfy TS typing
    if (primaryInputRef.current) {
      primaryInputRef.current.setAttribute('webkitdirectory','');
      primaryInputRef.current.setAttribute('directory','');
    }
    if (secondaryInputRef.current) {
      secondaryInputRef.current.setAttribute('webkitdirectory','');
      secondaryInputRef.current.setAttribute('directory','');
    }
  },[]);

  async function pickDirectory(kind: 'primary' | 'secondary') {
    // Prefer File System Access API if available
    try {
      // @ts-expect-error experimental
      if (window.showDirectoryPicker) {
        // @ts-expect-error experimental
        const handle: FileSystemDirectoryHandle = await window.showDirectoryPicker();
        const name = handle.name || '';
        if (kind === 'primary') setPrimary(name); else setSecondary(name);
        return;
      }
    } catch (e) {
      // Swallow expected errors (permission denied, user cancel). Log unexpected.
      if (e && typeof e === 'object' && (e as any).name && ['AbortError','NotAllowedError','SecurityError'].includes((e as any).name)) {
        // benign
      } else {
        // eslint-disable-next-line no-console
        console.debug('Directory picker fallback reason:', e);
      }
    }
    // Fallback: trigger hidden webkitdirectory input to at least capture top-level folder name
    if (kind === 'primary') primaryInputRef.current?.click(); else secondaryInputRef.current?.click();
  }

  function onHiddenDirChange(e: React.ChangeEvent<HTMLInputElement>, kind: 'primary' | 'secondary') {
    const files = e.target.files;
    if (!files || files.length === 0) return;
    // webkitRelativePath gives 'Folder/subfolder/file.ext' – take first segment as folder name (not full path)
    const rel = (files[0] as any).webkitRelativePath as string | undefined;
    if (rel) {
      const top = rel.split(/[\\/]/)[0];
      if (kind === 'primary') setPrimary(p => p || top); else setSecondary(s => s || top);
    }
    // Clear selection so user can re-select same folder later if needed
    e.target.value = '';
  }

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
        <label htmlFor="primaryPath" className="block text-xs font-semibold mb-1">Root folder path</label>
        <div className="flex gap-2">
          <input id="primaryPath" className="flex-1 rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={primary} onChange={e=>setPrimary(e.target.value)} placeholder="C:/photos" />
          <button type="button" onClick={()=>pickDirectory('primary')} className="btn-secondary px-2 py-1 text-xs" title="Browse...">…</button>
        </div>
  <input ref={primaryInputRef} type="file" style={{display:'none'}} multiple onChange={e=>onHiddenDirChange(e,'primary')} />
      </div>
      <div>
        <label htmlFor="secondaryPath" className="block text-xs font-semibold mb-1">Secondary folder path</label>
        <div className="flex gap-2">
          <input id="secondaryPath" className="flex-1 rounded bg-neutral-900 border border-neutral-700 px-2 py-1 text-sm" value={secondary} onChange={e=>setSecondary(e.target.value)} placeholder="D:/backup" />
          <button type="button" onClick={()=>pickDirectory('secondary')} className="btn-secondary px-2 py-1 text-xs" title="Browse...">…</button>
        </div>
  <input ref={secondaryInputRef} type="file" style={{display:'none'}} multiple onChange={e=>onHiddenDirChange(e,'secondary')} />
        <p className="mt-1 text-[10px] text-neutral-500 leading-snug">Note: Browsers cannot expose the full local path for security. The picker provides the folder name only; adjust manually if an absolute path is required on the server.</p>
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
