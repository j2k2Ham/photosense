import { useStored } from './stored';

/** Where this browser keeps the day the person accepted the note asking them to keep a backup. */
export const BACKUP_NOTE_KEY = 'photosense-backup-note';

/** What the note says, the same on a computer and a phone, as the terms of use say it. */
export const BACKUP_NOTE = {
  title: 'Keep a backup of photos you care about',
  lines: [
    'PhotoSense asks before it removes or moves anything, and what it removes can be got back for a while. Even so, a mistake can lose a photo or video.',
    'Before you use PhotoSense, keep a backup of the photos and videos you care about, and keep it up to date.',
    'Using PhotoSense means you accept its terms of use.',
  ],
  terms: 'Read the terms of use',
  accept: 'Accept and continue',
} as const;

/**
 * Whether the note asking the person to keep a backup is due, as it is until they accept it on this device (Jamie,
 * 2026-11-22: a click to accept), and accepting it. Not due until the browser's storage has been read.
 */
export function useBackupNote(): { readonly due: boolean; accept(): void } {
  const [accepted, keep] = useStored(BACKUP_NOTE_KEY);
  return { due: accepted === null, accept: () => keep(() => new Date().toISOString().slice(0, 10)) };
}
