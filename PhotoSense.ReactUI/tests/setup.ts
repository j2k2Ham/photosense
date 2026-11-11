import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterEach, vi } from 'vitest';

// Newer versions of Node bring a localStorage of their own that is not usable without a file to back it,
// and it hides the test browser's. The tests get a plain one that forgets everything between tests.
class MemoryStorage {
  private items = new Map<string, string>();
  getItem(key: string) { return this.items.get(key) ?? null; }
  setItem(key: string, value: string) { this.items.set(key, String(value)); }
  clear() { this.items.clear(); }
}
Object.defineProperty(globalThis, 'localStorage', { value: new MemoryStorage(), configurable: true, writable: true });

// The test browser cannot be asked whether its system is set to light, which the Auto theme follows: it says dark.
// A test of the theme stands in its own answer with vi.stubGlobal('matchMedia', …).
Object.defineProperty(window, 'matchMedia', {
  configurable: true, writable: true,
  value: (query: string) => ({ media: query, matches: false, addEventListener() {}, removeEventListener() {} }),
});

// The test browser has no IntersectionObserver; Library's stand-in sees nothing come into view unless a test says so.
class NoIntersections { observe() {} disconnect() {} }
Object.defineProperty(window, 'IntersectionObserver', { configurable: true, writable: true, value: NoIntersections });

afterEach(() => {
  localStorage.clear();
  cleanup();
  vi.restoreAllMocks();
  vi.useRealTimers();
  vi.unstubAllGlobals();
  vi.unstubAllEnvs();
});
