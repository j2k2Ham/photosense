import React from 'react';
import type { PhotoDto } from '../types';
import { formatCoordinates, formatFile, formatTaken, mapUrl } from '../lib/format';

/** Name, date, where the file is and where the picture was taken. */
export function PhotoDetails({ photo }: { readonly photo: PhotoDto }) {
  const map = mapUrl(photo);
  return (
    <dl className="grid grid-cols-[5rem_1fr] gap-x-3 gap-y-0.5 text-xs">
      <dt className="text-neutral-500">Name</dt><dd className="break-all">{photo.fileName}</dd>
      <dt className="text-neutral-500">Date</dt><dd>{formatTaken(photo.takenOn)}</dd>
      <dt className="text-neutral-500">Folder</dt><dd className="break-all">{photo.folder}</dd>
      <dt className="text-neutral-500">Taken at</dt>
      <dd>
        {photo.placeName && <span className="mr-2">{photo.placeName}</span>}
        <span className={photo.placeName ? 'text-neutral-400' : undefined}>{formatCoordinates(photo)}</span>
        {map && <a href={map} target="_blank" rel="noreferrer" className="ml-2 text-emerald-400 hover:underline">Show on map</a>}
      </dd>
      <dt className="text-neutral-500">File</dt><dd>{formatFile(photo)}</dd>
      {photo.cameraModel && <><dt className="text-neutral-500">Camera</dt><dd>{photo.cameraModel}</dd></>}
    </dl>
  );
}
