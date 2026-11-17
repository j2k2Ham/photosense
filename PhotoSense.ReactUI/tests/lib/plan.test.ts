import { describe, expect, it } from 'vitest';
import {
  comparison, contextLine, daysLeft, largest, limitLine, offeredPlans, overLimit, planDate, planDay, planLine, planName, planTitle, priceOf, theStore,
} from '../../lib/plan';
import { OFFERS, plan } from '../fixtures';

describe('the plans for sale', () => {
  it('are named, priced and worded as the design has them, every number from the store', () => {
    expect(offeredPlans(OFFERS, true)).toEqual([
      {
        kind: 'yearly', name: 'Yearly', sub: '7-day free trial, then $1.67 a month', price: '$19.99', badge: 'Save 44%', button: 'Start 7-day free trial',
        fine: 'Free for 7 days, then $19.99 a year. We remind you 2 days before the trial ends. Cancel any time in Settings.',
      },
      { kind: 'monthly', name: 'Monthly', sub: 'Billed every month', price: '$2.99', button: 'Subscribe for $2.99 a month', fine: 'Renews every month. Cancel any time in Settings.' },
      { kind: 'lifetime', name: 'Lifetime', sub: 'Pay once', price: '$39.00', button: 'Buy for $39.00', fine: 'One payment, no renewal.' },
      { kind: 'family', name: 'Family', sub: 'Pro for up to 6 people', price: '$29.99', button: 'Start Family for $29.99 a year', fine: 'Renews every year. Cancel any time in Settings.' },
    ]);
  });

  it('leave Family out where it is not offered, and any plan the store has not priced', () => {
    expect(offeredPlans(OFFERS, false).map(p => p.kind)).toEqual(['yearly', 'monthly', 'lifetime']);
    expect(offeredPlans(OFFERS.slice(2), true).map(p => p.kind)).toEqual(['lifetime', 'family']);
    expect(offeredPlans([], true)).toEqual([]);
  });

  it('say no saving without a monthly price to compare with, in the same currency, or when there is none', () => {
    expect(offeredPlans([OFFERS[0]], false)[0].badge).toBeUndefined();
    expect(offeredPlans([OFFERS[0], { ...OFFERS[1], currency: 'EUR' }], false)[0].badge).toBeUndefined();
    expect(offeredPlans([{ ...OFFERS[0], amount: 40 }, OFFERS[1]], false)[0].badge).toBeUndefined();
    expect(offeredPlans([{ kind: 'yearly', price: '€24,99', amount: 24.99, currency: 'EUR' }], false)[0].sub).toBe('7-day free trial, then €2.08 a month');
  });

  it('are called by their names', () => {
    expect((['yearly', 'monthly', 'lifetime', 'family'] as const).map(planName)).toEqual(['Yearly', 'Monthly', 'Lifetime', 'Family']);
  });
});

describe('the Pro sheet', () => {
  it('says why it opened, for each way in', () => {
    const p = plan();
    expect(contextLine({ reason: 'limit', count: 5998, label: 'items' }, p)).toBe('You chose 5,998 items. The free plan deletes 50 items a day, and you have 38 left today.');
    expect(contextLine({ reason: 'limit' }, p)).toBe('You chose 0 items. The free plan deletes 50 items a day, and you have 38 left today.');
    expect(contextLine({ reason: 'limit', count: 1, label: 'similar shots' }, plan({ leftToday: 0 }))).toBe("You have used today's 50 free removals. More are available tomorrow.");
    expect(contextLine({ reason: 'limit', count: 1 }, p)).toBe('You chose 1 item. The free plan deletes 50 items a day, and you have 38 left today.');
    expect(contextLine({ reason: 'limit', count: 1, label: 'duplicates' }, p)).toBe('You chose 1 duplicate. The free plan deletes 50 items a day, and you have 38 left today.');
    expect(contextLine({ reason: 'limit', count: 3, label: 'duplicates' }, plan({ leftToday: 0 }))).toBe("You have used today's 50 free removals. More are available tomorrow.");
    expect(contextLine({ reason: 'limit', count: 3 }, plan({ leftToday: null }))).toBe("You have used today's 50 free removals. More are available tomorrow.");
    expect(contextLine({ reason: 'auto' }, p)).toBe('Automatic backup is part of Pro. Sending photos yourself stays free.');
    expect(contextLine({ reason: 'compress' }, p)).toBe('Video compression is part of Pro.');
    expect(contextLine({ reason: 'search' }, p)).toBe('Searching for what is in photos is part of Pro. Searching by date and place stays free.');
    expect(contextLine({ reason: 'fix' }, p)).toBe('Fixing dates, places and file names is part of Pro.');
    expect(contextLine({ reason: 'vault' }, p)).toBe('The hidden vault is part of Pro.');
    expect(contextLine({ reason: 'none' }, p)).toBeUndefined();
  });
});

describe('the daily limit', () => {
  it('stops a removal of more than are left, never on Pro or before the plan is known', () => {
    expect(overLimit(plan(), 38)).toBe(false);
    expect(overLimit(plan(), 39)).toBe(true);
    expect(overLimit(plan({ leftToday: null }), 10_000)).toBe(false);
    expect(overLimit(undefined, 10_000)).toBe(false);
  });

  it('is said beside a button that removes, on the free plan only', () => {
    expect(limitLine(plan())).toBe('Free plan: 38 removals left today.');
    expect(limitLine(plan({ leftToday: 1 }))).toBe('Free plan: 1 removal left today.');
    expect(limitLine(plan({ leftToday: null }))).toBeUndefined();
    expect(limitLine(undefined)).toBeUndefined();
  });

  it('takes the largest first, in their own order, or all of them when there are no more', () => {
    const files = [{ id: 'a', sizeBytes: 5 }, { id: 'b', sizeBytes: 50 }, { id: 'c', sizeBytes: 9 }, { id: 'd', sizeBytes: 30 }];
    expect(largest(files, 2).map(f => f.id)).toEqual(['b', 'd']);
    expect(largest(files, 4)).toEqual(files);
    expect(largest(files, 9)).not.toBe(files);
  });

  it("is shown in the comparison with the free plan's own number", () => {
    expect(comparison(25)[1]).toEqual(['Removals each day', '25', 'Unlimited']);
    expect(comparison(50).map(r => r[0])).toEqual([
      'Full scan and Swipe to clean', 'Removals each day', 'Send photos to your computer', 'Automatic backup', 'Video compression', 'Search what is in photos', 'Fix dates and places', 'Hidden vault',
    ]);
  });
});

describe('the plan in Settings', () => {
  const now = new Date('2026-11-12T10:00:00Z');

  it('is titled by its tier', () => {
    expect([plan(), plan({ tier: 'trial' }), plan({ tier: 'pro' }), plan({ tier: 'family' })].map(planTitle))
      .toEqual(['PhotoSense Free', 'PhotoSense Pro trial', 'PhotoSense Pro', 'PhotoSense Family']);
  });

  it('says the removals left, the days of a trial and its price, or the plan and when it renews', () => {
    expect(planLine(plan(), now)).toBe('38 of 50 free removals left today');
    expect(planLine(plan({ leftToday: null }), now)).toBe('0 of 50 free removals left today');
    expect(planLine(plan({ tier: 'trial', kind: 'yearly', until: '2026-11-17T12:00:00Z' }), now)).toBe('5 days left · then $19.99 a year');
    expect(planLine(plan({ tier: 'trial', kind: 'yearly', until: '2026-11-13T09:00:00Z', offers: [] }), now)).toBe('1 day left');
    expect(planLine(plan({ tier: 'trial', kind: 'yearly' }), now)).toBe('then $19.99 a year');
    expect(planLine(plan({ tier: 'pro', kind: 'yearly', until: '2027-11-17T12:00:00Z' }), now)).toBe('Yearly plan · renews Nov 17, 2027');
    expect(planLine(plan({ tier: 'pro', kind: 'lifetime' }), now)).toBe('Lifetime plan');
    expect(planLine(plan({ tier: 'pro', kind: null }), now)).toBe('Yearly plan');
    expect(planLine(plan({ tier: 'family', kind: 'family', until: '2027-11-17T12:00:00Z', sharedBy: "Jamie's iPhone" }), now)).toBe("Up to 6 people · renews Nov 17, 2027 · from Jamie's iPhone");
  });

  it('writes dates and counts days as the design does', () => {
    expect(planDate('2027-11-17T12:00:00Z')).toBe('Nov 17, 2027');
    expect(planDay('2027-11-17T12:00:00Z')).toBe('Nov 17');
    expect(daysLeft('2026-11-12T10:00:01Z', now)).toBe(1);
    expect(daysLeft('2026-11-14T09:00:00Z', now)).toBe(1);
    expect(daysLeft('2026-11-14T10:00:00Z', now)).toBe(2);
    expect(daysLeft('2026-11-12T10:00:00Z', now)).toBe(0);
    expect(daysLeft('2026-11-11T10:00:00Z', now)).toBe(0);
    expect(priceOf(plan(), 'lifetime')).toBe('$39.00');
    expect(priceOf(plan({ offers: [] }), 'lifetime')).toBeUndefined();
    expect([theStore('App Store'), theStore('Google Play')]).toEqual(['the App Store', 'Google Play']);
  });
});
