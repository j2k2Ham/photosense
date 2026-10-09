import React from 'react';
import { BinIcon } from './shared';

interface BarProps {
  readonly count: number;
  /** How many files the gallery is showing; all of them can be selected at once while fewer are. */
  readonly shown: number;
  /** Said when there are folders the selection could be added to instead. */
  readonly hint: string;
  /** The bar has turned into the name of a new folder. */
  readonly naming: boolean;
  readonly name: string;
  onName(name: string): void;
  onSelectAll(): void;
  onStartNaming(): void;
  onCancelNaming(): void;
  onCreate(): void;
  /** Asks to delete the selected files; nothing goes until that is confirmed. */
  onDelete(): void;
  onClear(): void;
}

/** What can be done with the files that are selected, floating over the foot of the gallery. */
export function SelectionBar({ count, shown, hint, naming, name, onName, onSelectAll, onStartNaming, onCancelNaming, onCreate, onDelete, onClear }: BarProps) {
  return (
    <div role="region" aria-label="Selection" className="mx-6 mb-[18px] flex shrink-0 items-center gap-3 rounded-full border border-line bg-pop py-2 pl-5 pr-2 shadow-toast">
      <span className="whitespace-nowrap text-[14.5px] font-semibold">{count.toLocaleString()} selected</span>
      {naming ? (
        <>
          {/* eslint-disable-next-line jsx-a11y/no-autofocus */}
          <input autoFocus value={name} onChange={e => onName(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') onCreate(); }}
            aria-label="Name of the new folder" placeholder="Name the new folder, for example Trips\Glacier 2022"
            className="h-[38px] min-w-[180px] flex-1 rounded-full border border-brand bg-bg px-4 text-[14px] outline-none placeholder:text-t3" />
          <button type="button" onClick={onCancelNaming} className="pill-outline h-[38px] px-4 text-[14px] font-medium">Cancel</button>
          <button type="button" onClick={onCreate} className="pill-brand h-[38px] px-[18px] text-[14px]">Create folder</button>
        </>
      ) : (
        <>
          {count < shown && <button type="button" onClick={onSelectAll} className="whitespace-nowrap text-[13px] text-t2 underline underline-offset-[3px]">Select all {shown.toLocaleString()} shown</button>}
          <span className="flex-1" />
          <span className="whitespace-nowrap text-[13px] text-t3">{hint}</span>
          <button type="button" onClick={onStartNaming} className="pill-brand h-[38px] px-[18px] text-[14px]">New folder from these</button>
          <button type="button" aria-label={`Delete the ${count.toLocaleString()} selected ${count === 1 ? 'file' : 'files'}`} title="Delete the selected files" onClick={onDelete}
            className="flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-full border border-rose-line text-rose-t hover:bg-rose-bg">
            <BinIcon />
          </button>
          <button type="button" onClick={onClear} className="pill-outline h-[38px] px-4 text-[14px] font-medium">Clear</button>
        </>
      )}
    </div>
  );
}
