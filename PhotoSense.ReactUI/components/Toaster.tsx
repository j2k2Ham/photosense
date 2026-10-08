import React from 'react';

export type ToastKind = 'ok' | 'info' | 'error';
export interface Toast { id: number; message: string; kind: ToastKind; }
export interface ErrorEntry { id: number; time: Date; message: string; }

/** How long a message that is not an error stays up. */
export const TOAST_MS = 4500;

/**
 * Messages for the person using the page. Every one shows as a toast; an error is also kept in a list
 * that stays until it is cleared, because a toast that has gone can no longer be read.
 */
export function useToasts() {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const [errors, setErrors] = React.useState<ErrorEntry[]>([]);
  const next = React.useRef(0);

  const remove = React.useCallback((id: number) => setToasts(t => t.filter(x => x.id !== id)), []);
  const push = React.useCallback((message: string, kind: ToastKind = 'info') => {
    const id = ++next.current;
    setToasts(t => [...t, { id, message, kind }]);
    if (kind === 'error') setErrors(e => [{ id, time: new Date(), message }, ...e]);
    else setTimeout(() => remove(id), TOAST_MS);
  }, [remove]);
  const clearError = React.useCallback((id: number) => setErrors(e => e.filter(x => x.id !== id)), []);
  const clearErrors = React.useCallback(() => setErrors([]), []);

  return { toasts, errors, push, remove, clearError, clearErrors };
}

const dot: Record<ToastKind, string> = { ok: 'bg-keep', info: 'bg-brand', error: 'bg-rose' };

interface ToasterProps { readonly toasts: Toast[]; readonly remove: (id: number) => void; }

export function Toaster({ toasts, remove }: ToasterProps) {
  return (
    <div className="fixed bottom-6 right-6 z-50 flex flex-col items-end gap-2.5">
      {toasts.slice(-4).map(t => (
        <div key={t.id} role={t.kind === 'error' ? 'alert' : 'status'}
          className={`flex items-center gap-3 max-w-[540px] rounded-full bg-pop py-2.5 pl-4 pr-2.5 text-[14px] shadow-toast border ${t.kind === 'error' ? 'border-rose-line' : 'border-line'}`}>
          <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${dot[t.kind]}`} />
          <span className="min-w-0 break-words">{t.message}</span>
          {t.kind === 'error' && <span className="shrink-0 text-[12.5px] text-t3">Kept in Errors</span>}
          <button type="button" aria-label="Dismiss" onClick={() => remove(t.id)} className="pill-quiet h-7 w-7 shrink-0 text-[15px] leading-none">×</button>
        </div>
      ))}
    </div>
  );
}
