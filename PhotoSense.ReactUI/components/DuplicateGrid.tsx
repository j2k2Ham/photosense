import React from 'react';
import type { DuplicateGroupDto, PhotoDto } from '../types';
import { DuplicateCard } from './DuplicateCard';

interface Props { groups: DuplicateGroupDto[]; onSelect(p: PhotoDto): void; }

export function DuplicateGrid({ groups, onSelect }: Props) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(120px,1fr))] gap-3 p-3 overflow-y-auto">
      {groups.map(g => <DuplicateCard key={g.key} group={g} onSelect={onSelect} />)}
      {groups.length === 0 && <div className="text-xs text-neutral-500 col-span-full py-6 text-center">No groups</div>}
    </div>
  );
}
