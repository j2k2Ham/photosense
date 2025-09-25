import React from 'react';

export function LogsPanel({ lines }: { lines: string[] }) {
  return (
    <div className="panel p-3 text-[11px] h-40 overflow-y-auto font-mono whitespace-pre-wrap">
      {lines.slice(-200).map((l,i)=>(<div key={i}>{l}</div>))}
    </div>
  );
}
