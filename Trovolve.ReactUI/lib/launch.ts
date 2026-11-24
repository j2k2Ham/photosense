/**
 * How long the launch stays when the app first loads, in seconds, before it fades to show the app. Change it here
 * (Jamie, 2026-11-24: three minutes at first, then four).
 */
export const LAUNCH_SECONDS = 240;

/** How long the launch takes to fade, in milliseconds, at the end of those seconds. */
export const LAUNCH_FADE = 300;

/**
 * Where the page remembers it has played the launch since the app was opened: on a computer, for as long as its tab
 * stays open; on a phone, until the app is closed. A reload keeps it, so the launch plays only on the first load.
 */
export const LAUNCH_KEY = 'trovolve-launched';

/**
 * Whether the launch has played since the app was opened: the layout's first script says so before the page is drawn
 * (`data-launched`). Asked of the page, not of storage, so that asking twice, as React does in development, cannot
 * change the answer once the launch has noted itself.
 */
export function launchedBefore(): boolean {
  return document.documentElement.hasAttribute('data-launched');
}

/** Notes that the launch has played, so a reload does not play it again. False when the browser refuses storage. */
export function noteLaunched(): boolean {
  try { sessionStorage.setItem(LAUNCH_KEY, 'yes'); } catch { return false; }
  return true;
}
