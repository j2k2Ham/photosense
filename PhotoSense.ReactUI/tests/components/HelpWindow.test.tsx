import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { HelpWindow } from '../../components/HelpWindow';
import { GUIDE, guideFor, searchGuide, type GuidePlatform } from '../../lib/guide';

const COMPUTER = guideFor('computer');

function open(platform: GuidePlatform = 'computer') {
  const onGo = vi.fn(), onClose = vi.fn();
  const view = render(<HelpWindow platform={platform} onGo={onGo} onClose={onClose} />);
  return { onGo, onClose, ...view };
}
const guide = () => screen.getByRole('dialog', { name: 'Help' });
const topics = () => within(screen.getByRole('navigation', { name: 'Topics' })).queryAllByRole('button');
const topic = (title: string) => within(screen.getByRole('navigation', { name: 'Topics' })).getByRole('button', { name: new RegExp(`^${title}`) });
const page = () => screen.getByRole('article');
const search = () => screen.getByRole('textbox', { name: 'Search the guide' });
const reading = () => within(page()).getByRole('heading', { level: 2 }).textContent;
// Everything a platform's guide says, as one text.
const said = (platform: GuidePlatform) => guideFor(platform).flatMap(t => [t.title, t.summary, ...t.sections.flatMap(s => [s.heading ?? '', ...(s.text ?? []), ...(s.steps ?? []), ...(s.tips ?? [])])]).join(' ');
const ids = (query: string, platform: GuidePlatform) => searchGuide(query, platform).map(t => t.id);

describe('the guide itself', () => {
  it('has a topic for each part of PhotoSense on a computer, each told from the others, and says where its buttons go', () => {
    expect(COMPUTER.map(t => t.title)).toEqual([
      'Getting started', 'Scanning for duplicates', 'Reviewing duplicates', 'Similar shots', 'Large videos, screenshots, bursts and blurry shots', 'Organize: suggested folders', 'Organize: folders of your own',
      'Organize: previewing and moving', 'Organize: selecting and deleting files', 'Backing up a phone to a computer', 'Removed files and deleting permanently', 'The free plan and Pro', 'Settings, theme, errors and About', 'If something looks wrong',
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

  it("has a topic for each part of PhotoSense on an iPhone or iPad and on Android, with no button to a computer's screens", () => {
    const titles = ['Getting started', 'Library', 'Clean up', 'Organize', 'Backing up to your computer', 'Removed photos and deleting permanently', 'The free plan and Pro', 'Settings', 'If something looks wrong'];
    for (const platform of ['ios', 'android'] as const) {
      const phone = guideFor(platform);
      expect(phone.map(t => t.title)).toEqual(titles);
      expect(phone.filter(t => t.go)).toEqual([]);
      // Each topic says something on each platform, and no heading is said twice in one.
      for (const t of phone) {
        expect(t.sections.length).toBeGreaterThan(0);
        const headings = t.sections.flatMap(s => (s.heading ? [s.heading] : []));
        expect(new Set(headings).size).toBe(headings.length);
      }
    }
  });

  it('speaks of each platform in its own words, and of nothing another has', () => {
    const ios = said('ios'), android = said('android'), computer = said('computer');
    for (const word of ['album', 'PhotoSense Removed', 'Recently Deleted', 'App Store', 'Apple Account', 'iCloud', 'iPad', 'Favorites']) expect(ios).toContain(word);
    for (const word of ['Google Play', 'Pictures/PhotoSense Removed', 'DCIM/Camera', 'Google account', 'about once an hour']) expect(ios).not.toContain(word);
    for (const word of ['folder', 'Pictures/PhotoSense Removed', 'DCIM/Camera', 'Google Play', 'Google account', 'bin', 'about once an hour']) expect(android).toContain(word);
    for (const word of ['App Store', 'Apple Account', 'Recently Deleted', 'iCloud', 'iPad', 'Favorites', 'album']) expect(android).not.toContain(word);
    // A computer's guide has the computer's own screens, and a phone's has none of them.
    for (const word of ['_PhotoSense_Removed', 'Browse', 'Recycle Bin', 'Pair a phone', 'PhotoSense Free']) expect(computer).toContain(word);
    for (const word of ['App Store', 'Google Play', 'Library', 'Backup folder']) expect(computer).not.toContain(word);
    for (const word of ['_PhotoSense_Removed', 'Browse', 'Recycle Bin', 'Hover']) {
      expect(ios).not.toContain(word);
      expect(android).not.toContain(word);
    }
  });

  it('is searched by every word looked for, whatever their case and wherever in a topic they are', () => {
    expect(searchGuide('', 'computer')).toEqual(COMPUTER);
    expect(searchGuide('   ', 'computer')).toEqual(COMPUTER);
    expect(ids('SATELLITE', 'computer')).toEqual(['organize']);
    // Words marked as names on screen are found like any others, and all the words must be there.
    expect(ids('delete permanently recycle', 'computer')).toEqual(['removed']);
    expect(ids('burst frames', 'computer')).toContain('similar');
    expect(ids('zebra', 'computer')).toEqual([]);
    // A curly apostrophe and a straight one are the same.
    expect(ids("it's the same number", 'ios')).toEqual(['backup']);
    expect(ids('it’s the same number', 'android')).toEqual(['backup']);
  });

  it('finds each feature by the words a person would use, on each platform', () => {
    expect(ids('trash', 'computer')).toContain('removed');
    expect(ids('recover', 'computer')).toEqual(['removed']);
    expect(ids('transfer', 'computer')).toEqual(['phones']);
    expect(ids('price', 'computer')).toEqual(['plan']);
    expect(ids('dark mode', 'computer')).toEqual(['menu']);
    expect(ids('copies', 'computer')).toContain('duplicates');
    expect(ids('screenshot', 'computer')).toContain('categories');
    expect(ids('out of focus', 'computer')).toEqual(['categories']);
    expect(ids('whatsapp', 'computer')).toEqual(['categories']);

    for (const platform of ['ios', 'android'] as const) {
      expect(ids('recover', platform)).toEqual(['phone-removed']);
      expect(ids('transfer', platform)).toEqual(['backup']);
      expect(ids('cancel subscription', platform)).toEqual(['phone-plan']);
      expect(ids('dupes', platform)).toEqual(['phone-cleanup']);
      expect(ids('trip', platform)).toEqual(['phone-organize']);
      expect(ids('camera roll', platform)).toEqual(['library']);
      expect(ids('dark mode', platform)).toEqual(['phone-settings']);
      expect(ids('qr code', platform)).toContain('backup');
      expect(ids('sidebar', platform)).toEqual(['phone-start']);
      expect(ids('screenshot', platform)).toEqual(['phone-cleanup']);
      expect(ids('big videos', platform)).toEqual(['phone-cleanup']);
      expect(ids('free up space', platform)).toContain('phone-cleanup');
      expect(ids('shrink', platform)).toEqual(['phone-cleanup']);
      expect(ids('compress', platform)).toEqual(['library', 'phone-cleanup', 'phone-plan']);
      expect(ids('running out', platform)).toEqual(['phone-cleanup']);
      expect(ids('reminder', platform)).toContain('phone-plan');
      expect(ids('wrapped', platform)).toEqual(['library']);
      expect(ids('share card', platform)).toContain('library');
    }
    // What another platform has is not found.
    expect(ids('google play', 'ios')).toEqual([]);
    expect(ids('icloud', 'android')).toEqual([]);
    expect(ids('app store', 'computer')).toEqual([]);
  });
});

describe('the help window', () => {
  it('lists the topics, opens on the first, and shows what is named on screen in bold', () => {
    open();
    expect(guide()).toHaveTextContent(/^User guide/);
    expect(topics().map(t => t.firstElementChild!.textContent)).toEqual(COMPUTER.map(t => t.title));
    expect(topic('Getting started')).toHaveAttribute('aria-current', 'page');
    expect(topic('Similar shots')).not.toHaveAttribute('aria-current');
    expect(page()).toHaveAccessibleName('Getting started');
    expect(page()).toHaveTextContent('1 of 14');
    expect(within(page()).getByText('What PhotoSense does, and how to find your way around it.')).toBeInTheDocument();
    expect(within(page()).getAllByText('Clean up')[0].tagName).toBe('STRONG');
    expect(page()).not.toHaveTextContent('**');
    // A numbered list for what is done in order, a plain one for what is worth knowing.
    expect(within(page()).getByRole('heading', { level: 3, name: 'A good first run' }).parentElement!.querySelector('ol')!.children).toHaveLength(6);
    expect(within(page()).getByRole('heading', { level: 3, name: 'Finding your way' }).parentElement!.querySelector('ul')!.children).toHaveLength(5);
    // The first topic describes no one place, so it has no button to one.
    expect(within(page()).queryByRole('button', { name: /^(Go to|Show|Open)/ })).not.toBeInTheDocument();
  });

  it('shows every topic in full, each read from its top', async () => {
    open();
    for (const t of COMPUTER) {
      page().scrollTop = 300;
      await userEvent.click(topic(t.title));
      expect(reading()).toBe(t.title);
      expect(page().scrollTop).toBe(t === COMPUTER[0] ? 300 : 0);
      expect(within(page()).queryAllByRole('heading', { level: 3 }).map(h => h.textContent)).toEqual(t.sections.filter(s => s.heading).map(s => s.heading));
      expect(page()).not.toHaveTextContent('**');
    }
  });

  it('is read from one topic to the next and back', async () => {
    open();
    expect(within(page()).queryByRole('button', { name: /^Previous/ })).not.toBeInTheDocument();
    await userEvent.click(within(page()).getByRole('button', { name: /^Next\s*Scanning for duplicates$/ }));
    expect(reading()).toBe('Scanning for duplicates');
    expect(page()).toHaveTextContent('2 of 14');
    await userEvent.click(within(page()).getByRole('button', { name: /^Previous\s*Getting started$/ }));
    expect(reading()).toBe('Getting started');
    await userEvent.click(topic('If something looks wrong'));
    expect(within(page()).getByRole('button', { name: /^Previous\s*Settings, theme, errors and About$/ })).toBeInTheDocument();
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
    expect(topics().length).toBeLessThan(COMPUTER.length);
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
    expect(topics()).toHaveLength(COMPUTER.length);
    // The topic that was being read before nothing was found is back.
    expect(reading()).toBe('Similar shots');
  });

  it("on a phone shows the phone's own topics, in its words, with no button to a computer's screens", async () => {
    open('android');
    expect(topics().map(t => t.firstElementChild!.textContent)).toEqual(guideFor('android').map(t => t.title));
    expect(page()).toHaveTextContent('1 of 9');
    expect(page()).toHaveTextContent('The first launch');
    await userEvent.click(topic('Removed photos and deleting permanently'));
    expect(page()).toHaveTextContent('Pictures/PhotoSense Removed');
    expect(page()).not.toHaveTextContent('Recently Deleted');
    expect(within(page()).queryByRole('button', { name: /^(Go to|Show|Open)/ })).not.toBeInTheDocument();
    await userEvent.type(search(), 'google play');
    expect(topics().map(t => t.firstElementChild!.textContent)).toEqual(['The free plan and Pro']);
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
