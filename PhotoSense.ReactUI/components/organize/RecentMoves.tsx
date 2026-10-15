import React, { useEffect } from 'react';
import { formatBytes, leaf } from '../../lib/format';
import type { OrganizeBatchDto } from '../../types';
import { countFiles } from './shared';

interface Props {
  readonly batches: readonly OrganizeBatchDto[];
  readonly busy: boolean;
  onUndo(batch: OrganizeBatchDto): void;
  onClose(): void;
}

/** What was moved, copied or deleted lately, each with the way to take it back. */
export function RecentMovesWindow({ batches, busy, onUndo, onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-6" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label="Recently moved" className="flex max-h-full w-[560px] max-w-full flex-col gap-4 rounded-[20px] border border-line bg-pop p-7 shadow-dialog">
        <div className="flex items-start gap-3">
          <h2 className="flex-1 text-[21px] font-semibold">Recently moved</h2>
          <button type="button" onClick={onClose} aria-label="Close" className="pill-quiet -mr-2 -mt-1 h-[38px] w-[38px] shrink-0 text-[18px] leading-none">×</button>
        </div>
        <p className="text-[14px] text-t2">What was moved, copied or deleted lately. Undo puts the files back where they were.</p>
        {batches.length === 0
          ? <p className="py-4 text-center text-[14px] text-t3">Nothing moved lately can be undone now.</p>
          : (
            <ul className="flex min-h-0 flex-col gap-2.5 overflow-y-auto">
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
          )}
        <div className="flex justify-end pt-1">
          <button type="button" onClick={onClose} className="pill-brand h-11 px-6 text-[15px]">Done</button>
        </div>
      </div>
    </div>
  );
}
