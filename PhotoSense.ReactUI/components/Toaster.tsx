import React from 'react';

export type ToastKind = 'ok' | 'info' | 'error';
/** Something that can be done about what a toast says, such as taking it back. */
export interface ToastAction { label: string; run(): void; }
export interface Toast { id: number; message: string; kind: ToastKind; action?: ToastAction; }
export interface ErrorEntry { id: number; time: Date; message: string; }

/** How long a message that is not an error stays up; one that offers something to do stays longer. */
export const TOAST_MS = 4500;
export const ACTION_TOAST_MS = 9000;

/**
 * Messages for the person using the page. Every one shows as a toast; an error is also kept in a list
 * that stays until it is cleared, because a toast that has gone can no longer be read.
 */
export function useToasts() {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const [errors, setErrors] = React.useState<ErrorEntry[]>([]);
  const next = React.useRef(0);

  const remove = React.useCallback((id: number) => setToasts(t => t.filter(x => x.id !== id)), []);
  const push = React.useCallback((message: string, kind: ToastKind = 'info', action?: ToastAction) => {
    const id = ++next.current;
    setToasts(t => [...t, { id, message, kind, action }]);
    if (kind === 'error') setErrors(e => [{ id, time: new Date(), message }, ...e]);
    else setTimeout(() => remove(id), action ? ACTION_TOAST_MS : TOAST_MS);
  }, [remove]);
  const clearError = React.useCallback((id: number) => setErrors(e => e.filter(x => x.id !== id)), []);
  const clearErrors = React.useCallback(() => setErrors([]), []);

  return { toasts, errors, push, remove, clearError, clearErrors };
}

const dot: Record<ToastKind, string> = { ok: 'bg-keep', info: 'bg-brand', error: 'bg-rose' };

interface ToasterProps { readonly toasts: Toast[]; readonly remove: (id: number) => void; }

export function Toaster({ toasts, remove }: ToasterProps) {
  return (
    // On a phone the messages run the width of the screen, above its foot; on a wide screen they sit in the corner.
    <div className="fixed inset-x-3 bottom-3 z-50 flex flex-col items-stretch gap-2.5 sm:inset-x-auto sm:bottom-6 sm:right-6 sm:items-end">
      {toasts.slice(-4).map(t => (
        <div key={t.id} role={t.kind === 'error' ? 'alert' : 'status'}
          className={`flex items-center gap-3 rounded-2xl bg-pop py-2.5 pl-4 pr-2.5 text-[14px] shadow-toast border sm:max-w-[540px] sm:rounded-full ${t.kind === 'error' ? 'border-rose-line' : 'border-line'}`}>
          <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${dot[t.kind]}`} />
          <span className="min-w-0 flex-1 break-words">{t.message}</span>
          {t.kind === 'error' && <span className="hidden shrink-0 text-[12.5px] text-t3 sm:inline">Kept in Errors</span>}
          {t.action && <button type="button" onClick={() => { remove(t.id); t.action!.run(); }} className="pill-outline h-7 shrink-0 px-3 text-[13px]">{t.action.label}</button>}
          <button type="button" aria-label="Dismiss" onClick={() => remove(t.id)} className="pill-quiet h-7 w-7 shrink-0 text-[15px] leading-none">×</button>
        </div>
      ))}
    </div>
  );
}
