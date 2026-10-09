import React, { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { browseFolders } from '../lib/apiClient';
import type { FolderDto, FolderListingDto } from '../types';

interface Props {
  readonly title: string;
  /** Where to open: what is already typed, when that is a folder. */
  readonly startAt?: string;
  /** What choosing the folder is called here, when it is not simply "Use this folder". */
  readonly pickLabel?: string;
  /** A second thing that can be done with the folder, offered beside the first. */
  readonly other?: { readonly label: string; onPick(path: string): void };
  onPick(path: string): void;
  onCancel(): void;
}

const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));

/**
 * Browses the folders of the computer the service runs on. A browser's own folder dialog tells a web page
 * the name of the folder that was picked but never where it is, and a scan needs the full path.
 */
export function FolderPicker({ title, startAt, pickLabel = 'Use this folder', other, onPick, onCancel }: Props) {
  const [places, setPlaces] = useState<FolderDto[]>([]);
  const [listing, setListing] = useState<FolderListingDto>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);

  const open = useCallback(async (path: string, quietly = false) => {
    setLoading(true);
    setError(undefined);
    try {
      setListing(await browseFolders(path));
    } catch (e) {
      // What was typed is not a folder: start from the places rather than with a complaint.
      if (!quietly) setError(messageOf(e));
    } finally {
      setLoading(false);
    }
  }, []);

  // Only when the dialog opens: later changes to what is typed do not move it.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => {
    void (async () => {
      try { setPlaces((await browseFolders()).folders); } catch (e) { setError(messageOf(e)); }
      const typed = startAt?.trim();
      if (typed) await open(typed, true); else setLoading(false);
    })();
  }, [open]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel]);

  const here = listing?.path ?? undefined;
  const parent = listing?.parent ?? undefined;

  // Rendered on the page body, over everything else.
  return createPortal(
    <div className="fixed inset-0 z-[46] flex items-center justify-center bg-scrim p-6" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <div role="dialog" aria-modal="true" aria-label={title} className="flex h-[560px] max-h-full w-[720px] max-w-full flex-col rounded-[20px] border border-line bg-pop shadow-dialog">
        <div className="flex flex-col gap-3 px-6 pb-4 pt-5">
          <h2 className="text-[20px] font-semibold">{title}</h2>
          <div className="flex items-center gap-2.5">
            <button type="button" disabled={loading || !parent} onClick={() => parent && open(parent)} className="pill-quiet h-10 shrink-0 px-4 text-[14px]">Up</button>
            <span aria-label="Current folder" className="flex h-10 min-w-0 flex-1 items-center truncate rounded-xl border border-line bg-bg px-3.5 font-mono text-[13px]">{here ?? 'Choose where to start'}</span>
          </div>
        </div>

        <div className="grid min-h-0 flex-1 grid-cols-[180px_1fr] border-t border-line">
          <nav aria-label="Start from" className="flex flex-col gap-1 overflow-y-auto border-r border-line p-3">
            <div className="label-caps px-2 pb-1">Start from</div>
            {places.map(p => (
              <button key={p.path} type="button" disabled={loading} onClick={() => open(p.path)} title={p.path}
                className={`truncate rounded-full px-3 py-2 text-left text-[14px] hover:bg-s2 ${here === p.path ? 'bg-sel font-semibold' : ''}`}>{p.name}</button>
            ))}
          </nav>
          <div className="overflow-y-auto p-3">
            {error && <p role="alert" className="px-3 py-2 text-[13.5px] text-rose-t [overflow-wrap:anywhere]">{error}</p>}
            {loading && !listing && <p className="px-3 py-2 text-[13.5px] text-t3">Loading…</p>}
            {listing && listing.folders.length === 0 && <p className="px-3 py-2 text-[13.5px] text-t3">No folders inside this one.</p>}
            <ul>
              {listing?.folders.map(f => (
                <li key={f.path}>
                  <button type="button" disabled={loading} onClick={() => open(f.path)} title={f.path}
                    className="flex w-full items-center gap-3 rounded-[10px] px-3 py-2.5 text-left text-[14px] hover:bg-s2 disabled:opacity-60">
                    <span aria-hidden className="h-3.5 w-[18px] shrink-0 rounded-[3px] bg-brand" />
                    <span className="min-w-0 flex-1 truncate">{f.name}</span>
                    <span aria-hidden className="text-t3">›</span>
                  </button>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="flex items-center gap-2.5 border-t border-line px-6 py-4">
          <span className="mr-auto text-[12.5px] text-t3">Folders of the computer PhotoSense runs on.</span>
          <button type="button" onClick={onCancel} className="pill-outline h-11 px-5 text-[15px]">Cancel</button>
          {other && <button type="button" disabled={loading || !here} onClick={() => here && other.onPick(here)} className="pill-outline h-11 px-5 text-[15px]">{other.label}</button>}
          <button type="button" disabled={loading || !here} onClick={() => here && onPick(here)} className="pill-brand h-11 px-5 text-[15px]">{pickLabel}</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
