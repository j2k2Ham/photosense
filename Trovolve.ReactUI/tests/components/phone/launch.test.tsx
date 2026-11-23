import React from 'react';
import { act, createEvent, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { Launch, LAUNCH_FADE, LAUNCH_LONGEST } from '../../../components/phone/Launch';

const NAME = 'trovolve. Your memories. Evolved.';

/** Ends the name's animation as a browser would: jsdom has no AnimationEvent, so React listens for WebKit's own name, at the root. */
function nameRisen(container: HTMLElement) {
  const name = container.querySelector('.tv-name')!;
  fireEvent(name, createEvent('webkitAnimationEnd', name, {}, { EventType: 'Event', defaultInit: { bubbles: true } }));
}

describe('Launch', () => {
  beforeEach(() => { vi.useFakeTimers(); });
  afterEach(() => { vi.useRealTimers(); });

  it('plays over everything, fades once the name has risen, and goes', () => {
    const { container } = render(<Launch />);
    expect(screen.getByRole('img', { name: NAME })).toBeTruthy();
    expect(container.firstElementChild!.className).not.toContain('opacity-0');

    nameRisen(container);
    expect(container.firstElementChild!.className).toContain('opacity-0');
    act(() => { vi.advanceTimersByTime(LAUNCH_FADE); });
    expect(screen.queryByRole('img', { name: NAME })).toBeNull();
  });

  it('goes by the clock when nothing animates, as with reduced motion', () => {
    const { container } = render(<Launch />);
    act(() => { vi.advanceTimersByTime(LAUNCH_LONGEST - 1); });
    expect(container.firstElementChild!.className).not.toContain('opacity-0');
    act(() => { vi.advanceTimersByTime(1); });
    expect(container.firstElementChild!.className).toContain('opacity-0');
    act(() => { vi.advanceTimersByTime(LAUNCH_FADE); });
    expect(container.firstElementChild).toBeNull();
  });
});
