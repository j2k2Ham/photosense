import React from 'react';
import { act, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Launch } from '../../components/Launch';
import { LAUNCH_FADE, LAUNCH_KEY, LAUNCH_SECONDS, launchedBefore } from '../../lib/launch';

const NAME = 'trovolve. Your memories. Evolved.';

describe('Launch', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('stays over everything for the seconds set when the app first loads, then fades and goes', () => {
    expect(LAUNCH_SECONDS).toBe(180);
    const { container } = render(<Launch />);
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

  it('does not play again on a reload', () => {
    sessionStorage.setItem(LAUNCH_KEY, 'yes');
    const { container } = render(<Launch />);
    act(() => { vi.advanceTimersByTime(0); });
    expect(container.firstElementChild).toBeNull();
  });

  it('plays each time when the browser refuses to keep that it has', () => {
    vi.stubGlobal('sessionStorage', { getItem() { throw new Error('refused'); }, clear() {} });
    expect(launchedBefore()).toBe(false);
  });
});
