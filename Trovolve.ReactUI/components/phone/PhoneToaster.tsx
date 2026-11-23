import React from 'react';
import { usePhone } from '../../lib/phone';
import type { Toast } from '../Toaster';

interface Props {
  readonly toasts: Toast[];
  remove(id: number): void;
}

/**
 * The newest message, at the foot of the screen above the tab bar: a glass capsule on an iPhone, a snackbar on Android.
 * The region is always there, so that a screen reader reads out each message as it comes. An error stays until put away,
 * and stays listed under Errors in Settings.
 */
export function PhoneToaster({ toasts, remove }: Props) {
  const { ios, tablet, foot } = usePhone();
  const toast = toasts.at(-1);
  const bottom = tablet ? 'bottom-7' : foot === 'none' ? 'bottom-[calc(var(--safe-b)+112px)]' : ios ? 'bottom-[98px]' : 'bottom-[92px]';
  return (
    <div role="log" aria-label="Messages" className={`pointer-events-none fixed inset-x-3 z-[44] mx-auto max-w-[440px] ${bottom}`}>
      {toast && (
        <div key={toast.id}
          className={`pointer-events-auto flex min-h-[50px] animate-toast-in items-center gap-2 rounded-toast bg-toast py-2 pl-4 pr-1.5 text-on-toast shadow-[0_8px_24px_rgba(0,0,0,.25)] ${ios ? 'backdrop-blur-[20px]' : ''}`}>
          <span className="min-w-0 flex-1 text-[14px] leading-[1.35] [overflow-wrap:anywhere]">{toast.kind === 'error' && <span className="sr-only">Error: </span>}{toast.message}</span>
          {toast.action && (
            <button type="button" onClick={() => { remove(toast.id); toast.action!.run(); }} className="shrink-0 whitespace-nowrap px-3 py-2 text-[14.5px] font-semibold text-toast-act">{toast.action.label}</button>
          )}
          {toast.kind === 'error' && (
            <button type="button" onClick={() => remove(toast.id)} className="shrink-0 whitespace-nowrap px-3 py-2 text-[14.5px] font-semibold text-toast-act">OK</button>
          )}
        </div>
      )}
    </div>
  );
}
