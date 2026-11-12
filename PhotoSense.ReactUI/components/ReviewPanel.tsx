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
  /** Whether the review shows, as a display class: on a narrow screen it takes the list's place. */
  readonly className?: string;
  /** Goes back to the list, on a narrow screen where the two take turns. */
  onBack?(): void;
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
      <div className={`relative h-[clamp(150px,26vh,300px)] overflow-hidden rounded-[14px] bg-stage ${original ? 'shadow-[inset_0_0_0_2px_var(--keep-line)]' : 'shadow-[inset_0_0_0_1px_var(--line)]'}`}>
        <button type="button" onClick={onCompare} aria-label={`Compare ${photo.fileName}`} className="block h-full w-full cursor-zoom-in p-0.5">
          <PhotoStill photo={photo} />
        </button>
        {photo.isVideo && <button type="button" onClick={() => onOpenInViewer(photo)} className="pill-quiet absolute right-2 top-2 h-8 px-3 text-[13px] lg:right-3 lg:top-3 lg:px-3.5">Open in default player</button>}
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
export function ReviewPanel({ group, mode, busy, copyIndex, onSelectCopy, onCompare, onToggleKeep, onDeleteCopy, onDeleteGroup, onOpenInViewer, className = 'flex', onBack }: Props) {
  if (!group) return <div className={`${className} flex-1 items-center justify-center text-[16px] text-t3`}>Select a group to review</div>;

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
  const offerAll = removable > (similar ? 1 : 0);

  return (
    <section aria-label="Review" className={`${className} min-h-0 min-w-0 flex-1 flex-col`}>
      <div className="flex min-h-0 flex-1 flex-col gap-[18px] overflow-y-auto px-4 pb-5 pt-4 lg:px-7 lg:pt-[22px]">
        {onBack && (
          <button type="button" onClick={onBack} className="pill-outline h-9 self-start px-3.5 text-[13.5px] font-medium lg:hidden">
            <span aria-hidden>‹</span>All groups
          </button>
        )}
        <div className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
          <h2 className="min-w-0 max-w-full truncate text-[18px] font-semibold tracking-[-0.01em] lg:text-[20px]">{original.fileName}</h2>
          <span className="shrink-0 text-[14px] text-t2">{summary.allKept ? 'All copies marked keep' : similar ? summary.text : `${summary.text} to free`}</span>
        </div>

        <div className="grid grid-cols-2 gap-3 lg:gap-5">
          <div className="flex min-w-0 flex-col gap-2.5">
            <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[13.5px] text-t2">
              <span className="badge bg-keep-bg text-keep">{similar ? 'Best' : 'Original'}</span>
              {similar ? 'The best of these similar shots.' : `The best copy of this ${noun}. It stays.`}
            </div>
            <Pane photo={original} original onCompare={onCompare} onOpenInViewer={onOpenInViewer} />
          </div>
          <div className="flex min-w-0 flex-col gap-2.5">
            <div className="flex flex-wrap items-center gap-x-2.5 gap-y-1 text-[13.5px] text-t2">
              <MatchChip match={member.match} kept={copy.kept} />
              Copy {index + 1} of {group.members.length} · {copy.format ?? '?'}
              <span className="ml-auto hidden text-t3 xl:inline">Click either to compare</span>
            </div>
            <Pane photo={copy} sameFolder={copy.folder === original.folder ? `In the same folder as the ${similar ? 'best shot' : 'original'}` : undefined} onCompare={onCompare} onOpenInViewer={onOpenInViewer} />
          </div>
        </div>

        <div>
          <div className="mb-2.5 flex items-baseline gap-3">
            <h3 className="text-[15px] font-semibold">{similar ? `Similar shots (${group.members.length})` : `Duplicates of this ${noun} (${group.members.length})`}</h3>
            <span className="hidden text-[13px] text-t3 lg:inline">Hover for details, click to show it beside the original</span>
          </div>
          <DuplicateStrip original={original} members={group.members} selectedIndex={index} onSelect={onSelectCopy} />
        </div>

        <DiffTable original={original} copy={copy} />
      </div>

      {/* What can be done with the group stays in view, however far the details above it are scrolled. */}
      <div className="flex shrink-0 flex-wrap items-center gap-2.5 border-t border-line px-4 py-3.5 lg:gap-3 lg:px-7 lg:py-[18px]">
        {/* A line of its own on a narrow screen; on a wide one it shares the row with the buttons, as wide as they leave it. */}
        <div className="min-w-0 basis-full lg:min-w-[260px] lg:flex-1">
          <p className="text-[13.5px] text-t2 lg:text-[14px]">
            {similar ? 'A burst frame or an edited version. Never removed along with the duplicates.' : <>Original preferred: <span className="font-medium text-keep">{member.keeperReason}</span></>}
          </p>
          <p className="mt-0.5 hidden text-[12.5px] text-t3 sm:block">Deleting moves files to <span className="font-mono">_PhotoSense_Removed</span> inside the scanned folder, and Undo puts them back.</p>
        </div>
        {!similar && (
          <button type="button" disabled={busy} onClick={() => onToggleKeep(copy)} className={`pill-keep h-11 px-5 text-[14.5px] max-sm:flex-1 ${copy.kept ? 'bg-keep-bg' : ''}`}>
            {copy.kept ? 'Stop keeping this copy' : 'Keep this copy'}
          </button>
        )}
        {offerCopy && (
          <button type="button" disabled={busy || copy.kept} onClick={() => onDeleteCopy(member)} className={`${offerAll ? 'pill-rose-outline' : 'pill-rose'} h-11 px-5 text-[14.5px] max-sm:flex-1`}>
            Delete this copy · {formatBytes(copy.fileSizeBytes)}
          </button>
        )}
        {offerAll && (
          <button type="button" disabled={busy} onClick={() => onDeleteGroup(group)} className="pill-rose h-11 px-5 text-[14.5px] max-sm:flex-1">
            Delete {removable === 1 ? 'this duplicate' : `all ${removable} copies`} · {formatBytes(group.reclaimableBytes)}
          </button>
        )}
      </div>
    </section>
  );
}
