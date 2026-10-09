import React from 'react';
import { leaf } from '../lib/format';
import { ModeSwitch, type Area } from './ModeSwitch';

interface Props {
  /** Which of the two areas is showing. */
  readonly area: Area;
  onArea(area: Area): void;
  /** There are results: the chip that names them, with the way back to the folders, is shown. */
  readonly showScan: boolean;
  /** The folders that were scanned, when this browser remembers them. */
  readonly folders: string[];
  readonly files?: number;
  /** When the last scan finished, if the service still knows. */
  readonly scanned?: string | null;
  readonly errorCount: number;
  readonly menuOpen: boolean;
  onChangeFolders(): void;
  onToggleErrors(): void;
  onToggleMenu(): void;
}

export function TopBar({ area, onArea, showScan, folders, files, scanned, errorCount, menuOpen, onChangeFolders, onToggleErrors, onToggleMenu }: Props) {
  return (
    <header className="flex h-16 shrink-0 items-center gap-4 border-b border-line px-6">
      <div className="flex items-center gap-[11px]">
        <span aria-hidden className="flex h-[26px] w-[26px] items-center justify-center rounded-lg bg-brand">
          <span className="h-2.5 w-2.5 rounded-full border-2 border-on-brand" />
        </span>
        <h1 className="text-[17px] font-semibold">PhotoSense</h1>
      </div>
      <ModeSwitch area={area} onArea={onArea} />

      {showScan && (
        <div className="flex h-10 min-w-0 items-center gap-3 rounded-full border border-line pl-4 pr-[5px] text-[13px] text-t2">
          <span className="truncate" aria-label="Scanned folders">
            {folders.map((f, i) => (
              <React.Fragment key={f}>{i > 0 && ' + '}<span className="font-medium text-t1" title={f}>{leaf(f)}</span></React.Fragment>
            ))}
            {files !== undefined && <>{folders.length > 0 && ' · '}{files.toLocaleString()} {files === 1 ? 'file' : 'files'}</>}
            {scanned && <> · scanned {new Date(scanned).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })}</>}
          </span>
          <button type="button" onClick={onChangeFolders} className="pill-quiet h-[30px] shrink-0 px-3.5 text-[13px]">Change or rescan</button>
        </div>
      )}

      <span className="flex-1" />

      <span className="flex h-9 shrink-0 items-center gap-2 rounded-full bg-s2 px-3.5 text-[13px] text-t2">
        <span aria-hidden className="h-[7px] w-[7px] rounded-full bg-keep" />Runs on this computer. Nothing is uploaded.
      </span>

      {errorCount > 0 && (
        <button type="button" onClick={onToggleErrors} className="pill h-9 shrink-0 gap-2 border border-rose-line bg-rose-bg px-3.5 text-[13px] text-rose-t">
          <span aria-hidden className="h-[7px] w-[7px] rounded-full bg-rose" />{errorCount} {errorCount === 1 ? 'error' : 'errors'}
        </button>
      )}

      <button type="button" aria-label="Menu" aria-expanded={menuOpen} onClick={onToggleMenu}
        className={`flex h-10 w-10 shrink-0 flex-col items-center justify-center gap-1 rounded-full border border-line ${menuOpen ? 'bg-s3' : 'bg-s1 hover:bg-s2'}`}>
        <span className="h-0.5 w-4 bg-t1" /><span className="h-0.5 w-4 bg-t1" /><span className="h-0.5 w-4 bg-t1" />
      </button>
    </header>
  );
}
