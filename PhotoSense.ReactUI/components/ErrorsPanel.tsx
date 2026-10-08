import React from 'react';
import type { ErrorEntry } from './Toaster';
import { useDismiss } from '../lib/useDismiss';

interface Props {
  readonly errors: ErrorEntry[];
  onClear(id: number): void;
  onClearAll(): void;
  onClose(): void;
}

/** Every problem since the page was opened, kept until it is cleared: a toast that has gone cannot be read again. */
export function ErrorsPanel({ errors, onClear, onClearAll, onClose }: Props) {
  const ref = useDismiss<HTMLDivElement>(onClose);
  return (
    <div ref={ref} role="dialog" aria-label="Errors" className="fixed right-6 top-[58px] z-40 flex max-h-[480px] w-[440px] flex-col rounded-2xl border border-line bg-pop shadow-pop">
      <div className="flex items-center px-4 pb-2 pt-3.5">
        <h2 className="text-[15px] font-semibold">Errors</h2>
        {errors.length > 0 && <button type="button" onClick={onClearAll} className="pill-quiet ml-auto h-[30px] px-3.5 text-[13px]">Clear all</button>}
      </div>
      {errors.length === 0
        ? <p className="px-4 pb-4 text-[13.5px] text-t3">No errors. Problems stay listed here until you clear them.</p>
        : (
          <ul className="overflow-y-auto px-2 pb-2">
            {errors.map(e => (
              <li key={e.id} className="flex items-start gap-3 rounded-[10px] px-2 py-2.5">
                <span aria-hidden className="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-rose" />
                <span className="min-w-0 flex-1">
                  <span className="block break-words text-[14px]">{e.message}</span>
                  <span className="text-[12px] text-t3">{e.time.toLocaleTimeString()}</span>
                </span>
                <button type="button" onClick={() => onClear(e.id)} className="pill-quiet h-7 shrink-0 px-3 text-[12.5px]">Clear</button>
              </li>
            ))}
          </ul>
        )}
    </div>
  );
}
