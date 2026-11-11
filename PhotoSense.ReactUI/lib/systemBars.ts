import type { Theme } from './theme';

interface PhoneBridges {
  webkit?: { messageHandlers?: { photosenseBars?: { postMessage(theme: Theme): void } } };
  PhotoSenseBars?: { theme(theme: Theme): void };
}

/**
 * Tells the phone app the page is in which theme, so that it draws its status bar's icons (and Android its gesture
 * bar's) light on dark or dark on light. In a browser, or on a computer, nobody is listening and nothing happens.
 */
export function tellSystemBars(theme: Theme): void {
  const bridges = window as unknown as PhoneBridges;
  bridges.webkit?.messageHandlers?.photosenseBars?.postMessage(theme);
  bridges.PhotoSenseBars?.theme(theme);
}
