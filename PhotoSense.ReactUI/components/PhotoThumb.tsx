import React from 'react';
import clsx from 'clsx';
import type { PhotoDto } from '../types';
import { imageUrl, thumbnailUrl } from '../lib/apiClient';
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

/** The picture at full size, with its thumbnail underneath while it loads; for a video, a plain statement of what it is. */
export function PhotoView({ photo }: { readonly photo: PhotoDto }) {
  if (photo.isVideo)
    return (
      <div className="w-full h-full flex flex-col items-center justify-center gap-2 bg-black text-neutral-300 p-6 text-center">
        <span aria-hidden className="text-5xl leading-none">▶</span>
        <span className="text-sm font-semibold break-all">{photo.fileName}</span>
        <span className="text-xs text-neutral-400 max-w-sm">
          Videos are matched only when two files are identical, byte for byte, so the copies here are the same video. There is no preview.
        </span>
      </div>
    );
  return (
    <div className="w-full h-full bg-black bg-center bg-no-repeat bg-contain" style={{ backgroundImage: `url("${thumbnailUrl(photo.id)}")` }}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img key={photo.id} src={imageUrl(photo.id)} alt={photo.fileName} className="w-full h-full object-contain" />
    </div>
  );
}
