import React, { useEffect } from 'react';
import { contextLine, planLine, planTitle } from '../lib/plan';
import type { PlanDto } from '../types';
import { Scrim } from './Scrim';

interface Props {
  readonly plan: PlanDto;
  /** How many were chosen, and what they are called ("files", "duplicates"). */
  readonly count: number;
  readonly label: string;
  /** How many of them can go today, as whole groups or the largest files; none when not even one fits. */
  readonly partial: number;
  readonly busy: boolean;
  /** Removes those that can go today. */
  onPartial(): void;
  onCancel(): void;
}

/**
 * On a computer, a removal of more than the free plan has left today: how many were chosen and how many are left, how
 * to get Pro (bought on a phone, it covers this computer too), and the button that removes as many as can go today.
 */
export function LimitDialog({ plan, count, label, partial, busy, onPartial, onCancel }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onCancel]);
  // Asked only on the free plan, which has a number left today.
  const left = plan.leftToday!;
  const title = left > 0 ? `${left.toLocaleString()} free ${left === 1 ? 'removal' : 'removals'} left today` : 'No free removals left today';
  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={() => { if (!busy) onCancel(); }}>
      <div role="alertdialog" aria-modal="true" aria-label={title} className="flex max-h-full w-[520px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">{title}</h2>
        <div className="space-y-1.5 text-[15px] text-t2">
          <p>{contextLine({ reason: 'limit', count, label }, plan)}</p>
          <p>PhotoSense Pro has no daily limit. Get it in PhotoSense on your iPhone or Android phone: one purchase covers this computer too, from the next time the phone sends photos here.</p>
        </div>
        <div className="rounded-xl bg-s2 px-4 py-3 text-[13.5px] text-t2">Nothing is removed until you choose. What you remove goes to _PhotoSense_Removed, and Undo puts it back.</div>
        <div className="flex flex-wrap justify-end gap-2.5 pt-1">
          <button type="button" disabled={busy} onClick={onCancel} className="pill-outline h-11 px-5 text-[15px]">Not now</button>
          {partial > 0 && (
            <button type="button" disabled={busy} onClick={onPartial} className="pill-rose h-11 px-5 text-[15px]">{busy ? 'Working…' : `Delete ${partial.toLocaleString()} now`}</button>
          )}
        </div>
      </div>
    </Scrim>
  );
}

/** The plan in the computer's top bar: what it is, with the removals left today on the free plan, and more when pointed at. */
export function PlanPill({ plan }: { readonly plan: PlanDto }) {
  return (
    <span title={planLine(plan, new Date())} className="hidden h-8 shrink-0 items-center whitespace-nowrap rounded-full border border-line px-3 text-[13px] font-semibold md:flex">
      {planTitle(plan)}
      {plan.leftToday != null && <span className="font-normal text-t2">&nbsp;· {plan.leftToday.toLocaleString()} left today</span>}
    </span>
  );
}
