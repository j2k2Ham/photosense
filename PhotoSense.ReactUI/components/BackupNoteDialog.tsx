import React from 'react';
import { BACKUP_NOTE } from '../lib/backupNote';
import { Scrim } from './Scrim';

// The note is put away only by accepting it: neither Escape nor a press outside it does.
const stay = () => {};

/** On a computer, the first time PhotoSense opens: keep a backup of the photos you care about, and accept the terms of use. */
export function BackupNoteDialog({ onTerms, onAccept }: { onTerms(): void; onAccept(): void }) {
  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={stay}>
      <div role="alertdialog" aria-modal="true" aria-label={BACKUP_NOTE.title} className="flex max-h-full w-[520px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">{BACKUP_NOTE.title}</h2>
        <div className="space-y-2.5 text-[15px] text-t2">{BACKUP_NOTE.lines.map(line => <p key={line}>{line}</p>)}</div>
        <div className="flex flex-wrap items-center justify-end gap-2.5 pt-1">
          <button type="button" onClick={onTerms} className="pill-quiet mr-auto h-11 px-4 text-[15px]">{BACKUP_NOTE.terms}</button>
          <button type="button" onClick={onAccept} className="pill-brand h-11 px-5 text-[15px]">{BACKUP_NOTE.accept}</button>
        </div>
      </div>
    </Scrim>
  );
}
