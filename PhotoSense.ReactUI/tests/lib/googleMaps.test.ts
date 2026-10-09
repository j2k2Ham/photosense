import { afterEach, describe, expect, it, vi } from 'vitest';

type Library = typeof import('../../lib/googleMaps');
// What the module remembers (the library once fetched, a key once refused) is started afresh for each test.
const load = async (): Promise<Library> => { vi.resetModules(); return import('../../lib/googleMaps'); };
const page = window as unknown as { google?: unknown; photosenseMapsReady?(): void; gm_authFailure?(): void };
const scripts = () => [...document.head.querySelectorAll<HTMLScriptElement>('script[src*="maps.googleapis.com"]')];

afterEach(() => {
  vi.unstubAllEnvs();
  scripts().forEach(s => s.remove());
  delete page.google;
});

describe('Google\'s map library', () => {
  it('has a key only when one was set for the UI', async () => {
    const maps = await load();
    vi.stubEnv('NEXT_PUBLIC_GOOGLE_MAPS_KEY', '');
    expect(maps.googleMapsKey()).toBeUndefined();
    vi.stubEnv('NEXT_PUBLIC_GOOGLE_MAPS_KEY', 'key-1');
    expect(maps.googleMapsKey()).toBe('key-1');
  });

  it('is fetched from Google once, however many maps ask for it', async () => {
    const maps = await load();
    const first = maps.loadGoogleMaps('key 1&x'), second = maps.loadGoogleMaps('key 1&x');
    expect(second).toBe(first);
    expect(scripts().map(s => [s.src, s.async])).toEqual([['https://maps.googleapis.com/maps/api/js?key=key%201%26x&v=weekly&loading=async&callback=photosenseMapsReady', true]]);
    const library = { Map: vi.fn() };
    page.google = { maps: library };
    page.photosenseMapsReady!();
    await expect(first).resolves.toBe(library);
  });

  it('says so when Google cannot be reached, and tries again the next time a map is shown', async () => {
    const maps = await load();
    const failed = maps.loadGoogleMaps('k');
    (scripts()[0].onerror as () => void)();
    await expect(failed).rejects.toThrow('Google Maps could not be reached.');
    expect(scripts()).toHaveLength(0);
    void maps.loadGoogleMaps('k');
    expect(scripts()).toHaveLength(1);
  });

  it('tells whoever is watching when Google refuses the key, and whoever starts watching afterwards', async () => {
    const maps = await load();
    const early = vi.fn(), stopped = vi.fn(), late = vi.fn();
    maps.onGoogleMapsRefused(early);
    maps.onGoogleMapsRefused(stopped)();
    void maps.loadGoogleMaps('bad');
    expect(early).not.toHaveBeenCalled();
    page.gm_authFailure!();
    expect([early.mock.calls.length, stopped.mock.calls.length]).toEqual([1, 0]);
    maps.onGoogleMapsRefused(late);
    expect(late).toHaveBeenCalledOnce();
  });
});
