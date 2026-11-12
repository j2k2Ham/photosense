import { useEffect, useLayoutEffect, useRef } from 'react';

// What is open over the screens, oldest first: each a pushed screen, a sheet or a full-screen view with an entry of its
// own in the browser's history, so that Android's Back and iOS's swipe from the edge close the newest first.
const layers: { entry: number; close(): void }[] = [];
let made = 0;
let listening = false;
// Entries of layers closed some other way than by Back, not yet taken out of the history.
let unwanted = 0;

// Each entry is numbered as it is made. Going back to one closes every layer made after it: one for a Back, none for an
// entry the page took out itself, and all of them should a Back land on an entry left over from before.
function onPop(e: PopStateEvent) {
  const landed = (e.state as { photosense?: number } | null)?.photosense ?? 0;
  while ((layers.at(-1)?.entry ?? 0) > landed) layers.pop()!.close();
}

// Taken out once the page has settled: going back is not done at once, and a layer opened meanwhile would be closed by it.
function takeOut() {
  const n = unwanted;
  unwanted = 0;
  if (n > 0) history.go(-n);
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
      history.replaceState({ photosense: layer.entry }, '');
    } else {
      history.pushState({ photosense: layer.entry }, '');
    }
    return () => {
      const at = layers.indexOf(layer);
      // Closed by Back, its entry has gone already; closed any other way, it is taken out.
      if (at < 0) return;
      layers.splice(at, 1);
      unwanted++;
      queueMicrotask(takeOut);
    };
  }, [open]);
}
