import React, { useState } from 'react';
import { chooseMorePhotos, openPhotoSettings, usePhotoAccess } from '../lib/apiClient';
import type { ToastKind } from './Toaster';

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * On a phone, what PhotoSense has not been let see of the photos, and the way to let it see more: the phone's own
 * picker for choosing more, or PhotoSense's page of the phone's settings. Nothing at all on a computer.
 */
export function PhotoAccessNote({ notify }: { notify(message: string, kind: ToastKind): void }) {
  const { data } = usePhotoAccess();
  const [choosing, setChoosing] = useState(false);
  if (!data || data.access === 'everything') return null;

  async function chooseMore() {
    setChoosing(true);
    try {
      const after = await chooseMorePhotos();
      notify(after.access === 'everything' ? 'PhotoSense can now see all your photos.' : 'PhotoSense can see the photos you selected.', 'ok');
    } catch (e) {
      notify(`The photo picker could not be shown: ${said(e)}`, 'error');
    } finally {
      setChoosing(false);
    }
  }

  async function settings() {
    try { await openPhotoSettings(); } catch (e) { notify(`Settings could not be opened: ${said(e)}`, 'error'); }
  }

  const some = data.access === 'selected';
  return (
    <div role="note" className="flex w-[min(1240px,100%)] flex-wrap items-center gap-3 rounded-[20px] border border-line bg-s1 px-5 py-4">
      <p className="min-w-[240px] flex-1 text-[15px] text-t2">
        {some
          ? 'PhotoSense can see only the photos you selected. Choose more, or allow all of them in Settings.'
          : 'PhotoSense cannot see any of your photos. Allow it in Settings, under Privacy & Security, then Photos.'}
      </p>
      {some && (
        <button type="button" disabled={choosing} onClick={() => void chooseMore()} className="pill-brand h-11 px-5 text-[15px]">
          {choosing ? 'Choosing…' : 'Choose more photos'}
        </button>
      )}
      <button type="button" onClick={() => void settings()} className="pill-outline h-11 px-5 text-[15px]">Open Settings</button>
    </div>
  );
}
