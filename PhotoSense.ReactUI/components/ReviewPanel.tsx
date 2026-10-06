import React from 'react';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode } from '../types';
import { formatBytes } from '../lib/format';
import { DuplicateStrip } from './DuplicateStrip';
import { PhotoDetails } from './PhotoDetails';
import { PhotoView } from './PhotoThumb';

interface Props {
  readonly group?: DuplicateGroupDto;
  readonly mode: GroupMode;
  readonly busy: boolean;
  /** Opens the floating window on a copy, or on the original when no member is given. */
  onOpen(member?: GroupMemberDto): void;
  onRemoveGroup(group: DuplicateGroupDto): void;
}

/** The best copy shown as the original, with every photo matched against it underneath. */
export function ReviewPanel({ group, mode, busy, onOpen, onRemoveGroup }: Props) {
  if (!group) return <div className="panel flex-1 flex items-center justify-center text-sm text-neutral-500">Select a group to review</div>;
  const original = group.keeper;
  const removable = group.members.filter(m => !m.photo.kept).length;
  const noun = original.isVideo ? 'video' : 'picture';

  return (
    <div className="panel flex-1 min-w-0 flex flex-col overflow-hidden">
      <div className="px-4 py-2 border-b border-neutral-700 flex items-center gap-3 text-xs">
        <span className="badge bg-emerald-500 text-neutral-900">{mode === 'similar' ? 'BEST' : 'ORIGINAL'}</span>
        <span className="text-neutral-300">
          {mode === 'similar' ? 'The best of these similar shots.' : `The best copy of this ${noun}. It stays.`}
        </span>
      </div>

      <button type="button" onClick={() => onOpen()} title="Open this file" className="flex-1 min-h-0 cursor-zoom-in">
        <PhotoView photo={original} />
      </button>

      <div className="px-4 py-3 border-t border-neutral-700">
        <PhotoDetails photo={original} />
      </div>

      <div className="px-4 py-3 border-t border-neutral-700 max-h-[38%] overflow-y-auto">
        <div className="flex items-center gap-3 mb-2">
          <h3 className="text-xs font-semibold">
            {mode === 'similar' ? `Similar shots (${group.members.length})` : `Duplicates of this ${noun} (${group.members.length})`}
          </h3>
          <span className="text-[11px] text-neutral-500">Hover for details, click to open the file</span>
          {mode === 'duplicates' && removable > 0 && (
            <button type="button" disabled={busy} onClick={() => onRemoveGroup(group)} className="btn-danger ml-auto py-1 px-3 text-xs">
              Delete {removable === 1 ? 'this duplicate' : `these ${removable} duplicates`} · {formatBytes(group.reclaimableBytes)}
            </button>
          )}
        </div>
        <DuplicateStrip members={group.members} onOpen={onOpen} />
      </div>
    </div>
  );
}
