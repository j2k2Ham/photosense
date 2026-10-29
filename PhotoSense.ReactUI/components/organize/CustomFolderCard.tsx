import React from 'react';
import { formatBytes } from '../../lib/format';
import { totalBytes } from '../../lib/organize';
import type { OrganizeFileDto } from '../../types';
import { countFiles, useDrop } from './shared';

/** One of the person's own folders, with the files put in it so far. */
export interface OwnFolder { id: string; name: string; files: OrganizeFileDto[]; }

interface FolderProps {
  readonly folder: OwnFolder;
  readonly on: boolean;
  readonly picked: number;
  onFocus(): void;
  onAdd(): void;
  onPreview(): void;
  onRemove(): void;
  onDrop(): void;
}

/** One of the person's own folders: nothing is in it on disk until it is previewed and confirmed. */
export function CustomFolderCard({ folder, on, picked, onFocus, onAdd, onPreview, onRemove, onDrop }: FolderProps) {
  const drop = useDrop(onDrop), n = folder.files.length;
  return (
    <div {...drop.handlers} className={`flex items-center gap-3 rounded-[14px] border py-3 pl-3.5 pr-2.5 ${on || drop.hot ? 'border-brand bg-sel' : 'border-line bg-bg'}`}>
      <button type="button" aria-pressed={on} aria-label={`Show the files for ${folder.name}`} onClick={onFocus} className="flex min-w-0 flex-1 items-center gap-3 text-left">
        <span aria-hidden className="h-[22px] w-[30px] shrink-0 rounded-[5px] bg-brand opacity-75" />
        <span className="flex min-w-0 flex-1 flex-col gap-0.5">
          <span className="truncate text-[14.5px] font-semibold">{folder.name}</span>
          <span className="text-[12.5px] text-t2">{n > 0 ? `${countFiles(n)} · ${formatBytes(totalBytes(folder.files))} · not moved yet` : 'Empty. Select or drag files onto it.'}</span>
        </span>
      </button>
      {picked > 0 && <button type="button" onClick={onAdd} className="pill h-[34px] shrink-0 border border-brand px-3.5 text-[13px] text-brand">Add {picked.toLocaleString()} here</button>}
      <button type="button" aria-label={`Preview ${folder.name}`} disabled={n === 0} onClick={onPreview} className="pill-quiet h-[34px] shrink-0 border border-line px-3.5 text-[13px]">Preview</button>
      <button type="button" aria-label={`Remove ${folder.name}`} title="Remove this folder" onClick={onRemove} className="flex h-[30px] w-[30px] shrink-0 items-center justify-center rounded-full text-[16px] text-t2 hover:bg-s2">×</button>
    </div>
  );
}
