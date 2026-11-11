import { useEffect, useLayoutEffect, useRef } from 'react';

// What is open over the screens, oldest first: each a pushed screen, a sheet or a full-screen view with an entry of its
// own in the browser's history, so that Android's Back and iOS's swipe from the edge close the newest first.
const layers: { entry: number; close(): void }[] = [];
let made = 0;
let listening = false;

// Each entry is numbered as it is made. Going back to one closes every layer made after it: one for a Back, none for an
// entry the page took out itself, and all of them should a Back land on an entry left over from before.
function onPop(e: PopStateEvent) {
  const landed = (e.state as { photosense?: number } | null)?.photosense ?? 0;
  while ((layers.at(-1)?.entry ?? 0) > landed) layers.pop()!.close();
}

/**
 * Gives what is open its own entry in the history while it is open: Back closes it, by calling `onBack`, and closing it
 * any other way takes its entry back out, so that the next Back closes what is under it.
 */
export function useBackLayer(open: boolean, onBack: () => void): void {
  const back = useRef(onBack);
  useLayoutEffect(() => { back.current = onBack; });

  useEffect(() => {
    if (!open) return;
    if (!listening) { window.addEventListener('popstate', onPop); listening = true; }
    const layer = { entry: ++made, close: () => back.current() };
    layers.push(layer);
    history.pushState({ photosense: layer.entry }, '');
    return () => {
      const at = layers.indexOf(layer);
      // Closed by Back, its entry has gone already; closed any other way, it is taken out.
      if (at < 0) return;
      layers.splice(at, 1);
      history.back();
    };
  }, [open]);
}
