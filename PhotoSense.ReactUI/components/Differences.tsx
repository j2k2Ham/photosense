import React from 'react';
import type { PhotoDto } from '../types';
import { compareRows, differences } from '../lib/format';

/**
 * Spells out what sets a copy apart from its original. Two files of one picture can share a name, a
 * folder and a date, as a phone's HEIC and its JPEG conversion do, and then look like one file listed twice.
 */
export function Differences({ original, copy }: { readonly original: PhotoDto; readonly copy: PhotoDto }) {
  const found = differences(original, copy);
  return (
    <div>
      <div className="text-[12px] font-semibold uppercase tracking-[0.07em] text-amber">Differs from the original in</div>
      {found.length === 0
        ? <div className="mt-1.5 text-[13.5px]">Nothing that is recorded about it.</div>
        : (
          <ul className="mt-1.5 space-y-1 text-[13.5px]">
            {found.map(d => <li key={d.label} className="[overflow-wrap:anywhere]"><span className="font-semibold">{d.label}:</span> {d.text}</li>)}
          </ul>
        )}
    </div>
  );
}

/** The original and a copy side by side, detail by detail; what differs comes first and is marked. */
export function DiffTable({ original, copy }: { readonly original: PhotoDto; readonly copy: PhotoDto }) {
  const cols = 'grid grid-cols-[150px_1fr_1fr] items-center gap-x-4 px-3.5';
  return (
    <div role="table" aria-label="What differs">
      <div role="row" className={`${cols} label-caps pb-1.5`}>
        <span role="columnheader">What differs</span>
        <span role="columnheader" className="text-keep">Original</span>
        <span role="columnheader">This copy</span>
      </div>
      {compareRows(original, copy).map(r => (
        <div role="row" key={r.label} data-differs={r.differs} className={`${cols} min-h-[36px] rounded-lg py-1.5 text-[14px] ${r.differs ? 'mb-0.5 bg-diff' : ''}`}>
          <span role="rowheader" className="flex items-center gap-2 text-t2">
            <span aria-hidden className={`h-1.5 w-1.5 rounded-full ${r.differs ? 'bg-amber' : ''}`} />{r.label}
          </span>
          <span role="cell" className={`truncate ${r.mono ? 'font-mono text-[13px]' : ''}`} title={r.original}>
            {r.original}
            {r.map && <> · <a href={r.map} target="_blank" rel="noreferrer" className="text-brand underline">Show on map</a></>}
          </span>
          {r.differs
            ? <span role="cell" className={`truncate ${r.mono ? 'font-mono text-[13px]' : ''}`} title={r.copy}>{r.copy}</span>
            : <span role="cell" className="text-t3">Same</span>}
        </div>
      ))}
    </div>
  );
}
