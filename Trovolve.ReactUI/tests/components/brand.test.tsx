import React from 'react';
import { createEvent, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { FEATURE_NAMES, FeatureIcon, type FeatureIconName } from '../../components/FeatureIcons';
import { TrovolveMark, TrovolveSplash, TrovolveWordmark } from '../../components/TrovolveBrand';

/** Ends the name's animation as a browser would: jsdom has no AnimationEvent, so React listens for WebKit's own name, at the root. */
function nameRisen(container: HTMLElement) {
  const name = container.querySelector('.tv-name')!;
  fireEvent(name, createEvent('webkitAnimationEnd', name, {}, { EventType: 'Event', defaultInit: { bubbles: true } }));
}

describe('FeatureIcon', () => {
  it('draws every feature in the text colour, hidden from screen readers, 24 pixels unless told', () => {
    const names = Object.keys(FEATURE_NAMES) as FeatureIconName[];
    expect(names).toHaveLength(22);
    for (const name of names) {
      const { container, unmount } = render(<FeatureIcon name={name} />);
      const svg = container.querySelector('svg')!;
      expect(svg.getAttribute('aria-hidden')).toBe('true');
      expect(svg.getAttribute('width')).toBe('24');
      expect(svg.innerHTML).toContain('currentColor');
      unmount();
    }
  });

  it('takes a size and a class', () => {
    const { container } = render(<FeatureIcon name="vault" size={40} className="text-brand" />);
    const svg = container.querySelector('svg')!;
    expect(svg.getAttribute('height')).toBe('40');
    expect(svg.getAttribute('class')).toBe('text-brand');
  });
});

describe('TrovolveMark', () => {
  it('is decoration without a label, five layers in colour at 32 pixels', () => {
    const { container } = render(<TrovolveMark />);
    const svg = container.querySelector('svg')!;
    expect(svg.getAttribute('aria-hidden')).toBe('true');
    expect(svg.getAttribute('role')).toBeNull();
    expect(svg.querySelectorAll('polygon')).toHaveLength(5);
    expect(svg.querySelector('polygon')!.getAttribute('fill')).toBe('#d1f2f8');
  });

  it('is a picture with a label, in the deep tones for light surfaces', () => {
    render(<TrovolveMark label="Trovolve" tone="deep" size={64} />);
    const mark = screen.getByRole('img', { name: 'Trovolve' });
    expect(mark.getAttribute('aria-hidden')).toBeNull();
    expect(mark.querySelector('polygon')!.getAttribute('fill')).toBe('#3ebfc6');
  });

  it('draws three thicker layers below 32 pixels, in either tone', () => {
    const color = render(<TrovolveMark size={16} />).container.querySelectorAll('polygon');
    expect(color).toHaveLength(3);
    expect(color[0].getAttribute('fill')).toBe('#80dbeb');
    expect(color[0].getAttribute('stroke-width')).toBe('2.8');
    const deep = render(<TrovolveMark size={16} tone="deep" />).container.querySelectorAll('polygon');
    expect(deep[0].getAttribute('fill')).toBe('#1f9aa3');
  });

  it('draws in the text colour when mono', () => {
    const { container } = render(<TrovolveMark tone="mono" />);
    for (const layer of container.querySelectorAll('polygon')) expect(layer.getAttribute('fill')).toBe('currentColor');
  });
});

describe('TrovolveWordmark', () => {
  it('is a picture named trovolve, 24 pixels high unless told', () => {
    const { rerender } = render(<TrovolveWordmark />);
    expect(screen.getByRole('img', { name: 'trovolve' }).getAttribute('height')).toBe('24');
    rerender(<TrovolveWordmark height={48} className="text-t1" />);
    expect(screen.getByRole('img', { name: 'trovolve' }).getAttribute('height')).toBe('48');
  });
});

describe('TrovolveSplash', () => {
  it('says its name and line, and says when the name has risen', () => {
    const onDone = vi.fn();
    const { container } = render(<TrovolveSplash onDone={onDone} />);
    expect(screen.getByRole('img', { name: 'trovolve. Your memories. Evolved.' })).toBeTruthy();
    expect(container.querySelectorAll('.tv-layer')).toHaveLength(5);
    nameRisen(container);
    expect(onDone).toHaveBeenCalledOnce();
  });

  it('plays at any size with no one waiting for it', () => {
    const { container } = render(<TrovolveSplash size={60} />);
    expect(container.querySelector('svg')!.getAttribute('height')).toBe('60');
    nameRisen(container);
  });
});
