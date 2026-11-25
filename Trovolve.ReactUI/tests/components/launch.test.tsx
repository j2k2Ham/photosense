import React, { StrictMode } from 'react';
import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Launch } from '../../components/Launch';
import { LAUNCH_FADE, LAUNCH_KEY, LAUNCH_SECONDS, noteLaunched } from '../../lib/launch';

const NAME = 'trovolve. Your memories. Evolved.';

describe('Launch', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); document.documentElement.removeAttribute('data-launched'); });

  it('stays over everything for the seconds set when the app first loads, then fades and goes, as React runs it twice in development', () => {
    const { container } = render(<StrictMode><Launch /></StrictMode>);
    expect(screen.getByRole('img', { name: NAME })).toBeTruthy();
    expect(container.firstElementChild!.className).toContain('fixed inset-0 z-[60]');
    expect(sessionStorage.getItem(LAUNCH_KEY)).toBe('yes');
    act(() => { vi.advanceTimersByTime(LAUNCH_SECONDS * 1000 - LAUNCH_FADE - 1); });
    expect(container.firstElementChild!.className).not.toContain('opacity-0');
    act(() => { vi.advanceTimersByTime(1); });
    expect(container.firstElementChild!.className).toContain('opacity-0');
    act(() => { vi.advanceTimersByTime(LAUNCH_FADE); });
    expect(container.firstElementChild).toBeNull();
  });

  it('does not play again on a reload, which the layout marked before the page was drawn', () => {
    document.documentElement.setAttribute('data-launched', '');
    const { container } = render(<Launch />);
    act(() => { vi.advanceTimersByTime(0); });
    expect(container.firstElementChild).toBeNull();
  });

  it('says so when the browser refuses to keep that it has played', () => {
    expect(noteLaunched()).toBe(true);
    vi.stubGlobal('sessionStorage', { setItem() { throw new Error('refused'); }, clear() {} });
    expect(noteLaunched()).toBe(false);
  });
});
