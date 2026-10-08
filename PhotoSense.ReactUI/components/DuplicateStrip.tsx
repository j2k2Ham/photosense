import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import type { GroupMemberDto, PhotoDto } from '../types';
import { formatFile, formatPlace, formatTaken, matchLabel } from '../lib/format';
import { Differences } from './Differences';
import { MatchChip } from './MatchChip';
import { PhotoThumb } from './PhotoThumb';

interface Props {
  /** The best copy, which every member was matched with. */
  readonly original: PhotoDto;
  readonly members: GroupMemberDto[];
  /** The copy shown beside the original. */
  readonly selectedIndex: number;
  onSelect(index: number): void;
}

interface Hover { member: GroupMemberDto; left: number; bottom: number; }

const CARD_WIDTH = 420;

/** A tile for every photo matched against the original. Hover for details; click to show it beside the original. */
export function DuplicateStrip({ original, members, selectedIndex, onSelect }: Props) {
  const [hover, setHover] = useState<Hover>();

  // The card sits above the tile it describes, held inside the window.
  function show(member: GroupMemberDto, e: React.MouseEvent<HTMLElement>) {
    const tile = e.currentTarget.getBoundingClientRect();
    setHover({ member, left: Math.max(12, Math.min(tile.left, window.innerWidth - CARD_WIDTH - 12)), bottom: window.innerHeight - tile.top + 10 });
  }

  return (
    <div className="flex flex-wrap gap-3">
      {members.map((m, i) => (
        <div key={m.photo.id} className="w-[120px]">
          <button type="button" aria-label={`Show ${m.photo.fileName}`} aria-pressed={i === selectedIndex}
            onClick={() => { setHover(undefined); onSelect(i); }} onMouseEnter={e => show(m, e)} onMouseLeave={() => setHover(undefined)}
            className={`relative block h-[84px] w-[120px] overflow-hidden rounded-[10px] bg-s3 ${i === selectedIndex ? 'ring-2 ring-brand' : ''}`}>
            <PhotoThumb photo={m.photo} className="h-full w-full" />
            {/* The format in plain sight: a HEIC and its JPEG conversion are otherwise told apart only by their names. */}
            {m.photo.format && <span className="absolute left-1.5 top-1.5 rounded bg-black/55 px-1.5 font-mono text-[11px] text-white">{m.photo.format}</span>}
            <span className={`absolute inset-x-0 bottom-0 py-0.5 text-center text-[10px] font-bold uppercase tracking-[0.06em] ${m.photo.kept ? 'bg-keep text-on-brand' : 'bg-black/60 text-white'}`}>
              {m.photo.kept ? 'Keeping' : matchLabel[m.match]}
            </span>
          </button>
          <p aria-hidden className="mt-1 truncate text-center text-[11.5px] text-t2" title={m.photo.fileName}>{m.photo.fileName}</p>
        </div>
      ))}
      {/* Rendered on the page body, so that it is placed against the window and never clipped by the desk. */}
      {hover && createPortal(<HoverCard hover={hover} original={original} />, document.body)}
    </div>
  );
}

function HoverCard({ hover, original }: { hover: Hover; original: PhotoDto }) {
  const { photo, keeperReason, match } = hover.member;
  return (
    <div role="tooltip" style={{ left: hover.left, bottom: hover.bottom, width: CARD_WIDTH }}
      className="pointer-events-none fixed z-40 rounded-[14px] border border-line bg-pop p-4 text-[13.5px] shadow-pop">
      <div className="flex items-start gap-2">
        <span className="min-w-0 flex-1 text-[14.5px] font-semibold [overflow-wrap:anywhere]">{photo.fileName}</span>
        <MatchChip match={match} kept={photo.kept} />
      </div>
      <dl className="mt-2 grid grid-cols-[4.5rem_1fr] gap-x-2 gap-y-0.5 text-t2">
        <dt className="text-t3">Taken</dt><dd>{formatTaken(photo.takenOn)}</dd>
        <dt className="text-t3">Folder</dt><dd className="font-mono text-[12.5px] [overflow-wrap:anywhere]">{photo.folder}</dd>
        <dt className="text-t3">Place</dt><dd>{formatPlace(photo)}</dd>
        <dt className="text-t3">File</dt><dd>{formatFile(photo)}</dd>
      </dl>
      <div className="mt-3 border-t border-line pt-3"><Differences original={original} copy={photo} /></div>
      <div className="mt-3 border-t border-line pt-3 text-t2">
        {match === 'similar' ? 'A different shot or an edited version. Never removed along with the duplicates.' : <>Original preferred: <span className="font-medium text-keep">{keeperReason}</span></>}
      </div>
    </div>
  );
}
