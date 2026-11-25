import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import React from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { PlanProvider, usePlanGate, usePlanSheet } from '../../../components/phone/ProSheet';
import type { ProReason } from '../../../lib/plan';
import type { PlanDto } from '../../../types';
import { deferred } from '../../deferred';
import { plan } from '../../fixtures';
import { frame, inFrame } from './frame';

const api = vi.hoisted(() => ({ usePlan: vi.fn(), buyPlan: vi.fn() }));
vi.mock('../../../lib/apiClient', async importOriginal => ({ ...(await importOriginal<typeof import('../../../lib/apiClient')>()), ...api }));

const notify = vi.fn();
const ran = vi.fn();
const carried = vi.fn();

beforeEach(() => {
  api.usePlan.mockReset();
  api.buyPlan.mockReset();
  notify.mockReset();
  ran.mockReset();
  carried.mockReset();
  api.usePlan.mockReturnValue({ data: plan() });
});

// A screen that removes, or offers a Pro feature, as the phone's screens do.
function Screen() {
  const gate = usePlanGate();
  const offer = (reason: ProReason) => () => gate.offer(reason, carried);
  return (
    <>
      <button type="button" onClick={() => gate.remove(3, 'items', ran)}>Delete 3</button>
      <button type="button" onClick={() => gate.remove(60, 'duplicates', ran)}>Delete 60</button>
      <button type="button" onClick={() => gate.remove(5998, 'items', ran, true)}>Remove 5,998</button>
      <button type="button" onClick={offer('vault')}>Hidden</button>
      <button type="button" onClick={offer('none')}>Get Pro</button>
      <button type="button" onClick={offer('server')}>Connect to server</button>
      <button type="button" onClick={() => gate.offer('location', carried, { current: 'Google Drive', wanted: 'Dropbox', run: ran })}>Dropbox</button>
      <button type="button" onClick={() => gate.offer('location', carried, { current: 'Google Drive', wanted: 'OneDrive' })}>OneDrive</button>
    </>
  );
}

function Phone() {
  const { gate, sheet } = usePlanSheet(notify);
  return <PlanProvider value={gate}><Screen />{sheet}</PlanProvider>;
}

const show = (platform: 'ios' | 'android' = 'ios') => inFrame(<Phone />, frame(platform));
const sheet = () => screen.getByRole('dialog', { name: 'Trovolve Pro' });

describe('the plan gate', () => {
  it('lets a removal of no more than are left go ahead at once, with no sheet', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 3' }));
    expect(ran).toHaveBeenCalledExactlyOnceWith(3);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('lets any removal go ahead on Pro, or before the plan is known, and opens no sheet without a plan', async () => {
    api.usePlan.mockReturnValue({ data: plan({ tier: 'pro', kind: 'lifetime', leftToday: null }) });
    const { unmount } = show();
    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998' }));
    expect(ran).toHaveBeenLastCalledWith(5998);
    unmount();
    api.usePlan.mockReturnValue({});
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    expect(ran).toHaveBeenLastCalledWith(60);
    await userEvent.click(screen.getByRole('button', { name: 'Hidden' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('opens for a second location saying which is on, what Pro adds for locations, and uses the new one instead', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Dropbox' }));
    expect(sheet()).toHaveTextContent('The free plan backs up to your computer and one other location, and Google Drive is on. Pro adds as many as you like.');
    expect(sheet()).toHaveTextContent('Back up to as many locations as you like');
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Use Dropbox instead' }));
    expect(ran).toHaveBeenCalledOnce();
    expect(screen.queryByRole('dialog', { name: 'Trovolve Pro' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Connect to server' }));
    expect(sheet()).toHaveTextContent('Connecting your own server, like Amazon S3, Azure or a home NAS, is part of Pro.');
    expect(within(sheet()).queryByRole('button', { name: /instead$/ })).not.toBeInTheDocument();
  });

  it('once backups are on the one that is on, offers only Pro for a second location, never one in its place', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'OneDrive' }));
    expect(sheet()).toHaveTextContent('The free plan backs up to your computer and one other location, and your backups are on Google Drive. Pro adds as many as you like.');
    expect(within(sheet()).queryByRole('button', { name: /instead$/ })).not.toBeInTheDocument();
  });

  it('opens for a Pro feature on any plan, with no partial delete', async () => {
    api.usePlan.mockReturnValue({ data: plan({ tier: 'trial', leftToday: null }) });
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Hidden' }));
    expect(sheet()).toHaveTextContent('The hidden vault is part of Pro.');
    expect(within(sheet()).queryByRole('button', { name: /^(Delete|Remove) .* now$/ })).not.toBeInTheDocument();
  });

  it('is asked for only inside the phone layout', () => {
    vi.spyOn(console, 'error').mockImplementation(() => {});
    expect(() => render(<Screen />)).toThrow('The plan is asked for only inside the phone layout.');
  });
});

describe('the Pro sheet', () => {
  it('says how many were chosen and how many are left, what Pro adds, and the plans at the store prices, the yearly one chosen', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    expect(ran).not.toHaveBeenCalled();
    const s = sheet();
    expect(s).toHaveTextContent('You chose 60 duplicates. The free plan deletes 50 items a day, and you have 38 left today.');
    expect(s).toHaveTextContent('Trovolve ProOne purchase covers phone, tablet and computer');
    expect(within(s).getAllByRole('listitem').map(li => li.textContent)).toEqual(['Unlimited removals every day', 'Automatic backup to your computer', 'Video compression']);
    const plans = within(s).getAllByRole('radio');
    expect(plans.map(p => p.textContent)).toEqual([
      'YearlySave 44%7-day free trial, then $1.67 a month$19.99', 'MonthlyBilled every month$2.99', 'LifetimePay once$39.00',
    ]);
    expect(plans[0]).toHaveAttribute('aria-checked', 'true');
    expect(within(s).getByRole('button', { name: 'Start 7-day free trial' })).toBeEnabled();
    expect(s).toHaveTextContent('Free for 7 days, then $19.99 a year. We remind you 2 days before the trial ends. Cancel any time in Settings.');

    await userEvent.click(plans[1]);
    expect(within(s).getByRole('button', { name: 'Subscribe for $2.99 a month' })).toBeInTheDocument();
    expect(s).toHaveTextContent('Renews every month. Cancel any time in Settings.');
  });

  it('deletes as many as are left now, or the largest of them, and goes away', async () => {
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Delete 38 now' }));
    expect(ran).toHaveBeenCalledExactlyOnceWith(38);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Remove 5,998' }));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Remove the 38 largest now' }));
    expect(ran).toHaveBeenLastCalledWith(38);
  });

  it('offers no partial delete once none are left today', async () => {
    api.usePlan.mockReturnValue({ data: plan({ leftToday: 0, removedToday: 50 }) });
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 3' }));
    expect(sheet()).toHaveTextContent("You have used today's 50 free removals. More are available tomorrow.");
    expect(within(sheet()).queryByRole('button', { name: /^(Delete|Remove) .* now$/ })).not.toBeInTheDocument();
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Not now' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(ran).not.toHaveBeenCalled();
  });

  it('once bought says so and carries on with all of them; a trial, Pro or Family each in its words', async () => {
    api.buyPlan.mockResolvedValue({ outcome: 'bought', plan: plan({ tier: 'trial' }) });
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(api.buyPlan).toHaveBeenCalledExactlyOnceWith('yearly');
    expect(notify).toHaveBeenCalledExactlyOnceWith('Your 7-day trial has started.', 'ok');
    expect(ran).toHaveBeenCalledExactlyOnceWith(60);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Hidden' }));
    expect(sheet()).toHaveTextContent('The hidden vault is part of Pro.');
    await userEvent.click(within(sheet()).getByRole('radio', { name: /^Lifetime/ }));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Buy for $39.00' }));
    expect(notify).toHaveBeenLastCalledWith('Trovolve Pro is on.', 'ok');
    expect(carried).toHaveBeenCalledOnce();
  });

  it('opened for its own sake says no reason, and carries on with nothing once bought', async () => {
    api.buyPlan.mockResolvedValue({ outcome: 'bought', plan: plan({ tier: 'pro' }) });
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Get Pro' }));
    expect(sheet().querySelector('.bg-amber-soft')).toBeNull();
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(carried).toHaveBeenCalledOnce();
  });

  it('waits for approval, says nothing when the store sheet is put away, and says what went wrong', async () => {
    const buying = deferred<{ outcome: string; plan: PlanDto }>();
    api.buyPlan.mockReturnValueOnce(buying.promise);
    show();
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(within(sheet()).getByRole('button', { name: 'Not now' })).toBeDisabled();
    buying.resolve({ outcome: 'cancelled', plan: plan() });
    await vi.waitFor(() => expect(within(sheet()).getByRole('button', { name: 'Not now' })).toBeEnabled());
    expect(notify).not.toHaveBeenCalled();

    api.buyPlan.mockRejectedValueOnce(new Error('The App Store could not be reached. Try again.'));
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(notify).toHaveBeenLastCalledWith('The App Store could not be reached. Try again.', 'error');
    api.buyPlan.mockRejectedValueOnce('no answer');
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(notify).toHaveBeenLastCalledWith('no answer', 'error');

    api.buyPlan.mockResolvedValueOnce({ outcome: 'pending', plan: plan() });
    await userEvent.click(within(sheet()).getByRole('button', { name: 'Start 7-day free trial' }));
    expect(notify).toHaveBeenLastCalledWith('The purchase is waiting for approval. Pro turns on once it is approved.', 'info');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(ran).not.toHaveBeenCalled();
  });

  it('says when the store cannot be reached for its prices, in the platform words, and buys nothing', async () => {
    api.usePlan.mockReturnValue({ data: plan({ offers: [], store: null }) });
    show('android');
    await userEvent.click(screen.getByRole('button', { name: 'Delete 60' }));
    expect(within(sheet()).getByRole('alert')).toHaveTextContent('Google Play could not be reached, so its prices cannot be shown. Try again later.');
    expect(within(sheet()).getByRole('button', { name: 'Get Pro' })).toBeDisabled();
    expect(within(sheet()).queryByRole('radio')).not.toBeInTheDocument();
  });
});
