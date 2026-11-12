import { useCallback, useEffect, useState } from 'react';
import type { DistanceUnit } from './distance';

/** How the person has set PhotoSense to behave. Kept in this browser. */
export interface AppSettings {
  /** Deleting permanently asks a second time after its warning. */
  eraseAskTwice: boolean;
  /** Distances are said in miles or kilometres, on a computer and a phone alike. */
  distanceUnit: DistanceUnit;
}

/** The settings until the person changes them. */
export const defaultAppSettings: AppSettings = { eraseAskTwice: true, distanceUnit: 'miles' };

const KEY = 'photosense-settings';

/** The settings as this browser remembers them, with the default of any added since. */
export function loadAppSettings(): AppSettings {
  // A setting added since the others were saved has its default.
  try { return { ...defaultAppSettings, ...JSON.parse(localStorage.getItem(KEY) ?? '{}') }; } catch { return defaultAppSettings; }
}

/** The settings in use, and a way to change some of them. */
export function useAppSettings(): [AppSettings, (change: Partial<AppSettings>) => void] {
  const [settings, setSettings] = useState(defaultAppSettings);
  // Read after mounting: the server has nothing saved to render with.
  useEffect(() => { setSettings(loadAppSettings()); }, []);

  const change = useCallback((next: Partial<AppSettings>) => setSettings(before => {
    const now = { ...before, ...next };
    // A browser that refuses storage still gets the setting for this visit.
    try { localStorage.setItem(KEY, JSON.stringify(now)); } catch { /* not remembered */ }
    return now;
  }), []);

  return [settings, change];
}
