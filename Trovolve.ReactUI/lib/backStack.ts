import { useEffect, useLayoutEffect, useRef } from 'react';

// What is open over the screens, oldest first: each a pushed screen, a sheet or a full-screen view with an entry of its
// own in the browser's history, so that Android's Back and iOS's swipe from the edge close the newest first.
const layers: { entry: number; close(): void }[] = [];
let made = 0;
let listening = false;
// Entries of layers closed some other way than by Back, not yet taken out of the history.
let unwanted = 0;
// Steps back the page has taken itself and whose arrival the browser has not yet said; and the entries of layers opened
// meanwhile, put in the history once they have arrived, so that arriving does not close them.
let going = 0;
const waiting: number[] = [];

// Each entry is numbered as it is made. Going back to one closes every layer made after it: one for a Back, none for an
// entry the page took out itself, and all of them should a Back land on an entry left over from before.
function onPop(e: PopStateEvent) {
  if (going > 0) {
    going--;
    if (going === 0) for (const entry of waiting.splice(0)) history.pushState({ trovolve: entry }, '');
    return;
  }
  const landed = (e.state as { trovolve?: number } | null)?.trovolve ?? 0;
  while ((layers.at(-1)?.entry ?? 0) > landed) layers.pop()!.close();
}

// Taken out once the page has settled: going back is not done at once, and a layer opened meanwhile would be closed by it.
function takeOut() {
  const n = unwanted;
  unwanted = 0;
  if (n === 0) return;
  going++;
  history.go(-n);
}

/**
 * Gives what is open its own entry in the history while it is open: Back closes it, by calling `onBack`, and closing it
 * any other way takes its entry back out, so that the next Back closes what is under it. Something opened as another
 * closes, as when one sheet gives way to the next, takes over the other's entry.
 */
export function useBackLayer(open: boolean, onBack: () => void): void {
  const back = useRef(onBack);
  useLayoutEffect(() => { back.current = onBack; });

  useEffect(() => {
    if (!open) return;
    if (!listening) { window.addEventListener('popstate', onPop); listening = true; }
    const layer = { entry: ++made, close: () => back.current() };
    layers.push(layer);
    if (unwanted > 0) {
      unwanted--;
      history.replaceState({ trovolve: layer.entry }, '');
    } else if (going > 0) {
      waiting.push(layer.entry);
    } else {
      history.pushState({ trovolve: layer.entry }, '');
    }
    return () => {
      const at = layers.indexOf(layer);
      // Closed by Back, its entry has gone already; closed any other way, it is taken out, or never put in.
      if (at < 0) return;
      layers.splice(at, 1);
      const queued = waiting.indexOf(layer.entry);
      if (queued >= 0) { waiting.splice(queued, 1); return; }
      unwanted++;
      queueMicrotask(takeOut);
    };
  }, [open]);
}
