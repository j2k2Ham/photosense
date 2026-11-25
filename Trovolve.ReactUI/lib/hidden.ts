import type { Platform } from './phone';
import type { VaultDto } from '../types';
import { items } from './fixes';

/** The hidden vault's tile in Library (26): how many it holds, "Pro" on the free plan before it is on, or "Turn on". */
export function hiddenTile(vault: VaultDto | undefined, free: boolean): string {
  if (vault === undefined) return 'Looking…';
  if (vault.stage === 'off') return free ? 'Pro' : 'Turn on';
  return vault.count === 0 ? 'Nothing hidden' : items(vault.count);
}

/** Where a phone keeps its photos, as the vault's words name it. */
const libraryName = (platform: Platform) => (platform === 'ios' ? 'Photos library' : 'gallery');

/** What the vault is (33): kept behind the phone's own lock, encrypted, out of the library. */
export function introLine(platform: Platform): string {
  return `Keep private photos behind ${platform === 'ios' ? 'Face ID' : 'your fingerprint'}. They are encrypted inside Trovolve and removed from your ${libraryName(platform)}.`;
}

/** The button that asks the phone's own question (34). */
export const unlockLabel = (platform: Platform) => (platform === 'ios' ? 'Unlock with Face ID' : 'Unlock with fingerprint');

/** The info card under the grid (34): what hiding does, and where backups go. */
export function infoLine(platform: Platform, computer: string | null): string {
  const kept = `Hidden items are encrypted inside Trovolve and removed from your ${libraryName(platform)}.`;
  return computer ? `${kept} Backups to ${computer} stay encrypted.` : `${kept} Pair your computer to back them up, still encrypted.`;
}

/** The amber warning (34): what deleting Trovolve would take with it; null when everything is backed up. */
export function warningLine(notBackedUp: number): string | null {
  if (notBackedUp === 0) return null;
  return `If you delete Trovolve, hidden items that are not backed up are deleted too. ${items(notBackedUp)} ${notBackedUp === 1 ? 'is' : 'are'} not backed up yet.`;
}

/** What happens to photos chosen to hide, said before they go. */
export function hideNote(platform: Platform, device: string): string {
  return platform === 'ios'
    ? `They are encrypted into Hidden first. Then your ${device} asks before they leave Photos, and they wait in Recently Deleted for 30 days.`
    : `They are encrypted into Hidden first. Then Android asks before they leave the gallery, and they wait in the bin for 30 days.`;
}

/** The question before an item of the vault is deleted, naming it. */
export function deleteQuestion(name: string, bin: string): { title: string; lines: string[] } {
  return {
    title: `Delete ${name}?`,
    lines: [`${name} goes back to your library, then to ${bin}, where it can be found for 30 days.`],
  };
}

/** How bringing the vault's items back from the computer stands; null when it was not asked since Trovolve started, or is done. */
export function restoreLine(vault: VaultDto | undefined): string | null {
  const restore = vault?.restore;
  if (!restore || restore.stage !== 'running') return null;
  return `Bringing your hidden items back from ${vault!.computer ?? 'your computer'}…`;
}
