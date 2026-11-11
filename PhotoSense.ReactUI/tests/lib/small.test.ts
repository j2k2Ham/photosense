import { act, renderHook } from '@testing-library/react';
import { createElement } from 'react';
import { renderToStaticMarkup } from 'react-dom/server';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadLastScan, loadOrganizeRoot, saveLastScan, saveOrganizeRoot } from '../../lib/lastScan';
import { loadAppSettings, useAppSettings } from '../../lib/settings';
import { skinOf, useSkin } from '../../lib/skin';
import { useTheme } from '../../lib/theme';
import { SYSTEM_LIGHT, THEME_KEY } from '../../lib/themeKey';

afterEach(() => {
  document.documentElement.classList.remove('light');
  delete document.documentElement.dataset.skin;
});

/** A system whose light or dark setting the test turns, as the person would in the system's own settings. */
function system(light: boolean) {
  const heard = new Set<() => void>();
  const query = {
    media: SYSTEM_LIGHT, matches: light,
    addEventListener: (_: string, changed: () => void) => heard.add(changed),
    removeEventListener: (_: string, changed: () => void) => heard.delete(changed),
  };
  vi.stubGlobal('matchMedia', (asked: string) => { expect(asked).toBe(SYSTEM_LIGHT); return query; });
  return {
    turn(to: boolean) { query.matches = to; heard.forEach(changed => changed()); },
    listening: () => heard.size,
  };
}

/** A browser that refuses storage, as some do in private windows. */
function refuseStorage() {
  vi.spyOn(localStorage, 'getItem').mockImplementation(() => { throw new Error('denied'); });
  vi.spyOn(localStorage, 'setItem').mockImplementation(() => { throw new Error('denied'); });
}

describe('the theme', () => {
  it('is dark until the person chooses light, and the choice is remembered and applied at once', () => {
    const { result } = renderHook(() => useTheme());
    expect(result.current[0]).toBe('dark');

    act(() => result.current[1]('light'));
    expect(result.current[0]).toBe('light');
    expect(document.documentElement).toHaveClass('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('light');

    act(() => result.current[1]('dark'));
    expect(document.documentElement).not.toHaveClass('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('dark');
  });

  it('comes back as it was left', () => {
    localStorage.setItem(THEME_KEY, 'light');
    expect(renderHook(() => useTheme()).result.current[0]).toBe('light');
    localStorage.setItem(THEME_KEY, 'something else');
    expect(renderHook(() => useTheme()).result.current[0]).toBe('dark');
  });

  it('in Auto follows the system, and changes when the system does, until the person chooses one', () => {
    const light = system(true);
    const { result, unmount } = renderHook(() => useTheme());
    act(() => result.current[1]('auto'));
    expect(result.current).toEqual(['light', expect.any(Function), 'auto']);
    expect(document.documentElement).toHaveClass('light');
    expect(localStorage.getItem(THEME_KEY)).toBe('auto');

    act(() => light.turn(false));
    expect(result.current[0]).toBe('dark');
    expect(document.documentElement).not.toHaveClass('light');

    act(() => result.current[1]('light'));
    act(() => light.turn(false));
    expect(result.current.slice(0, 1).concat(result.current[2])).toEqual(['light', 'light']);
    unmount();
    expect(light.listening()).toBe(0);
  });

  it('starts as the caller says until the person chooses, as a phone starts in Auto, and a saved choice wins', () => {
    system(true);
    expect(renderHook(() => useTheme('auto')).result.current).toEqual(['light', expect.any(Function), 'auto']);
    localStorage.setItem(THEME_KEY, 'dark');
    expect(renderHook(() => useTheme('auto')).result.current[0]).toBe('dark');
    localStorage.setItem(THEME_KEY, 'auto');
    expect(renderHook(() => useTheme('dark')).result.current[0]).toBe('light');
  });

  it('is dark when the page is made ahead on the server, which has no system to follow', () => {
    system(true);
    function Probe() { return createElement('p', null, useTheme('auto')[0]); }
    expect(renderToStaticMarkup(createElement(Probe))).toBe('<p>dark</p>');
  });

  it('still works for this visit where the browser refuses to remember it', () => {
    refuseStorage();
    const { result } = renderHook(() => useTheme());
    expect(result.current[0]).toBe('dark');
    act(() => result.current[1]('light'));
    expect(result.current[0]).toBe('light');
    expect(document.documentElement).toHaveClass('light');
  });
});

describe('the skin', () => {
  it('is iOS or Material by the program, dark or light by the theme, and none on a computer', () => {
    expect([skinOf('iphone', 'dark'), skinOf('iphone', 'light'), skinOf('android', 'dark'), skinOf('android', 'light')])
      .toEqual(['ios-native-dark', 'ios-native-light', 'and-native-dark', 'and-native-light']);
    expect([skinOf('computer', 'dark'), skinOf(undefined, 'light')]).toEqual([undefined, undefined]);
  });

  it('is put on the page\'s root, follows the theme, and is taken off where there is none', () => {
    const root = document.documentElement;
    const { rerender } = renderHook(({ program, theme }) => useSkin(program, theme), { initialProps: { program: 'android' as string | undefined, theme: 'dark' as 'dark' | 'light' } });
    expect(root.dataset.skin).toBe('and-native-dark');
    rerender({ program: 'android', theme: 'light' });
    expect(root.dataset.skin).toBe('and-native-light');
    rerender({ program: undefined, theme: 'light' });
    expect(root).not.toHaveAttribute('data-skin');
  });
});

describe('the settings', () => {
  it('ask twice before deleting permanently until told not to, and the change is remembered', () => {
    const { result } = renderHook(() => useAppSettings());
    expect(result.current[0]).toEqual({ eraseAskTwice: true });

    act(() => result.current[1]({ eraseAskTwice: false }));
    expect(result.current[0]).toEqual({ eraseAskTwice: false });
    expect(JSON.parse(localStorage.getItem('photosense-settings')!)).toEqual({ eraseAskTwice: false });
    // The next visit starts from what was left.
    expect(renderHook(() => useAppSettings()).result.current[0]).toEqual({ eraseAskTwice: false });
    // A change that names nothing leaves everything as it is.
    act(() => result.current[1]({}));
    expect(result.current[0]).toEqual({ eraseAskTwice: false });
  });

  it('have their defaults for anything not saved, or saved in a way that cannot be read', () => {
    localStorage.setItem('photosense-settings', '{"somethingOlder":1}');
    expect(loadAppSettings()).toEqual({ eraseAskTwice: true, somethingOlder: 1 });
    localStorage.setItem('photosense-settings', 'not json');
    expect(loadAppSettings()).toEqual({ eraseAskTwice: true });
  });

  it('still work for this visit where the browser refuses to remember them', () => {
    refuseStorage();
    const { result } = renderHook(() => useAppSettings());
    expect(result.current[0]).toEqual({ eraseAskTwice: true });
    act(() => result.current[1]({ eraseAskTwice: false }));
    expect(result.current[0]).toEqual({ eraseAskTwice: false });
  });
});

describe('the folder last organized', () => {
  it('is remembered, and simply not known where the browser refuses storage', () => {
    expect(loadOrganizeRoot()).toBe('');
    saveOrganizeRoot('C:\\Phone Pictures');
    expect(loadOrganizeRoot()).toBe('C:\\Phone Pictures');
    refuseStorage();
    saveOrganizeRoot('D:\\other');
    expect(loadOrganizeRoot()).toBe('');
  });
});

describe('the last scan', () => {
  it('is remembered so the page can name the folders again', () => {
    expect(loadLastScan()).toEqual({ root: '', second: '', recursive: true });
    saveLastScan({ root: 'C:\\photos', second: 'D:\\backup', recursive: false });
    expect(loadLastScan()).toEqual({ root: 'C:\\photos', second: 'D:\\backup', recursive: false });
  });

  it('is simply not known when what was stored cannot be read, or storage is refused', () => {
    localStorage.setItem('photosense-last-scan', 'not json');
    expect(loadLastScan()).toEqual({ root: '', second: '', recursive: true });
    refuseStorage();
    saveLastScan({ root: 'C:\\photos', second: '', recursive: true });
    expect(loadLastScan()).toEqual({ root: '', second: '', recursive: true });
  });
});
