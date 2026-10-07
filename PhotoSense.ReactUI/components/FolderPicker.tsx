import React, { useCallback, useEffect, useState } from 'react';
import { createPortal } from 'react-dom';
import { browseFolders } from '../lib/apiClient';
import type { FolderListingDto } from '../types';

interface Props {
  readonly title: string;
  /** Where to open: what is already typed, when that is a folder. */
  readonly startAt?: string;
  onPick(path: string): void;
  onCancel(): void;
}

/**
 * Browses the folders of the computer the service runs on. A browser's own folder dialog tells a web page
 * the name of the folder that was picked but never where it is, and a scan needs the full path.
 */
export function FolderPicker({ title, startAt, onPick, onCancel }: Props) {
  const [listing, setListing] = useState<FolderListingDto>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);

  const open = useCallback(async (path?: string, orStartingPlaces = false): Promise<void> => {
    setLoading(true);
    setError(undefined);
    try {
      setListing(await browseFolders(path));
    } catch (e) {
      // What was typed is not a folder: start from the top rather than with a complaint.
      if (orStartingPlaces && path) { await open(); return; }
      setError(e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));
    } finally {
      setLoading(false);
    }
  }, []);

  // Only when the dialog opens: later changes to what is typed do not move it.
  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { void open(startAt?.trim() || undefined, true); }, [open]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onCancel]);

  const here = listing?.path ?? undefined;

  // Rendered on the page body: inside a panel, "fixed" would be measured from the panel, not the window.
  return createPortal(
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/70 p-6" onMouseDown={e => { if (e.target === e.currentTarget) onCancel(); }}>
      <div role="dialog" aria-modal="true" aria-label={title} className="rounded-lg border border-neutral-700 bg-neutral-800 shadow-2xl w-full max-w-lg h-[70vh] flex flex-col">
        <div className="flex items-center gap-3 px-4 py-2 border-b border-neutral-700">
          <h2 className="text-sm font-semibold">{title}</h2>
          <button type="button" onClick={onCancel} aria-label="Close" className="ml-auto text-neutral-400 hover:text-white text-xl leading-none px-2">×</button>
        </div>

        <div className="flex items-center gap-2 px-4 py-2 border-b border-neutral-700">
          <button type="button" disabled={loading || !here} onClick={() => open(listing?.parent ?? undefined)} className="btn-secondary px-2 py-1 text-xs shrink-0">Up</button>
          <span className="text-xs font-mono break-all" aria-label="Current folder">{here ?? 'This computer'}</span>
        </div>

        <div className="flex-1 overflow-y-auto py-1">
          {error && <p role="alert" className="px-4 py-2 text-xs text-rose-400 break-words">{error}</p>}
          {!listing && loading && <p className="px-4 py-2 text-xs text-neutral-500">Loading…</p>}
          {listing && listing.folders.length === 0 && <p className="px-4 py-2 text-xs text-neutral-500">No folders inside this one.</p>}
          <ul>
            {listing?.folders.map(f => (
              <li key={f.path}>
                <button type="button" disabled={loading} onClick={() => open(f.path)} title={f.path}
                  className="w-full text-left px-4 py-1.5 text-sm hover:bg-neutral-700/60 disabled:opacity-60 truncate">
                  {f.name}
                </button>
              </li>
            ))}
          </ul>
        </div>

        <div className="flex items-center gap-2 px-4 py-3 border-t border-neutral-700">
          <span className="text-[11px] text-neutral-500 mr-auto">Folders of the computer PhotoSense runs on.</span>
          <button type="button" onClick={onCancel} className="btn-secondary">Cancel</button>
          <button type="button" disabled={loading || !here} onClick={() => here && onPick(here)} className="btn-primary">Use this folder</button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
