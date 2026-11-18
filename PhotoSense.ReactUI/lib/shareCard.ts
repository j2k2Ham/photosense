import type { YearReviewDto } from '../types';
import { gigabytes } from './backup';
import { amountOf } from './categories';

/** How a card is shaped: a square, or as tall as a story. */
export type CardFormat = 'square' | 'story';

/** The size each format is drawn at, in pixels, for sharing. */
export const CARD_SIZE: Record<CardFormat, readonly [number, number]> = { square: [1080, 1080], story: [1080, 1920] };

/** The share card's colours (the design's 25): its background, its text, its line and its pills. */
export const CARD_COLOURS = { background: '#0F2E33', text: '#FFFFFF', line: '#A8EEF7', pill: 'rgba(255,255,255,.14)' } as const;

/** What a result's card says: how much was freed, on what, and, when shown, the streak and how many items. None of it is a photo. */
export interface ResultCard {
  /** "28.1 GB", as the screen it came from says it. */
  readonly amount: string;
  /** "iPhone", "iPad", "phone" or "tablet". */
  readonly device: string;
  /** Days in a row something was swiped; none when 0. */
  readonly streak: number;
  /** Photos and videos cleaned up. */
  readonly items: number;
}

/** The card's pills: the streak, when there is one, and the items cleaned up. */
export function cardPills(card: ResultCard): string[] {
  return [card.streak > 0 ? `${card.streak.toLocaleString()}-day streak` : undefined, `${card.items.toLocaleString()} ${card.items === 1 ? 'item' : 'items'} cleaned up`].filter((p): p is string => !!p);
}

/** A card of Year in review: what it says, in its colour. */
export interface YearSlide {
  /** Its colour, as the design gives it. */
  readonly background: string;
  /** The line over the figure, in the accent colour. */
  readonly kicker: string;
  /** The figure. */
  readonly big: string;
  /** What the figure is. */
  readonly line?: string;
  /** Figures under it, each with what it is. */
  readonly rows?: readonly (readonly [string, string])[];
  /** The months' bars, with the busiest marked. */
  readonly months?: { readonly counts: readonly number[]; readonly busiest: number };
}

const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

/** Year in review's colours, card by card (the design's 41–44). */
export const YEAR_COLOURS = ['oklch(0.28 0.05 205)', 'oklch(0.28 0.06 245)', 'oklch(0.27 0.06 285)', 'oklch(0.29 0.06 165)', 'oklch(0.25 0.04 205)'] as const;

/** What the year freed, as a phone says a size: "41.6 GB", or smaller amounts in PhotoSense's own units. */
export const freedSaid = (bytes: number) => (bytes >= 1e9 ? gigabytes(bytes) : amountOf(bytes));

const count = (n: number, one: string, many: string) => `${n.toLocaleString()} ${n === 1 ? one : many}`;

/** The year's photos, places and what was freed in a line: "7,512 photos, 12 places, 41.6 GB freed". */
export const yearLine = (y: YearReviewDto) => `${count(y.photos, 'photo', 'photos')}, ${count(y.places, 'place', 'places')}, ${freedSaid(y.freed)} freed`;

/** Year in review's five cards, from the year's figures; `device` names the phone in "freed on your iPhone". */
export function yearSlides(y: YearReviewDto, device: string): YearSlide[] {
  const year = y.year!;
  const top = y.topPlaces[0];
  const busiest = y.busiestMonth > 0 ? y.months[y.busiestMonth - 1] : 0;
  return [
    { background: YEAR_COLOURS[0], kicker: 'Your year in photos', big: String(year), line: `${count(y.photos, 'photo', 'photos')} and ${count(y.videos, 'video', 'videos')}` },
    {
      background: YEAR_COLOURS[1], kicker: 'Places', big: count(y.places, 'place', 'places'),
      line: top ? `Most photos: ${top.town}, with ${top.photos.toLocaleString()}.` : 'None of this year’s photos say where they were taken.',
      rows: y.topPlaces.map(p => [p.photos.toLocaleString(), p.town] as const),
    },
    y.busiestMonth > 0
      ? {
        background: YEAR_COLOURS[2], kicker: 'Busiest month', big: MONTHS[y.busiestMonth - 1],
        line: `${count(busiest, 'photo', 'photos')}.${y.busiestPlace ? ` ${y.busiestPlace.photos.toLocaleString()} of them ${y.busiestPlace.photos === 1 ? 'is' : 'are'} from ${y.busiestPlace.town}.` : ''}`,
        months: { counts: y.months, busiest: y.busiestMonth },
      }
      : { background: YEAR_COLOURS[2], kicker: 'Busiest month', big: 'None yet', line: `No photos were taken in ${year}.` },
    {
      background: YEAR_COLOURS[3], kicker: 'Clean up', big: freedSaid(y.freed), line: `freed on your ${device} this year`,
      rows: [[y.removed.toLocaleString(), 'items removed'], [count(y.longestStreak, 'day', 'days'), 'longest swipe streak'], [y.sessions.toLocaleString(), 'swipe sessions']],
    },
    {
      background: YEAR_COLOURS[4], kicker: 'PhotoSense', big: `That was ${year}`, line: `See you in ${year + 1}.`,
      rows: [[count(y.photos, 'photo', 'photos'), ''], [count(y.places, 'place', 'places'), ''], [`${freedSaid(y.freed)} freed`, '']],
    },
  ];
}

/** What a card is drawn with: a canvas's 2D context, or a stand-in in tests. */
export type Pen = Pick<CanvasRenderingContext2D, 'fillRect' | 'fillText' | 'beginPath' | 'roundRect' | 'fill' | 'measureText' | 'drawImage'>
  & { fillStyle: CanvasRenderingContext2D['fillStyle']; font: string; textBaseline: CanvasTextBaseline };

// A pill: rounded, its text inside.
function pill(pen: Pen, text: string, x: number, y: number, size: number) {
  pen.font = `600 ${size}px ${FONT()}`;
  const width = pen.measureText(text).width + size * 1.4;
  pen.fillStyle = CARD_COLOURS.pill;
  pen.beginPath();
  pen.roundRect(x, y, width, size * 1.9, size);
  pen.fill();
  pen.fillStyle = CARD_COLOURS.text;
  pen.fillText(text, x + size * 0.7, y + size * 0.45);
  return size * 1.9;
}

// The page's own sans-serif, DM Sans, which the cards are drawn in.
const FONT = () => {
  const named = getComputedStyle(document.documentElement).getPropertyValue('--font-sans').trim();
  return named || 'sans-serif';
};

// The PhotoSense mark at the top left: the icon and the name.
function mark(pen: Pen, icon: CanvasImageSource | undefined, x: number, y: number, size: number) {
  if (icon) pen.drawImage(icon, x, y, size, size);
  pen.fillStyle = CARD_COLOURS.text;
  pen.font = `700 ${size * 0.62}px ${FONT()}`;
  pen.fillText('PhotoSense', x + size * 1.3, y + size * 0.18);
}

/** Draws a result's card, the size `format` gives, with the pills when `withPills`. */
export function drawResultCard(pen: Pen, card: ResultCard, format: CardFormat, withPills: boolean, icon?: CanvasImageSource) {
  const [width, height] = CARD_SIZE[format];
  pen.textBaseline = 'top';
  pen.fillStyle = CARD_COLOURS.background;
  pen.fillRect(0, 0, width, height);
  mark(pen, icon, 80, 80, 88);
  const big = format === 'square' ? 220 : 200;
  let y = height / 2 - big * 0.7;
  pen.fillStyle = CARD_COLOURS.text;
  pen.font = `700 ${big}px ${FONT()}`;
  pen.fillText(card.amount, 80, y);
  y += big * 1.15;
  pen.fillStyle = CARD_COLOURS.line;
  pen.font = `500 ${big * 0.3}px ${FONT()}`;
  pen.fillText(`freed on my ${card.device}`, 80, y);
  y += big * 0.55;
  if (withPills) for (const text of cardPills(card)) y += pill(pen, text, 80, y, 40) + 24;
}

/** Draws one of Year in review's cards, as tall as a story. */
export function drawYearSlide(pen: Pen, slide: YearSlide, icon?: CanvasImageSource) {
  const [width, height] = CARD_SIZE.story;
  const font = FONT();
  pen.textBaseline = 'top';
  pen.fillStyle = slide.background;
  pen.fillRect(0, 0, width, height);
  mark(pen, icon, 80, 120, 80);
  let y = 620;
  pen.fillStyle = CARD_COLOURS.line;
  pen.font = `700 52px ${font}`;
  pen.fillText(slide.kicker, 80, y);
  y += 90;
  pen.fillStyle = CARD_COLOURS.text;
  pen.font = `700 180px ${font}`;
  pen.fillText(slide.big, 80, y);
  y += 220;
  if (slide.line) {
    pen.font = `500 50px ${font}`;
    pen.fillText(slide.line, 80, y);
    y += 100;
  }
  for (const [figure, what] of slide.rows ?? []) {
    pen.fillStyle = CARD_COLOURS.text;
    pen.font = `700 64px ${font}`;
    pen.fillText(figure, 80, y);
    pen.font = `500 40px ${font}`;
    pen.fillText(what, 420, y + 14);
    y += 96;
  }
  if (slide.months) {
    const most = Math.max(1, ...slide.months.counts);
    slide.months.counts.forEach((n, i) => {
      const bar = Math.max(8, (n / most) * 360);
      pen.fillStyle = i + 1 === slide.months!.busiest ? CARD_COLOURS.line : 'rgba(255,255,255,.3)';
      pen.fillRect(80 + i * 78, y + 360 - bar, 60, bar);
    });
  }
  pen.fillStyle = CARD_COLOURS.text;
  pen.font = `500 36px ${font}`;
  pen.fillText('No photos are on these cards.', 80, height - 140);
}

/**
 * Draws a card on a canvas of `format`'s size, with the PhotoSense icon from `iconUrl` once it has loaded, and answers
 * it as a PNG in base 64, as the phone's share sheet takes it.
 */
export async function cardPng(format: CardFormat, draw: (pen: Pen, icon?: CanvasImageSource) => void, iconUrl: string): Promise<string> {
  const canvas = document.createElement('canvas');
  [canvas.width, canvas.height] = CARD_SIZE[format];
  const pen = canvas.getContext('2d');
  if (!pen) throw new Error('This phone could not draw the card.');
  await document.fonts?.ready;
  draw(pen, await iconOf(iconUrl));
  return canvas.toDataURL('image/png').replace(/^data:image\/png;base64,/, '');
}

// The icon, or none when it cannot be loaded: the card is drawn without it.
async function iconOf(url: string): Promise<HTMLImageElement | undefined> {
  const image = new Image();
  image.src = url;
  try {
    await image.decode();
    return image;
  } catch (e) {
    // An icon that would not load is left off the card; the card still says PhotoSense.
    if (e instanceof DOMException) return undefined;
    throw e;
  }
}
