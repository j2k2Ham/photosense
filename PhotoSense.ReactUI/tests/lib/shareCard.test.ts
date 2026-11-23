import { afterEach, describe, expect, it, vi } from 'vitest';
import { cardPills, cardPng, CARD_SIZE, drawResultCard, drawYearSlide, freedSaid, yearLine, yearSlides, type Pen } from '../../lib/shareCard';
import type { YearReviewDto } from '../../types';

const review = (more: Partial<YearReviewDto> = {}): YearReviewDto => ({
  year: 2026, photos: 7512, videos: 316, trips: 3, topTrips: [{ name: 'Big Sky, Montana', month: 3, photos: 214 }, { name: 'Glacier National Park', month: 7, photos: 512 }, { name: 'Yellowstone', month: 8, photos: 512 }],
  months: [10, 20, 30, 40, 50, 60, 1402, 70, 80, 90, 100, 110], busiestMonth: 7, busiestPlace: { town: 'West Glacier', photos: 512 },
  freed: 41_600_000_000, removed: 9846, longestStreak: 38, sessions: 142, ...more,
});

/** A pen that keeps what it was asked to write and fill. */
function pen() {
  const written: string[] = [], filled: string[] = [];
  const drawn = vi.fn();
  const p: Pen = {
    fillStyle: '', font: '', textBaseline: 'alphabetic',
    fillRect: () => { filled.push(String(p.fillStyle)); },
    fillText: (text: string) => { written.push(text); },
    beginPath: vi.fn(), roundRect: vi.fn(), fill: vi.fn(),
    measureText: (text: string) => ({ width: text.length * 10 }) as TextMetrics,
    drawImage: drawn,
  };
  return { p, written, filled, drawn };
}

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals(); document.documentElement.style.removeProperty('--font-sans'); });

describe("a result's card", () => {
  it('says the streak when there is one, and how many items', () => {
    expect(cardPills({ amount: '28.1 GB', device: 'iPhone', streak: 6, items: 5998 })).toEqual(['6-day streak', '5,998 items cleaned up']);
    expect(cardPills({ amount: '1 KB', device: 'phone', streak: 0, items: 1 })).toEqual(['1 item cleaned up']);
  });

  it('is drawn with the mark, the amount, what it was freed on, and the pills only when shown', () => {
    const { p, written, filled, drawn } = pen();
    const icon = {} as CanvasImageSource;
    drawResultCard(p, { amount: '28.1 GB', device: 'iPhone', streak: 6, items: 5998 }, 'square', true, icon);
    expect(written).toEqual(['PhotoSense', '28.1 GB', 'freed on my iPhone', '6-day streak', '5,998 items cleaned up']);
    expect(filled[0]).toBe('#0F2E33');
    expect(drawn).toHaveBeenCalledWith(icon, 80, 80, 88, 88);
    const plain = pen();
    drawResultCard(plain.p, { amount: '28.1 GB', device: 'iPad', streak: 6, items: 5998 }, 'story', false);
    expect(plain.written).toEqual(['PhotoSense', '28.1 GB', 'freed on my iPad']);
    expect(plain.drawn).not.toHaveBeenCalled();
  });

  it('draws in the page\'s own font when it has one', () => {
    document.documentElement.style.setProperty('--font-sans', '"DM Sans"');
    const { p } = pen();
    drawResultCard(p, { amount: '1 GB', device: 'iPhone', streak: 0, items: 1 }, 'square', false);
    expect(p.font).toContain('"DM Sans"');
  });
});

describe("Year in review's cards", () => {
  it('say the year, its places, the busiest month, what was cleaned up and the end', () => {
    const slides = yearSlides(review(), 'iPhone');
    expect(slides.map(s => [s.kicker, s.big, s.line])).toEqual([
      ['Your year in photos', '2026', '7,512 photos and 316 videos'],
      ['Places', '3 trips', 'Most photos: Glacier National Park, with 512.'],
      ['Busiest month', 'July', '1,402 photos. 512 of them are from West Glacier.'],
      ['Clean up', '41.6 GB', 'freed on your iPhone this year'],
      ['PhotoSense', 'That was 2026', 'See you in 2027.'],
    ]);
    // Listed in the order taken; of two with as many, the first has most.
    expect(slides[1].rows).toEqual([['March', 'Big Sky, Montana'], ['July', 'Glacier National Park'], ['August', 'Yellowstone']]);
    expect(slides[3].rows).toEqual([['9,846', 'items removed'], ['38 days', 'longest swipe streak'], ['142', 'swipe sessions']]);
    expect(slides[4].rows!.map(r => r[0])).toEqual(['7,512 photos', '3 trips', '41.6 GB freed']);
    expect(yearLine(review())).toBe('7,512 photos, 3 trips, 41.6 GB freed');
  });

  it('say so when there was no trip, nothing was taken, or one of each', () => {
    const slides = yearSlides(review({ photos: 1, videos: 1, trips: 0, topTrips: [], busiestMonth: 3, months: [0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0], busiestPlace: { town: 'Butte', photos: 1 }, freed: 2048, longestStreak: 1 }), 'phone');
    expect(slides[0].line).toBe('1 photo and 1 video');
    expect(slides[1].big).toBe('No trips');
    expect(slides[1].line).toBe('None of 2026’s photos were taken away from home.');
    expect(yearSlides(review({ trips: 1, topTrips: [{ name: 'Yellowstone', month: 8, photos: 1 }] }), 'phone')[1]).toMatchObject({ big: '1 trip', line: 'Most photos: Yellowstone, with 1.' });
    expect(yearLine(review({ trips: 1 }))).toBe('7,512 photos, 1 trip, 41.6 GB freed');
    expect(slides[2].line).toBe('1 photo. 1 of them is from Butte.');
    expect(slides[3].big).toBe('2 KB');
    expect(slides[3].rows![1][0]).toBe('1 day');
    const none = yearSlides(review({ busiestMonth: 0, busiestPlace: null }), 'phone');
    expect(none[2]).toMatchObject({ big: 'None yet', line: 'No photos were taken in 2026.' });
    expect(yearSlides(review({ busiestPlace: null }), 'phone')[2].line).toBe('1,402 photos.');
    expect(freedSaid(0)).toBe('0 KB');
  });

  it('are drawn as stories: the mark, the figures, the bars with the busiest marked, and the note', () => {
    const slides = yearSlides(review(), 'iPhone');
    const month = pen();
    drawYearSlide(month.p, slides[2], {} as CanvasImageSource);
    expect(month.written).toEqual(['PhotoSense', 'Busiest month', 'July', '1,402 photos. 512 of them are from West Glacier.', 'No photos are on these cards.']);
    expect(month.filled.filter(f => f === '#A8EEF7')).toHaveLength(1);
    const end = pen();
    drawYearSlide(end.p, { ...slides[4], line: undefined, months: { counts: Array(12).fill(0), busiest: 1 } });
    expect(end.written).toEqual(['PhotoSense', 'PhotoSense', 'That was 2026', '7,512 photos', '', '3 trips', '', '41.6 GB freed', '', 'No photos are on these cards.']);
  });
});

describe('making the PNG', () => {
  function canvas(context: unknown) {
    const toDataURL = vi.fn(() => 'data:image/png;base64,iVBOR');
    const real = document.createElement.bind(document);
    vi.spyOn(document, 'createElement').mockImplementation((tag: string) => (tag === 'canvas' ? ({ width: 0, height: 0, getContext: () => context, toDataURL } as unknown as HTMLElement) : real(tag)));
    return toDataURL;
  }
  function image(decode: () => Promise<void>) {
    vi.stubGlobal('Image', class { src = ''; decode = decode; });
  }

  it('draws the card at its size, with the icon, once the fonts are ready, and answers it in base 64', async () => {
    canvas({});
    image(async () => undefined);
    Object.defineProperty(document, 'fonts', { configurable: true, value: { ready: Promise.resolve() } });
    const draw = vi.fn();
    await expect(cardPng('story', draw, '/api/about/icon')).resolves.toBe('iVBOR');
    expect(draw).toHaveBeenCalledWith({}, expect.objectContaining({ src: '/api/about/icon' }));
    expect(CARD_SIZE.story).toEqual([1080, 1920]);
    Reflect.deleteProperty(document, 'fonts');
  });

  it('leaves the icon off when it will not load, and says when the phone cannot draw', async () => {
    canvas({});
    image(async () => { throw new DOMException('broken', 'EncodingError'); });
    const draw = vi.fn();
    await cardPng('square', draw, '/icon');
    expect(draw).toHaveBeenCalledWith({}, undefined);
    image(async () => { throw new TypeError('odd'); });
    await expect(cardPng('square', vi.fn(), '/icon')).rejects.toThrow('odd');
    vi.restoreAllMocks();
    canvas(null);
    await expect(cardPng('square', vi.fn(), '/icon')).rejects.toThrow('This phone could not draw the card.');
  });
});
