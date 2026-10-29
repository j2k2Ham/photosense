import React, { useState } from 'react';
import { organizeThumbnailUrl } from '../../lib/apiClient';
import { formatDuration } from '../../lib/format';
import type { OrganizeFileDto } from '../../types';

/** "1 file", "4,212 files". */
export const countFiles = (n: number) => `${n.toLocaleString()} ${n === 1 ? 'file' : 'files'}`;

const VIDEO_TILE = 'bg-[#1c2124]';

/** A file's picture, small: its preview, or a dark tile with a play mark for a video (videos are never decoded). */
export function Thumb({ file, className = '', play = 14, duration = false }: { readonly file: Pick<OrganizeFileDto, 'id' | 'isVideo' | 'durationSeconds'>; readonly className?: string; readonly play?: number; readonly duration?: boolean }) {
  if (file.isVideo)
    return (
      <span className={`flex items-center justify-center ${VIDEO_TILE} ${className}`}>
        <span aria-hidden className="ml-1 h-0 w-0 border-y-transparent border-l-white" style={{ borderLeftWidth: play, borderTopWidth: play * 0.64, borderBottomWidth: play * 0.64 }} />
        {duration && file.durationSeconds != null && <span className="absolute right-2 top-2 font-mono text-[11.5px] font-medium text-white">{formatDuration(file.durationSeconds)}</span>}
      </span>
    );
  // The tile is what is dragged, not the picture inside it.
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={organizeThumbnailUrl(file.id)} alt="" loading="lazy" decoding="async" draggable={false} className={`bg-s3 object-cover ${className}`} />;
}

/** The bin, on a button that deletes. */
export function BinIcon({ className = 'h-[18px] w-[18px]' }: { readonly className?: string }) {
  return (
    <svg aria-hidden viewBox="0 0 24 24" className={className} fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M4 7h16M9 7V4h6v3M6 7l1 12a2 2 0 0 0 2 2h6a2 2 0 0 0 2-2l1-12M10 11v6M14 11v6" />
    </svg>
  );
}

/** Four corners pulled apart, on a button that opens something larger. */
export function EnlargeIcon() {
  return (
    <svg aria-hidden viewBox="0 0 24 24" className="h-4 w-4" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M9 4H4v5M15 4h5v5M9 20H4v-5M15 20h5v-5" />
    </svg>
  );
}

/** The tick box over a tile's corner. */
export function Check({ on, small = false }: { readonly on: boolean; readonly small?: boolean }) {
  return (
    <span aria-hidden className={`absolute flex items-center justify-center border-[1.5px] font-bold text-on-brand ${small ? 'left-1.5 top-1.5 h-[18px] w-[18px] rounded-[5px] text-[11px]' : 'left-[7px] top-[7px] h-5 w-5 rounded-md text-[12px]'} ${on ? 'border-brand bg-brand' : 'border-white/85 bg-black/30'}`}>
      {on && '✓'}
    </span>
  );
}

/** Prev, "Page 1 of 71", Next. */
export function Pager({ page, pages, onPage }: { readonly page: number; readonly pages: number; onPage(page: number): void }) {
  const button = 'pill-outline h-[30px] px-[13px] text-[13px] font-normal';
  return (
    <span className="flex items-center gap-2.5 text-[13px] text-t2">
      <button type="button" disabled={page <= 1} onClick={() => onPage(page - 1)} className={button}>Prev</button>
      <span className="whitespace-nowrap">Page {page} of {pages}</span>
      <button type="button" disabled={page >= pages} onClick={() => onPage(page + 1)} className={button}>Next</button>
    </span>
  );
}

/** Makes something a place files can be dropped on. It lights up while they are held over it. */
export function useDrop(onDrop: () => void) {
  const [hot, setHot] = useState(false);
  return {
    hot,
    handlers: {
      // Without this the browser does not let anything be dropped here.
      onDragOver: (e: React.DragEvent) => { e.preventDefault(); setHot(true); },
      onDragLeave: () => setHot(false),
      onDrop: (e: React.DragEvent) => { e.preventDefault(); setHot(false); onDrop(); },
    },
  };
}
