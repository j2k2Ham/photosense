import React from 'react';
import { BACKUP_NOTE } from '../lib/backupNote';
import { Scrim } from './Scrim';

// The note is put away only by accepting it, or by a phone that accepted it: neither Escape nor a press outside it does.
const stay = () => {};

/**
 * On a computer, until the person accepts it here or on a phone paired with this computer: keep a backup of the photos
 * you care about, and accept the terms of use. Pair a phone is for someone who accepted on their phone already.
 */
export function BackupNoteDialog({ onTerms, onPair, onAccept }: { onTerms(): void; onPair(): void; onAccept(): void }) {
  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={stay}>
      <div role="alertdialog" aria-modal="true" aria-label={BACKUP_NOTE.title} className="flex max-h-full w-[520px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">{BACKUP_NOTE.title}</h2>
        <div className="space-y-2.5 text-[15px] text-t2">{BACKUP_NOTE.lines.map(line => <p key={line}>{line}</p>)}</div>
        <p className="text-[13.5px] text-t3">{BACKUP_NOTE.paired}</p>
        <div className="flex flex-wrap items-center justify-end gap-2.5 pt-1">
          <button type="button" onClick={onTerms} className="pill-quiet h-11 px-4 text-[15px]">{BACKUP_NOTE.terms}</button>
          <button type="button" onClick={onPair} className="pill-quiet mr-auto h-11 px-4 text-[15px]">{BACKUP_NOTE.pair}</button>
          <button type="button" onClick={onAccept} className="pill-brand h-11 px-5 text-[15px]">{BACKUP_NOTE.accept}</button>
        </div>
      </div>
    </Scrim>
  );
}
