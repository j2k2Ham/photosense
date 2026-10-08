/** The folders of the last scan started from this browser, so the page can name them after a reload. */
export interface LastScan { root: string; second: string; recursive: boolean; }

const KEY = 'photosense-last-scan';
const NONE: LastScan = { root: '', second: '', recursive: true };

export function loadLastScan(): LastScan {
  try { return { ...NONE, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') }; } catch { return NONE; }
}

export function saveLastScan(scan: LastScan): void {
  // A browser that refuses storage only loses the reminder.
  try { localStorage.setItem(KEY, JSON.stringify(scan)); } catch { /* not remembered */ }
}
