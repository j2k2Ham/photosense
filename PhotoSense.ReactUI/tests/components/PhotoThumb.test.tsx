import React from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { PhotoThumb, PhotoView } from '../../components/PhotoThumb';
import { imageUrl, thumbnailUrl, videoUrl } from '../../lib/apiClient';
import { photo, video } from '../fixtures';

describe('PhotoThumb', () => {
  it('shows a picture by its cached preview, loaded only when it comes into view', () => {
    const { container } = render(<PhotoThumb photo={photo()} className="w-14 h-14" />);
    const img = container.querySelector('img')!;
    expect(img).toHaveAttribute('src', thumbnailUrl('p1'));
    expect(img).toHaveAttribute('loading', 'lazy');
    expect(img).toHaveClass('object-cover', 'w-14', 'h-14');
  });

  it('shows a video as a labelled tile with its playing time', () => {
    const { container } = render(<PhotoThumb photo={video()} className="w-14" />);
    expect(container.querySelector('img')).toBeNull();
    expect(container.firstElementChild).toHaveClass('w-14');
    expect(container).toHaveTextContent('▶VIDEO1:24');
  });

  it('leaves the playing time off when it is not known, and shows one of nothing', () => {
    expect(render(<PhotoThumb photo={video({ durationSeconds: undefined })} />).container).toHaveTextContent(/^▶VIDEO$/);
    expect(render(<PhotoThumb photo={video({ durationSeconds: 0 })} />).container).toHaveTextContent(/^▶VIDEO0:00$/);
  });
});

describe('PhotoView', () => {
  it('shows a picture at full size over its preview', () => {
    const { container } = render(<PhotoView photo={photo()} onOpenInViewer={() => undefined} />);
    expect(screen.getByRole('img', { name: 'IMG_4198.JPG' })).toHaveAttribute('src', imageUrl('p1'));
    expect(container.firstElementChild).toHaveStyle({ backgroundImage: `url("${thumbnailUrl('p1')}")` });
    expect(screen.queryByRole('button')).not.toBeInTheDocument();
  });

  it('plays a video in the page, and offers the system\'s own player beside it', async () => {
    const onOpenInViewer = vi.fn();
    const clip = video();
    const { container } = render(<PhotoView photo={clip} onOpenInViewer={onOpenInViewer} />);

    const player = container.querySelector('video')!;
    expect(player).toHaveAttribute('src', videoUrl('v1'));
    expect(player).toHaveAttribute('controls');
    expect(player).toHaveAttribute('preload', 'metadata');

    await userEvent.click(screen.getByRole('button', { name: 'Open in default player' }));
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
  });

  it('keeps playing a video the browser can decode', () => {
    const { container } = render(<PhotoView photo={video()} onOpenInViewer={() => undefined} />);
    const player = container.querySelector('video')!;
    Object.defineProperty(player, 'videoWidth', { value: 1920 });

    fireEvent.loadedMetadata(player);

    expect(container.querySelector('video')).not.toBeNull();
    expect(screen.queryByText(/cannot play this video/)).not.toBeInTheDocument();
  });

  it.each([
    ['fails to load', (player: HTMLVideoElement) => fireEvent.error(player)],
    // Sound but no picture: the browser lacks the decoder for what the phone recorded.
    ['loads without a picture', (player: HTMLVideoElement) => fireEvent.loadedMetadata(player)],
  ])('says so when a video %s, and still offers the system\'s player', async (_what, happen) => {
    const onOpenInViewer = vi.fn();
    const clip = video();
    const { container } = render(<PhotoView photo={clip} onOpenInViewer={onOpenInViewer} />);

    happen(container.querySelector('video')!);

    expect(container.querySelector('video')).toBeNull();
    expect(screen.getByText('This browser cannot play this video. Open it in your default player instead.')).toBeInTheDocument();
    expect(screen.getByText('IMG_0042.MOV')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Open in default player' }));
    expect(onOpenInViewer).toHaveBeenCalledExactlyOnceWith(clip);
  });

  it('tries again with the next video after one that could not be played', () => {
    const { container, rerender } = render(<PhotoView photo={video()} onOpenInViewer={() => undefined} />);
    fireEvent.error(container.querySelector('video')!);
    expect(container.querySelector('video')).toBeNull();

    rerender(<PhotoView photo={video({ id: 'v2', fileName: 'IMG_0043.MOV' })} onOpenInViewer={() => undefined} />);

    expect(container.querySelector('video')).toHaveAttribute('src', videoUrl('v2'));
  });
});
