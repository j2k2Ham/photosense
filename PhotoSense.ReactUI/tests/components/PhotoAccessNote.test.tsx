import React from 'react';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PhotoAccessNote } from '../../components/PhotoAccessNote';
import { deferred } from '../deferred';

const api = vi.hoisted(() => ({ usePhotoAccess: vi.fn(), chooseMorePhotos: vi.fn(), openPhotoSettings: vi.fn() }));
vi.mock('../../lib/apiClient', () => api);

beforeEach(() => {
  api.usePhotoAccess.mockReset().mockReturnValue({ data: { access: 'selected' } });
  api.chooseMorePhotos.mockReset();
  api.openPhotoSettings.mockReset().mockResolvedValue(undefined);
});

describe('what PhotoSense may see of the photos', () => {
  it('is not mentioned on a computer, where it sees everything, nor before the phone has said', () => {
    api.usePhotoAccess.mockReturnValue({ data: { access: 'everything' } });
    const { container, rerender } = render(<PhotoAccessNote notify={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
    api.usePhotoAccess.mockReturnValue({ data: undefined });
    rerender(<PhotoAccessNote notify={vi.fn()} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('is only the photos selected, with the phone\'s own picker to choose more, which says what can be seen once it is put away', async () => {
    const notify = vi.fn();
    const picked = deferred<{ access: string }>();
    api.chooseMorePhotos.mockReturnValueOnce(picked.promise);
    render(<PhotoAccessNote notify={notify} />);
    expect(screen.getByRole('note')).toHaveTextContent('PhotoSense can see only the photos you selected. Choose more, or allow all of them in Settings.');

    await userEvent.click(screen.getByRole('button', { name: 'Choose more photos' }));
    expect(screen.getByRole('button', { name: 'Choosing…' })).toBeDisabled();
    picked.resolve({ access: 'selected' });
    expect(await screen.findByRole('button', { name: 'Choose more photos' })).toBeEnabled();
    expect(notify).toHaveBeenLastCalledWith('PhotoSense can see the photos you selected.', 'ok');

    api.chooseMorePhotos.mockResolvedValueOnce({ access: 'everything' });
    await userEvent.click(screen.getByRole('button', { name: 'Choose more photos' }));
    expect(notify).toHaveBeenLastCalledWith('PhotoSense can now see all your photos.', 'ok');
  });

  it('is nothing at all, with the way to PhotoSense\'s page of the settings and no picker', async () => {
    api.usePhotoAccess.mockReturnValue({ data: { access: 'nothing' } });
    render(<PhotoAccessNote notify={vi.fn()} />);
    expect(screen.getByRole('note')).toHaveTextContent('PhotoSense cannot see any of your photos. Allow it in Settings, under Privacy & Security, then Photos.');
    expect(screen.queryByRole('button', { name: 'Choose more photos' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(api.openPhotoSettings).toHaveBeenCalledOnce();
  });

  it('says why the picker or the settings could not be shown', async () => {
    const notify = vi.fn();
    api.chooseMorePhotos.mockRejectedValueOnce(new Error('The PhotoSense service is not answering.')).mockRejectedValueOnce('gone');
    api.openPhotoSettings.mockRejectedValueOnce(new Error('No settings here.'));
    render(<PhotoAccessNote notify={notify} />);

    await userEvent.click(screen.getByRole('button', { name: 'Choose more photos' }));
    expect(notify).toHaveBeenLastCalledWith('The photo picker could not be shown: The PhotoSense service is not answering.', 'error');
    await userEvent.click(screen.getByRole('button', { name: 'Choose more photos' }));
    expect(notify).toHaveBeenLastCalledWith('The photo picker could not be shown: gone', 'error');
    await userEvent.click(screen.getByRole('button', { name: 'Open Settings' }));
    expect(notify).toHaveBeenLastCalledWith('Settings could not be opened: No settings here.', 'error');
  });
});
