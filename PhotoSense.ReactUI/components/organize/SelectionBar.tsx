import React from 'react';
import { BinIcon } from './shared';

interface BarProps {
  readonly count: number;
  /** How many files the gallery is showing; all of them can be selected at once while fewer are. */
  readonly shown: number;
  /** Said when there are folders the selection could be added to instead. */
  readonly hint: string;
  onSelectAll(): void;
  /** Asks which folder the selected files go in: a new one, or one already there. */
  onNewFolder(): void;
  /** On a phone: offers the selected files to a computer. */
  onSend?(): void;
  /** Asks to delete the selected files; nothing goes until that is confirmed. */
  onDelete(): void;
  onClear(): void;
}

/** What can be done with the files that are selected, floating over the foot of the gallery. */
export function SelectionBar({ count, shown, hint, onSelectAll, onNewFolder, onSend, onDelete, onClear }: BarProps) {
  return (
    // A pill across the foot of the gallery, drawn over the pictures it floats above; on a narrow screen a card whose buttons wrap.
    <div role="region" aria-label="Selection" className="relative z-10 mx-3 mb-3 flex shrink-0 flex-wrap items-center gap-x-3 gap-y-2 rounded-2xl border border-line bg-pop py-2 pl-4 pr-2 shadow-toast sm:mx-6 sm:mb-[18px] sm:flex-nowrap sm:rounded-full sm:pl-5">
      <span className="whitespace-nowrap text-[14.5px] font-semibold">{count.toLocaleString()} selected</span>
      {count < shown && <button type="button" onClick={onSelectAll} className="whitespace-nowrap text-[13px] text-t2 underline underline-offset-[3px]">Select all {shown.toLocaleString()} shown</button>}
      <span className="flex-1" />
      <span className="hidden whitespace-nowrap text-[13px] text-t3 xl:inline">{hint}</span>
      <button type="button" onClick={onNewFolder} className="pill-brand h-[38px] px-[18px] text-[14px]">New folder from these</button>
      {onSend && <button type="button" onClick={onSend} className="pill-outline h-[38px] px-[18px] text-[14px] font-medium">Send to computer</button>}
      <button type="button" aria-label={`Delete the ${count.toLocaleString()} selected ${count === 1 ? 'file' : 'files'}`} title="Delete the selected files" onClick={onDelete}
        className="flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-full border border-rose-line text-rose-t hover:bg-rose-bg">
        <BinIcon />
      </button>
      <button type="button" onClick={onClear} className="pill-outline h-[38px] px-4 text-[14px] font-medium">Clear</button>
    </div>
  );
}
