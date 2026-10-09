import React, { useEffect } from 'react';
import type { AppSettings } from '../lib/settings';

interface Props {
  readonly settings: AppSettings;
  onChange(change: Partial<AppSettings>): void;
  onClose(): void;
}

/** One setting: what it is, what it does, and the switch that turns it on or off. */
function Switch({ title, about, on, onChange }: { readonly title: string; readonly about: string; readonly on: boolean; onChange(on: boolean): void }) {
  return (
    <label className="flex cursor-pointer select-none items-start gap-4">
      <span className="flex min-w-0 flex-1 flex-col gap-0.5">
        <span className="text-[15px] font-medium">{title}</span>
        <span className="text-[13px] text-t2">{about}</span>
      </span>
      <input type="checkbox" role="switch" checked={on} onChange={e => onChange(e.target.checked)} className="peer sr-only" />
      <span aria-hidden className="relative mt-1 h-[22px] w-10 shrink-0 rounded-full bg-s3 transition after:absolute after:left-0.5 after:top-0.5 after:h-[18px] after:w-[18px] after:rounded-full after:bg-t1 after:transition peer-checked:bg-brand peer-checked:after:translate-x-[18px] peer-checked:after:bg-on-brand peer-focus-visible:ring-2 peer-focus-visible:ring-brand" />
    </label>
  );
}

/** The settings of PhotoSense, each taking effect as it is changed. They are kept in this browser. */
export function SettingsWindow({ settings, onChange, onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-6" onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div role="dialog" aria-modal="true" aria-label="Settings" className="flex w-[560px] max-w-full flex-col gap-5 rounded-[20px] border border-line bg-pop p-7 shadow-dialog">
        <h2 className="text-[21px] font-semibold">Settings</h2>
        <div className="flex flex-col gap-3">
          <div className="label-caps">Deleting permanently</div>
          <Switch title="Ask a second time before deleting permanently" on={settings.eraseAskTwice} onChange={eraseAskTwice => onChange({ eraseAskTwice })}
            about="After the warning, Delete permanently asks “Are you sure?” once more. Turned off, the files are erased as soon as the warning is confirmed." />
        </div>
        <div className="flex items-center gap-3 pt-1">
          <p className="flex-1 text-[12.5px] text-t3">Settings are kept in this browser.</p>
          <button type="button" onClick={onClose} className="pill-brand h-11 px-6 text-[15px]">Done</button>
        </div>
      </div>
    </div>
  );
}
