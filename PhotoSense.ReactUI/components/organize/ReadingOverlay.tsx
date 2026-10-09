import React from 'react';
import type { OrganizeReading } from '../../lib/useOrganizeReading';

interface Props {
  /** The folder being read, by its own name. */
  readonly folder: string;
  /** How far the service says it has got; absent until it has begun. */
  readonly progress?: OrganizeReading;
  /** A strip above the files already read, in place of a panel laid over the whole area. */
  readonly compact?: boolean;
}

/**
 * Shown while a folder is being read, so that a long read never looks like nothing happening: a turning
 * ring, and how many of the folder's files have been gone through. Laid over the Organize area until
 * there is a file to show, and a strip above the files from then on.
 */
export function ReadingOverlay({ folder, progress, compact }: Props) {
  const counting = progress?.reading === true && progress.total > 0;
  const percent = counting ? Math.floor(progress.done / progress.total * 100) : 0;
  const bar = counting ? (
    <div role="progressbar" aria-label="Files read" aria-valuemin={0} aria-valuemax={progress.total} aria-valuenow={progress.done}
      className={`overflow-hidden rounded-full bg-s3 ${compact ? 'h-1.5 min-w-[80px] flex-1' : 'h-2 w-full'}`}>
      <div className="h-full rounded-full bg-brand transition-[width] duration-500" style={{ width: `${percent}%` }} />
    </div>
  ) : (
    <div aria-hidden className={`overflow-hidden rounded-full bg-s3 ${compact ? 'h-1.5 min-w-[80px] flex-1' : 'h-2 w-full'}`}><div className="reading-sweep h-full w-1/3 rounded-full bg-brand" /></div>
  );
  // Once the service has been through the files it stops counting, a moment before the whole list arrives.
  const count = counting ? `${progress.done.toLocaleString()} of ${progress.total.toLocaleString()} files · ${percent}%`
    : progress?.reading === false ? 'Putting the list together…' : 'Looking through the folder…';

  if (compact) {
    return (
      <div role="status" aria-label={`Reading ${folder}`} className="flex shrink-0 flex-wrap items-center gap-x-3.5 gap-y-1.5 border-b border-line bg-s1 px-6 py-2.5 text-[13px]">
        <span aria-hidden className="h-4 w-4 shrink-0 animate-spin rounded-full border-2 border-s3 border-t-brand" />
        <span className="shrink-0 font-semibold">Still reading {folder}…</span>
        {bar}
        <span className="shrink-0 font-mono text-t2">{count}</span>
        <span className="shrink-0 text-t3">Folders can be previewed once every file is in.</span>
      </div>
    );
  }
  return (
    <div role="status" aria-label={`Reading ${folder}`} className="absolute inset-0 z-20 flex items-center justify-center bg-scrim">
      <div className="flex w-[480px] max-w-[90%] flex-col items-center gap-4 rounded-[20px] border border-line bg-pop px-8 py-7 text-center shadow-dialog">
        <span aria-hidden className="h-11 w-11 animate-spin rounded-full border-[3px] border-s3 border-t-brand" />
        <p className="text-[18px] font-semibold">Reading dates and places in {folder}…</p>
        {bar}
        <p className="font-mono text-[13px] text-t2">{count}</p>
        <p className="text-[13.5px] text-t2">
          Files a scan has already been through are listed at once. The rest are read one at a time, so a large folder on an external disk can take a few minutes the first time.
          After that it is quick. Files appear here as they are read. Nothing is compared or moved.
        </p>
      </div>
    </div>
  );
}
