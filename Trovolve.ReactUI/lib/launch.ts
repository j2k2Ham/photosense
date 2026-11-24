/**
 * How long the launch stays when the app first loads, in seconds, before it fades to show the app. Change it here
 * (Jamie, 2026-11-24: three minutes for now).
 */
export const LAUNCH_SECONDS = 240;

/** How long the launch takes to fade, in milliseconds, at the end of those seconds. */
export const LAUNCH_FADE = 300;

/**
 * Where the page remembers it has played the launch since the app was opened: on a computer, for as long as its tab
 * stays open; on a phone, until the app is closed. A reload keeps it, so the launch plays only on the first load.
 */
export const LAUNCH_KEY = 'trovolve-launched';

/** Whether the launch has played since the app was opened, noting that it now has. False when the browser refuses storage. */
export function launchedBefore(): boolean {
  try {
    const before = sessionStorage.getItem(LAUNCH_KEY) !== null;
    sessionStorage.setItem(LAUNCH_KEY, 'yes');
    return before;
  } catch { return false; }
}
