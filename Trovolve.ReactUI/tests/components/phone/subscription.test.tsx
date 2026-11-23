import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PlaceContext } from '../../../components/phone/Screens';
import { SubscriptionScreen } from '../../../components/phone/Subscription';
import type { PlanDto } from '../../../types';
import { plan } from '../../fixtures';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({ buyPlan: vi.fn(), restorePurchases: vi.fn(), manageSubscription: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const calls = { onRemind: vi.fn(), notify: vi.fn(), onTerms: vi.fn(), onPrivacy: vi.fn(), onBack: vi.fn() };

beforeEach(() => {
  Object.values(api).forEach(f => f.mockReset());
  Object.values(calls).forEach(f => f.mockReset());
  vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-12T10:00:00Z') });
});

function show(given: PlanDto, platform: 'ios' | 'android' = 'ios', remind = true) {
  return inFrame(<SubscriptionScreen plan={given} remind={remind} {...calls} />, frame(platform));
}
const page = () => screen.getByRole('dialog', { name: 'Subscription' });

const TRIAL = plan({ tier: 'trial', kind: 'yearly', started: '2026-11-10T12:00:00Z', until: '2026-11-17T12:00:00Z', leftToday: null });
const YEARLY = plan({ tier: 'pro', kind: 'yearly', started: '2025-11-17T12:00:00Z', until: '2027-11-17T12:00:00Z', leftToday: null });

describe('on the free plan', () => {
  it('says the removals left today, compares Free with Pro, and offers the four plans, the yearly one chosen', () => {
    show(plan());
    const p = page();
    expect(p).toHaveTextContent('Trovolve Free38 of 50free removals left today');
    expect(within(p).getByRole('progressbar', { name: 'Free removals left today' })).toHaveAttribute('aria-valuenow', '38');
    const rows = within(p).getAllByRole('row').map(r => r.textContent);
    expect(rows).toEqual([
      'What you getFreePro', 'Full scan and Swipe to cleanYesYes', 'Removals each day50Unlimited', 'Send photos to your computerYesYes', 'Automatic backup–NoYes',
      'Video compression–NoYes', 'Search what is in photos–NoYes', 'Fix dates and places–NoYes', 'Hidden vault–NoYes',
    ]);
    const plans = within(within(p).getByRole('radiogroup', { name: 'Choose a plan' })).getAllByRole('radio');
    expect(plans.map(r => r.textContent)).toEqual([
      'YearlySave 44%7-day free trial, then $1.67 a month$19.99', 'MonthlyBilled every month$2.99', 'LifetimePay once$39.00', 'FamilyPro for up to 6 people$29.99',
    ]);
    expect(within(p).getByRole('button', { name: 'Start 7-day free trial' })).toBeInTheDocument();
    expect(p).toHaveTextContent('Free for 7 days, then $19.99 a year. We remind you 2 days before the trial ends. Cancel any time in Settings.');
    // Tied to the store's account; the computer is Pro through a phone paired with it, and comes from the web site (Jamie, 2026-11-20).
    expect(p).toHaveTextContent('One purchase covers Trovolve on every iPhone and iPad signed in to the same Apple Account, and on the computers you pair with them. Trovolve for Mac and PC is coming soon.');
  });

  it('buys the plan chosen, saying how it went', async () => {
    vi.useRealTimers();
    api.buyPlan.mockResolvedValueOnce({ outcome: 'bought', plan: plan({ tier: 'family' }) });
    show(plan({ leftToday: null }));
    await userEvent.click(screen.getByRole('radio', { name: /^Family/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Start Family for $29.99 a year' }));
    expect(api.buyPlan).toHaveBeenCalledExactlyOnceWith('family');
    expect(calls.notify).toHaveBeenLastCalledWith('Trovolve Family is on.', 'ok');

    api.buyPlan.mockResolvedValueOnce({ outcome: 'pending', plan: plan() });
    await userEvent.click(screen.getByRole('button', { name: 'Start Family for $29.99 a year' }));
    expect(calls.notify).toHaveBeenLastCalledWith('The purchase is waiting for approval. Pro turns on once it is approved.', 'info');
    api.buyPlan.mockResolvedValueOnce({ outcome: 'cancelled', plan: plan() });
    await userEvent.click(screen.getByRole('button', { name: 'Start Family for $29.99 a year' }));
    api.buyPlan.mockRejectedValueOnce(new Error('Google Play could not be reached. Try again.'));
    await userEvent.click(screen.getByRole('button', { name: 'Start Family for $29.99 a year' }));
    expect(calls.notify).toHaveBeenLastCalledWith('Google Play could not be reached. Try again.', 'error');
    api.buyPlan.mockRejectedValueOnce('no answer');
    await userEvent.click(screen.getByRole('button', { name: 'Start Family for $29.99 a year' }));
    expect(calls.notify).toHaveBeenLastCalledWith('no answer', 'error');
    expect(calls.notify).toHaveBeenCalledTimes(4);
  });

  it('offers nothing to buy while the store has priced nothing', () => {
    show(plan({ offers: [] }));
    expect(within(page()).queryByRole('radio')).not.toBeInTheDocument();
    expect(within(page()).queryByRole('button', { name: /trial|Subscribe|Buy/ })).not.toBeInTheDocument();
  });
});

describe('on a trial', () => {
  it('says the days left, the reminder and the renewal on a timeline, with the reminder on, and Manage in the store', async () => {
    show(TRIAL);
    const p = page();
    expect(p).toHaveTextContent('Trovolve Pro trial5 days leftThen $19.99 a year, unless you cancel');
    expect(within(p).getByRole('list', { name: 'Your trial' }).textContent).toBe('Nov 10Trial startedNov 15We remind youNov 17Renews at $19.99 a year');
    const remind = within(p).getByRole('switch', { name: 'Remind me 2 days before it ends A notification on Nov 15' });
    expect(remind).toBeChecked();
    vi.useRealTimers();
    await userEvent.click(remind);
    expect(calls.onRemind).toHaveBeenCalledExactlyOnceWith(false);
    expect(within(p).getByRole('region', { name: 'Your trial includes' })).toHaveTextContent(
      'Unlimited removals every dayAutomatic backup and video compressionSearch for what is in photosFix dates and places, and the hidden vault');
    await userEvent.click(within(p).getByRole('button', { name: 'Manage in App Store' }));
    expect(api.manageSubscription).toHaveBeenCalledOnce();
    expect(p).toHaveTextContent('To cancel, use Manage. You keep Pro until Nov 17.');
  });

  it('leaves out what the store has not said: the start, the end, the price', () => {
    show(plan({ tier: 'trial', kind: 'yearly', offers: [], store: 'Google Play' }), 'android', false);
    const p = page();
    expect(p).toHaveTextContent('Trovolve Pro trial');
    expect(within(p).queryByText(/days? left/)).not.toBeInTheDocument();
    expect(within(p).queryByRole('list', { name: 'Your trial' })).not.toBeInTheDocument();
    expect(within(p).getByRole('switch', { name: 'Remind me 2 days before it ends' })).not.toBeChecked();
    expect(within(p).getByRole('button', { name: 'Manage in Google Play' })).toBeInTheDocument();
    expect(p).toHaveTextContent('To cancel, use Manage.');
    expect(p).not.toHaveTextContent('You keep Pro until');
  });

  it('says 1 day on the last day, and a renewal with no price known', () => {
    show(plan({ tier: 'trial', kind: 'yearly', until: '2026-11-12T20:00:00Z', offers: [] }));
    expect(page()).toHaveTextContent('1 day left');
    expect(within(page()).getByRole('list', { name: 'Your trial' }).textContent).toBe('Nov 10We remind youNov 12Renews');
  });
});

describe('on Pro or Family', () => {
  it('says the plan, when it renews, its price, where it is billed and what it covers, with the current plan marked', async () => {
    vi.useRealTimers();
    show(YEARLY);
    const p = page();
    expect(p).toHaveTextContent('Trovolve ProYearly planActive');
    expect([...p.querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['RenewsNov 17, 2027', 'Price$19.99 a year', 'Billed throughApp Store', 'CoversPhone, tablet and computer']);
    const plans = within(within(p).getByRole('radiogroup', { name: 'Change plan' })).getAllByRole('radio');
    expect(plans.map(r => r.textContent)).toEqual([
      'YearlyCurrent7-day free trial, then $1.67 a month$19.99', 'MonthlyBilled every month$2.99', 'LifetimePay once$39.00', 'FamilyPro for up to 6 people$29.99',
    ]);
    expect(p).toHaveTextContent('Cancel any time in App Store. You keep Pro until the end of the period you paid for.');
    await userEvent.click(within(p).getByRole('button', { name: 'Manage in App Store' }));
    expect(api.manageSubscription).toHaveBeenCalledOnce();

    await userEvent.click(plans[1]);
    expect(p).toHaveTextContent('The change is handled by App Store. Unused time on your current plan is credited.');
    api.buyPlan.mockResolvedValueOnce({ outcome: 'bought', plan: plan({ tier: 'pro', kind: 'monthly' }) });
    await userEvent.click(within(p).getByRole('button', { name: 'Switch to Monthly for $2.99' }));
    expect(api.buyPlan).toHaveBeenCalledExactlyOnceWith('monthly');
    expect(calls.notify).toHaveBeenLastCalledWith('You are now on the Monthly plan.', 'ok');
  });

  it('says a plan paid once was paid once, and a monthly plan its price a month', () => {
    const { again } = show(plan({ tier: 'pro', kind: 'lifetime', leftToday: null }));
    expect([...page().querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['PaidOnce, $39.00', 'Billed throughApp Store', 'CoversPhone, tablet and computer']);
    again(<SubscriptionScreen plan={plan({ tier: 'pro', kind: 'lifetime', offers: [] })} remind {...calls} />);
    expect([...page().querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['PaidOnce', 'Billed throughApp Store', 'CoversPhone, tablet and computer']);
    again(<SubscriptionScreen plan={plan({ tier: 'pro', kind: 'monthly', until: '2026-12-01T12:00:00Z' })} remind {...calls} />);
    expect([...page().querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['RenewsDec 1, 2026', 'Price$2.99 a month', 'Billed throughApp Store', 'CoversPhone, tablet and computer']);
  });

  it('names Family for what it covers, and a plan with no kind or price as best it can', () => {
    const { again } = show(plan({ tier: 'family', kind: 'family', until: '2027-11-17T12:00:00Z', store: 'Google Play' }), 'android');
    expect(page()).toHaveTextContent('Trovolve FamilyFamily planActive');
    expect([...page().querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['RenewsNov 17, 2027', 'Price$29.99 a year', 'Billed throughGoogle Play', 'CoversUp to 6 people']);
    expect(within(page()).getByRole('radio', { name: /^FamilyCurrent/ })).toHaveAttribute('aria-checked', 'true');
    again(<SubscriptionScreen plan={plan({ tier: 'pro', kind: null, offers: [], store: null })} remind {...calls} />, frame('android'));
    expect(page()).toHaveTextContent('Yearly plan');
    expect([...page().querySelectorAll('dl > div')].map(d => d.textContent)).toEqual(['Billed throughGoogle Play', 'CoversPhone, tablet and computer']);
    expect(within(page()).getByRole('button', { name: 'Manage in Google Play' })).toBeInTheDocument();
  });
});

describe('every state', () => {
  it('restores purchases, saying what was found in the platform words', async () => {
    vi.useRealTimers();
    api.restorePurchases.mockResolvedValueOnce(plan());
    const { again } = show(plan());
    await userEvent.click(screen.getByRole('button', { name: 'Restore purchases' }));
    expect(calls.notify).toHaveBeenLastCalledWith('Checked the App Store. No earlier purchases found for this Apple Account.', 'info');
    api.restorePurchases.mockResolvedValueOnce(plan({ tier: 'pro' }));
    await userEvent.click(screen.getByRole('button', { name: 'Restore purchases' }));
    expect(calls.notify).toHaveBeenLastCalledWith('Purchases restored. Pro is on.', 'ok');
    again(<SubscriptionScreen plan={plan({ store: 'Google Play' })} remind {...calls} />, frame('android'));
    api.restorePurchases.mockResolvedValueOnce(plan());
    await userEvent.click(screen.getByRole('button', { name: 'Restore purchases' }));
    expect(calls.notify).toHaveBeenLastCalledWith('Checked Google Play. No earlier purchases found for this Google account.', 'info');
    expect(page()).toHaveTextContent('on every phone and tablet signed in to the same Google account, and on the computers you pair with them.');
  });

  it('opens the terms and the privacy policy, and goes back to Settings by its button or Escape', async () => {
    vi.useRealTimers();
    show(YEARLY);
    await userEvent.click(screen.getByRole('button', { name: 'Terms' }));
    await userEvent.click(screen.getByRole('button', { name: 'Privacy policy' }));
    expect(calls.onTerms).toHaveBeenCalledOnce();
    expect(calls.onPrivacy).toHaveBeenCalledOnce();
    await userEvent.keyboard('a');
    expect(calls.onBack).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Back' }));
    await userEvent.keyboard('{Escape}');
    expect(calls.onBack).toHaveBeenCalledTimes(2);
  });
});

describe('beside Settings on a tablet (T5)', () => {
  const beside = (given: PlanDto) => inFrame(<PlaceContext.Provider value><SubscriptionScreen plan={given} remind {...calls} /></PlaceContext.Provider>, frame('ios', { tablet: true }));

  it('on the free plan sets Free beside Pro and the plans side by side, the button and the links under the plans, with no way back', async () => {
    beside(plan());
    const p = screen.getByRole('region', { name: 'Subscription' });
    expect(within(p).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    const side = within(p).getByRole('radiogroup', { name: 'Choose a plan' }).closest('.grid')!;
    expect(side.firstElementChild!.tagName).toBe('TABLE');
    await userEvent.click(within(p).getByRole('radio', { name: /^Monthly/ }));
    expect(within(p).getByRole('button', { name: 'Subscribe for $2.99 a month' })).toBeInTheDocument();
    expect(within(p).getByRole('button', { name: 'Restore purchases' })).toBeInTheDocument();
  });

  it('on Pro is the phone\'s page, its foot at the pane\'s foot, with no way back', () => {
    beside(YEARLY);
    const p = screen.getByRole('region', { name: 'Subscription' });
    expect(within(p).queryByRole('button', { name: 'Back' })).not.toBeInTheDocument();
    expect(within(p).getByRole('button', { name: 'Manage in App Store' }).parentElement).toHaveClass('border-t');
  });
});
