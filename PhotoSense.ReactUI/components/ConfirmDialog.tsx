import React, { useEffect } from 'react';

interface Props {
  readonly title: string;
  readonly confirmLabel: string;
  readonly busy: boolean;
  /** Rose for something that removes files, brand for anything else. */
  readonly tone?: 'rose' | 'brand';
  /** A small one, for a question asked on top of another that was just answered. */
  readonly small?: boolean;
  /** There is something to say yes to. While there is not, as when what it is about is still being looked up, it can only be cancelled. */
  readonly ready?: boolean;
  /** Where the files go, or what is left untouched: shown in a box under the body. */
  readonly note?: React.ReactNode;
  readonly children: React.ReactNode;
  onConfirm(): void;
  onCancel(): void;
}

export function ConfirmDialog({ title, confirmLabel, busy, tone = 'rose', small = false, ready = true, note, children, onConfirm, onCancel }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onCancel]);

  return (
    <div className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-6" onMouseDown={e => { if (e.target === e.currentTarget && !busy) onCancel(); }}>
      <div role="alertdialog" aria-modal="true" aria-label={title} className={`flex max-w-full flex-col gap-4 rounded-[20px] border border-line bg-pop shadow-dialog ${small ? 'w-[400px] p-6' : 'w-[520px] p-7'}`}>
        <h2 className={`font-semibold ${small ? 'text-[18px]' : 'text-[21px]'}`}>{title}</h2>
        <div className="space-y-1.5 text-[15px] text-t2">{children}</div>
        {note && <div className="rounded-xl bg-s2 px-4 py-3 text-[13.5px] text-t2">{note}</div>}
        <div className="flex justify-end gap-2.5 pt-1">
          <button type="button" disabled={busy} onClick={onCancel} className="pill-outline h-11 px-5 text-[15px]">Cancel</button>
          <button type="button" disabled={busy || !ready} onClick={onConfirm} className={`${tone === 'rose' ? 'pill-rose' : 'pill-brand'} h-11 px-5 text-[15px]`}>{busy ? 'Working…' : confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
