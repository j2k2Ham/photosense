import React from 'react';
import { formatBytes, leaf } from '../../lib/format';
import type { OrganizeBatchDto } from '../../types';
import { countFiles } from './shared';

/** What was moved, copied or deleted lately, each with the way to take it back. */
export function RecentMoves({ batches, busy, onUndo }: { readonly batches: readonly OrganizeBatchDto[]; readonly busy: boolean; onUndo(batch: OrganizeBatchDto): void }) {
  return (
    <>
      <h3 className="mt-2 border-t border-line pt-[18px] text-[17px] font-semibold">Recently moved</h3>
      <ul className="flex flex-col gap-2.5">
        {batches.map(b => {
          // A deletion is known by the folder the files were taken out of; anything else by the folder they went to.
          const gone = b.mode === 'remove', what = gone ? `deleted from ${leaf(b.label)}` : `${b.mode === 'copy' ? 'copied' : 'moved'} to ${b.label}`;
          return (
          <li key={b.id} className="flex items-center gap-3 rounded-xl bg-keep-bg py-2.5 pl-3.5 pr-2.5">
            <span aria-hidden className="h-2 w-2 shrink-0 rounded-full bg-keep" />
            <span className="flex min-w-0 flex-1 flex-col gap-px">
              <span className="truncate text-[14px] font-medium">{countFiles(b.count)} {what}</span>
              <span className="text-[12px] text-t2">{formatBytes(b.bytes)} · {new Date(b.utc).toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })}</span>
            </span>
            <button type="button" disabled={busy} aria-label={gone ? `Undo deleting from ${leaf(b.label)}` : `Undo ${b.label}`} onClick={() => onUndo(b)} className="pill h-8 shrink-0 border border-line bg-s1 px-3.5 text-[13px] hover:bg-s2 disabled:opacity-50">Undo</button>
          </li>
          );
        })}
      </ul>
    </>
  );
}
