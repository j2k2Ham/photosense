import { act, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadLastScan, saveLastScan } from '../../lib/lastScan';
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
