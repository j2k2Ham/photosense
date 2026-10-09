import React from 'react';
import { GALLERY_PAGE, placeOf, shortDate } from '../../lib/organize';
import type { OrganizeFileDto, OrganizePlaceDto } from '../../types';
import { Segmented } from '../ModeSwitch';
import { Check, Pager, Thumb } from './shared';

export type FileFilter = 'all' | 'todo' | 'noloc';

interface Props {
  /** The files to show, on every page together: what the filter, the search and the folder picked leave. */
  readonly files: readonly OrganizeFileDto[];
  readonly places: readonly OrganizePlaceDto[];
  readonly page: number;
  readonly filter: FileFilter;
  /** How many files each filter would show. */
  readonly counts: Readonly<Record<FileFilter, number>>;
  /** The folder whose files alone are showing, named: "Suggested: Anaconda". */
  readonly focus?: string;
  /** Not every file is showing: a folder is picked, something is searched for, or a filter is on. */
  readonly narrowed: boolean;
  readonly picked: ReadonlySet<string>;
  /** The folder is still being read, so that nothing shown is not yet "nothing there". */
  readonly reading?: boolean;
  /** What to say when there is nothing to show. */
  readonly empty: string;
  tagOf(file: OrganizeFileDto): { text: string; done: boolean } | undefined;
  onPage(page: number): void;
  onFilter(filter: FileFilter): void;
  onClearFocus(): void;
  /** Puts away the folder picked, the search and the filter together, so that every file shows again. */
  onClearAll(): void;
  /** A file was clicked; with Shift held, everything from the last one clicked to it is meant. */
  onPick(file: OrganizeFileDto, index: number, range: boolean): void;
  onOpen(file: OrganizeFileDto): void;
  onDragStart(file: OrganizeFileDto): void;
  onDragEnd(): void;
}

const count = (n: number) => <span className="font-mono text-[11.5px] font-medium opacity-75">{n.toLocaleString()}</span>;

/** Every file of the folder as a grid of pictures, a page at a time, to select from and drag out of. */
export function FileGallery({ files, places, page, filter, counts, focus, narrowed, picked, reading, empty, tagOf, onPage, onFilter, onClearFocus, onClearAll, onPick, onOpen, onDragStart, onDragEnd }: Props) {
  const pages = Math.max(1, Math.ceil(files.length / GALLERY_PAGE)), from = (page - 1) * GALLERY_PAGE;
  return (
    <>
      <div className="flex shrink-0 flex-wrap items-center gap-3 px-6 py-3.5 text-[13px] text-t2">
        <span className="whitespace-nowrap"><span className="font-semibold text-t1">{files.length.toLocaleString()}</span> {files.length === 1 ? 'file' : 'files'}</span>
        <Segmented label="Show" size="medium" value={filter} onChange={onFilter} options={[
          { value: 'all', name: <>All{count(counts.all)}</> },
          { value: 'todo', name: <>Not organized{count(counts.todo)}</> },
          { value: 'noloc', name: <>No location{count(counts.noloc)}</> },
        ]} />
        {focus && (
          <span className="flex h-[30px] max-w-[360px] items-center gap-1.5 rounded-full border border-brand bg-sel pl-3 pr-1 font-medium text-t1">
            <span className="truncate">{focus}</span>
            <button type="button" aria-label="Show every folder's files again" onClick={onClearFocus} className="flex h-[22px] w-[22px] shrink-0 items-center justify-center rounded-full hover:bg-s3">×</button>
          </span>
        )}
        {narrowed && <button type="button" title="Show every file again: no folder picked, no search, no filter" onClick={onClearAll} className="pill-outline h-[30px] px-3.5 text-[13px] font-medium">Clear all</button>}
        <span className="flex-1" />
        <Pager page={page} pages={pages} onPage={onPage} />
      </div>

      <div className="min-h-0 flex-1 overflow-y-auto px-6 pb-6 pt-1">
        <ul aria-label="Files" className="grid grid-cols-[repeat(auto-fill,minmax(128px,1fr))] gap-x-3 gap-y-[18px]">
          {files.slice(from, from + GALLERY_PAGE).map((f, i) => {
            const on = picked.has(f.id), tag = tagOf(f);
            return (
              <li key={f.id}>
                {/* Not a button element: some browsers will not let one be dragged. */}
                <div role="button" tabIndex={0} aria-pressed={on} draggable title={f.name} className="flex w-full min-w-0 cursor-pointer select-none flex-col gap-1.5 text-left"
                  onClick={e => onPick(f, from + i, e.shiftKey)} onDoubleClick={() => onOpen(f)} onDragEnd={onDragEnd}
                  onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onPick(f, from + i, e.shiftKey); } }}
                  // A drag that carries nothing is not started by every browser.
                  onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/plain', f.name); onDragStart(f); }}>
                  <span className={`relative block aspect-square overflow-hidden rounded-[10px] ${on ? 'shadow-[0_0_0_3px_var(--bg),0_0_0_5px_var(--brand)]' : ''}`}>
                    <Thumb file={f} className="h-full w-full" duration />
                    <Check on={on} />
                    {tag && <span className={`absolute inset-x-[7px] bottom-[7px] truncate rounded-md px-[7px] py-[3px] text-[11px] font-semibold text-white ${tag.done ? 'bg-[#1d6b4c]/90' : 'bg-[#1c6078]/90'}`}>{tag.text}</span>}
                  </span>
                  <span className="flex min-w-0 flex-col gap-px">
                    <span className="truncate text-[13px] font-medium">{f.name}</span>
                    <span className="truncate text-[12px] text-t3">{shortDate(f.date)} · {placeOf(f, places)}</span>
                  </span>
                </div>
              </li>
            );
          })}
        </ul>
        {!reading && files.length === 0 && <p className="px-5 py-20 text-center text-[15px] text-t3">{empty}</p>}
      </div>
    </>
  );
}
