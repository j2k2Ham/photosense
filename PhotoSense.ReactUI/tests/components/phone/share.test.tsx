import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ResultCardView, ShareRow, ShareSheet } from '../../../components/phone/Share';
import { YearCard, YearStories } from '../../../components/phone/YearInReview';
import type { YearReviewDto } from '../../../types';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({ shareImage: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));
const card = vi.hoisted(() => ({ cardPng: vi.fn() }));
vi.mock('../../../lib/shareCard', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/shareCard')>()), ...card }));

const RESULT = { amount: '28.1 GB', device: 'iPhone', streak: 6, items: 5998 };
const review = (more: Partial<YearReviewDto> = {}): YearReviewDto => ({
  year: 2026, photos: 7512, videos: 316, trips: 3, topTrips: [{ name: 'Glacier National Park', month: 7, photos: 512 }], months: [1, 2, 3, 4, 5, 6, 1402, 8, 9, 10, 11, 12], busiestMonth: 7,
  busiestPlace: { town: 'West Glacier', photos: 512 }, freed: 41_600_000_000, removed: 9846, longestStreak: 38, sessions: 142, ...more,
});

beforeEach(() => {
  api.shareImage.mockReset().mockResolvedValue(undefined);
  card.cardPng.mockReset().mockImplementation(async (_format: string, draw: (pen: unknown, icon?: unknown) => void) => { draw(pen(), undefined); return 'iVBOR'; });
});

// A pen that draws nothing, for the card's drawing to run against.
function pen() {
  return { fillStyle: '', font: '', textBaseline: 'top', fillRect() {}, fillText() {}, beginPath() {}, roundRect() {}, fill() {}, measureText: () => ({ width: 10 }), drawImage() {} };
}

describe('the share card (25)', () => {
  it('shows the card, square or as a story, with or without the streak and items, and shares it as drawn', async () => {
    const notify = vi.fn(), onClose = vi.fn();
    inFrame(<ShareSheet card={RESULT} notify={notify} onClose={onClose} />);
    const sheet = screen.getByRole('dialog', { name: 'Share your result' });
    expect(within(sheet).getByText('None of your photos are on the card.')).toBeInTheDocument();
    const shown = () => within(sheet).getByRole('figure', { name: 'Share card' });
    expect(shown()).toHaveTextContent('PhotoSense28.1 GBfreed on my iPhone6-day streak5,998 items cleaned up');
    expect(shown()).toHaveClass('aspect-square');
    await userEvent.click(within(sheet).getByRole('radio', { name: 'Story' }));
    expect(shown()).toHaveClass('aspect-[9/16]');
    await userEvent.click(within(sheet).getByRole('switch', { name: 'Show streak and item count' }));
    expect(shown()).not.toHaveTextContent('streak');
    await userEvent.click(within(sheet).getByRole('button', { name: 'Share' }));
    expect(card.cardPng).toHaveBeenCalledWith('story', expect.any(Function), expect.stringMatching(/about\/icon$/));
    expect(api.shareImage).toHaveBeenCalledWith('iVBOR', 'PhotoSense.png');
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce());
  });

  it('says why it could not share, and Not now puts it away', async () => {
    const notify = vi.fn(), onClose = vi.fn();
    inFrame(<ShareSheet card={RESULT} notify={notify} onClose={onClose} />);
    api.shareImage.mockRejectedValueOnce(new Error('The share sheet could not open.')).mockRejectedValueOnce('gone');
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('The share sheet could not open.', 'error'));
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('gone', 'error'));
    expect(onClose).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Not now' }));
    expect(onClose).toHaveBeenCalledOnce();
  });

  it('is offered on a result screen as a row naming what was freed', async () => {
    const onOpen = vi.fn();
    inFrame(<ShareRow card={RESULT} onOpen={onOpen} />);
    await userEvent.click(screen.getByRole('button', { name: /^I freed 28.1 GB with PhotoSense/ }));
    expect(onOpen).toHaveBeenCalledOnce();
    inFrame(<ResultCardView card={{ ...RESULT, streak: 0 }} format="square" withPills />);
    expect(screen.getAllByRole('figure', { name: 'Share card' })[0]).toHaveTextContent('5,998 items cleaned up');
  });
});

describe('Year in review (41–44)', () => {
  it('is offered in Library with the year in a line, and Watch opens it', async () => {
    const onWatch = vi.fn();
    inFrame(<YearCard review={review()} onWatch={onWatch} />);
    expect(screen.getByRole('region', { name: 'Year in review' })).toHaveTextContent('Year in reviewYour 2026 in photos7,512 photos, 3 trips, 41.6 GB freed');
    await userEvent.click(screen.getByRole('button', { name: 'Watch' }));
    expect(onWatch).toHaveBeenCalledOnce();
  });

  it('shows five cards in turn, forward on the right and back on the left or by the arrow keys, and closes', async () => {
    const onClose = vi.fn();
    inFrame(<YearStories review={review()} notify={vi.fn()} onClose={onClose} />, frame('android'));
    const stories = screen.getByRole('dialog', { name: 'Year in review' });
    const now = () => within(stories).getByRole('heading', { level: 2 }).textContent;
    const at = () => within(stories).getByRole('progressbar', { name: 'Cards' }).getAttribute('aria-valuenow');
    expect([now(), at()]).toEqual(['2026', '1']);
    expect(within(stories).getByRole('button', { name: 'Previous card' })).toBeDisabled();
    await userEvent.click(within(stories).getByRole('button', { name: 'Next card' }));
    expect(now()).toBe('3 trips');
    await userEvent.click(within(stories).getByRole('button', { name: 'Next card' }));
    expect(now()).toBe('July');
    expect(within(stories).getByRole('img', { name: 'Photos by month' })).toBeInTheDocument();
    await userEvent.keyboard('{ArrowRight}');
    expect(now()).toBe('41.6 GB');
    expect(stories).toHaveTextContent('freed on your phone this year');
    await userEvent.keyboard('{ArrowRight}');
    expect(now()).toBe('That was 2026');
    expect(within(stories).getByRole('button', { name: 'Next card' })).toBeDisabled();
    await userEvent.keyboard('{ArrowRight}');
    expect(at()).toBe('5');
    await userEvent.keyboard('{ArrowLeft}');
    await userEvent.click(within(stories).getByRole('button', { name: 'Previous card' }));
    expect(now()).toBe('July');
    await userEvent.keyboard('{ArrowLeft}{ArrowLeft}{ArrowLeft}');
    expect(at()).toBe('1');
    await userEvent.keyboard('{Enter}');
    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledOnce();
    await userEvent.click(within(stories).getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(2);
    // The phone's own Back closes it too.
    act(() => history.back());
    await waitFor(() => expect(onClose).toHaveBeenCalledTimes(3));
  });

  it("shares the card showing, drawn as a story, and says why it could not", async () => {
    const notify = vi.fn();
    inFrame(<YearStories review={review()} notify={notify} onClose={vi.fn()} />);
    expect(screen.getByText('No photos are on these cards.')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    expect(card.cardPng).toHaveBeenCalledWith('story', expect.any(Function), expect.any(String));
    expect(api.shareImage).toHaveBeenCalledWith('iVBOR', 'PhotoSense 2026.png');
    api.shareImage.mockRejectedValueOnce(new Error('No share sheet.')).mockRejectedValueOnce('gone');
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('No share sheet.', 'error'));
    await userEvent.click(screen.getByRole('button', { name: 'Share' }));
    await waitFor(() => expect(notify).toHaveBeenCalledWith('gone', 'error'));
  });
});
