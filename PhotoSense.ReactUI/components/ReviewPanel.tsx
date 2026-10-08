import React from 'react';
import type { DuplicateGroupDto, GroupMemberDto, GroupMode, PhotoDto } from '../types';
import { formatBytes, groupSummary } from '../lib/format';
import { DiffTable } from './Differences';
import { DuplicateStrip } from './DuplicateStrip';
import { MatchChip } from './MatchChip';
import { PhotoStill } from './PhotoThumb';

interface Props {
  readonly group?: DuplicateGroupDto;
  readonly mode: GroupMode;
  readonly busy: boolean;
  /** Which of the group's copies is shown beside the original. */
  readonly copyIndex: number;
  onSelectCopy(index: number): void;
  /** Opens the comparison window on the original and the copy beside it. */
  onCompare(): void;
  onToggleKeep(photo: PhotoDto): void;
  onDeleteCopy(member: GroupMemberDto): void;
  onDeleteGroup(group: DuplicateGroupDto): void;
  onOpenInViewer(photo: PhotoDto): void;
}

interface PaneProps {
  readonly photo: PhotoDto;
  readonly original?: boolean;
  /** Said in place of the folder when the copy sits in the very folder the original is in. */
  readonly sameFolder?: string;
  onCompare(): void;
  onOpenInViewer(photo: PhotoDto): void;
}

function Pane({ photo, original = false, sameFolder, onCompare, onOpenInViewer }: PaneProps) {
  return (
    <>
      <div className={`relative h-[clamp(200px,28vh,300px)] overflow-hidden rounded-[14px] bg-stage ${original ? 'shadow-[inset_0_0_0_2px_var(--keep-line)]' : 'shadow-[inset_0_0_0_1px_var(--line)]'}`}>
        <button type="button" onClick={onCompare} aria-label={`Compare ${photo.fileName}`} className="block h-full w-full cursor-zoom-in p-0.5">
          <PhotoStill photo={photo} />
        </button>
        {photo.isVideo && <button type="button" onClick={() => onOpenInViewer(photo)} className="pill-quiet absolute right-3 top-3 h-8 px-3.5 text-[13px]">Open in default player</button>}
        <span className="pointer-events-none absolute bottom-3 right-3 rounded-full bg-black/55 px-2.5 py-0.5 text-[12px] text-white">{photo.format ?? '?'} · {formatBytes(photo.fileSizeBytes)}</span>
      </div>
      {/* Which file this is, and where: two files holding one picture look alike, and are two files all the same. */}
      <div className="min-w-0 px-0.5" data-file={original ? 'original' : 'copy'}>
        <p className="truncate text-[14.5px] font-semibold" title={photo.fileName}>{photo.fileName}</p>
        <p className={`truncate text-[12.5px] text-t3 ${sameFolder ? '' : 'font-mono'}`} title={photo.folder}>{sameFolder ?? photo.folder}</p>
      </div>
    </>
  );
}

/** The review desk: the best copy beside one of its copies, what differs between them, and what can be done. */
export function ReviewPanel({ group, mode, busy, copyIndex, onSelectCopy, onCompare, onToggleKeep, onDeleteCopy, onDeleteGroup, onOpenInViewer }: Props) {
  if (!group) return <div className="flex flex-1 items-center justify-center text-[16px] text-t3">Select a group to review</div>;

  const original = group.keeper;
  const index = Math.min(copyIndex, group.members.length - 1);
  const member = group.members[index];
  const copy = member.photo;
  const similar = mode === 'similar';
  const noun = original.isVideo ? 'video' : 'picture';
  const removable = group.members.filter(m => !m.photo.kept).length;
  const summary = groupSummary(group, mode);
  // One copy to delete is the whole group's worth; with several, this copy and all of them are offered apart.
  const offerCopy = similar || group.members.length > 1;

  return (
    <section aria-label="Review" className="flex min-w-0 flex-1 flex-col gap-[18px] overflow-y-auto px-7 pb-7 pt-[22px]">
      <div className="flex items-baseline gap-3">
        <h2 className="truncate text-[20px] font-semibold tracking-[-0.01em]">{original.fileName}</h2>
        <span className="shrink-0 text-[14px] text-t2">{summary.allKept ? 'All copies marked keep' : similar ? summary.text : `${summary.text} to free`}</span>
      </div>

      <div className="grid grid-cols-2 gap-5">
        <div className="flex min-w-0 flex-col gap-2.5">
          <div className="flex items-center gap-2.5 text-[13.5px] text-t2">
            <span className="badge bg-keep-bg text-keep">{similar ? 'Best' : 'Original'}</span>
            {similar ? 'The best of these similar shots.' : `The best copy of this ${noun}. It stays.`}
          </div>
          <Pane photo={original} original onCompare={onCompare} onOpenInViewer={onOpenInViewer} />
        </div>
        <div className="flex min-w-0 flex-col gap-2.5">
          <div className="flex items-center gap-2.5 text-[13.5px] text-t2">
            <MatchChip match={member.match} kept={copy.kept} />
            Copy {index + 1} of {group.members.length} · {copy.format ?? '?'}
            <span className="ml-auto text-t3">Click either to compare</span>
          </div>
          <Pane photo={copy} sameFolder={copy.folder === original.folder ? `In the same folder as the ${similar ? 'best shot' : 'original'}` : undefined} onCompare={onCompare} onOpenInViewer={onOpenInViewer} />
        </div>
      </div>

      <div>
        <div className="mb-2.5 flex items-baseline gap-3">
          <h3 className="text-[15px] font-semibold">{similar ? `Similar shots (${group.members.length})` : `Duplicates of this ${noun} (${group.members.length})`}</h3>
          <span className="text-[13px] text-t3">Hover for details, click to show it beside the original</span>
        </div>
        <DuplicateStrip original={original} members={group.members} selectedIndex={index} onSelect={onSelectCopy} />
      </div>

      <DiffTable original={original} copy={copy} />

      <div className="flex flex-wrap items-center gap-3 border-t border-line pt-[18px]">
        <div className="min-w-[260px] flex-1">
          <p className="text-[14px] text-t2">
            {similar ? 'A burst frame or an edited version. Never removed in bulk.' : <>Original preferred: <span className="font-medium text-keep">{member.keeperReason}</span></>}
          </p>
          <p className="mt-0.5 text-[12.5px] text-t3">Deleting moves files to <span className="font-mono">_PhotoSense_Removed</span> inside the scanned folder. Move them back to restore them.</p>
        </div>
        {!similar && (
          <button type="button" disabled={busy} onClick={() => onToggleKeep(copy)} className={`pill-keep h-11 px-5 text-[14.5px] ${copy.kept ? 'bg-keep-bg' : ''}`}>
            {copy.kept ? 'Stop keeping this copy' : 'Keep this copy'}
          </button>
        )}
        {offerCopy && (
          <button type="button" disabled={busy || copy.kept} onClick={() => onDeleteCopy(member)} className={`${similar ? 'pill-rose' : 'pill-rose-outline'} h-11 px-5 text-[14.5px]`}>
            Delete this copy · {formatBytes(copy.fileSizeBytes)}
          </button>
        )}
        {!similar && removable > 0 && (
          <button type="button" disabled={busy} onClick={() => onDeleteGroup(group)} className="pill-rose h-11 px-5 text-[14.5px]">
            Delete {removable === 1 ? 'this duplicate' : `these ${removable} duplicates`} · {formatBytes(group.reclaimableBytes)}
          </button>
        )}
      </div>
    </section>
  );
}
