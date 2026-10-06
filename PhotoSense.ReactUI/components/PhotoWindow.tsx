import React, { useEffect, useState } from 'react';
import clsx from 'clsx';
import type { GroupMemberDto, GroupMode, PhotoDto } from '../types';
import { matchLabel } from '../lib/format';
import { PhotoDetails } from './PhotoDetails';
import { PhotoView } from './PhotoThumb';

interface Props {
  /** The group's original, for flipping back and forth against the opened copy. */
  readonly original: PhotoDto;
  readonly mode: GroupMode;
  /** The copy that was clicked; absent when the original itself was opened. */
  readonly member?: GroupMemberDto;
  readonly busy: boolean;
  onClose(): void;
  onToggleKeep(photo: PhotoDto): void;
  onRemove(photo: PhotoDto): void;
}

/** Floating window showing one image file, so a match can be checked by eye before anything is removed. */
export function PhotoWindow({ original, mode, member, busy, onClose, onToggleKeep, onRemove }: Props) {
  const [showOriginal, setShowOriginal] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const shown = member && !showOriginal ? member.photo : original;
  const onOriginal = shown === original;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-30 flex items-center justify-center bg-black/70 p-6" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label={shown.fileName} className="panel bg-neutral-800 shadow-2xl flex flex-col w-full max-w-6xl h-full max-h-[92vh] overflow-hidden">
        <div className="flex items-center gap-3 px-4 py-2 border-b border-neutral-700">
          <span className="text-sm font-semibold truncate">{shown.fileName}</span>
          {member && (
            <div className="flex rounded-md overflow-hidden border border-neutral-600 text-xs shrink-0" title="Flip between the two files to compare them">
              <button type="button" onClick={() => { setShowOriginal(false); setConfirming(false); }} className={clsx('px-3 py-1', !showOriginal ? 'bg-emerald-500 text-neutral-900 font-semibold' : 'bg-neutral-700 hover:bg-neutral-600')}>This copy</button>
              <button type="button" onClick={() => { setShowOriginal(true); setConfirming(false); }} className={clsx('px-3 py-1', showOriginal ? 'bg-emerald-500 text-neutral-900 font-semibold' : 'bg-neutral-700 hover:bg-neutral-600')}>{mode === 'similar' ? 'Best shot' : 'Original'}</button>
            </div>
          )}
          <button type="button" onClick={onClose} aria-label="Close" className="ml-auto text-neutral-400 hover:text-white text-xl leading-none px-2">×</button>
        </div>

        <div className="flex flex-1 min-h-0">
          <div className="flex-1 min-w-0">
            <PhotoView photo={shown} />
          </div>

          <aside className="w-80 shrink-0 border-l border-neutral-700 p-4 flex flex-col gap-4 overflow-y-auto">
            <div className="text-[11px] font-semibold tracking-wide text-neutral-400">
              {!onOriginal ? matchLabel[member!.match].toUpperCase() : mode === 'similar' ? 'THE BEST OF THESE SIMILAR SHOTS' : 'ORIGINAL — THE BEST COPY, KEPT'}
            </div>
            <PhotoDetails photo={shown} />
            {member && !onOriginal && (
              <p className="text-xs text-neutral-400">
                {member.match === 'similar'
                  ? 'This looks like the best shot but is a different shot or an edited version. It is never removed in bulk.'
                  : <>Original preferred: {member.keeperReason}</>}
              </p>
            )}

            <div className="mt-auto flex flex-col gap-2">
              {member && !onOriginal && member.match !== 'similar' && (
                <button type="button" disabled={busy} onClick={() => onToggleKeep(member.photo)} className="btn-secondary">
                  {member.photo.kept ? 'Stop keeping this copy' : 'Keep this copy too'}
                </button>
              )}
              {confirming ? (
                <div className="rounded-md border border-rose-500/60 p-2 text-xs">
                  <p className="mb-2">
                    Move <span className="font-semibold break-all">{shown.fileName}</span> to the removed folder? Its edit sidecar and Live Photo video go with it, unless another picture of the same shot stays in the folder.
                    {onOriginal && mode === 'duplicates' && ' The next best copy becomes the original.'}
                  </p>
                  <div className="flex gap-2">
                    <button type="button" disabled={busy} onClick={() => onRemove(shown)} className="btn-danger flex-1 py-1">Delete</button>
                    <button type="button" onClick={() => setConfirming(false)} className="btn-secondary flex-1 py-1">Cancel</button>
                  </div>
                </div>
              ) : onOriginal ? (
                // Deliberately the quieter button: the original is the copy that is meant to stay.
                <button type="button" disabled={busy} onClick={() => setConfirming(true)} className="btn-secondary text-rose-300">
                  {mode === 'similar' ? 'Delete this shot' : 'Delete the original instead'}
                </button>
              ) : (
                <button type="button" disabled={busy} onClick={() => setConfirming(true)} className="btn-danger">Delete this copy</button>
              )}
            </div>
          </aside>
        </div>
      </div>
    </div>
  );
}
