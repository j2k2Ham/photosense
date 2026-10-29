import React from 'react';
import { formatBytes } from '../../lib/format';
import { dateRange, totalBytes, type Suggestion } from '../../lib/organize';
import { Thumb, countFiles, useDrop } from './shared';

interface CardProps {
  readonly suggestion: Suggestion;
  readonly on: boolean;
  onFocus(): void;
  onPreview(): void;
  onDrop(): void;
}

/** A suggested folder: what it would be called, what would go in it, and the way to look before it is made. */
export function SuggestionCard({ suggestion: s, on, onFocus, onPreview, onDrop }: CardProps) {
  const drop = useDrop(onDrop);
  return (
    <div {...drop.handlers} className={`flex items-center gap-3.5 rounded-[14px] border p-3 ${on || drop.hot ? 'border-brand bg-sel' : 'border-line bg-bg'}`}>
      <button type="button" aria-pressed={on} aria-label={`Show the files for ${s.name}`} onClick={onFocus} className="flex min-w-0 flex-1 items-center gap-3.5 text-left">
        <span aria-hidden className="grid h-16 w-16 shrink-0 grid-cols-2 gap-0.5 overflow-hidden rounded-[10px]">
          {[0, 1, 2, 3].map(i => (s.files[i] ? <Thumb key={s.files[i].id} file={s.files[i]} className="relative h-[31px] w-[31px]" play={7} /> : <span key={i} className="bg-s3" />))}
        </span>
        <span className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="truncate text-[15px] font-semibold">{s.name}</span>
          <span className="text-[13px] text-t2">{countFiles(s.files.length)} · {formatBytes(totalBytes(s.files))}</span>
          <span className="text-[12.5px] text-t3">{dateRange(s.files)}</span>
          {s.split && <span className="text-[12.5px] text-t3">Inside: a folder for each {s.split}</span>}
          {s.merge && <span className="text-[12.5px] text-ident">{s.merge}</span>}
          {s.fallback && <span className="text-[12.5px] text-t3">No landmark known here, so it is named after the town</span>}
        </span>
      </button>
      <button type="button" aria-label={`Preview ${s.name}`} onClick={onPreview} className="pill-quiet h-9 shrink-0 border border-line px-4 text-[13.5px]">Preview</button>
    </div>
  );
}
