import { useCallback, useMemo } from 'react';
import type { DistanceUnit } from './distance';
import { launchedBefore } from './launch';
import { readStored, useStored } from './stored';

/** How the person has set Trovolve to behave. Kept in this browser. */
export interface AppSettings {
  /** Deleting permanently asks a second time after its warning. */
  eraseAskTwice: boolean;
  /** Distances are said in miles or kilometres, on a computer and a phone alike. */
  distanceUnit: DistanceUnit;
  /** Phones has been opened on this computer, so its tab no longer has the dot that says it is new. */
  phonesSeen: boolean;
  /** On a phone, Share photo starts with Remove location on. */
  removeLocationWhenSharing: boolean;
  /**
   * When Trovolve opens, Clean up shows the last results and Organize the last folder. Not set until the person
   * answers the question asked after their first scan, or changes it in Settings; not set, it is on.
   */
  continueWhereLeftOff?: boolean;
}

/** The settings until the person changes them. */
export const defaultAppSettings: AppSettings = { eraseAskTwice: true, distanceUnit: 'miles', phonesSeen: false, removeLocationWhenSharing: true };

const KEY = 'trovolve-settings';

// The settings in what was kept, with the default of any added since; the defaults when nothing readable was.
function settingsIn(kept: string | null | undefined): AppSettings {
  try { return { ...defaultAppSettings, ...JSON.parse(kept ?? '{}') }; } catch { return defaultAppSettings; }
}

/**
 * Whether this load of the app starts fresh, the last results and folders forgotten: its first load in this tab, not
 * a reload, with Continue where you left off turned off. False where there is no browser storage to ask, as when the
 * page is made ahead on the server.
 */
export function startsFresh(): boolean {
  return loadAppSettings().continueWhereLeftOff === false && !launchedBefore();
}

/** The settings as this browser remembers them, with the default of any added since. */
export function loadAppSettings(): AppSettings {
  return settingsIn(readStored(KEY));
}

/** The settings in use, and a way to change some of them. The defaults until the browser's storage is read. */
export function useAppSettings(): [AppSettings, (change: Partial<AppSettings>) => void] {
  const [kept, keepSettings] = useStored(KEY);
  const settings = useMemo(() => settingsIn(kept), [kept]);
  // A browser that refuses storage still gets the setting for this visit.
  const change = useCallback((next: Partial<AppSettings>) => keepSettings(before => JSON.stringify({ ...settingsIn(before), ...next })), [keepSettings]);
  return [settings, change];
}
