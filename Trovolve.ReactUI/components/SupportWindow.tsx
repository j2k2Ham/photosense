import React, { useEffect } from 'react';
import { DocumentTopic } from './AboutWindow';
import { useAbout } from '../lib/apiClient';
import { supportDetails, supportMailto } from '../lib/support';
import type { ToastKind } from './Toaster';
import { WindowShell } from './WindowShell';

interface Props {
  /** The errors of this session, oldest first, as the Errors list has them. */
  readonly errors: readonly { readonly message: string }[];
  /** Opens the guide. */
  onHelp(): void;
  /** Opens the list of errors. */
  onErrors(): void;
  /** Says what happened, as a message like every other. */
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

/**
 * Support: the ways to get help (the guide, the errors this session, an email to support with Trovolve's version and
 * the system already in it), beside the support document's common questions.
 */
export function SupportWindow({ errors, onHelp, onErrors, notify, onClose }: Props) {
  const about = useAbout().data;
  const details = supportDetails(about, navigator.userAgent, errors.map(e => e.message));

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(details);
      notify('Details copied.', 'ok');
    } catch {
      // A page without the clipboard (or refused it) still shows the details, to be selected and copied by hand.
      notify('Trovolve could not copy the details. Select them above and copy them.', 'info');
    }
  };

  const card = 'flex flex-col gap-3 rounded-2xl border border-line bg-bg p-4';
  return (
    <WindowShell label="Support" columns="lg:grid-cols-[360px_minmax(0,1fr)]" onClose={onClose}
      header={<span className="whitespace-nowrap text-[18px] font-semibold">Support</span>}>
      <aside aria-label="Get help" className="flex min-h-0 flex-col gap-4 overflow-y-auto border-line bg-s1 p-4 max-lg:border-b lg:border-r">
        <section aria-label="The guide" className={card}>
          <p className="text-[14px] text-t2">The guide explains every screen, and its search finds a feature by the words you would use.</p>
          <button type="button" onClick={onHelp} className="pill-outline h-10 text-[14px]">Read the guide</button>
        </section>
        <section aria-label="Errors" className={card}>
          <p className="text-[14px] text-t2">{errors.length === 0 ? 'No errors this session.' : `${errors.length.toLocaleString()} ${errors.length === 1 ? 'error' : 'errors'} this session.`}</p>
          <button type="button" onClick={onErrors} className="pill-outline h-10 text-[14px]">See errors</button>
        </section>
        {about?.supportEmail && (
          <section aria-label="Write to support" className={card}>
            <p className="text-[14px] text-t2">
              Write to <span className="font-medium text-t1 [overflow-wrap:anywhere]">{about.supportEmail}</span>. The email starts with these details, and asks what you did, what you expected and what happened instead. Please send no photos unless asked.
            </p>
            <pre aria-label="Details for support" className="whitespace-pre-wrap break-words rounded-xl bg-s2 p-3 font-mono text-[12.5px] text-t1">{details}</pre>
            <a href={supportMailto(about.supportEmail, details)} className="pill-brand h-10 text-[14px]">Email support</a>
            <button type="button" onClick={() => void copy()} className="pill-quiet h-9 text-[13.5px]">Copy details</button>
          </section>
        )}
      </aside>
      {/* A long document scrolls on its own, so the keyboard must be able to reach it to scroll it. */}
      {/* eslint-disable-next-line jsx-a11y/no-noninteractive-tabindex */}
      <article tabIndex={0} aria-label="Common questions" className="min-h-0 overflow-y-auto bg-bg px-5 py-6 lg:px-10 lg:py-9">
        <div className="mx-auto flex max-w-[760px] flex-col gap-6">
          <h2 className="text-[24px] font-semibold leading-tight tracking-[-0.01em] lg:text-[30px]">Common questions</h2>
          <DocumentTopic document="support" />
        </div>
      </article>
    </WindowShell>
  );
}
