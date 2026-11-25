import { useMemo } from 'react';
import { keep, readStored, useStored } from './stored';

/** The folders of the last scan started from this browser, so the page can name them after a reload. */
export interface LastScan { root: string; second: string; recursive: boolean; }

const KEY = 'trovolve-last-scan';
const NONE: LastScan = { root: '', second: '', recursive: true };

// The scan in what was kept; none when nothing readable was.
function scanIn(kept: string | null | undefined): LastScan {
  try { return { ...NONE, ...JSON.parse(kept ?? '{}') }; } catch { return NONE; }
}

/** The folders last scanned, as this browser remembers them, or none. */
export function loadLastScan(): LastScan {
  return scanIn(readStored(KEY));
}

/** The folders last scanned, as this browser remembers them, changing when a scan is started; none until the browser's storage is read. */
export function useLastScan(): LastScan {
  const [kept] = useStored(KEY);
  return useMemo(() => scanIn(kept), [kept]);
}

/** Remembers the folders scanned, in this browser. */
export function saveLastScan(scan: LastScan): void {
  // A browser that refuses storage only loses the reminder.
  keep(KEY, JSON.stringify(scan));
}

const ORGANIZE_KEY = 'trovolve-organize-root';

/** The folder last chosen to organize in this browser; empty when none was. */
export function loadOrganizeRoot(): string {
  return readStored(ORGANIZE_KEY) ?? '';
}

/**
 * The folder last chosen to organize in this browser: empty once Organize's results were cleared, so that no folder is
 * shown until one is chosen; null when none was ever chosen, or until the browser's storage is read.
 */
export function useOrganizeRoot(): string | null {
  return useStored(ORGANIZE_KEY)[0] ?? null;
}

/** Remembers the folder last organized, in this browser. */
export function saveOrganizeRoot(root: string): void {
  keep(ORGANIZE_KEY, root);
}

/** Forgets the folder organized, so that Organize asks for one again, even where a scan's folder could stand in. */
export function forgetOrganizeRoot(): void {
  keep(ORGANIZE_KEY, '');
}
