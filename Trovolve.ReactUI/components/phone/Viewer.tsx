import React, { useEffect, useState } from 'react';
import { organizeImageUrl, organizeThumbnailUrl, setFavorite, sharePhoto, usePhotoDetails } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { formatBytes, formatDuration } from '../../lib/format';
import { clockTime, takenAt, whereIs } from '../../lib/library';
import { joinPath, placeInFull, shortDate } from '../../lib/organize';
import { usePhone } from '../../lib/phone';
import { placeLine, shareLine } from '../../lib/search';
import type { OrganizeFileDto, OrganizePlaceDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { ListGroup, PhoneButton, PhoneSwitch } from './Controls';
import { AddToFolderIcon, ChevronBackIcon, ArrowBackIcon, HeartIcon, InfoIcon, SendIcon, ShareIcon, TrashIcon } from './Icons';
import { Sheet } from './Sheet';

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

interface Props {
  readonly file: OrganizeFileDto;
  readonly places: readonly OrganizePlaceDto[];
  readonly root: string;
  /** Whether Share photo starts with Remove location on, as Settings has it. */
  readonly removeLocation: boolean;
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
  onSend(): void;
  onPut(): void;
  onDelete(): void;
}

/**
 * 28: one picture or video, whole, on black, with when and where it was taken above it and Share, Favorite, Info and
 * Delete below: a glass capsule of icons on iOS, a bar with labels on Android. Info shows what the file is, with Send
 * and Add to album (Move to folder); Share opens Share photo.
 */
export function Viewer({ file, places, root, removeLocation, notify, onClose, onSend, onPut, onDelete }: Props) {
  const { ios, words } = usePhone();
  const path = joinPath(file.folder, file.name);
  const details = usePhotoDetails(path).data;
  const [info, setInfo] = useState(false);
  const [sharing, setSharing] = useState(false);
  useBackLayer(true, onClose);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  const place = file.place == null ? undefined : placeInFull(places[file.place]);
  const favorite = details?.favorite ?? false;

  async function markFavorite() {
    try {
      await setFavorite(path, !favorite);
    } catch (e) {
      notify(said(e), 'error');
    }
  }

  const size = [formatBytes(file.sizeBytes), file.width > 0 ? `${file.width} × ${file.height}` : undefined, file.durationSeconds == null ? undefined : formatDuration(file.durationSeconds)];
  const rows: readonly [string, string][] = [
    ['Name', file.name],
    ['Date', takenAt(file.date)],
    ['Place', place ?? 'No location'],
    ['Camera', details === undefined ? '…' : details.camera ?? 'Not known'],
    ['Size', size.filter(Boolean).join(' · ')],
    [words.Album, whereIs(file.folder, root, words)],
  ];
  const tools = [
    { label: 'Share', icon: <ShareIcon size={22} />, run: () => setSharing(true) },
    { label: 'Favorite', icon: <HeartIcon filled={favorite} />, run: () => void markFavorite(), pressed: favorite },
    { label: 'Info', icon: <InfoIcon />, run: () => setInfo(i => !i), pressed: info },
    { label: 'Delete', icon: <TrashIcon size={22} />, run: onDelete },
  ];

  return (
    <div role="dialog" aria-modal aria-label={file.name} className="fixed inset-0 z-40 flex animate-fade-in flex-col bg-stage font-ui text-white">
      <div className={`flex items-center gap-3 pb-2 pt-[calc(var(--safe-t)+8px)] ${ios ? 'px-3' : 'px-1'}`}>
        <button type="button" aria-label="Back" onClick={onClose} className={`grid shrink-0 place-items-center rounded-full ${ios ? 'h-11 w-11 bg-white/[.14]' : 'h-12 w-12'}`}>
          {ios ? <ChevronBackIcon /> : <ArrowBackIcon size={24} />}
        </button>
        <div className={`min-w-0 flex-1 ${ios ? 'text-center' : ''}`}>
          <p className="truncate text-[15px] font-semibold">{shortDate(file.date)}</p>
          <p className="truncate text-[12.5px] text-white/[.72]">{[place ?? 'No location', clockTime(file.date)].join(' · ')}</p>
        </div>
        {ios && <span className="w-11 shrink-0" />}
      </div>
      <div className="flex min-h-0 flex-1 items-center justify-center">
        {file.isVideo
          ? <span className="text-[15px] text-white/[.78]">Trovolve cannot play videos yet.</span>
          : (
            // The small preview shows at once, under the whole picture, which can take a moment to arrive.
            <span className="relative h-full w-full max-w-[680px]">
              <img src={organizeThumbnailUrl(file.id)} alt="" className="absolute inset-0 h-full w-full object-contain" />
              <img src={organizeImageUrl(file.id)} alt={file.name} className="absolute inset-0 h-full w-full object-contain" />
            </span>
          )}
      </div>
      {info && (
        <section aria-label="Info" className="mx-auto flex w-full max-w-[680px] flex-col gap-2 px-4 pt-2">
          <dl className="overflow-hidden rounded-cell bg-white/[.1] text-[15px]">
            {rows.map(([key, value], i) => (
              <div key={key} className={`flex min-h-[42px] items-center gap-3 px-4 py-2 ${i > 0 ? 'border-t border-white/[.12]' : ''}`}>
                <dt className="w-[72px] shrink-0 text-white/[.66]">{key}</dt>
                <dd className="min-w-0 flex-1 break-words">{value}</dd>
              </div>
            ))}
          </dl>
          <div className="grid grid-cols-2 gap-2">
            <button type="button" onClick={onSend} className="flex h-11 items-center justify-center gap-2 rounded-full bg-white/[.14] text-[15px] font-semibold"><SendIcon size={18} />Send</button>
            <button type="button" onClick={onPut} className="flex h-11 items-center justify-center gap-2 rounded-full bg-white/[.14] text-[15px] font-semibold"><AddToFolderIcon size={18} />{words.put}</button>
          </div>
        </section>
      )}
      {ios
        ? (
          <div role="toolbar" aria-label="Photo" className="mx-auto mb-[calc(var(--safe-b)+8px)] mt-2 flex h-[58px] items-center gap-1 rounded-full border-[0.5px] border-white/[.14] bg-white/[.14] px-1.5 backdrop-blur-xl">
            {tools.map(t => (
              <button key={t.label} type="button" aria-label={t.label} aria-pressed={t.pressed} onClick={t.run}
                className={`grid h-[46px] w-14 place-items-center rounded-full ${t.label === 'Info' && info ? 'bg-white/[.16]' : ''}`}>{t.icon}</button>
            ))}
          </div>
        )
        : (
          <div role="toolbar" aria-label="Photo" className="grid h-20 shrink-0 grid-cols-4 items-center pb-[var(--safe-b)] text-[12px] font-medium">
            {tools.map(t => (
              <button key={t.label} type="button" aria-pressed={t.pressed} onClick={t.run} className="flex flex-col items-center gap-1">
                <span className={`grid h-[30px] w-14 place-items-center rounded-[15px] ${t.label === 'Info' && info ? 'bg-white/[.16]' : ''}`}>{t.icon}</span>{t.label}
              </button>
            ))}
          </div>
        )}
      {sharing && <SharePhotoSheet file={file} path={path} place={place} removeLocation={removeLocation} notify={notify} onClose={() => setSharing(false)} />}
    </div>
  );
}

interface ShareProps {
  readonly file: OrganizeFileDto;
  readonly path: string;
  /** Where it was taken, in full; undefined when it does not say. */
  readonly place: string | undefined;
  readonly removeLocation: boolean;
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

/**
 * 28's sheet, Share photo: a copy without where it was taken (on to start with, as Settings has it) and, when asked,
 * without the camera's details, handed to the phone's share sheet. The photo in the library keeps them.
 */
export function SharePhotoSheet({ file, path, place, removeLocation: startWithout, notify, onClose }: ShareProps) {
  const [removeLocation, setRemoveLocation] = useState(startWithout);
  const [removeCamera, setRemoveCamera] = useState(false);
  const [busy, setBusy] = useState(false);
  async function share() {
    setBusy(true);
    try {
      await sharePhoto(path, removeLocation, removeCamera);
      onClose();
    } catch (e) {
      notify(said(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  return (
    <Sheet title="Share photo" bare onClose={onClose}>
      <div className="flex flex-col gap-4 px-5 pb-2 pt-3 text-t1">
        <div className="flex items-center gap-3.5">
          <span className="grid h-[60px] w-[60px] shrink-0 place-items-center overflow-hidden rounded-[12px] bg-fill text-t2">
            {file.isVideo ? <span className="text-[12px]">Video</span> : <img src={organizeThumbnailUrl(file.id)} alt="" className="h-full w-full object-cover" />}
          </span>
          <div className="min-w-0">
            <h2 className="font-title text-[20px] font-bold">Share photo</h2>
            <p className="text-[14px] text-t2">{shareLine(removeLocation, removeCamera)}</p>
          </div>
        </div>
        <ListGroup inSheet>
          <PhoneSwitch inList label="Remove location" detail={placeLine(place, removeLocation)} on={removeLocation} onChange={setRemoveLocation} />
          <PhoneSwitch inList label="Remove camera details" detail="Camera model, lens and exposure" on={removeCamera} onChange={setRemoveCamera} />
        </ListGroup>
        <p className="text-[13.5px] leading-[1.4] text-t2">The photo in your library keeps its location and details. Only the shared copy changes.</p>
        <PhoneButton disabled={busy} onClick={() => void share()}><span className="flex items-center justify-center gap-2"><ShareIcon size={18} />Share</span></PhoneButton>
        <button type="button" onClick={onClose} className="py-1.5 text-center text-[15px] font-semibold text-acc">Not now</button>
      </div>
    </Sheet>
  );
}
