import React from 'react';
import type { GroupMemberDto, PhotoDto } from '../types';
import { matchLabel } from '../lib/format';
import { Differences } from './Differences';
import { useHoverCard } from './HoverCard';
import { MatchChip } from './MatchChip';
import { PhotoFacts, PhotoThumb } from './PhotoThumb';

interface Props {
  /** The best copy, which every member was matched with. */
  readonly original: PhotoDto;
  readonly members: GroupMemberDto[];
  /** The copy shown beside the original. */
  readonly selectedIndex: number;
  onSelect(index: number): void;
}

/** A tile for every photo matched against the original. Hover for details; click to show it beside the original. */
export function DuplicateStrip({ original, members, selectedIndex, onSelect }: Props) {
  const hover = useHoverCard<GroupMemberDto>();
  return (
    <div className="flex flex-wrap gap-3">
      {members.map((m, i) => (
        <div key={m.photo.id} className="w-[100px] sm:w-[120px]">
          <button type="button" aria-label={`Show ${m.photo.fileName}`} aria-pressed={i === selectedIndex} onClick={() => onSelect(i)} {...hover.on(m.photo.id, m)}
            className={`relative block h-[70px] w-[100px] overflow-hidden sm:h-[84px] sm:w-[120px] rounded-[10px] bg-s3 ${i === selectedIndex ? 'ring-2 ring-brand' : ''}`}>
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
      {hover.card(m => <CopyFacts member={m} original={original} />, 420)}
    </div>
  );
}

function CopyFacts({ member, original }: { readonly member: GroupMemberDto; readonly original: PhotoDto }) {
  const { photo, keeperReason, match } = member;
  return (
    <>
      <div className="mb-2 flex items-start gap-2">
        <span className="min-w-0 flex-1 text-[14.5px] font-semibold text-t1 [overflow-wrap:anywhere]">{photo.fileName}</span>
        <MatchChip match={match} kept={photo.kept} />
      </div>
      <PhotoFacts photo={photo} />
      <div className="mt-3 border-t border-line pt-3"><Differences original={original} copy={photo} /></div>
      <div className="mt-3 border-t border-line pt-3 text-t2">
        {match === 'similar' ? 'A different shot or an edited version. Never removed along with the duplicates.' : <>Original preferred: <span className="font-medium text-keep">{keeperReason}</span></>}
      </div>
    </>
  );
}
