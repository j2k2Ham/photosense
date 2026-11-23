import { describe, expect, it } from 'vitest';
import {
  bytesOf, cardLine, dayLetter, dayName, kept, marked, markedAll, nextCard, overNotice, reviewed, runningLine, streakLine, summaryFoot, summaryKicker, swipeLimit,
} from '../../lib/swipe';
import { swipeCard, swipeState } from '../fixtures';

const MB = 1024 ** 2;
// Today's set of four: one marked, one kept, one marked and kept after all, one to go; and one marked on a day before.
const state = () => swipeState({
  set: [swipeCard('a', { delete: true, bytes: 3 * MB }), swipeCard('b', { delete: false }), swipeCard('c', { delete: true, spared: true }), swipeCard('d')],
  earlier: [swipeCard('e', { delete: true, bytes: 2 * MB })],
});

describe('Swipe to clean\'s counts', () => {
  it('count what is marked, from days before first, what was kept, what was swiped, and what is next', () => {
    const s = state();
    expect(marked(s).map(c => c.id)).toEqual(['e', 'a']);
    expect(markedAll(s).map(c => c.id)).toEqual(['e', 'a', 'c']);
    expect(bytesOf(marked(s))).toBe(5 * MB);
    expect([reviewed(s), kept(s), nextCard(s)?.id]).toEqual([3, 2, 'd']);
    // One marked on a day before and then kept counts as kept too.
    expect(kept(swipeState({ earlier: [swipeCard('e', { delete: true, spared: true })] }))).toBe(1);
  });
});

describe('the Swipe to clean card', () => {
  it('says how far through today\'s set, and offers Start, Continue or Review deletions', () => {
    expect(cardLine(swipeState({ set: [swipeCard('a'), swipeCard('b')] }))).toEqual({ text: 'Today’s set of 2 photos is ready', button: 'Start' });
    expect(cardLine(swipeState({ set: [swipeCard('a')] }))).toEqual({ text: 'Today’s set of 1 photo is ready', button: 'Start' });
    expect(cardLine(state())).toEqual({ text: 'Today’s set · 3 of 4 reviewed', button: 'Continue' });
    expect(cardLine(swipeState({ set: [swipeCard('a', { delete: true })] }))).toEqual({ text: 'Today’s set is reviewed. Deletions are waiting for you to confirm.', button: 'Review deletions' });
  });

  it('is done for the day once nothing is left to swipe or to confirm, and says when every photo has been swiped', () => {
    expect(cardLine(swipeState({ set: [swipeCard('a', { delete: false })] }))).toEqual({ text: 'Done for today. A new set is ready tomorrow.' });
    expect(cardLine(swipeState())).toEqual({ text: 'You have swiped every photo and video. New ones join tomorrow’s set.' });
    // Nothing new to swipe, but some marked on a day before still wait.
    expect(cardLine(swipeState({ earlier: [swipeCard('e', { delete: true })] })).button).toBe('Review deletions');
  });

  it('says the streak, or invites one, and names each day of the week', () => {
    expect([streakLine(7), streakLine(1), streakLine(0)]).toEqual(['7-day streak', '1-day streak', 'Swipe today to start a streak']);
    expect(dayLetter({ date: '2026-11-17', swiped: true })).toBe('T');
    expect(dayLetter({ date: '2026-11-15', swiped: true })).toBe('S');
    expect(dayName({ date: '2026-11-16', swiped: true })).toBe('Monday, swiped');
    expect(dayName({ date: '2026-11-16', swiped: false })).toBe('Monday, not swiped');
  });
});

describe('the swipe screen\'s foot', () => {
  it('says what is marked so far, or how to swipe before anything is', () => {
    expect(runningLine(state())).toBe('5.0 MB to free from 2 items');
    expect(runningLine(swipeState({ set: [swipeCard('a', { delete: true })] }))).toBe('1.0 MB to free from 1 item');
    expect(runningLine(swipeState())).toBe('Swipe left to delete, right to keep');
  });

  it('says the free removals left today, and once more are marked, that the rest wait; nothing on Pro', () => {
    expect(swipeLimit(state(), 50)).toEqual({ text: 'Free plan: 50 removals left today.', over: false });
    expect(swipeLimit(state(), 1)).toEqual({ text: 'Free plan: 1 removal left today. The rest stay marked for tomorrow.', over: true });
    expect(swipeLimit(state(), null)).toBeNull();
    expect(swipeLimit(state(), undefined)).toBeNull();
  });
});

describe('the swipe summary', () => {
  it('says whether today\'s set is done or how far through it', () => {
    expect(summaryKicker(state())).toBe('You reviewed 3 of 4');
    expect(summaryKicker(swipeState({ set: [swipeCard('a', { delete: true })] }))).toBe('Today’s set is done');
  });

  it('says, over the free removals left, what goes now and what waits, and nothing when all can go', () => {
    expect(overNotice(40, 30, 50)).toBe('The free plan deletes 50 items a day and 30 are left today. The 30 largest go now. The other 10 stay marked for tomorrow.');
    expect(overNotice(2, 1, 50)).toBe('The free plan deletes 50 items a day and 1 is left today. The largest goes now. The other one stays marked for tomorrow.');
    expect(overNotice(5, 0, 50)).toBe('You have used today’s 50 free removals. These stay marked for tomorrow.');
    expect(overNotice(30, 30, 50)).toBeNull();
    expect(overNotice(30, null, 50)).toBeNull();
  });

  it('says what the deletion uses of the free removals, and where deleted photos wait, in the platform\'s words', () => {
    expect(summaryFoot(40, 50, 'Recently Deleted')).toBe('Uses 40 of your 50 free removals left today. Items stay in Recently Deleted for 30 days.');
    expect(summaryFoot(40, 30, 'the bin')).toBe('Items stay in the bin for 30 days.');
    expect(summaryFoot(40, null, 'Recently Deleted')).toBe('Items stay in Recently Deleted for 30 days.');
    expect(summaryFoot(0, 50, 'Recently Deleted')).toBe('Items stay in Recently Deleted for 30 days.');
  });
});
