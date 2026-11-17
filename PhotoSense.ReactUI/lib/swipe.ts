import type { SwipeCardDto, SwipeDayDto, SwipeStateDto } from '../types';
import { formatBytes } from './format';
import { items } from './library';

/** The photos marked to delete and not kept after all: those from days before, then today's. */
export function marked(s: SwipeStateDto): SwipeCardDto[] {
  return [...s.earlier.filter(c => !c.spared), ...s.set.filter(c => c.delete === true && !c.spared)];
}

/** Everything marked to delete, kept after all or not, as the summary's grid shows it. */
export function markedAll(s: SwipeStateDto): SwipeCardDto[] {
  return [...s.earlier, ...s.set.filter(c => c.delete === true)];
}

/** What the photos given take. */
export const bytesOf = (cards: readonly SwipeCardDto[]) => cards.reduce((n, c) => n + c.bytes, 0);

/** How many of today's set have been swiped. */
export const reviewed = (s: SwipeStateDto) => s.set.filter(c => c.delete !== null).length;

/** How many were kept: today's kept, and those marked then kept on the summary. */
export const kept = (s: SwipeStateDto) => s.set.filter(c => c.delete === false).length + markedAll(s).filter(c => c.spared).length;

/** The next photo to swipe, if any is left in today's set. */
export const nextCard = (s: SwipeStateDto) => s.set.find(c => c.delete === null);

/** What the Swipe to clean card on Clean up says of today, and its button, if it has one. */
export function cardLine(s: SwipeStateDto): { readonly text: string; readonly button?: 'Start' | 'Continue' | 'Review deletions' } {
  const n = s.set.length, done = reviewed(s), waiting = marked(s).length;
  if (n === 0 && waiting === 0) return { text: 'You have swiped every photo and video. New ones join tomorrow’s set.' };
  if (nextCard(s) === undefined) {
    return waiting > 0
      ? { text: 'Today’s set is reviewed. Deletions are waiting for you to confirm.', button: 'Review deletions' }
      : { text: 'Done for today. A new set is ready tomorrow.' };
  }
  if (done > 0) return { text: `Today’s set · ${done.toLocaleString()} of ${n.toLocaleString()} reviewed`, button: 'Continue' };
  return { text: `Today’s set of ${n.toLocaleString()} ${n === 1 ? 'photo is' : 'photos is'} ready`, button: 'Start' };
}

/** "7-day streak"; before any, an invitation to start one. */
export const streakLine = (streak: number) => (streak > 0 ? `${streak.toLocaleString()}-day streak` : 'Swipe today to start a streak');

/** The day's initial, for the week's dots: "M", "T". */
export function dayLetter(day: SwipeDayDto): string {
  const [year, month, date] = day.date.split('-').map(Number);
  return 'SMTWTFS'[new Date(year, month - 1, date).getDay()];
}

/** The day in full, for those who cannot see the dot: "Monday, swiped". */
export function dayName(day: SwipeDayDto): string {
  const [year, month, date] = day.date.split('-').map(Number);
  const name = new Date(year, month - 1, date).toLocaleDateString('en-US', { weekday: 'long' });
  return `${name}, ${day.swiped ? 'swiped' : 'not swiped'}`;
}

/** What swiping has marked so far, at the foot of the card: "289 MB to free from 14 items". */
export function runningLine(s: SwipeStateDto): string {
  const going = marked(s);
  return going.length > 0 ? `${formatBytes(bytesOf(going))} to free from ${items(going.length)}` : 'Swipe left to delete, right to keep';
}

/** The free plan's line at the foot of the card; it says the rest wait once more are marked than are left today. Null on Pro. */
export function swipeLimit(s: SwipeStateDto, left: number | null | undefined): { readonly text: string; readonly over: boolean } | null {
  if (left == null) return null;
  const over = marked(s).length > left;
  const text = `Free plan: ${left.toLocaleString()} ${left === 1 ? 'removal' : 'removals'} left today.`;
  return { text: over ? `${text} The rest stay marked for tomorrow.` : text, over };
}

/** What the summary says over its figure: done for today, today's set done, or how far through it. */
export function summaryKicker(s: SwipeStateDto): string {
  if (nextCard(s) === undefined) return 'Today’s set is done';
  return `You reviewed ${reviewed(s).toLocaleString()} of ${s.set.length.toLocaleString()}`;
}

/** When the free plan has fewer left today than are marked: what goes now and what waits. Null when all can go. */
export function overNotice(count: number, left: number | null | undefined, perDay: number): string | null {
  if (left == null || count <= left) return null;
  if (left === 0) return `You have used today’s ${perDay.toLocaleString()} free removals. These stay marked for tomorrow.`;
  const rest = count - left;
  const go = left === 1 ? 'The largest goes now.' : `The ${left.toLocaleString()} largest go now.`;
  const stay = rest === 1 ? 'The other one stays marked for tomorrow.' : `The other ${rest.toLocaleString()} stay marked for tomorrow.`;
  return `The free plan deletes ${perDay.toLocaleString()} items a day and ${left.toLocaleString()} ${left === 1 ? 'is' : 'are'} left today. ${go} ${stay}`;
}

/** The line under the summary's button: what it uses of the free removals, and where deleted photos wait. */
export function summaryFoot(count: number, left: number | null | undefined, bin: string): string {
  const stay = `Items stay in ${bin} for 30 days.`;
  if (left == null || count === 0 || count > left) return stay;
  return `Uses ${count.toLocaleString()} of your ${left.toLocaleString()} free removals left today. ${stay}`;
}
