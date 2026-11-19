import { render } from '@testing-library/react';
import React from 'react';
import { vi } from 'vitest';
import { PhoneContext, WORDS, type PhoneFrame, type Platform } from '../../../lib/phone';
import { PlanProvider, type PlanGate } from '../../../components/phone/ProSheet';

/** A phone's frame for a part drawn on its own: an iPhone unless the test says otherwise. */
export function frame(platform: Platform = 'ios', more: Partial<PhoneFrame> = {}): PhoneFrame {
  return {
    platform, form: 'phone', ios: platform === 'ios', tablet: false, words: WORDS[platform], sidebarHidden: false, foot: 'tabs',
    openSettings: vi.fn(), placeSlot: null, ...more,
  };
}

/** The plan's gate for a part drawn on its own: no plan known yet, so every removal goes ahead in full, unless the test says otherwise. */
export function gate(more: Partial<PlanGate> = {}): PlanGate {
  return { plan: undefined, remove: vi.fn((count: number, _label: string, run: (allowed: number) => void) => run(count)), offer: vi.fn(), ...more };
}

/** Draws a part of the phone layout inside the frame given, with the plan's gate given. */
export function inFrame(ui: React.ReactElement, given: PhoneFrame = frame(), plan: PlanGate = gate()) {
  const wrap = (inner: React.ReactElement, f: PhoneFrame) => <PhoneContext.Provider value={f}><PlanProvider value={plan}>{inner}</PlanProvider></PhoneContext.Provider>;
  const view = render(wrap(ui, given));
  return { ...view, again: (next: React.ReactElement, other: PhoneFrame = given) => view.rerender(wrap(next, other)) };
}

/** A system that answers whether the page is a tablet's width, and can be turned. */
export function width(tablet: boolean) {
  const heard = new Set<() => void>();
  const query = { matches: tablet, addEventListener: (_: string, f: () => void) => heard.add(f), removeEventListener: (_: string, f: () => void) => heard.delete(f) };
  vi.stubGlobal('matchMedia', () => query);
  return { turn(to: boolean) { query.matches = to; heard.forEach(f => f()); } };
}
