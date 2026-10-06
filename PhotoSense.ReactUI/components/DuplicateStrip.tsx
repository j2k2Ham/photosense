import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import clsx from 'clsx';
import type { GroupMemberDto } from '../types';
import { formatFile, formatPlace, formatTaken, matchLabel } from '../lib/format';
import { PhotoThumb } from './PhotoThumb';

interface Props {
  readonly members: GroupMemberDto[];
  onOpen(member: GroupMemberDto): void;
}

interface Hover { member: GroupMemberDto; x: number; y: number; }

const CARD_WIDTH = 320;

/** Thumbnails of every photo matched against the original. Hover for details, click to open the file. */
export function DuplicateStrip({ members, onOpen }: Props) {
  const [hover, setHover] = useState<Hover>();

  function track(member: GroupMemberDto, e: React.MouseEvent) {
    setHover({ member, x: e.clientX, y: e.clientY });
  }

  return (
    <div className="flex flex-wrap gap-2">
      {members.map(m => (
        <button key={m.photo.id} type="button" onClick={() => { setHover(undefined); onOpen(m); }}
          onMouseEnter={e => track(m, e)} onMouseMove={e => track(m, e)} onMouseLeave={() => setHover(undefined)}
          aria-label={`Open ${m.photo.fileName}`}
          className={clsx('relative w-24 h-24 rounded-md overflow-hidden border-2 bg-neutral-700 focus:outline-none focus:ring-2 focus:ring-emerald-400',
            m.photo.kept ? 'border-emerald-500' : 'border-transparent hover:border-neutral-400')}>
          <PhotoThumb photo={m.photo} className="w-full h-full" />
          <span className={clsx('absolute bottom-0 inset-x-0 text-[9px] font-semibold tracking-wide text-center py-0.5',
            m.match === 'similar' ? 'bg-amber-500/80 text-neutral-900' : 'bg-neutral-900/75 text-neutral-200')}>
            {m.photo.kept ? 'KEEPING' : matchLabel[m.match].toUpperCase()}
          </span>
        </button>
      ))}
      {/* Rendered on the page body: inside a panel, "fixed" would be measured from the panel, not the window. */}
      {hover && createPortal(<HoverCard hover={hover} />, document.body)}
    </div>
  );
}

// Follows the pointer and stays inside the window.
function HoverCard({ hover }: { hover: Hover }) {
  const { photo, keeperReason, match } = hover.member;
  const left = Math.min(hover.x + 16, window.innerWidth - CARD_WIDTH - 12);
  const flipUp = hover.y > window.innerHeight - 230;
  return (
    <div role="tooltip" style={{ left, width: CARD_WIDTH, ...(flipUp ? { bottom: window.innerHeight - hover.y + 12 } : { top: hover.y + 16 }) }}
      className="fixed z-40 pointer-events-none rounded-md border border-neutral-600 bg-neutral-900/95 shadow-xl p-3 text-[11px] leading-relaxed">
      <div className="text-xs font-semibold text-neutral-100 break-all">{photo.fileName}</div>
      <dl className="mt-1 grid grid-cols-[4.5rem_1fr] gap-x-2 text-neutral-300">
        <dt className="text-neutral-500">Date</dt><dd>{formatTaken(photo.takenOn)}</dd>
        <dt className="text-neutral-500">Folder</dt><dd className="break-all">{photo.folder}</dd>
        <dt className="text-neutral-500">Taken at</dt><dd>{formatPlace(photo)}</dd>
        <dt className="text-neutral-500">File</dt><dd>{formatFile(photo)}</dd>
      </dl>
      <div className="mt-2 pt-2 border-t border-neutral-700 text-neutral-400">
        {match === 'similar' ? 'A different shot or an edited version. Not removed in bulk.' : <>Original preferred: {keeperReason}</>}
      </div>
    </div>
  );
}
