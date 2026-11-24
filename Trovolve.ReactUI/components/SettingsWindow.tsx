import React, { useEffect, useId } from 'react';
import { chooseTestPlan, usePlan } from '../lib/apiClient';
import { DISTANCE_UNITS } from '../lib/distance';
import { binName } from '../lib/format';
import { Segmented } from './ModeSwitch';
import { Scrim } from './Scrim';
import type { AppSettings } from '../lib/settings';
import { TEST_PLANS, testPlanSaid } from '../lib/testPlans';
import type { ToastKind } from './Toaster';

interface Props {
  readonly settings: AppSettings;
  onChange(change: Partial<AppSettings>): void;
  /** Says what happened, as every message is said. */
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

/** One setting: what it is, what it does, and the switch that turns it on or off. */
function Switch({ title, about, on, onChange }: { readonly title: string; readonly about: string; readonly on: boolean; onChange(on: boolean): void }) {
  const id = useId();
  return (
    // A screen reader names the switch by its title and reads what it does after.
    <label className="flex cursor-pointer select-none items-start gap-4">
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span id={`${id}-title`} className="text-[15px] font-medium">{title}</span>
        <span id={`${id}-about`} className="text-[13px] text-t2">{about}</span>
      </span>
      <input type="checkbox" role="switch" checked={on} onChange={e => onChange(e.target.checked)} aria-labelledby={`${id}-title`} aria-describedby={`${id}-about`} className="peer sr-only" />
      <span aria-hidden className="relative mt-1 h-[22px] w-10 shrink-0 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-[18px] after:w-[18px] after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-[18px] peer-checked:after:bg-on-brand peer-focus-visible:ring-2 peer-focus-visible:ring-brand" />
    </label>
  );
}

/** The settings of Trovolve, each taking effect as it is changed. They are kept in this browser. */
export function SettingsWindow({ settings, onChange, notify, onClose }: Props) {
  const testing = usePlan().data?.testPlan;
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={onClose}>
      <div role="dialog" aria-modal="true" aria-label="Settings" className="flex max-h-full w-[560px] max-w-full flex-col gap-5 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">Settings</h2>
        <div className="flex flex-col gap-3">
          <div className="label-caps">Deleting permanently</div>
          <Switch title={`Ask a second time before moving to the ${binName()}`} on={settings.eraseAskTwice} onChange={eraseAskTwice => onChange({ eraseAskTwice })}
            about={`After the first question, Move to ${binName()} asks “Are you sure?” once more. Turned off, the files go as soon as the first question is confirmed.`} />
        </div>
        <div className="flex flex-col gap-3">
          <div className="label-caps">Distances</div>
          <div className="flex flex-wrap items-center gap-4">
            <p className="min-w-0 flex-1 text-[13px] text-t2">How far apart places are, in Organize and on its map.</p>
            <Segmented label="Distances" value={settings.distanceUnit} onChange={distanceUnit => onChange({ distanceUnit })} options={DISTANCE_UNITS} size="medium" />
          </div>
        </div>
        {/* A development build alone has plans to test as (Jamie, 2026-11-24): one for others has none, and this is not drawn. */}
        {testing && (
          <div className="flex flex-col gap-3">
            <div className="label-caps">For testing</div>
            <p className="text-[13px] text-t2">Use Trovolve as someone on this plan would, in place of the plan as bought. Only a development build has this.</p>
            <div role="radiogroup" aria-label="Test as" className="flex flex-wrap gap-2">
              {TEST_PLANS.map(p => (
                <button key={p.id} type="button" role="radio" aria-checked={testing === p.id}
                  onClick={() => void chooseTestPlan(p.id).then(() => notify(testPlanSaid(p.id), 'ok'), (e: unknown) => notify(e instanceof Error ? e.message : String(e), 'error'))}
                  className={`pill h-8 px-3.5 text-[13px] ${testing === p.id ? 'bg-t1 text-bg' : 'border border-line text-t2'}`}>{p.label}</button>
              ))}
            </div>
          </div>
        )}
        <div className="flex items-center gap-3 pt-1">
          <p className="flex-1 text-[12.5px] text-t3">Settings are kept in this browser.</p>
          <button type="button" onClick={onClose} className="pill-brand h-11 px-6 text-[15px]">Done</button>
        </div>
      </div>
    </Scrim>
  );
}
