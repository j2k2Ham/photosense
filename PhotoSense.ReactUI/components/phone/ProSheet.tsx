import React, { createContext, useContext, useState } from 'react';
import { appIconUrl, buyPlan, usePlan } from '../../lib/apiClient';
import { contextLine, LOCATION_FEATURES, offeredPlans, overLimit, PRO_FEATURES, theStore, type OfferedPlan, type ProAsk, type ProReason } from '../../lib/plan';
import { usePhone } from '../../lib/phone';
import type { PlanDto, PlanKind } from '../../types';
import type { ToastKind } from '../Toaster';
import { PhoneButton, Radio } from './Controls';
import { CheckIcon } from './Icons';
import { Sheet } from './Sheet';

/** How a screen asks before it removes, or before a Pro feature: the Pro sheet opens only when the plan calls for it. */
export interface PlanGate {
  /** The plan, once the service has said. */
  readonly plan?: PlanDto;
  /**
   * Removes `count` things called `label` ("items", "duplicates"), through `run`, with how many it may. When the free
   * plan has fewer left today, the Pro sheet opens first: buying lets all of them go, "Delete L now" the L left.
   * `largest` says the L that go are the largest, as the partial button then says.
   */
  remove(count: number, label: string, run: (allowed: number) => void, largest?: boolean): void;
  /**
   * Opens the Pro sheet, for its own sake or for a Pro feature, carrying on with `then` once Pro is bought. For a second
   * location, `instead` names the one on and, until backups have gone there, offers to use the new one in its place.
   */
  offer(reason: ProReason, then?: () => void, instead?: Instead): void;
}

/** A second location on the free plan: the one that is on, and the new one to use in its place. */
export interface Instead {
  /** The location that is on. */
  readonly current: string;
  /** The new one's name, for "Use Dropbox instead". */
  readonly wanted: string;
  /** Turns the new one on in place of the one that is on; none once backups have gone there, as the free plan is then held to it. */
  run?(): void;
}

const PlanContext = createContext<PlanGate | null>(null);

/** The plan's gate, from inside the phone layout. */
export function usePlanGate(): PlanGate {
  const gate = useContext(PlanContext);
  if (!gate) throw new Error('The plan is asked for only inside the phone layout.');
  return gate;
}

/** Gives the phone layout's screens the plan's gate. */
export const PlanProvider = PlanContext.Provider;

interface Asked extends ProAsk {
  /** Carries on once Pro is bought, with every one of them. */
  readonly then?: () => void;
  /** Removes the ones the free plan has left today. */
  readonly partial?: (allowed: number) => void;
  readonly largest?: boolean;
  /** Uses the new location in place of the one that is on. */
  readonly instead?: Instead;
}

/** The gate the phone layout gives its screens, and the Pro sheet it opens, to be drawn over everything. */
export function usePlanSheet(notify: (message: string, kind: ToastKind) => void): { gate: PlanGate; sheet: React.ReactNode } {
  const plan = usePlan().data;
  const [asked, setAsked] = useState<Asked>();
  const gate: PlanGate = {
    plan,
    remove(count, label, run, largest) {
      if (!overLimit(plan, count)) run(count);
      else setAsked({ reason: 'limit', count, label, then: () => run(count), partial: run, largest });
    },
    offer(reason, then, instead) { setAsked({ reason, then, instead, current: instead?.current, held: instead !== undefined && !instead.run }); },
  };
  const sheet = asked && plan && (
    <ProSheet plan={plan} ask={asked} notify={notify} onClose={() => setAsked(undefined)}
      onBought={() => { setAsked(undefined); asked.then?.(); }}
      onPartial={asked.partial && plan.leftToday ? () => { setAsked(undefined); asked.partial!(plan.leftToday!); } : undefined}
      onInstead={asked.instead?.run && (() => { setAsked(undefined); asked.instead!.run!(); })} />
  );
  return { gate, sheet };
}

interface Props {
  readonly plan: PlanDto;
  readonly ask: Asked;
  notify(message: string, kind: ToastKind): void;
  /** Pro is on now. */
  onBought(): void;
  /** Removes as many as the free plan has left today; absent when none are left, or the sheet is not about removing. */
  readonly onPartial?: () => void;
  /** Uses the new location in place of the one that is on; absent when the sheet is not about a second location. */
  readonly onInstead?: () => void;
  onClose(): void;
}

const capital = (text: string) => text[0].toUpperCase() + text.slice(1);

/** What is said once a purchase has gone through: the trial has started, or Pro or Family is on. */
export function boughtSaid(kind: PlanKind) {
  if (kind === 'yearly') return 'Your 7-day trial has started.';
  return kind === 'family' ? 'PhotoSense Family is on.' : 'PhotoSense Pro is on.';
}

/**
 * The Pro sheet: why it opened, what Pro adds, the plans at the store's prices, the button that buys the one chosen,
 * and, when it opened because too many were chosen, the button that removes the ones the free plan has left today.
 */
export function ProSheet({ plan, ask, notify, onBought, onPartial, onInstead, onClose }: Props) {
  const { words } = usePhone();
  const plans = offeredPlans(plan.offers, false);
  const [chosen, setChosen] = useState<PlanKind>('yearly');
  const [busy, setBusy] = useState(false);
  // The plan chosen, or the first the store prices; none while the store has priced none.
  const selected: OfferedPlan | undefined = plans.find(p => p.kind === chosen) ?? plans[0];
  const context = contextLine(ask, plan);
  const left = plan.leftToday ?? 0;

  async function buy() {
    setBusy(true);
    try {
      const result = await buyPlan(selected!.kind);
      if (result.outcome === 'bought') {
        notify(boughtSaid(selected!.kind), 'ok');
        onBought();
      } else if (result.outcome === 'pending') {
        notify('The purchase is waiting for approval. Pro turns on once it is approved.', 'info');
        onClose();
      }
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  return (
    <Sheet title="PhotoSense Pro" bare onClose={onClose}>
      <div className="flex flex-col gap-4 px-4 pb-2 pt-3">
        {context && <p className="rounded-[16px] bg-amber-soft px-4 py-3 text-[14.5px] leading-[1.4]">{context}</p>}
        <div className="flex items-center gap-3.5">
          <img src={appIconUrl} alt="" className="h-12 w-12 shrink-0 rounded-[12px]" />
          <div className="flex min-w-0 flex-col">
            <h2 className="font-title text-[24px] font-bold leading-tight">PhotoSense Pro</h2>
            <p className="text-[14px] text-t2">One purchase covers phone, tablet and computer</p>
          </div>
        </div>
        <ul className="flex flex-col gap-2.5">
          {(ask.reason === 'location' || ask.reason === 'server' ? LOCATION_FEATURES : PRO_FEATURES).map(f => <li key={f} className="flex items-center gap-2.5 text-[15px]"><span className="text-acc"><CheckIcon /></span>{f}</li>)}
        </ul>
        {plans.length > 0 ? (
          <div role="radiogroup" aria-label="Plans" className="flex flex-col gap-2">
            {plans.map(p => <PlanCard key={p.kind} plan={p} on={p.kind === selected!.kind} onChoose={() => setChosen(p.kind)} />)}
          </div>
        ) : (
          <p role="alert" className="text-[15px] text-t2">{capital(theStore(plan.store ?? words.store))} could not be reached, so its prices cannot be shown. Try again later.</p>
        )}
      </div>
      <div className="flex flex-col gap-1 px-4 pt-2">
        <PhoneButton disabled={busy || !selected} onClick={() => void buy()}>{selected?.button ?? 'Get Pro'}</PhoneButton>
        {onPartial && (
          <PhoneButton kind="text" disabled={busy} onClick={onPartial}>{ask.largest ? `Remove the ${left.toLocaleString()} largest now` : `Delete ${left.toLocaleString()} now`}</PhoneButton>
        )}
        {onInstead && <PhoneButton kind="text" disabled={busy} onClick={onInstead}>Use {ask.instead!.wanted} instead</PhoneButton>}
        <PhoneButton kind="text" disabled={busy} onClick={onClose}>Not now</PhoneButton>
        {selected && <p className="px-2 pb-1 text-center text-[12.5px] leading-[1.4] text-t2">{selected.fine}</p>}
      </div>
    </Sheet>
  );
}

interface CardProps {
  readonly plan: OfferedPlan;
  readonly on: boolean;
  /** A badge in place of the plan's own, such as Current; null for none at all. */
  readonly badge?: { readonly text: string; readonly current: boolean } | null;
  onChoose(): void;
}

/** A plan to choose: its name and badge, the line under it, and its price, with a ring that is filled when it is chosen. */
export function PlanCard({ plan, on, badge, onChoose }: CardProps) {
  const shown = badge !== undefined ? badge : plan.badge ? { text: plan.badge, current: false } : null;
  return (
    <button type="button" role="radio" aria-checked={on} onClick={onChoose}
      className={`flex min-h-[60px] w-full items-center gap-3 rounded-[16px] border-2 bg-sheet-cell px-3.5 py-2.5 text-left ${on ? 'border-acc' : 'border-transparent'}`}>
      <Radio on={on} />
      <span className="flex min-w-0 flex-1 flex-col">
        <span className="flex items-center gap-2 text-[16px] font-semibold">
          {plan.name}
          {shown && <span className={`rounded-full px-2 py-[1px] text-[11.5px] font-semibold ${shown.current ? 'bg-fill text-t2' : 'bg-keep-soft text-keep'}`}>{shown.text}</span>}
        </span>
        <span className="truncate text-[13px] text-t2">{plan.sub}</span>
      </span>
      <span className="shrink-0 text-[16px] font-semibold tabular-nums">{plan.price}</span>
    </button>
  );
}
