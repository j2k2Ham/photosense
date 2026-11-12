import { render } from '@testing-library/react';
import React from 'react';
import { vi } from 'vitest';
import { PhoneContext, WORDS, type PhoneFrame, type Platform } from '../../../lib/phone';

/** A phone's frame for a part drawn on its own: an iPhone unless the test says otherwise. */
export function frame(platform: Platform = 'ios', more: Partial<PhoneFrame> = {}): PhoneFrame {
  return {
    platform, form: 'phone', ios: platform === 'ios', tablet: false, words: WORDS[platform], sidebarHidden: false, foot: 'tabs',
    openSettings: vi.fn(), ...more,
  };
}

/** Draws a part of the phone layout inside the frame given. */
export function inFrame(ui: React.ReactElement, given: PhoneFrame = frame()) {
  const view = render(<PhoneContext.Provider value={given}>{ui}</PhoneContext.Provider>);
  return { ...view, again: (next: React.ReactElement, other: PhoneFrame = given) => view.rerender(<PhoneContext.Provider value={other}>{next}</PhoneContext.Provider>) };
}

/** A system that answers whether the page is a tablet's width, and can be turned. */
export function width(tablet: boolean) {
  const heard = new Set<() => void>();
  const query = { matches: tablet, addEventListener: (_: string, f: () => void) => heard.add(f), removeEventListener: (_: string, f: () => void) => heard.delete(f) };
  vi.stubGlobal('matchMedia', () => query);
  return { turn(to: boolean) { query.matches = to; heard.forEach(f => f()); } };
}
