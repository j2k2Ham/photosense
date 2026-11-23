import React from 'react';

/** What a message is: something done, something to know, or an error. */
export type ToastKind = 'ok' | 'info' | 'error';
/** Something that can be done about what a toast says, such as taking it back. */
export interface ToastAction { label: string; run(): void; }
/** A message shown for a moment, with what can be done about it, if anything. */
export interface Toast { id: number; message: string; kind: ToastKind; action?: ToastAction; }
/** An error kept in the list of errors, with when it happened. */
export interface ErrorEntry { id: number; time: Date; message: string; }

/** How long a message that is not an error stays up; one that offers something to do stays longer. */
export const TOAST_MS = 4500;
/** How long a message that offers something to do stays up. */
export const ACTION_TOAST_MS = 9000;

/** How long a message that is not an error stays up on a phone, as its own apps keep theirs. */
export const PHONE_TOAST_MS = 5500;

/**
 * Messages for the person using the page. Every one shows as a toast; an error is also kept in a list
 * that stays until it is cleared, because a toast that has gone can no longer be read.
 * @param lasts How long a message that is not an error, and offers nothing to do, stays up.
 */
export function useToasts(lasts = TOAST_MS) {
  const [toasts, setToasts] = React.useState<Toast[]>([]);
  const [errors, setErrors] = React.useState<ErrorEntry[]>([]);
  const next = React.useRef(0);
  const short = React.useRef(lasts);
  React.useLayoutEffect(() => { short.current = lasts; });
  // A message's timer is let go when the page goes, so that it does not fire into a page that is no longer there.
  const timers = React.useRef(new Set<ReturnType<typeof setTimeout>>());
  React.useEffect(() => {
    const pending = timers.current;
    return () => { pending.forEach(clearTimeout); pending.clear(); };
  }, []);

  const remove = React.useCallback((id: number) => setToasts(t => t.filter(x => x.id !== id)), []);
  const push = React.useCallback((message: string, kind: ToastKind = 'info', action?: ToastAction) => {
    const id = ++next.current;
    setToasts(t => [...t, { id, message, kind, action }]);
    if (kind === 'error') { setErrors(e => [{ id, time: new Date(), message }, ...e]); return; }
    const timer = setTimeout(() => { timers.current.delete(timer); remove(id); }, action ? ACTION_TOAST_MS : short.current);
    timers.current.add(timer);
  }, [remove]);
  const clearError = React.useCallback((id: number) => setErrors(e => e.filter(x => x.id !== id)), []);
  const clearErrors = React.useCallback(() => setErrors([]), []);

  return { toasts, errors, push, remove, clearError, clearErrors };
}

const dot: Record<ToastKind, string> = { ok: 'bg-keep', info: 'bg-brand', error: 'bg-rose' };

interface ToasterProps { readonly toasts: Toast[]; readonly remove: (id: number) => void; }

/** The messages shown now, newest last, each read out by a screen reader as it is added. */
export function Toaster({ toasts, remove }: ToasterProps) {
  return (
    // On a phone the messages run the width of the screen, above its foot; on a wide screen they sit in the corner.
    // Always there, so that a screen reader reads out each message as it is added.
    <div role="log" aria-label="Messages" className="fixed inset-x-3 bottom-3 z-50 flex flex-col items-stretch gap-2.5 sm:inset-x-auto sm:bottom-6 sm:right-6 sm:items-end">
      {toasts.slice(-4).map(t => (
        <div key={t.id}
          className={`flex items-center gap-3 rounded-2xl bg-pop py-2.5 pl-4 pr-2.5 text-[14px] shadow-toast border sm:max-w-[540px] sm:rounded-full ${t.kind === 'error' ? 'border-rose-line' : 'border-line'}`}>
          <span aria-hidden className={`h-2 w-2 shrink-0 rounded-full ${dot[t.kind]}`} />
          <span className="min-w-0 flex-1 break-words">{t.kind === 'error' && <span className="sr-only">Error: </span>}{t.message}</span>
          {t.kind === 'error' && <span className="hidden shrink-0 text-[12.5px] text-t3 sm:inline">Kept in Errors</span>}
          {t.action && <button type="button" onClick={() => { remove(t.id); t.action!.run(); }} className="pill-outline h-7 shrink-0 px-3 text-[13px]">{t.action.label}</button>}
          <button type="button" aria-label="Dismiss" onClick={() => remove(t.id)} className="pill-quiet h-7 w-7 shrink-0 text-[15px] leading-none">×</button>
        </div>
      ))}
    </div>
  );
}
