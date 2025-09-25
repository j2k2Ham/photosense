"use client";
import React, { useState } from 'react';
import { SettingsPanel } from '../components/SettingsPanel';
import { DuplicateGrid } from '../components/DuplicateGrid';
import { PreviewPanel } from '../components/PreviewPanel';
import { ProgressPanel } from '../components/ProgressPanel';
import { LogsPanel } from '../components/LogsPanel';
import { useDuplicateGroups, useScanProgress, connectLogStream } from '../lib/apiClient';
import type { PhotoDto } from '../types';

export default function HomePage() {
  const [instanceId, setInstanceId] = useState<string>();
  const [filter, setFilter] = useState('');
  const [selected, setSelected] = useState<PhotoDto | undefined>();
  const [nearMode, setNearMode] = useState(false);
  const [threshold, setThreshold] = useState(12);
  const [page, setPage] = useState(1);
  const dupes = useDuplicateGroups(filter, nearMode, threshold, page);
  const progress = useScanProgress(instanceId);
  const [logs, setLogs] = useState<string[]>(["Ready."]);
  React.useEffect(()=>{
    const dispose = connectLogStream(line => setLogs(l=>[...l.slice(-199), line]));
    return dispose;
  },[]);

  return (
    <div className="flex flex-1 overflow-hidden">
      <SettingsPanel onStarted={id => { setInstanceId(id); }} />
      <div className="flex flex-col flex-1 overflow-hidden gap-3 p-3">
        <div className="flex items-center gap-2">
          <input value={filter} onChange={e=>setFilter(e.target.value)} placeholder="Search duplicates" className="flex-1 rounded bg-neutral-800 border border-neutral-700 px-2 py-1 text-sm" />
          <button className={`btn-secondary ${!nearMode ? 'ring-1 ring-emerald-500' : ''}`} onClick={()=>{setNearMode(false); setPage(1);}}>Exact</button>
          <button className={`btn-secondary ${nearMode ? 'ring-1 ring-amber-500' : ''}`} onClick={()=>{setNearMode(true); setPage(1);}}>Near</button>
          {nearMode && <input type="range" className="w-40" min={0} max={32} value={threshold} onChange={e=>{setThreshold(parseInt(e.target.value)); setPage(1);}} />}
        </div>
        <div className="flex flex-1 gap-3 overflow-hidden">
          <div className="panel flex-1 flex flex-col overflow-hidden">
            <div className="text-[11px] px-3 py-2 border-b border-neutral-700 flex items-center gap-4">
              <span>Groups: {dupes.data?.items ? dupes.data.items.length : (dupes.data as any)?.length ?? 0}</span>
              {dupes.data && (
                <span className="text-neutral-500">Page {dupes.data.page}/{dupes.data.totalPages} • Total {dupes.data.total}</span>
              )}
              <div className="ml-auto flex items-center gap-2">
                <button disabled={page<=1} onClick={()=>setPage(p=>Math.max(1,p-1))} className="btn-secondary h-6 px-2">Prev</button>
                <button disabled={!dupes.data || page>=dupes.data.totalPages} onClick={()=>setPage(p=>dupes.data? Math.min(dupes.data.totalPages, p+1):p)} className="btn-secondary h-6 px-2">Next</button>
                {dupes.data && (
                  <input type="number" min={1} max={dupes.data.totalPages} value={page} onChange={e=>{ const v = parseInt(e.target.value)||1; setPage(Math.min(Math.max(1,v), dupes.data!.totalPages)); }} className="w-16 bg-neutral-800 border border-neutral-700 rounded px-1 h-6 text-[11px]" />
                )}
              </div>
              <button className="btn-secondary h-6 px-2">Keep Best</button>
              <button className="btn-secondary h-6 px-2">Move Others</button>
            </div>
            <DuplicateGrid groups={(dupes.data?.items as any) || ([] as any)} onSelect={p=>setSelected(p)} />
          </div>
          <div className="w-80 flex flex-col gap-3 shrink-0">
            <PreviewPanel photo={selected} />
            <ProgressPanel progress={progress.data} />
            <LogsPanel lines={logs} />
          </div>
        </div>
      </div>
    </div>
  );
}
