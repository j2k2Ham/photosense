import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadLastScan, loadOrganizeRoot, saveLastScan, saveOrganizeRoot } from '../../lib/lastScan';
import { loadAppSettings, useAppSettings } from '../../lib/settings';
import { useTheme } from '../../lib/theme';
import { THEME_KEY } from '../../lib/themeKey';

afterEach(() => {
  document.documentElement.classList.remove('light');
});

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

  it('still works for this visit where the browser refuses to remember it', () => {
    refuseStorage();
    const { result } = renderHook(() => useTheme());
    expect(result.current[0]).toBe('dark');
    act(() => result.current[1]('light'));
    expect(result.current[0]).toBe('light');
    expect(document.documentElement).toHaveClass('light');
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
