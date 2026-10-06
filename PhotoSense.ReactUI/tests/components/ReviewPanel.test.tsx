import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ReviewPanel } from '../../components/ReviewPanel';
import type { DuplicateGroupDto, GroupMode } from '../../types';
import { group, member, video } from '../fixtures';

function show(shown: DuplicateGroupDto | undefined, mode: GroupMode = 'duplicates', busy = false) {
  const onOpen = vi.fn(), onRemoveGroup = vi.fn(), onOpenInViewer = vi.fn();
  const view = render(<ReviewPanel group={shown} mode={mode} busy={busy} onOpen={onOpen} onRemoveGroup={onRemoveGroup} onOpenInViewer={onOpenInViewer} />);
  return { onOpen, onRemoveGroup, onOpenInViewer, ...view };
}

const two = [member({ id: 'm1', fileName: 'IMG_4198 (1).JPG' }), member({ id: 'm2', fileName: 'IMG_4198 (2).JPG' })];

describe('ReviewPanel', () => {
  it('asks for a group when none is chosen', () => {
    show(undefined);
    expect(screen.getByText('Select a group to review')).toBeInTheDocument();
  });

  it('shows the best copy as the original, with its duplicates underneath', () => {
    show(group({ members: two }));
    expect(screen.getByText('ORIGINAL')).toBeInTheDocument();
    expect(screen.getByText('The best copy of this picture. It stays.')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'IMG_4198.JPG' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Duplicates of this picture (2)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open IMG_4198 (1).JPG' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Open IMG_4198 (2).JPG' })).toBeInTheDocument();
  });

  it('opens the original when it is clicked, and a copy when its tile is', async () => {
    const { onOpen } = show(group({ members: two }));

    await userEvent.click(screen.getByTitle('Open this file'));
    expect(onOpen).toHaveBeenLastCalledWith();

    await userEvent.click(screen.getByRole('button', { name: 'Open IMG_4198 (2).JPG' }));
    expect(onOpen).toHaveBeenLastCalledWith(two[1]);
  });

  it('offers to delete the duplicates of the group, however many there are', async () => {
    const several = group({ members: two });
    const { onRemoveGroup, unmount } = show(several);
    await userEvent.click(screen.getByRole('button', { name: 'Delete these 2 duplicates · 5.8 MB' }));
    expect(onRemoveGroup).toHaveBeenCalledExactlyOnceWith(several);
    unmount();

    // A copy marked keep is not counted among those to delete.
    show(group({ members: [two[0], member({ id: 'm2', kept: true })] }));
    expect(screen.getByRole('button', { name: 'Delete this duplicate · 5.8 MB' })).toBeEnabled();
  });

  it('offers no deletion when every copy is marked keep, and none while something is under way', () => {
    const { unmount } = show(group({ members: [member({ kept: true })] }));
    expect(screen.queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
    unmount();

    show(group({ members: two }), 'duplicates', true);
    expect(screen.getByRole('button', { name: /^Delete these 2 duplicates/ })).toBeDisabled();
  });

  it('presents similar shots as shots to review, never to delete together', () => {
    show(group({ members: [member({ id: 'm1', fileName: 'IMG_4199.JPG' }, 'similar')] }), 'similar');
    expect(screen.getByText('BEST')).toBeInTheDocument();
    expect(screen.getByText('The best of these similar shots.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Similar shots (1)' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /^Delete/ })).not.toBeInTheDocument();
  });

  it('plays a video original in place rather than opening it on a click', async () => {
    const clip = video();
    const { container, onOpen, onOpenInViewer } = show(group({ keeper: clip, members: [member({ id: 'v2', fileName: 'IMG_0042 (1).MOV', isVideo: true })] }));

    expect(screen.getByText('The best copy of this video. It stays.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Duplicates of this video (1)' })).toBeInTheDocument();
    expect(container.querySelector('video')).not.toBeNull();
    expect(screen.queryByTitle('Open this file')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Open in default player' }));
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
    expect(onOpen).not.toHaveBeenCalled();
  });
});
