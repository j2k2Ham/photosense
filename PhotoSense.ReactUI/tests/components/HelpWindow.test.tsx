import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { HelpWindow } from '../../components/HelpWindow';
import { GUIDE, searchGuide } from '../../lib/guide';

function open() {
  const onGo = vi.fn(), onClose = vi.fn();
  const view = render(<HelpWindow onGo={onGo} onClose={onClose} />);
  return { onGo, onClose, ...view };
}
const guide = () => screen.getByRole('dialog', { name: 'Help' });
const topics = () => within(screen.getByRole('navigation', { name: 'Topics' })).queryAllByRole('button');
const topic = (title: string) => within(screen.getByRole('navigation', { name: 'Topics' })).getByRole('button', { name: new RegExp(`^${title}`) });
const page = () => screen.getByRole('article');
const search = () => screen.getByRole('textbox', { name: 'Search the guide' });
const reading = () => within(page()).getByRole('heading', { level: 2 }).textContent;

describe('the guide itself', () => {
  it('has a topic for each part of PhotoSense, each told from the others, and says where its buttons go', () => {
    expect(GUIDE.map(t => t.title)).toEqual([
      'Getting started', 'Scanning for duplicates', 'Reviewing duplicates', 'Similar shots', 'Organize: suggested folders', 'Organize: folders of your own',
      'Organize: previewing and moving', 'Organize: selecting and deleting files', 'Removed files and deleting permanently', 'Settings, theme and errors', 'If something looks wrong',
    ]);
    expect(new Set(GUIDE.map(t => t.id)).size).toBe(GUIDE.length);
    expect(GUIDE.filter(t => t.go).map(t => [t.id, t.go!.place])).toEqual([
      ['scan', 'scan'], ['duplicates', 'duplicates'], ['similar', 'similar'], ['organize', 'organize'], ['own', 'organize'], ['move', 'organize'], ['select', 'organize'],
      ['removed', 'settings'], ['menu', 'settings'],
    ]);
    // Every section says something, and what is marked as a name on screen is closed again.
    for (const t of GUIDE) {
      for (const section of t.sections) {
        const lines = [...(section.text ?? []), ...(section.steps ?? []), ...(section.tips ?? [])];
        expect(lines.length).toBeGreaterThan(0);
        for (const line of lines) expect(line.split('**').length % 2).toBe(1);
      }
    }
  });

  it('is searched by every word looked for, whatever their case and wherever in a topic they are', () => {
    expect(searchGuide('')).toEqual(GUIDE);
    expect(searchGuide('   ')).toEqual(GUIDE);
    expect(searchGuide('SATELLITE').map(t => t.id)).toEqual(['organize']);
    // Words marked as names on screen are found like any others, and all the words must be there.
    expect(searchGuide('delete permanently recycle').map(t => t.id)).toEqual(['removed']);
    expect(searchGuide('burst frames').map(t => t.id)).toContain('similar');
    expect(searchGuide('zebra')).toEqual([]);
  });
});

describe('the help window', () => {
  it('lists the topics, opens on the first, and shows what is named on screen in bold', () => {
    open();
    expect(guide()).toHaveTextContent(/^User guide/);
    expect(topics().map(t => t.firstElementChild!.textContent)).toEqual(GUIDE.map(t => t.title));
    expect(topic('Getting started')).toHaveAttribute('aria-current', 'page');
    expect(topic('Similar shots')).not.toHaveAttribute('aria-current');
    expect(page()).toHaveAccessibleName('Getting started');
    expect(page()).toHaveTextContent('1 of 11');
    expect(within(page()).getByText('What PhotoSense does, and how to find your way around it.')).toBeInTheDocument();
    expect(within(page()).getAllByText('Clean up')[0].tagName).toBe('STRONG');
    expect(page()).not.toHaveTextContent('**');
    // A numbered list for what is done in order, a plain one for what is worth knowing.
    expect(within(page()).getByRole('heading', { level: 3, name: 'A good first run' }).parentElement!.querySelector('ol')!.children).toHaveLength(5);
    expect(within(page()).getByRole('heading', { level: 3, name: 'Finding your way' }).parentElement!.querySelector('ul')!.children).toHaveLength(4);
    // The first topic describes no one place, so it has no button to one.
    expect(within(page()).queryByRole('button', { name: /^(Go to|Show|Open)/ })).not.toBeInTheDocument();
  });

  it('shows every topic in full, each read from its top', async () => {
    open();
    for (const t of GUIDE) {
      page().scrollTop = 300;
      await userEvent.click(topic(t.title));
      expect(reading()).toBe(t.title);
      expect(page().scrollTop).toBe(t === GUIDE[0] ? 300 : 0);
      expect(within(page()).queryAllByRole('heading', { level: 3 }).map(h => h.textContent)).toEqual(t.sections.filter(s => s.heading).map(s => s.heading));
      expect(page()).not.toHaveTextContent('**');
    }
  });

  it('is read from one topic to the next and back', async () => {
    open();
    expect(within(page()).queryByRole('button', { name: /^Previous/ })).not.toBeInTheDocument();
    await userEvent.click(within(page()).getByRole('button', { name: /^Next\s*Scanning for duplicates$/ }));
    expect(reading()).toBe('Scanning for duplicates');
    expect(page()).toHaveTextContent('2 of 11');
    await userEvent.click(within(page()).getByRole('button', { name: /^Previous\s*Getting started$/ }));
    expect(reading()).toBe('Getting started');
    await userEvent.click(topic('If something looks wrong'));
    expect(within(page()).getByRole('button', { name: /^Previous\s*Settings, theme and errors$/ })).toBeInTheDocument();
    expect(within(page()).queryByRole('button', { name: /^Next/ })).not.toBeInTheDocument();
  });

  it('goes to the place a topic describes', async () => {
    const { onGo } = open();
    await userEvent.click(topic('Scanning for duplicates'));
    await userEvent.click(within(page()).getByRole('button', { name: 'Go to the scan screen' }));
    await userEvent.click(topic('Removed files and deleting permanently'));
    await userEvent.click(within(page()).getByRole('button', { name: 'Open Settings' }));
    expect(onGo.mock.calls).toEqual([['scan'], ['settings']]);
  });

  it('can be searched: the topics that mention what is looked for are listed, and one of them is shown', async () => {
    open();
    await userEvent.click(topic('Similar shots'));
    // Still found, the topic being read stays; no longer found, the first that is found takes its place.
    await userEvent.type(search(), 'burst');
    expect(topics().length).toBeLessThan(GUIDE.length);
    expect(reading()).toBe('Similar shots');
    await userEvent.clear(search());
    await userEvent.type(search(), 'satellite');
    expect(topics().map(t => t.firstElementChild!.textContent)).toEqual(['Organize: suggested folders']);
    expect(reading()).toBe('Organize: suggested folders');
    expect(page()).toHaveTextContent('1 of 1');
    expect(within(page()).queryByRole('button', { name: /^(Previous|Next)/ })).not.toBeInTheDocument();

    await userEvent.clear(search());
    await userEvent.type(search(), ' zebra ');
    expect(topics().map(t => t.textContent)).toEqual(['Show every topic']);
    expect(screen.getByRole('navigation', { name: 'Topics' })).toHaveTextContent('Nothing in the guide mentions “zebra”.');
    expect(page()).toHaveAccessibleName('Nothing found');
    expect(page()).toBeEmptyDOMElement();
    await userEvent.click(screen.getByRole('button', { name: 'Show every topic' }));
    expect(search()).toHaveValue('');
    expect(topics()).toHaveLength(GUIDE.length);
    // The topic that was being read before nothing was found is back.
    expect(reading()).toBe('Similar shots');
  });

  it('closes on Escape, on its own button and on a press outside it, and on nothing else', async () => {
    const { onClose, container, unmount } = open();
    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(guide());
    expect(onClose).not.toHaveBeenCalled();
    fireEvent.keyDown(window, { key: 'Escape' });
    await userEvent.click(within(guide()).getByRole('button', { name: 'Close' }));
    fireEvent.mouseDown(container.firstElementChild!);
    expect(onClose).toHaveBeenCalledTimes(3);
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});
