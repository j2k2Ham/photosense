import React from 'react';
import type { ScanProgressSnapshotDto } from '../types';

export function ProgressPanel({ progress }: { progress?: ScanProgressSnapshotDto }) {
  const p = progress?.overallPercent ?? 0;
  return (
    <div className="panel p-3 flex flex-col gap-2">
      <div className="text-xs font-semibold">Scan Progress</div>
      <div className="h-3 bg-neutral-700 rounded overflow-hidden">
        <div className="h-full bg-emerald-500 transition-all" style={{ width: `${p.toFixed(1)}%` }} />
      </div>
      <div className="text-[11px] text-neutral-400">{p.toFixed(1)}%</div>
      {progress && <div className="text-[10px] text-neutral-500">Primary {progress.primaryProcessed}/{progress.primaryTotal} • Secondary {progress.secondaryProcessed}/{progress.secondaryTotal}</div>}
    </div>
  );
}
