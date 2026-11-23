import { acceptTerms, termsNow, useTerms } from './apiClient';

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
  /** On a computer, for someone who accepted on their phone already. */
  paired: 'Accepted it on your phone already? Pair the phone with this computer, and PhotoSense does not ask here.',
  pair: 'Pair a phone',
} as const;

/**
 * Whether the note asking the person to keep a backup is due, as it is until they accept it once, on any of their
 * devices (Jamie, 2026-11-23: once per person): the service keeps the day, and a phone and its computer tell each other.
 * Not due until the service has answered. `check` asks the service again, as pairing may just have told it.
 */
export function useBackupNote(): { readonly due: boolean; accept(): Promise<unknown>; check(): Promise<boolean> } {
  const accepted = useTerms().data?.accepted;
  return { due: accepted === null, accept: acceptTerms, check: async () => (await termsNow()).accepted !== null };
}
