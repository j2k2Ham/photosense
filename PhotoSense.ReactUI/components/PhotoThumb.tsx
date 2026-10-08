import React, { useState } from 'react';
import type { PhotoDto } from '../types';
import { imageUrl, thumbnailUrl, videoUrl } from '../lib/apiClient';
import { formatDuration } from '../lib/format';

/** Small preview of a picture, or a dark tile with its playing time for a video (videos are never decoded). */
export function PhotoThumb({ photo, className = '' }: { readonly photo: PhotoDto; readonly className?: string }) {
  if (photo.isVideo)
    return (
      <span className={`flex flex-col items-center justify-center gap-1.5 bg-[#1c2124] text-white ${className}`}>
        <span aria-hidden className="h-0 w-0 border-y-[9px] border-l-[15px] border-y-transparent border-l-white" />
        {photo.durationSeconds != null && <span className="font-mono text-[13px]">{formatDuration(photo.durationSeconds)}</span>}
      </span>
    );
  // eslint-disable-next-line @next/next/no-img-element
  // Fetched at once, not as it scrolls into view: a page holds few enough for that, and none then appears late.
  return <img src={thumbnailUrl(photo.id)} alt="" decoding="async" className={`bg-s3 object-cover ${className}`} />;
}

/** The picture letterboxed at its true shape, its preview underneath while it loads; a still tile for a video. */
export function PhotoStill({ photo }: { readonly photo: PhotoDto }) {
  if (photo.isVideo)
    return (
      <span className="flex h-full w-full items-center justify-center bg-[#1c2124]">
        <span aria-hidden className="flex h-14 w-14 items-center justify-center rounded-full bg-white/20">
          <span className="ml-1 h-0 w-0 border-y-[10px] border-l-[16px] border-y-transparent border-l-white" />
        </span>
      </span>
    );
  return (
    <span className="block h-full w-full bg-contain bg-center bg-no-repeat" style={{ backgroundImage: `url("${thumbnailUrl(photo.id)}")` }}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img key={photo.id} src={imageUrl(photo.id)} alt={photo.fileName} className="h-full w-full object-contain" />
    </span>
  );
}

interface ViewProps {
  readonly photo: PhotoDto;
  /** Opens the file in the operating system's own viewer or player. */
  onOpenInViewer(photo: PhotoDto): void;
}

/** The picture at full size; for a video, a player. */
export function PhotoView({ photo, onOpenInViewer }: ViewProps) {
  if (photo.isVideo) return <VideoView key={photo.id} photo={photo} onOpenInViewer={onOpenInViewer} />;
  return <PhotoStill photo={photo} />;
}

function VideoView({ photo, onOpenInViewer }: ViewProps) {
  // Whether a browser can play a video depends on how the phone encoded it; when it cannot, the system player can.
  const [unplayable, setUnplayable] = useState(false);
  return (
    <div className="flex h-full w-full flex-col bg-stage">
      {unplayable ? (
        <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 p-6 text-center text-t2">
          <span className="break-all text-[14px] font-semibold text-t1">{photo.fileName}</span>
          <span className="max-w-sm text-[13px]">This browser cannot play this video. Open it in your default player instead.</span>
        </div>
      ) : (
        <video src={videoUrl(photo.id)} controls preload="metadata" className="min-h-0 w-full flex-1 object-contain"
          onError={() => setUnplayable(true)}
          // Sound without a picture: the browser lacks the video decoder.
          onLoadedMetadata={e => { if (e.currentTarget.videoWidth === 0) setUnplayable(true); }} />
      )}
      <div className="flex items-center gap-3 px-3 py-2 text-[12.5px] text-t3">
        <span>Copies of a video are matched only when the files are identical, byte for byte.</span>
        <button type="button" className="pill-quiet ml-auto h-8 shrink-0 px-3.5 text-[13px]" onClick={() => onOpenInViewer(photo)}>Open in default player</button>
      </div>
    </div>
  );
}
