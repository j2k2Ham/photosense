import React, { useEffect, useRef } from 'react';
import type { DuplicateGroupDto, GroupMode } from '../types';
import { groupSummary } from '../lib/format';
import { PhotoThumb } from './PhotoThumb';

interface Props {
  readonly groups: DuplicateGroupDto[];
  readonly mode: GroupMode;
  /** Groups in every page together; absent while the first answer is awaited. */
  readonly total?: number;
  readonly page: number;
  readonly totalPages: number;
  readonly query: string;
  readonly selectedKey?: string;
  onSelect(key: string): void;
  onPage(page: number): void;
}

function emptyMessage(mode: GroupMode, query: string): string {
  if (query.trim()) return `No groups match "${query.trim()}"`;
  return mode === 'similar' ? 'No similar shots found' : 'No duplicates to show';
}

/** The groups as a gallery of their best copies, a page at a time. */
export function GroupList({ groups, mode, total, page, totalPages, query, selectedKey, onSelect, onPage }: Props) {
  const pager = 'pill-outline h-[30px] px-3 text-[13px]';
  const list = useRef<HTMLUListElement>(null);
  // A page starts at its top, wherever the one before it was left.
  useEffect(() => {
    if (list.current) list.current.scrollTop = 0;
  }, [page, mode, query]);
  return (
    <section aria-label="Groups" className="flex min-h-0 flex-col border-r border-line bg-s1">
      <div className="flex items-center px-5 py-3.5 text-[13px] text-t2">
        <span>{total === undefined ? 'Loading…' : <><span className="font-semibold text-t1">{total.toLocaleString()}</span> {total === 1 ? 'group' : 'groups'}</>}</span>
        {totalPages > 1 && (
          <span className="ml-auto flex items-center gap-2.5">
            <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} className={pager}>Prev</button>
            <span>Page {page} of {totalPages}</span>
            <button type="button" disabled={page >= totalPages} onClick={() => onPage(page + 1)} className={pager}>Next</button>
          </span>
        )}
      </div>

      {groups.length === 0
        ? total !== undefined && <p className="px-5 py-10 text-center text-[14px] text-t3">{emptyMessage(mode, query)}</p>
        : (
          <ul ref={list} className="grid min-h-0 flex-1 grid-cols-[repeat(auto-fill,minmax(150px,1fr))] content-start gap-x-3.5 gap-y-5 overflow-y-auto px-5 pb-6 pt-1.5">
            {groups.map(g => {
              const summary = groupSummary(g, mode);
              return (
                <li key={g.key}>
                  <button type="button" aria-pressed={g.key === selectedKey} onClick={() => onSelect(g.key)} className="block w-full text-left">
                    <span className={`relative block aspect-square overflow-hidden rounded-[10px] ${g.key === selectedKey ? 'shadow-selected' : ''}`}>
                      <PhotoThumb photo={g.keeper} className="h-full w-full" />
                      {g.keeper.isVideo && <span className="absolute left-2 top-2 rounded bg-white/15 px-1.5 text-[11px] font-bold text-white">VIDEO</span>}
                      <span className="absolute right-2 top-2 rounded-full bg-black/55 px-2 font-mono text-[11.5px] text-white">+{g.members.length}</span>
                    </span>
                    <span className="mt-2 block truncate text-[14px] font-medium">{g.keeper.fileName}</span>
                    <span className={`block truncate text-[12.5px] ${summary.allKept ? 'text-keep' : 'text-t2'}`}>{summary.text}</span>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
    </section>
  );
}
