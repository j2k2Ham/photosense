import React from 'react';
import type { PhotoDto } from '../types';

interface Props { photo?: PhotoDto; }

export function PreviewPanel({ photo }: Props) {
  if (!photo) return <div className="panel flex flex-col items-center justify-center text-sm text-neutral-500">Select a photo</div>;
  return (
    <div className="panel p-4 flex flex-col gap-4 overflow-auto">
      <div className="aspect-video w-full bg-neutral-700 rounded" />
      <div className="space-y-1 text-xs">
        <div><span className="text-neutral-400">Filename:</span> {photo.fileName}</div>
        <div><span className="text-neutral-400">Path:</span> {photo.sourcePath}</div>
        <div><span className="text-neutral-400">Size:</span> {(photo.fileSizeBytes/1024/1024).toFixed(2)} MB</div>
        {photo.takenOn && <div><span className="text-neutral-400">Taken:</span> {new Date(photo.takenOn).toLocaleString()}</div>}
        {photo.cameraModel && <div><span className="text-neutral-400">Camera:</span> {photo.cameraModel}</div>}
      </div>
      <div className="mt-auto flex gap-2">
        <button className="btn-primary flex-1">Keep</button>
        <button className="btn-secondary flex-1">Move</button>
        <button className="btn-danger flex-1">Delete</button>
      </div>
    </div>
  );
}
