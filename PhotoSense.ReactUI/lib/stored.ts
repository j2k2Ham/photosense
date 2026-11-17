import { useCallback, useState, useSyncExternalStore } from 'react';

// Every component reading a key hears when this page keeps something new, and when another tab does.
const listeners = new Set<() => void>();

function listen(changed: () => void) {
  listeners.add(changed);
  window.addEventListener('storage', changed);
  return () => {
    listeners.delete(changed);
    window.removeEventListener('storage', changed);
  };
}

/** What this browser keeps under a key; null when nothing is, or when the browser refuses storage. */
export function readStored(key: string): string | null {
  try { return localStorage.getItem(key); } catch { return null; }
}

/** Keeps a value under a key in this browser, and tells every component reading it. False when the browser refuses. */
export function keep(key: string, value: string): boolean {
  try { localStorage.setItem(key, value); } catch { return false; }
  listeners.forEach(changed => changed());
  return true;
}

// The page is made ahead on the server, which has no storage: nothing is known there, nor until the page has woken.
const unknownOnTheServer = () => undefined;

/**
 * What this browser keeps under a key, and a way to change it. Undefined until the browser's storage is read, as
 * when the page is made ahead on the server; null when nothing is kept. A browser that refuses storage still has a
 * change made for the rest of this visit, in the component that made it.
 */
export function useStored(key: string): [string | null | undefined, (change: (before: string | null) => string) => void] {
  const stored = useSyncExternalStore(listen, () => readStored(key), unknownOnTheServer);
  const [unkept, setUnkept] = useState<{ readonly value: string }>();
  const change = useCallback((update: (before: string | null) => string) => {
    if (keep(key, update(readStored(key)))) setUnkept(undefined);
    else setUnkept(was => ({ value: update(was?.value ?? null) }));
  }, [key]);
  return [unkept ? unkept.value : stored, change];
}
