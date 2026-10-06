import React from 'react';
import clsx from 'clsx';
import type { DuplicateGroupDto, GroupMode } from '../types';
import { formatBytes } from '../lib/format';
import { PhotoThumb } from './PhotoThumb';

interface Props {
  readonly groups: DuplicateGroupDto[];
  readonly mode: GroupMode;
  readonly selectedKey?: string;
  onSelect(key: string): void;
}

function summary(group: DuplicateGroupDto, mode: GroupMode): string {
  const n = group.members.length;
  if (mode === 'similar') return `${n} similar ${n === 1 ? 'shot' : 'shots'}`;
  const kept = group.members.filter(m => m.photo.kept).length;
  const removable = n - kept;
  if (removable === 0) return `${n} ${n === 1 ? 'copy' : 'copies'}, all marked keep`;
  return `${removable} ${removable === 1 ? 'duplicate' : 'duplicates'} · ${formatBytes(group.reclaimableBytes)}`;
}

export function GroupList({ groups, mode, selectedKey, onSelect }: Props) {
  if (groups.length === 0)
    return <div className="text-xs text-neutral-500 py-8 text-center px-3">{mode === 'similar' ? 'No similar shots found.' : 'No duplicates to show.'}</div>;
  return (
    <ul className="overflow-y-auto flex-1">
      {groups.map(g => (
        <li key={g.key}>
          <button type="button" onClick={() => onSelect(g.key)}
            className={clsx('w-full flex items-center gap-3 px-3 py-2 text-left border-l-2 hover:bg-neutral-700/50',
              g.key === selectedKey ? 'bg-neutral-700/70 border-emerald-400' : 'border-transparent')}>
            <PhotoThumb photo={g.keeper} className="w-14 h-14 rounded shrink-0" />
            <span className="min-w-0">
              <span className="block text-xs text-neutral-200 truncate">{g.keeper.fileName}</span>
              <span className="block text-[11px] text-neutral-400">{summary(g, mode)}</span>
            </span>
          </button>
        </li>
      ))}
    </ul>
  );
}
