import React, { useState } from 'react';
import clsx from 'clsx';
import type { PhotoDto } from '../types';
import { imageUrl, thumbnailUrl, videoUrl } from '../lib/apiClient';
import { formatDuration } from '../lib/format';

/** Small preview of a picture, or a labelled tile for a video (videos are matched as identical files and never decoded). */
export function PhotoThumb({ photo, className }: { readonly photo: PhotoDto; readonly className?: string }) {
  if (photo.isVideo)
    return (
      <span className={clsx('flex flex-col items-center justify-center gap-0.5 bg-neutral-700 text-neutral-300', className)}>
        <span aria-hidden className="text-lg leading-none">▶</span>
        <span className="text-[9px] font-semibold tracking-wide">VIDEO</span>
        {photo.durationSeconds != null && <span className="text-[9px] text-neutral-400">{formatDuration(photo.durationSeconds)}</span>}
      </span>
    );
  // eslint-disable-next-line @next/next/no-img-element
  return <img src={thumbnailUrl(photo.id)} alt="" loading="lazy" className={clsx('object-cover bg-neutral-700', className)} />;
}

interface ViewProps {
  readonly photo: PhotoDto;
  /** Opens the file in the operating system's own viewer or player. */
  onOpenInViewer(photo: PhotoDto): void;
}

/** The picture at full size, with its thumbnail underneath while it loads; for a video, a player. */
export function PhotoView({ photo, onOpenInViewer }: ViewProps) {
  if (photo.isVideo) return <VideoView key={photo.id} photo={photo} onOpenInViewer={onOpenInViewer} />;
  return (
    <div className="w-full h-full bg-black bg-center bg-no-repeat bg-contain" style={{ backgroundImage: `url("${thumbnailUrl(photo.id)}")` }}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img key={photo.id} src={imageUrl(photo.id)} alt={photo.fileName} className="w-full h-full object-contain" />
    </div>
  );
}

function VideoView({ photo, onOpenInViewer }: ViewProps) {
  // Whether a browser can play a video depends on how the phone encoded it; when it cannot, the system player can.
  const [unplayable, setUnplayable] = useState(false);
  return (
    <div className="w-full h-full flex flex-col bg-black">
      {unplayable ? (
        <div className="flex-1 min-h-0 flex flex-col items-center justify-center gap-2 text-neutral-300 p-6 text-center">
          <span aria-hidden className="text-5xl leading-none">▶</span>
          <span className="text-sm font-semibold break-all">{photo.fileName}</span>
          <span className="text-xs text-neutral-400 max-w-sm">This browser cannot play this video. Open it in your default player instead.</span>
        </div>
      ) : (
        <video src={videoUrl(photo.id)} controls preload="metadata" className="flex-1 min-h-0 w-full object-contain"
          onError={() => setUnplayable(true)}
          // Sound without a picture: the browser lacks the video decoder.
          onLoadedMetadata={e => { if (e.currentTarget.videoWidth === 0) setUnplayable(true); }} />
      )}
      <div className="flex items-center gap-3 px-3 py-2 text-[11px] text-neutral-400 border-t border-neutral-800 text-left">
        <span>Copies of a video are matched only when the files are identical, byte for byte.</span>
        <button type="button" className="btn-secondary ml-auto shrink-0 py-1 px-3 text-xs" onClick={() => onOpenInViewer(photo)}>Open in default player</button>
      </div>
    </div>
  );
}
