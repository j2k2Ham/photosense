import React, { useEffect } from 'react';

interface Props {
  readonly title: string;
  readonly confirmLabel: string;
  readonly busy: boolean;
  readonly children: React.ReactNode;
  onConfirm(): void;
  onCancel(): void;
}

export function ConfirmDialog({ title, confirmLabel, busy, children, onConfirm, onCancel }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape' && !busy) onCancel(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [busy, onCancel]);

  return (
    <div className="fixed inset-0 z-40 flex items-center justify-center bg-black/70 p-6">
      <div role="alertdialog" aria-modal="true" aria-label={title} className="panel bg-neutral-800 shadow-2xl w-full max-w-md p-5 flex flex-col gap-4">
        <h2 className="text-base font-semibold">{title}</h2>
        <div className="text-sm text-neutral-300 space-y-2">{children}</div>
        <div className="flex justify-end gap-2">
          <button type="button" disabled={busy} onClick={onCancel} className="btn-secondary">Cancel</button>
          <button type="button" disabled={busy} onClick={onConfirm} className="btn-danger">{busy ? 'Working…' : confirmLabel}</button>
        </div>
      </div>
    </div>
  );
}
