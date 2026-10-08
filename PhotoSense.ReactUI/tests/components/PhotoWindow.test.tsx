import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { PhotoWindow } from '../../components/PhotoWindow';
import type { GroupMemberDto, GroupMode } from '../../types';
import { member, photo } from '../fixtures';

const original = photo({ fileName: 'IMG_4299.JPG', fileSizeBytes: 25_165_824 });
const copy = member({ id: 'm1', fileName: 'IMG_4299.HEIC', folder: 'D:\\backup\\phone', format: 'HEIC', fileSizeBytes: 12_582_912, latitude: 46.1283, longitude: -112.9423, placeName: 'Near Anaconda, Montana, US', cameraModel: 'iPhone 12 Pro Max' }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');

type Options = { member?: GroupMemberDto; mode?: GroupMode; busy?: boolean; index?: number; count?: number };

function open(options: Options = {}) {
  const handlers = { onClose: vi.fn(), onSelectCopy: vi.fn(), onToggleKeep: vi.fn(), onRemove: vi.fn(), onOpenInViewer: vi.fn() };
  const props = { original, member: copy, mode: 'duplicates' as GroupMode, busy: false, index: 0, count: 1, ...options, ...handlers };
  const view = render(<PhotoWindow {...props} />);
  // The same window, drawn again with something about it changed.
  return { ...handlers, ...view, again: (changes: Options) => view.rerender(<PhotoWindow {...props} {...changes} />) };
}
const win = () => screen.getByRole('dialog');
const button = (name: string | RegExp) => within(win()).getByRole('button', { name });
const click = (name: string | RegExp) => userEvent.click(button(name));
const pressed = () => within(screen.getByRole('group', { name: 'View' })).getAllByRole('button').filter(b => b.getAttribute('aria-pressed') === 'true').map(b => b.textContent);

describe('the comparison window', () => {
  it('opens side by side, with both files captioned and the copy described', () => {
    open();
    expect(screen.getByRole('dialog', { name: 'Compare IMG_4299.HEIC' })).toBeInTheDocument();
    expect(pressed()).toEqual(['Side by side']);
    // Two files, each with its own name and its own place, said before anything else.
    expect(win()).toHaveTextContent(/^IMG_4299\.HEICa copy of IMG_4299\.JPGSame picture/);
    expect(win()).toHaveTextContent('ORIGINAL IMG_4299.JPG · 4032 × 3024 · JPEG · 24.0 MBC:\\photos\\2024');
    expect(win()).toHaveTextContent('THIS COPY IMG_4299.HEIC · 4032 × 3024 · HEIC · 12.0 MBD:\\backup\\phone');
    expect(win()).toHaveTextContent('Same picture. The same shot converted, resized or re-compressed.');
    expect(win()).toHaveTextContent('Near Anaconda, Montana, US46.1283° N, 112.9423° W');
    expect(within(win()).getByRole('link', { name: 'Show on map' })).toHaveAttribute('href', expect.stringContaining('mlat=46.1283'));
    expect(win()).toHaveTextContent('iPhone 12 Pro Max');
    expect(within(win()).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Name: the original is IMG_4299.JPG', 'Format: HEIC, the original is JPEG', 'File size: 12.0 MB, the original is 24.0 MB', 'Folder: phone, the original is in 2024']);
    expect(win()).toHaveTextContent('Original preferred: Opens everywhere: JPEG rather than HEIC');
    expect(win()).toHaveFocus();
  });

  it('shows one file at a time in the same spot, labelled for what it is', async () => {
    open();
    await click('This copy');
    expect(pressed()).toEqual(['This copy']);
    expect(win()).toHaveTextContent('This copy · Same picture');
    expect(screen.getByRole('dialog', { name: 'Compare IMG_4299.HEIC' })).toBeInTheDocument();

    await click('Original');
    expect(win()).toHaveTextContent('Original · it stays');
    expect(win()).toHaveTextContent(/^IMG_4299\.JPGthe original · it stays/);
    expect(win()).toHaveTextContent('IMG_4299.JPG · 4032 × 3024 · JPEG · 24.0 MBC:\\photos\\2024');
    expect(screen.getByRole('dialog', { name: 'Compare IMG_4299.JPG' })).toBeInTheDocument();
    await click('Side by side');
    expect(pressed()).toEqual(['Side by side']);
  });

  it('flips between the copy and the original on Space, from wherever it is', () => {
    open();
    fireEvent.keyDown(win(), { key: ' ' });
    expect(pressed()).toEqual(['Original']);
    fireEvent.keyDown(window, { key: ' ' });
    expect(pressed()).toEqual(['This copy']);
    fireEvent.keyDown(win(), { key: ' ' });
    expect(pressed()).toEqual(['Original']);
  });

  it('leaves Space to a button that has the focus, and ignores other keys', () => {
    open();
    fireEvent.keyDown(button('Open in default viewer'), { key: ' ' });
    fireEvent.keyDown(win(), { key: 'a' });
    expect(pressed()).toEqual(['Side by side']);
  });

  it('describes a file with nothing recorded about where or with what it was taken', () => {
    open({ member: member({ id: 'm2', fileName: 'copy.JPG', kept: true }, 'identical') });
    expect(win()).toHaveTextContent('Identical file. Byte-for-byte the same file.');
    expect(win()).toHaveTextContent('No location in this file');
    expect(win()).toHaveTextContent('Not recorded');
    expect(within(win()).queryByRole('link')).not.toBeInTheDocument();
    expect(within(win()).getAllByText('Keeping').length).toBe(2);
    expect(button('Stop keeping this copy')).toHaveClass('bg-keep-bg');
  });

  it('opens whichever file is showing in the system\'s viewer, and toggles keep on the copy', async () => {
    const { onOpenInViewer, onToggleKeep } = open();
    await click('Open in default viewer');
    expect(onOpenInViewer).toHaveBeenLastCalledWith(copy.photo);
    await click('Original');
    await click('Open in default viewer');
    expect(onOpenInViewer).toHaveBeenLastCalledWith(original);
    await click('Keep this copy too');
    expect(onToggleKeep).toHaveBeenCalledExactlyOnceWith(copy.photo);
  });

  it('deletes the copy only after being asked twice, in place', async () => {
    const { onRemove } = open();
    await click('Delete this copy · 12.0 MB');
    expect(win()).toHaveTextContent('Move IMG_4299.HEIC (12.0 MB) to _PhotoSense_Removed?The original, IMG_4299.JPG, stays where it is. The copy can be moved back later.');
    expect(onRemove).not.toHaveBeenCalled();
    await click('Cancel');
    expect(button('Delete this copy · 12.0 MB')).toBeInTheDocument();

    await click('Delete this copy · 12.0 MB');
    await click('Delete this copy');
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(copy.photo, original);
  });

  it('asks afresh about the copy that takes the place of one just deleted', async () => {
    const { again, onRemove } = open();
    await click('Delete this copy · 12.0 MB');
    expect(button('Delete this copy')).toBeInTheDocument();

    // The copy has gone and the group's next one is shown in the same window: nothing is armed for it.
    again({ member: member({ id: 'm9', fileName: 'IMG_4299 (1).JPG', fileSizeBytes: 25_165_824 }, 'identical') });
    expect(button('Delete this copy · 24.0 MB')).toBeEnabled();
    expect(within(win()).queryByRole('button', { name: 'Delete this copy' })).not.toBeInTheDocument();
    expect(within(win()).queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument();
    expect(onRemove).not.toHaveBeenCalled();
  });

  it('can delete the original instead, saying which file then takes its place', async () => {
    const { onRemove } = open();
    await click('Delete the original instead');
    expect(win()).toHaveTextContent('Move IMG_4299.JPG (24.0 MB) to _PhotoSense_Removed and keep IMG_4299.HEIC as the original instead?');
    await click('Cancel');
    await click('Delete the original instead');
    await click('Delete the original');
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(original, copy.photo);
  });

  it('shows that it is working, and lets nothing else be started meanwhile', async () => {
    const { again } = open();
    await click('Delete this copy · 12.0 MB');
    again({ busy: true });
    expect(button('Working…')).toBeDisabled();
    for (const name of ['Open in default viewer', 'Keep this copy too', 'Delete the original instead']) expect(button(name)).toBeDisabled();
    expect(button('Cancel')).toBeEnabled();
  });

  it('holds its content to its own height, so that a video cannot push the buttons out of sight', () => {
    open({ member: member({ id: 'v2', fileName: 'copy.MOV', isVideo: true, format: 'MOV' }, 'identical') });
    const content = button('Open in default viewer').closest('.grid')!;
    expect(content).toHaveClass('min-h-0', 'grid-rows-[minmax(0,1fr)]');
    expect(content.firstElementChild).toHaveClass('min-h-0');
    expect(button(/^Delete this copy ·/)).toBeEnabled();
  });

  it('shows working on the other confirmation too', async () => {
    const { again } = open({ mode: 'similar', member: member({ id: 'm3', fileName: 'IMG_4300.JPG' }, 'similar') });
    await click('Delete the best shot instead');
    expect(win()).toHaveTextContent(/to _PhotoSense_Removed\?/);
    expect(button('Delete the best shot')).toBeEnabled();
    again({ busy: true });
    expect(button('Working…')).toBeDisabled();
  });

  it('presents a similar shot as a different picture: no keeping, and the best shot named as such', async () => {
    open({ mode: 'similar', member: member({ id: 'm3', fileName: 'IMG_4300.JPG' }, 'similar') });
    expect(win()).toHaveTextContent('Similar. A burst frame or an edited version. Never removed along with the duplicates.');
    expect(win()).toHaveTextContent('BEST IMG_4299.JPG');
    expect(win()).toHaveTextContent(/^IMG_4300\.JPGsimilar to IMG_4299\.JPGSimilar/);
    expect(within(win()).queryByRole('button', { name: /keep/i })).not.toBeInTheDocument();
    expect(win()).not.toHaveTextContent('Original preferred');
    await click(/^Delete this copy ·/);
    expect(win()).toHaveTextContent('The best shot, IMG_4299.JPG, stays where it is.');
    await click('Original');
    expect(win()).toHaveTextContent('Best · it stays');
    expect(win()).toHaveTextContent(/^IMG_4299\.JPGthe best shot · it stays/);
  });

  describe('with several copies in the group', () => {
    const steps = () => within(win()).queryAllByRole('button', { name: /^(Previous|Next) copy$/ }).map(b => b.getAttribute('aria-label'));

    it('says which copy this is, and steps to the one before or after it from the picture or the keyboard', async () => {
      const { onSelectCopy } = open({ index: 1, count: 3 });
      expect(win()).toHaveTextContent(/^IMG_4299\.HEICa copy of IMG_4299\.JPGSame pictureCopy 2 of 3/);
      expect(win()).toHaveTextContent('to flip between this copy and the original in the same spot, ← → to move between the 3 copies');
      expect(steps()).toEqual(['Previous copy', 'Next copy']);

      await click('Next copy');
      expect(onSelectCopy).toHaveBeenLastCalledWith(2);
      await click('Previous copy');
      expect(onSelectCopy).toHaveBeenLastCalledWith(0);
      fireEvent.keyDown(win(), { key: 'ArrowRight' });
      expect(onSelectCopy).toHaveBeenLastCalledWith(2);
      fireEvent.keyDown(window, { key: 'ArrowLeft' });
      expect(onSelectCopy).toHaveBeenLastCalledWith(0);
      expect(onSelectCopy).toHaveBeenCalledTimes(4);
      // The two stay side by side: only the copy changes.
      expect(pressed()).toEqual(['Side by side']);
    });

    it('offers only the way there is from the first copy and from the last', () => {
      const { again, onSelectCopy } = open({ index: 0, count: 3 });
      expect(steps()).toEqual(['Next copy']);
      fireEvent.keyDown(win(), { key: 'ArrowLeft' });
      again({ index: 2 });
      expect(steps()).toEqual(['Previous copy']);
      fireEvent.keyDown(win(), { key: 'ArrowRight' });
      expect(onSelectCopy).not.toHaveBeenCalled();
    });

    it('brings up the copy when another one is asked for from the original\'s own view', async () => {
      const { onSelectCopy } = open({ index: 0, count: 2 });
      await click('Original');
      expect(steps()).toEqual(['Next copy']);
      await click('Next copy');
      expect(onSelectCopy).toHaveBeenCalledExactlyOnceWith(1);
      expect(pressed()).toEqual(['This copy']);
      expect(steps()).toEqual(['Next copy']);
    });

    it('leaves the arrow keys to a video\'s player, where they move through the video', () => {
      const { onSelectCopy, container } = open({ index: 0, count: 2, member: member({ id: 'v2', fileName: 'copy.MOV', isVideo: true, format: 'MOV' }, 'identical') });
      fireEvent.keyDown(container.querySelector('video')!, { key: 'ArrowRight' });
      expect(onSelectCopy).not.toHaveBeenCalled();
      fireEvent.keyDown(button('Next copy'), { key: 'ArrowRight' });
      expect(onSelectCopy).toHaveBeenCalledExactlyOnceWith(1);
    });

    it('stays on the copy it is working on', () => {
      const { onSelectCopy } = open({ index: 1, count: 3, busy: true });
      expect(button('Previous copy')).toBeDisabled();
      expect(button('Next copy')).toBeDisabled();
      fireEvent.keyDown(win(), { key: 'ArrowRight' });
      expect(onSelectCopy).not.toHaveBeenCalled();
    });
  });

  it('with a single copy, has nowhere to step to and does not say so', () => {
    const { onSelectCopy } = open();
    expect(within(win()).queryByRole('button', { name: /^(Previous|Next) copy$/ })).not.toBeInTheDocument();
    expect(win()).not.toHaveTextContent(/Copy 1 of 1|to move between/);
    fireEvent.keyDown(win(), { key: 'ArrowRight' });
    fireEvent.keyDown(win(), { key: 'ArrowLeft' });
    expect(onSelectCopy).not.toHaveBeenCalled();
  });

  it('closes on the close button, on Escape and on a press outside it, and not on one inside', async () => {
    const { onClose, container, unmount } = open();
    fireEvent.mouseDown(win());
    expect(onClose).not.toHaveBeenCalled();
    await click('Close');
    fireEvent.keyDown(window, { key: 'Escape' });
    fireEvent.mouseDown(container.firstElementChild!);
    expect(onClose).toHaveBeenCalledTimes(3);
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(3);
  });
});
