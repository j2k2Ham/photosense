import React, { useState } from 'react';
import { appIconUrl, buyPlan, manageSubscription, restorePurchases } from '../../lib/apiClient';
import { comparison, daysLeft, offeredPlans, planDate, planDay, planName, priceOf, theStore, TRIAL_FEATURES, type OfferedPlan } from '../../lib/plan';
import { usePhone } from '../../lib/phone';
import type { PlanDto, PlanKind } from '../../types';
import type { ToastKind } from '../Toaster';
import { ListGroup, PhoneButton, PhoneSwitch } from './Controls';
import { CheckIcon, TickIcon } from './Icons';
import { boughtSaid, PlanCard } from './ProSheet';
import { LayerScreen, PushedHeader } from './Screens';

interface Props {
  readonly plan: PlanDto;
  /** A trial is to be followed by a reminder two days before it renews. */
  readonly remind: boolean;
  onRemind(on: boolean): void;
  notify(message: string, kind: ToastKind): void;
  onTerms(): void;
  onPrivacy(): void;
  onBack(): void;
}

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/**
 * The Subscription page, from the plan at the top of Settings. On the free plan: the removals left today, Free beside
 * Pro, and the plans to choose from. On a trial: the days left, when the reminder comes and when it renews. On Pro or
 * Family: the plan, when it renews and where it is billed, and the other plans to change to.
 */
export function SubscriptionScreen({ plan, remind, onRemind, notify, onTerms, onPrivacy, onBack }: Props) {
  const { words } = usePhone();
  const store = plan.store ?? words.store;
  const plans = offeredPlans(plan.offers, true);
  const current: PlanKind | undefined = plan.tier === 'family' ? 'family' : plan.tier === 'pro' ? plan.kind ?? undefined : undefined;
  const [chosen, setChosen] = useState<PlanKind>(current ?? 'yearly');
  const [busy, setBusy] = useState(false);
  const selected: OfferedPlan | undefined = plans.find(p => p.kind === chosen) ?? plans[0];

  async function act(run: () => Promise<void>) {
    setBusy(true);
    try { await run(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }
  const buy = (kind: PlanKind, switching: boolean) => act(async () => {
    const result = await buyPlan(kind);
    if (result.outcome === 'bought') notify(switching ? `You are now on the ${planName(kind)} plan.` : boughtSaid(kind), 'ok');
    else if (result.outcome === 'pending') notify('The purchase is waiting for approval. Pro turns on once it is approved.', 'info');
  });
  const restore = () => act(async () => {
    const after = await restorePurchases();
    if (after.tier !== 'free') notify('Purchases restored. Pro is on.', 'ok');
    else notify(`Checked ${theStore(store)}. No earlier purchases found for this ${words.account}.`, 'info');
  });
  const manage = () => act(manageSubscription);

  let body: React.ReactNode;
  let footer: React.ReactNode;
  if (plan.tier === 'free') {
    body = <FreePlan plan={plan} plans={plans} chosen={selected?.kind} onChoose={setChosen} />;
    footer = selected && (
      <>
        <PhoneButton disabled={busy} onClick={() => void buy(selected.kind, false)}>{selected.button}</PhoneButton>
        <p className="text-center text-[12.5px] leading-[1.4] text-t2">{selected.fine}</p>
      </>
    );
  } else if (plan.tier === 'trial') {
    body = <TrialPlan plan={plan} remind={remind} onRemind={onRemind} />;
    footer = (
      <>
        <PhoneButton kind="secondary" disabled={busy} onClick={() => void manage()}>Manage in {store}</PhoneButton>
        <p className="text-center text-[12.5px] leading-[1.4] text-t2">To cancel, use Manage.{plan.until ? ` You keep Pro until ${planDay(plan.until)}.` : ''}</p>
      </>
    );
  } else {
    const switching = selected && selected.kind !== current;
    body = <PaidPlan plan={plan} store={store} plans={plans} current={current} chosen={selected?.kind} onChoose={setChosen} />;
    footer = switching ? (
      <>
        <PhoneButton disabled={busy} onClick={() => void buy(selected.kind, true)}>Switch to {selected.name} for {selected.price}</PhoneButton>
        <p className="text-center text-[12.5px] leading-[1.4] text-t2">The change is handled by {store}. Unused time on your current plan is credited.</p>
      </>
    ) : (
      <>
        <PhoneButton kind="secondary" disabled={busy} onClick={() => void manage()}>Manage in {store}</PhoneButton>
        <p className="text-center text-[12.5px] leading-[1.4] text-t2">Cancel any time in {store}. You keep Pro until the end of the period you paid for.</p>
      </>
    );
  }

  return (
    <LayerScreen label="Subscription" onBack={onBack} footer={footer}>
      <PushedHeader title="Subscription" onBack={onBack} />
      <div className="flex flex-col gap-6 px-4 pb-6 pt-2">
        {body}
        <div className="flex flex-wrap justify-center gap-x-5 gap-y-1 text-[14px] font-semibold text-acc">
          <button type="button" disabled={busy} onClick={() => void restore()} className="py-1 disabled:opacity-40">Restore purchases</button>
          <button type="button" onClick={onTerms} className="py-1">Terms</button>
          <button type="button" onClick={onPrivacy} className="py-1">Privacy policy</button>
        </div>
        <p className="px-4 text-center text-[13px] leading-[1.45] text-t2">One purchase covers PhotoSense on your phone, tablet and computer when you sign in with the same {words.account}.</p>
      </div>
    </LayerScreen>
  );
}

interface PlansProps {
  readonly plans: readonly OfferedPlan[];
  readonly chosen?: PlanKind;
  onChoose(kind: PlanKind): void;
}

function PlanList({ header, plans, chosen, current, onChoose }: PlansProps & { readonly header: string; readonly current?: PlanKind }) {
  const { ios } = usePhone();
  return (
    <section aria-label={header} className="flex flex-col gap-2">
      <h3 className={`px-1 ${ios ? 'text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'text-[14px] font-medium text-acc'}`}>{header}</h3>
      <div role="radiogroup" aria-label={header} className="flex flex-col gap-2">
        {plans.map(p => (
          <PlanCard key={p.kind} plan={p} on={p.kind === chosen} onChoose={() => onChoose(p.kind)}
            badge={current === undefined ? undefined : p.kind === current ? { text: 'Current', current: true } : null} />
        ))}
      </div>
    </section>
  );
}

function FreePlan({ plan, plans, chosen, onChoose }: PlansProps & { readonly plan: PlanDto }) {
  const left = plan.leftToday ?? 0;
  return (
    <>
      <div className="flex flex-col gap-1">
        <p className="text-[14px] font-semibold text-t2">PhotoSense Free</p>
        <p className="font-title text-[34px] font-bold leading-tight tabular-nums">{left.toLocaleString()} of {plan.removalsPerDay.toLocaleString()}</p>
        <p className="text-[15px] text-t2">free removals left today</p>
        <div role="progressbar" aria-label="Free removals left today" aria-valuemin={0} aria-valuemax={plan.removalsPerDay} aria-valuenow={left}
          className="mt-3 h-[6px] overflow-hidden rounded-full bg-fill2">
          <div className="h-full rounded-full bg-acc" style={{ width: `${(left / plan.removalsPerDay) * 100}%` }} />
        </div>
      </div>
      <table className="w-full overflow-hidden rounded-cell bg-cell text-[15px]">
        <thead>
          <tr className="text-[13px] font-semibold text-t2">
            <th scope="col" className="py-2.5 pl-4 text-left font-semibold"><span className="sr-only">What you get</span></th>
            <th scope="col" className="w-16 py-2.5 text-center font-semibold">Free</th>
            <th scope="col" className="w-20 py-2.5 pr-2 text-center font-semibold text-acc">Pro</th>
          </tr>
        </thead>
        <tbody>
          {comparison(plan.removalsPerDay).map(([what, free, pro]) => (
            <tr key={what} className="border-t-[0.5px] border-sep">
              <th scope="row" className="py-2.5 pl-4 text-left font-normal">{what}</th>
              <td className="text-center text-t2"><Answer value={free} /></td>
              <td className="pr-2 text-center"><Answer value={pro} /></td>
            </tr>
          ))}
        </tbody>
      </table>
      <PlanList header="Choose a plan" plans={plans} chosen={chosen} onChoose={onChoose} />
    </>
  );
}

// A tick for yes, the value written out, or a dash for none.
function Answer({ value }: { readonly value: string | boolean | null }) {
  if (value === true) return <span className="inline-flex text-acc"><CheckIcon /><span className="sr-only">Yes</span></span>;
  if (value === null) return <span><span aria-hidden>–</span><span className="sr-only">No</span></span>;
  return <span className="text-[14px] font-semibold">{value}</span>;
}

// A day on the trial's way: when, what happens, and whether it has.
interface Step { readonly date: string; readonly what: string; readonly done: boolean }

function TrialPlan({ plan, remind, onRemind }: { readonly plan: PlanDto; readonly remind: boolean; onRemind(on: boolean): void }) {
  const yearly = priceOf(plan, 'yearly');
  const until = plan.until;
  const reminder = until && new Date(new Date(until).getTime() - 2 * 86_400_000).toISOString();
  const days = until ? daysLeft(until, new Date()) : undefined;
  const steps: Step[] = [];
  if (plan.started) steps.push({ date: planDay(plan.started), what: 'Trial started', done: true });
  if (until) {
    steps.push({ date: planDay(reminder!), what: 'We remind you', done: false });
    steps.push({ date: planDay(until), what: yearly ? `Renews at ${yearly} a year` : 'Renews', done: false });
  }
  return (
    <>
      <div className="flex flex-col gap-1">
        <p className="text-[14px] font-semibold text-t2">PhotoSense Pro trial</p>
        {days !== undefined && <p className="font-title text-[34px] font-bold leading-tight">{days} {days === 1 ? 'day' : 'days'} left</p>}
        {yearly && <p className="text-[15px] text-t2">Then {yearly} a year, unless you cancel</p>}
      </div>
      {steps.length > 0 && (
        <ol aria-label="Your trial" className="flex flex-col rounded-cell bg-cell px-4 py-3">
          {steps.map((s, i) => (
            <li key={s.what} className="flex gap-3">
              <span className="flex flex-col items-center">
                <span className={`mt-[3px] grid h-[18px] w-[18px] place-items-center rounded-full ${s.done ? 'bg-acc text-on-acc' : 'border-2 border-t3'}`}>{s.done && <TickIcon size={11} />}</span>
                {i < steps.length - 1 && <span className="w-[2px] flex-1 bg-sep" />}
              </span>
              <span className="flex flex-col pb-3">
                <span className="text-[12.5px] font-semibold text-t2">{s.date}</span>
                <span className="text-[16px]">{s.what}</span>
              </span>
            </li>
          ))}
        </ol>
      )}
      <PhoneSwitch label="Remind me 2 days before it ends" detail={reminder ? `A notification on ${planDay(reminder)}` : undefined} on={remind} onChange={onRemind} />
      <ListGroup header="Your trial includes">
        {TRIAL_FEATURES.map(f => (
          <li key={f} className="flex min-h-[44px] items-center gap-2.5 px-4 py-2 text-[15px]"><span className="text-acc"><CheckIcon /></span>{f}</li>
        ))}
      </ListGroup>
    </>
  );
}

function PaidPlan({ plan, store, plans, current, chosen, onChoose }: PlansProps & { readonly plan: PlanDto; readonly store: string; readonly current?: PlanKind }) {
  const kind = plan.kind ?? 'yearly';
  const price = priceOf(plan, kind);
  const covers = plan.tier === 'family' ? 'Up to 6 people' : 'Phone, tablet and computer';
  const rows: [string, string][] = kind === 'lifetime'
    ? [['Paid', price ? `Once, ${price}` : 'Once'], ['Billed through', store], ['Covers', covers]]
    : [
      ...(plan.until ? [['Renews', planDate(plan.until)] as [string, string]] : []),
      ...(price ? [['Price', `${price} ${kind === 'monthly' ? 'a month' : 'a year'}`] as [string, string]] : []),
      ['Billed through', store], ['Covers', covers],
    ];
  return (
    <>
      <div className="flex items-center gap-3.5 rounded-cell bg-cell p-4">
        <img src={appIconUrl} alt="" className="h-12 w-12 shrink-0 rounded-[12px]" />
        <div className="flex min-w-0 flex-1 flex-col">
          <p className="text-[18px] font-semibold">{plan.tier === 'family' ? 'PhotoSense Family' : 'PhotoSense Pro'}</p>
          <p className="text-[14px] text-t2">{planName(kind)} plan</p>
        </div>
        <span className="rounded-full bg-keep-soft px-2.5 py-[2px] text-[12.5px] font-semibold text-keep">Active</span>
      </div>
      <dl className="flex flex-col overflow-hidden rounded-cell bg-cell">
        {rows.map(([k, v], i) => (
          <div key={k} className={`flex min-h-[48px] items-center justify-between gap-3 px-4 py-2 text-[15px] ${i > 0 ? 'border-t-[0.5px] border-sep' : ''}`}>
            <dt>{k}</dt><dd className="text-right text-t2">{v}</dd>
          </div>
        ))}
      </dl>
      <PlanList header="Change plan" plans={plans} chosen={chosen} current={current} onChoose={onChoose} />
    </>
  );
}
