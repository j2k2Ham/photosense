import React, { useEffect } from 'react';

interface Props {
  readonly title: string;
  readonly confirmLabel: string;
  readonly busy: boolean;
  /** Rose for something that removes files, brand for anything else. */
  readonly tone?: 'rose' | 'brand';
  /** Where the files go, or what is left untouched: shown in a box under the body. */
  readonly note?: React.ReactNode;
  readonly children: React.ReactNode;
  onConfirm(): void;
  onCancel(): void;
}

export function ConfirmDialog({ title, confirmLabel, busy, tone = 'rose', note, children, onConfirm, onCancel }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onCancel]);

  return (
    <div className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-6" onMouseDown={e => { if (e.target === e.currentTarget && !busy) onCancel(); }}>
      <div role="alertdialog" aria-modal="true" aria-label={title} className="flex w-[520px] max-w-full flex-col gap-4 rounded-[20px] border border-line bg-pop p-7 shadow-dialog">
        <h2 className="text-[21px] font-semibold">{title}</h2>
        <div className="space-y-1.5 text-[15px] text-t2">{children}</div>
        {note && <div className="rounded-xl bg-s2 px-4 py-3 text-[13.5px] text-t2">{note}</div>}
        <div className="flex justify-end gap-2.5 pt-1">
          <button type="button" disabled={busy} onClick={onCancel} className="pill-outline h-11 px-5 text-[15px]">Cancel</button>
          <button type="button" disabled={busy} onClick={onConfirm} className={`${tone === 'rose' ? 'pill-rose' : 'pill-brand'} h-11 px-5 text-[15px]`}>{busy ? 'Working…' : confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
