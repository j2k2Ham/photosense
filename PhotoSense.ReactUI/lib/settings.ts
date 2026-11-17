import { useCallback, useMemo } from 'react';
import type { DistanceUnit } from './distance';
import { readStored, useStored } from './stored';

/** How the person has set PhotoSense to behave. Kept in this browser. */
export interface AppSettings {
  /** Deleting permanently asks a second time after its warning. */
  eraseAskTwice: boolean;
  /** Distances are said in miles or kilometres, on a computer and a phone alike. */
  distanceUnit: DistanceUnit;
  /** A free trial is to be followed by a reminder two days before it renews. */
  remindTrialEnd: boolean;
  /** Phones has been opened on this computer, so its tab no longer has the dot that says it is new. */
  phonesSeen: boolean;
}

/** The settings until the person changes them. */
export const defaultAppSettings: AppSettings = { eraseAskTwice: true, distanceUnit: 'miles', remindTrialEnd: true, phonesSeen: false };

const KEY = 'photosense-settings';

// The settings in what was kept, with the default of any added since; the defaults when nothing readable was.
function settingsIn(kept: string | null | undefined): AppSettings {
  try { return { ...defaultAppSettings, ...JSON.parse(kept ?? '{}') }; } catch { return defaultAppSettings; }
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
