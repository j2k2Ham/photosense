import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { PhotoWindow } from '../../components/PhotoWindow';
import type { GroupMemberDto, GroupMode } from '../../types';
import { member, photo, video } from '../fixtures';

const original = photo();
const copy = member({ id: 'm1', fileName: 'IMG_4198 (1).JPG' }, 'identical', 'Same quality; this one is in the primary folder');

function open(options: { member?: GroupMemberDto; mode?: GroupMode; busy?: boolean; original?: typeof original } = {}) {
  const handlers = { onClose: vi.fn(), onToggleKeep: vi.fn(), onRemove: vi.fn(), onOpenInViewer: vi.fn() };
  const view = render(<PhotoWindow original={options.original ?? original} mode={options.mode ?? 'duplicates'} member={options.member} busy={options.busy ?? false} {...handlers} />);
  return { ...handlers, ...view };
}

const button = (name: string | RegExp) => screen.getByRole('button', { name });
const click = (name: string | RegExp) => userEvent.click(button(name));

describe('PhotoWindow on a copy', () => {
  it('shows the copy, how it matched and why the original was preferred', () => {
    open({ member: copy });
    expect(screen.getByRole('dialog', { name: 'IMG_4198 (1).JPG' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'IMG_4198 (1).JPG' })).toBeInTheDocument();
    expect(screen.getByText('IDENTICAL FILE')).toBeInTheDocument();
    expect(screen.getByText('Original preferred: Same quality; this one is in the primary folder')).toBeInTheDocument();
    expect(button('This copy')).toHaveClass('bg-emerald-500');
    expect(button('Original')).not.toHaveClass('bg-emerald-500');
  });

  it('names a converted or resized copy as the same picture', () => {
    open({ member: member({ id: 'm2', fileName: 'IMG_4198.HEIC' }, 'samePicture') });
    expect(screen.getByText('SAME PICTURE')).toBeInTheDocument();
  });

  it('flips to the original and back for comparing the two', async () => {
    open({ member: copy });

    await click('Original');
    expect(screen.getByRole('dialog', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
    expect(screen.getByText('ORIGINAL — THE BEST COPY, KEPT')).toBeInTheDocument();
    expect(screen.queryByText(/Original preferred/)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /keep/i })).not.toBeInTheDocument();
    expect(button('Original')).toHaveClass('bg-emerald-500');
    expect(button('This copy')).not.toHaveClass('bg-emerald-500');

    await click('This copy');
    expect(screen.getByRole('dialog', { name: 'IMG_4198 (1).JPG' })).toBeInTheDocument();
    expect(screen.getByText('IDENTICAL FILE')).toBeInTheDocument();
  });

  it('marks the copy to keep, or stops keeping it', async () => {
    const { onToggleKeep, unmount } = open({ member: copy });
    await click('Keep this copy too');
    expect(onToggleKeep).toHaveBeenCalledExactlyOnceWith(copy.photo);
    unmount();

    const kept = member({ id: 'm1', kept: true });
    const second = open({ member: kept });
    await click('Stop keeping this copy');
    expect(second.onToggleKeep).toHaveBeenCalledExactlyOnceWith(kept.photo);
  });

  it('deletes the copy only after being asked twice', async () => {
    const { onRemove } = open({ member: copy });

    await click('Delete this copy');
    expect(screen.getByText(/to the removed folder\?/)).toHaveTextContent('Move IMG_4198 (1).JPG to the removed folder? Its edit sidecar and Live Photo video go with it, unless another picture of the same shot stays in the folder.');
    expect(screen.queryByText(/next best copy/)).not.toBeInTheDocument();
    expect(onRemove).not.toHaveBeenCalled();

    await click('Cancel');
    expect(button('Delete this copy')).toBeInTheDocument();
    expect(onRemove).not.toHaveBeenCalled();

    await click('Delete this copy');
    await click('Delete');
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(copy.photo);
  });

  it('opens whichever file is showing in the system\'s viewer', async () => {
    const { onOpenInViewer } = open({ member: copy });
    await click('Open in default viewer');
    expect(onOpenInViewer).toHaveBeenLastCalledWith(copy.photo);

    await click('Original');
    await click('Open in default viewer');
    expect(onOpenInViewer).toHaveBeenLastCalledWith(original);
  });

  it('withdraws a half-given answer when the other file is flipped to', async () => {
    open({ member: copy });
    await click('Delete this copy');
    await click('Original');
    expect(screen.queryByText(/to the removed folder\?/)).not.toBeInTheDocument();

    await click('Delete the original instead');
    await click('This copy');
    expect(screen.queryByText(/to the removed folder\?/)).not.toBeInTheDocument();
    expect(button('Delete this copy')).toBeInTheDocument();
  });

  it('lets nothing be changed while something is under way', async () => {
    open({ member: copy, busy: true });
    expect(button('Open in default viewer')).toBeDisabled();
    expect(button('Keep this copy too')).toBeDisabled();
    expect(button('Delete this copy')).toBeDisabled();
    // Comparing is still allowed.
    await click('Original');
    expect(button('Delete the original instead')).toBeDisabled();
  });
});

describe('PhotoWindow on the original', () => {
  it('shows the original alone, with nothing to flip to', () => {
    open();
    expect(screen.getByRole('dialog', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
    expect(screen.getByText('ORIGINAL — THE BEST COPY, KEPT')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'This copy' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /keep/i })).not.toBeInTheDocument();
  });

  it('can delete the original instead, saying what then becomes of the group', async () => {
    const { onRemove } = open({ member: copy });
    await click('Original');

    await click('Delete the original instead');
    expect(screen.getByText(/to the removed folder\?/)).toHaveTextContent('Move IMG_4198.JPG to the removed folder?');
    expect(screen.getByText(/to the removed folder\?/)).toHaveTextContent('The next best copy becomes the original.');
    expect(button('Delete')).toBeEnabled();

    await click('Delete');
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(original);
  });

  it('holds the confirmed deletion back while something is under way', async () => {
    const { rerender, onClose, onToggleKeep, onRemove, onOpenInViewer } = open();
    await click('Delete the original instead');
    rerender(<PhotoWindow original={original} mode="duplicates" busy onClose={onClose} onToggleKeep={onToggleKeep} onRemove={onRemove} onOpenInViewer={onOpenInViewer} />);
    expect(button('Delete')).toBeDisabled();
    expect(button('Cancel')).toBeEnabled();
  });
});

describe('PhotoWindow on similar shots', () => {
  const shot = member({ id: 'm3', fileName: 'IMG_4199.JPG' }, 'similar');

  it('presents the other shot as a different picture, not a copy to keep or drop in bulk', () => {
    open({ member: shot, mode: 'similar' });
    expect(screen.getByText('SIMILAR')).toBeInTheDocument();
    expect(screen.getByText('This looks like the best shot but is a different shot or an edited version. It is never removed in bulk.')).toBeInTheDocument();
    expect(button('Best shot')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /keep/i })).not.toBeInTheDocument();
    expect(button('Delete this copy')).toBeInTheDocument();
  });

  it('calls the best shot what it is, and deleting it changes nothing else', async () => {
    const { onRemove } = open({ member: shot, mode: 'similar' });
    await click('Best shot');
    expect(screen.getByText('THE BEST OF THESE SIMILAR SHOTS')).toBeInTheDocument();

    await click('Delete this shot');
    expect(screen.getByText(/to the removed folder\?/)).toBeInTheDocument();
    expect(screen.queryByText(/next best copy/)).not.toBeInTheDocument();
    await click('Delete');
    expect(onRemove).toHaveBeenCalledExactlyOnceWith(original);
  });
});

describe('PhotoWindow on a video', () => {
  it('plays it, leaving the one way out to the system\'s player on the player itself', async () => {
    const clip = video();
    const { container, onOpenInViewer } = open({ original: clip });
    expect(container.querySelector('video')).not.toBeNull();
    expect(screen.queryByRole('button', { name: 'Open in default viewer' })).not.toBeInTheDocument();

    await click('Open in default player');
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
  });
});

describe('closing the PhotoWindow', () => {
  it('happens on the close button, on Escape and on a click outside it', async () => {
    const { onClose, container } = open({ member: copy });

    await click('Close');
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(2);

    fireEvent.mouseDown(container.firstElementChild!);
    expect(onClose).toHaveBeenCalledTimes(3);
  });

  it('does not happen on any other key, or on a click inside it', () => {
    const { onClose, unmount } = open({ member: copy });

    fireEvent.keyDown(window, { key: 'Enter' });
    fireEvent.mouseDown(screen.getByRole('dialog'));
    fireEvent.mouseDown(screen.getByRole('img', { name: 'IMG_4198 (1).JPG' }));
    expect(onClose).not.toHaveBeenCalled();

    // Once it is gone it no longer listens for Escape.
    unmount();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).not.toHaveBeenCalled();
  });
});
