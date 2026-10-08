import React, { useEffect, useRef, useState } from 'react';
import type { GroupMemberDto, GroupMode, PhotoDto } from '../types';
import { formatBytes, formatCoordinates, formatFile, formatTaken, hasCoordinates, mapUrl, matchLabel, matchMeaning } from '../lib/format';
import { Differences } from './Differences';
import { MatchChip } from './MatchChip';
import { PhotoView } from './PhotoThumb';

interface Props {
  /** The group's best copy. */
  readonly original: PhotoDto;
  /** The copy it is being compared with. */
  readonly member: GroupMemberDto;
  readonly mode: GroupMode;
  readonly busy: boolean;
  onClose(): void;
  onToggleKeep(photo: PhotoDto): void;
  /** Removes one of the two files; the other is the one that stays. */
  onRemove(photo: PhotoDto, stays: PhotoDto): void;
  onOpenInViewer(photo: PhotoDto): void;
}

type View = 'side' | 'copy' | 'original';
const views: { id: View; name: string }[] = [{ id: 'side', name: 'Side by side' }, { id: 'copy', name: 'This copy' }, { id: 'original', name: 'Original' }];

const caption = (p: PhotoDto) => `${p.fileName} · ${formatFile(p)}`;

/** A file's name and details, with the folder it is in underneath: the two files are told apart by these. */
function Caption({ photo, children }: { readonly photo: PhotoDto; readonly children?: React.ReactNode }) {
  return (
    <figcaption className="min-w-0 text-center text-[13px] text-t2">
      {children} {caption(photo)}
      <span className="block truncate font-mono text-[12px] text-t3" title={photo.folder}>{photo.folder}</span>
    </figcaption>
  );
}

/** The comparison window: the original and a copy side by side, or one at a time in the same spot to flip between. */
export function PhotoWindow({ original, member, mode, busy, onClose, onToggleKeep, onRemove, onOpenInViewer }: Props) {
  const [view, setView] = useState<View>('side');
  // Which deletion is being asked about a second time, in place of its button.
  const [confirm, setConfirm] = useState<'copy' | 'original'>();
  const copy = member.photo;
  const similar = mode === 'similar';
  const shown = view === 'original' ? original : copy;
  // What the file that stays is called here.
  const best = similar ? 'best shot' : 'original';
  const panel = useRef<HTMLDivElement>(null);

  // Focus comes into the window, so that Space flips the view rather than pressing whatever opened it.
  useEffect(() => { panel.current?.focus(); }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      // Space on a button or a player belongs to that control.
      if (e.key === ' ' && !(e.target instanceof Element && e.target.closest('button, a, video, input'))) {
        e.preventDefault();
        setView(v => (v === 'original' ? 'copy' : 'original'));
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-30 bg-scrim p-10" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={panel} tabIndex={-1} role="dialog" aria-modal="true" aria-label={`Compare ${shown.fileName}`} className="flex h-full outline-none flex-col overflow-hidden rounded-[18px] border border-line bg-s1 shadow-dialog">
        <div className="flex h-[72px] shrink-0 items-center gap-3 border-b border-line px-6">
          <span className="truncate text-[18px] font-semibold">{shown.fileName}</span>
          {/* Said outright: the copy and the original are two files, each with a name of its own. */}
          <span className="shrink-0 text-[14px] text-t2">
            {view === 'original' ? `the ${best} · it stays` : <>{similar ? 'similar to' : 'a copy of'} <span className="font-medium text-keep">{original.fileName}</span></>}
          </span>
          <MatchChip match={member.match} kept={copy.kept} />
          <div role="group" aria-label="View" className="mx-auto flex shrink-0 rounded-full bg-s2 p-1">
            {views.map(v => (
              <button key={v.id} type="button" aria-pressed={view === v.id} onClick={() => setView(v.id)}
                className={`pill h-9 px-[18px] text-[14px] ${view === v.id ? 'bg-t1 text-bg' : 'font-medium text-t2'}`}>
                {v.id === 'original' && <span aria-hidden className="h-2 w-2 rounded-full bg-keep" />}{v.name}
              </button>
            ))}
          </div>
          <kbd className="rounded-md border border-line px-2 py-0.5 font-mono text-[12px] text-t3">Esc</kbd>
          <button type="button" onClick={onClose} aria-label="Close" className="pill-quiet h-[38px] w-[38px] text-[18px] leading-none">×</button>
        </div>

        <div className="grid min-h-0 flex-1 grid-cols-[1fr_420px]">
          <div className="flex min-w-0 flex-col gap-3 bg-stage p-6">
            {view === 'side' ? (
              <div className="grid min-h-0 flex-1 grid-cols-2 gap-5">
                <figure className="flex min-h-0 flex-col gap-2">
                  <div className="min-h-0 flex-1 overflow-hidden rounded-[10px] shadow-[inset_0_0_0_3px_var(--keep-line)] p-[3px]"><PhotoView photo={original} onOpenInViewer={onOpenInViewer} /></div>
                  <Caption photo={original}><span className="font-bold text-keep">{similar ? 'BEST' : 'ORIGINAL'}</span></Caption>
                </figure>
                <figure className="flex min-h-0 flex-col gap-2">
                  <div className="min-h-0 flex-1 overflow-hidden rounded-[10px] shadow-[inset_0_0_0_1px_var(--line)] p-px"><PhotoView photo={copy} onOpenInViewer={onOpenInViewer} /></div>
                  <Caption photo={copy}><span className="font-bold">THIS COPY</span></Caption>
                </figure>
              </div>
            ) : (
              <figure className="flex min-h-0 flex-1 flex-col gap-2">
                <div className={`relative min-h-0 flex-1 overflow-hidden rounded-[10px] ${view === 'original' ? 'p-1 shadow-[inset_0_0_0_4px_var(--keep)]' : 'p-px shadow-[inset_0_0_0_1px_var(--line)]'}`}>
                  <PhotoView photo={shown} onOpenInViewer={onOpenInViewer} />
                  <span className={`badge absolute left-4 top-4 ${view === 'original' ? 'bg-keep text-on-brand' : 'bg-black/60 text-white'}`}>
                    {view === 'original' ? `${similar ? 'Best' : 'Original'} · it stays` : `This copy · ${matchLabel[member.match]}`}
                  </span>
                </div>
                <Caption photo={shown} />
              </figure>
            )}
            <p className="text-center text-[12.5px] text-t3">Press <kbd className="rounded border border-line px-1.5 font-mono">Space</kbd> to flip between this copy and the original in the same spot</p>
          </div>

          <aside className="flex min-h-0 flex-col gap-5 overflow-y-auto border-l border-line p-6">
            <div>
              <div className="label-caps">Match</div>
              <p className="mt-1 text-[14px] text-t2"><span className="font-semibold text-t1">{matchLabel[member.match]}.</span> {matchMeaning[member.match]}</p>
              {copy.kept && <span className="badge mt-2 bg-keep-bg text-keep">Keeping</span>}
            </div>

            <dl className="grid grid-cols-[72px_1fr] gap-x-3 gap-y-1.5 text-[13.5px]">
              <dt className="text-t3">Name</dt><dd className="font-medium [overflow-wrap:anywhere]">{copy.fileName}</dd>
              <dt className="text-t3">Taken</dt><dd>{formatTaken(copy.takenOn)}</dd>
              <dt className="text-t3">Folder</dt><dd className="font-mono text-[12.5px] [overflow-wrap:anywhere]">{copy.folder}</dd>
              <dt className="text-t3">Place</dt>
              <dd>
                {copy.placeName && <span className="block">{copy.placeName}</span>}
                <span className={copy.placeName ? 'text-t3' : undefined}>{formatCoordinates(copy)}</span>
                {hasCoordinates(copy) && <> · <a href={mapUrl(copy)} target="_blank" rel="noreferrer" className="text-brand underline">Show on map</a></>}
              </dd>
              <dt className="text-t3">File</dt><dd>{formatFile(copy)}</dd>
              <dt className="text-t3">Camera</dt><dd>{copy.cameraModel ?? 'Not recorded'}</dd>
            </dl>

            <div className="rounded-xl bg-amber-bg p-3.5"><Differences original={original} copy={copy} /></div>

            {!similar && <p className="text-[13.5px] text-t2">Original preferred: <span className="font-medium text-keep">{member.keeperReason}</span></p>}

            <div className="mt-auto flex flex-col gap-2.5">
              <button type="button" disabled={busy} onClick={() => onOpenInViewer(shown)} className="pill-quiet h-11 text-[14px]">Open in default viewer</button>
              {!similar && (
                <button type="button" disabled={busy} onClick={() => onToggleKeep(copy)} className={`pill-keep h-11 text-[14px] ${copy.kept ? 'bg-keep-bg' : ''}`}>
                  {copy.kept ? 'Stop keeping this copy' : 'Keep this copy too'}
                </button>
              )}

              {confirm === 'copy' ? (
                <div className="rounded-[14px] border border-rose-line bg-rose-bg p-3.5 text-[13.5px]">
                  <p>Move <span className="font-semibold [overflow-wrap:anywhere]">{copy.fileName}</span> ({formatBytes(copy.fileSizeBytes)}) to _PhotoSense_Removed?</p>
                  <p className="mt-1.5">The {best}, <span className="font-semibold [overflow-wrap:anywhere]">{original.fileName}</span>, stays where it is. The copy can be moved back later.</p>
                  <div className="mt-3 flex gap-2">
                    <button type="button" onClick={() => setConfirm(undefined)} className="pill-outline h-10 flex-1 text-[14px]">Cancel</button>
                    <button type="button" disabled={busy} onClick={() => onRemove(copy, original)} className="pill-rose h-10 flex-1 text-[14px]">{busy ? 'Working…' : 'Delete this copy'}</button>
                  </div>
                </div>
              ) : (
                <button type="button" disabled={busy} onClick={() => setConfirm('copy')} className="pill-rose h-[46px] text-[14.5px]">Delete this copy · {formatBytes(copy.fileSizeBytes)}</button>
              )}
              <p className="text-[12.5px] text-t3">Deleting moves files to <span className="font-mono">_PhotoSense_Removed</span> inside the scanned folder. Move them back to restore them.</p>

              {confirm === 'original' ? (
                <div className="rounded-[14px] bg-s2 p-3.5 text-[13.5px]">
                  <p>Move <span className="font-semibold [overflow-wrap:anywhere]">{original.fileName}</span> ({formatBytes(original.fileSizeBytes)}) to _PhotoSense_Removed{similar ? '?' : <> and keep <span className="font-semibold [overflow-wrap:anywhere]">{copy.fileName}</span> as the original instead?</>}</p>
                  <div className="mt-3 flex gap-2">
                    <button type="button" onClick={() => setConfirm(undefined)} className="pill-outline h-10 flex-1 text-[14px]">Cancel</button>
                    <button type="button" disabled={busy} onClick={() => onRemove(original, copy)} className="pill-rose-outline h-10 flex-1 text-[14px]">{busy ? 'Working…' : similar ? 'Delete the best shot' : 'Delete the original'}</button>
                  </div>
                </div>
              ) : (
                <button type="button" disabled={busy} onClick={() => setConfirm('original')} className="self-start text-[13px] text-t3 underline disabled:opacity-50">
                  {similar ? 'Delete the best shot instead' : 'Delete the original instead'}
                </button>
              )}
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}
