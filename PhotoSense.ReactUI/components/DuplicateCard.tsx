import React from 'react';
import type { DuplicateGroupDto, PhotoDto } from '../types';
import clsx from 'clsx';

interface Props { group: DuplicateGroupDto; onSelect(photo: PhotoDto): void; }

export function DuplicateCard({ group, onSelect }: Props) {
  const first = group.photos[0];
  const badgeClass = group.distance != null ? 'badge-near' : 'badge-exact';
  const label = group.distance != null ? `NEAR · ${group.distance}` : 'EXACT';
  return (
    <div className="relative group cursor-pointer" onClick={()=>onSelect(first)}>
      <div className="aspect-square w-full rounded-md bg-neutral-700 flex items-center justify-center text-xs text-neutral-400">IMG</div>
      <div className={clsx('absolute top-1 left-1', badgeClass)}>{label}</div>
      <div className="mt-1 text-[11px] leading-tight line-clamp-2 text-neutral-300">
        {first.fileName}<br/><span className="text-neutral-500">{(first.fileSizeBytes/1024/1024).toFixed(1)} MB</span>
      </div>
    </div>
  );
}
