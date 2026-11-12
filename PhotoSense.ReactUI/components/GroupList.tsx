import React, { useEffect, useRef } from 'react';
import type { DuplicateGroupDto, GroupMode } from '../types';
import { groupSummary } from '../lib/format';
import { useHoverCard } from './HoverCard';
import { PhotoFacts, PhotoThumb } from './PhotoThumb';
import { SelectAll } from './SelectAll';

interface Props {
  readonly groups: DuplicateGroupDto[];
  readonly mode: GroupMode;
  /** Groups in every page together; absent while the first answer is awaited. */
  readonly total?: number;
  readonly page: number;
  readonly totalPages: number;
  readonly query: string;
  readonly selectedKey?: string;
  /** The groups that are ticked, on this page or another, by key. */
  readonly chosen: { has(key: string): boolean };
  /** What can be done with the ticked groups, shown under the groups. */
  readonly footer?: React.ReactNode;
  /** Groups of the other kind, similar shots when these are duplicates: said when there are none of this kind to show. */
  readonly otherTotal?: number;
  onSelect(key: string): void;
  onPage(page: number): void;
  /** Ticks a group, or takes its tick off. */
  onChoose(group: DuplicateGroupDto): void;
  /** Ticks every group on this page (true), or takes the tick off every one of them (false). */
  onChooseAll(all: boolean): void;
  /** Shows the groups of the other kind. */
  onOther?(): void;
  /** Whether the list shows, as a display class: on a narrow screen it gives way to the review. */
  readonly className?: string;
}

function emptyMessage(mode: GroupMode, query: string): string {
  if (query.trim()) return `No groups match "${query.trim()}"`;
  return mode === 'similar' ? 'No similar shots found' : 'No duplicates to show';
}

/** The groups as a gallery of their best copies, a page at a time. */
export function GroupList({ groups, mode, total, page, totalPages, query, selectedKey, chosen, footer, otherTotal = 0, onSelect, onPage, onChoose, onChooseAll, onOther, className = 'flex' }: Props) {
  const pager = 'pill-outline h-[30px] px-3 text-[13px]';
  const chosenHere = groups.filter(g => chosen.has(g.key)).length;
  const list = useRef<HTMLUListElement>(null);
  const hover = useHoverCard<DuplicateGroupDto>();
  // A page starts at its top, wherever the one before it was left.
  useEffect(() => {
    if (list.current) list.current.scrollTop = 0;
  }, [page, mode, query]);
  return (
    <section aria-label="Groups" className={`${className} min-h-0 flex-col border-line bg-s1 lg:border-r`}>
      <div className="flex flex-wrap items-center gap-x-4 gap-y-2 px-4 py-3.5 text-[13px] text-t2 lg:px-5">
        <span>{total === undefined ? 'Loading…' : <><span className="font-semibold text-t1">{total.toLocaleString()}</span> {total === 1 ? 'group' : 'groups'}</>}</span>
        {groups.length > 0 && (
          <SelectAll label={totalPages > 1 ? 'Select all on this page' : 'Select all'} checked={chosenHere === groups.length} partial={chosenHere > 0} onChange={onChooseAll}
            title={`Select every group on this page, to delete their ${mode === 'similar' ? 'similar shots' : 'duplicates'} together`} />
        )}
        {totalPages > 1 && (
          <span className="ml-auto flex items-center gap-2.5">
            <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} className={pager}>Prev</button>
            <span>Page {page} of {totalPages}</span>
            <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)} className={pager}>Next</button>
          </span>
        )}
      </div>

      {groups.length === 0
        ? total !== undefined && (
          <div className="flex flex-col items-center gap-3.5 px-5 py-10 text-center">
            <p className="text-[14px] text-t3">{emptyMessage(mode, query)}</p>
            {/* An empty tab is not an empty result: what was found may all be under the other one. */}
            {!query.trim() && otherTotal > 0 && (
              <button type="button" onClick={onOther} className="pill-outline h-9 px-4 text-[13.5px] font-medium">
                Show the {otherTotal.toLocaleString()} {mode === 'similar' ? 'duplicate' : 'similar'} {otherTotal === 1 ? 'group' : 'groups'}
              </button>
            )}
          </div>
        )
        : (
          <ul ref={list} className="grid min-h-0 flex-1 grid-cols-[repeat(auto-fill,minmax(140px,1fr))] content-start gap-x-3 gap-y-5 overflow-y-auto px-4 pb-6 pt-1.5 lg:grid-cols-[repeat(auto-fill,minmax(150px,1fr))] lg:gap-x-3.5 lg:px-5">
            {groups.map(g => {
              const summary = groupSummary(g, mode), on = chosen.has(g.key);
              return (
                <li key={g.key} className="relative">
                  <button type="button" aria-pressed={g.key === selectedKey} onClick={() => onSelect(g.key)} {...hover.on(g.key, g)} className="block w-full text-left">
                    <span className={`relative block aspect-square overflow-hidden rounded-[10px] ${g.key === selectedKey ? 'shadow-selected' : ''}`}>
                      <PhotoThumb photo={g.keeper} className="h-full w-full" />
                      {g.keeper.isVideo && <span className="absolute bottom-2 left-2 rounded bg-white/15 px-1.5 text-[11px] font-bold text-white">VIDEO</span>}
                      <span className="absolute right-2 top-2 rounded-full bg-black/55 px-2 font-mono text-[11.5px] text-white">+{g.members.length}</span>
                    </span>
                    <span className="mt-2 block truncate text-[14px] font-medium">{g.keeper.fileName}</span>
                    <span className={`block truncate text-[12.5px] ${summary.allKept ? 'text-keep' : 'text-t2'}`}>{summary.text}</span>
                  </button>
                  {/* Beside the tile, not inside it: the tile shows the group, the tick selects it. */}
                  <button type="button" role="checkbox" aria-checked={on} aria-label={`Select the group of ${g.keeper.fileName}`} onClick={() => onChoose(g)}
                    className={`absolute left-[7px] top-[7px] flex h-[22px] w-[22px] items-center justify-center rounded-md border-[1.5px] text-[12px] font-bold text-on-brand ${on ? 'border-brand bg-brand' : 'border-white/85 bg-black/35 hover:bg-black/55'}`}>
                    {on && '✓'}
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      {hover.card(g => (
        <>
          <div className="mb-2 flex items-start gap-2">
            <span className="min-w-0 flex-1 text-[14.5px] font-semibold text-t1 [overflow-wrap:anywhere]">{g.keeper.fileName}</span>
            <span className="badge bg-keep-bg text-keep">{mode === 'similar' ? 'Best shot' : 'Original'}</span>
          </div>
          <PhotoFacts photo={g.keeper} />
          <div className="mt-3 border-t border-line pt-3 text-t2">
            <p>{mode === 'similar' ? 'The best shot of its group, which stays.' : 'The best copy, which stays.'}</p>
            <p className="text-t3">With it: {groupSummary(g, mode).text}</p>
          </div>
        </>
      ))}
      {footer}
    </section>
  );
}
