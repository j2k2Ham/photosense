import React from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { PhotoWindow } from '../../components/PhotoWindow';
import type { GroupMemberDto, GroupMode } from '../../types';
import { member, photo } from '../fixtures';

const original = photo({ fileName: 'IMG_4299.JPG', fileSizeBytes: 25_165_824 });
const copy = member({ id: 'm1', fileName: 'IMG_4299.HEIC', folder: 'D:\\backup\\phone', format: 'HEIC', fileSizeBytes: 12_582_912, latitude: 46.1283, longitude: -112.9423, placeName: 'Near Anaconda, Montana, US', cameraModel: 'iPhone 12 Pro Max' }, 'samePicture', 'Opens everywhere: JPEG rather than HEIC');

function open(options: { member?: GroupMemberDto; mode?: GroupMode; busy?: boolean } = {}) {
  const handlers = { onClose: vi.fn(), onToggleKeep: vi.fn(), onRemove: vi.fn(), onOpenInViewer: vi.fn() };
  const view = render(<PhotoWindow original={original} member={options.member ?? copy} mode={options.mode ?? 'duplicates'} busy={options.busy ?? false} {...handlers} />);
  return { ...handlers, ...view };
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
    const { rerender, onClose, onToggleKeep, onRemove, onOpenInViewer } = open();
    await click('Delete this copy · 12.0 MB');
    const busy = <PhotoWindow original={original} member={copy} mode="duplicates" busy onClose={onClose} onToggleKeep={onToggleKeep} onRemove={onRemove} onOpenInViewer={onOpenInViewer} />;
    rerender(busy);
    expect(button('Working…')).toBeDisabled();
    for (const name of ['Open in default viewer', 'Keep this copy too', 'Delete the original instead']) expect(button(name)).toBeDisabled();
    expect(button('Cancel')).toBeEnabled();
  });

  it('shows working on the other confirmation too', async () => {
    const { rerender, onClose, onToggleKeep, onRemove, onOpenInViewer } = open({ mode: 'similar', member: member({ id: 'm3', fileName: 'IMG_4300.JPG' }, 'similar') });
    await click('Delete the best shot instead');
    expect(win()).toHaveTextContent(/to _PhotoSense_Removed\?/);
    expect(button('Delete the best shot')).toBeEnabled();
    rerender(<PhotoWindow original={original} member={member({ id: 'm3', fileName: 'IMG_4300.JPG' }, 'similar')} mode="similar" busy onClose={onClose} onToggleKeep={onToggleKeep} onRemove={onRemove} onOpenInViewer={onOpenInViewer} />);
    expect(button('Working…')).toBeDisabled();
  });

  it('presents a similar shot as a different picture: no keeping, and the best shot named as such', async () => {
    open({ mode: 'similar', member: member({ id: 'm3', fileName: 'IMG_4300.JPG' }, 'similar') });
    expect(win()).toHaveTextContent('Similar. A burst frame or an edited version. Never removed in bulk.');
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
