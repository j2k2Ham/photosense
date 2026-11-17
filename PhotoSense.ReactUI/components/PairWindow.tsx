import React, { useEffect, useLayoutEffect, useRef } from 'react';
import { closePairing, confirmPairing, openPairing, usePairing } from '../lib/apiClient';
import { qrModules, qrPath } from '../lib/qr';
import { Scrim } from './Scrim';
import type { ToastKind } from './Toaster';

interface Props {
  notify(message: string, kind: ToastKind): void;
  onClose(): void;
}

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** A QR code drawn black on white with its quiet margin, as a camera needs it whatever the theme. */
function QrCode({ text }: { readonly text: string }) {
  const modules = qrModules(text);
  const size = modules.length;
  return (
    <svg role="img" aria-label="QR code for the phone to scan" viewBox={`-2 -2 ${size + 4} ${size + 4}`} shapeRendering="crispEdges"
      className="h-[220px] w-[220px] shrink-0 rounded-[14px] bg-white">
      <path d={qrPath(modules)} fill="#000" />
    </svg>
  );
}

/**
 * Pair a phone, on the computer: the QR code for the phone to scan, and the code to type instead, both changing every
 * five minutes while the computer waits; then the number to compare once a phone typed the code, and what came of it.
 * Closing it stops the waiting.
 */
export function PairWindow({ notify, onClose }: Props) {
  const state = usePairing(true).data;
  // Closing stops the waiting, whichever way it is closed.
  const close = useRef(onClose);
  useLayoutEffect(() => { close.current = () => { void closePairing().catch(() => undefined); onClose(); }; });

  useEffect(() => {
    openPairing().catch(e => notify(`Pair a phone could not start: ${said(e)}`, 'error'));
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close.current(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [notify]);

  const answer = (same: boolean) => confirmPairing(same).catch(e => notify(said(e), 'error'));
  const code = state?.code ? `${state.code.slice(0, 3)} ${state.code.slice(3)}` : '';

  let body: React.ReactNode;
  if (state?.stage === 'comparing') {
    body = (
      <div className="flex flex-col gap-4">
        <p className="text-[15px] text-t2">Does {state.other} show this number? Pair only if it does.</p>
        <p aria-label="The number" className="font-mono text-[34px] font-semibold tracking-[.06em] text-t1">{state.check}</p>
        <div className="flex justify-end gap-2.5">
          <button type="button" onClick={() => void answer(false)} className="pill-outline h-11 px-5 text-[15px]">{"It's different"}</button>
          <button type="button" onClick={() => void answer(true)} className="pill-brand h-11 px-5 text-[15px]">{"It's the same number"}</button>
        </div>
      </div>
    );
  } else if (state?.stage === 'paired' || state?.stage === 'failed') {
    const paired = state.stage === 'paired';
    body = (
      <div className="flex flex-col gap-4">
        <p role={paired ? 'status' : 'alert'} className="text-[15px] text-t1">{paired ? `${state.other} is paired with this computer.` : state.problem}</p>
        <div className="flex justify-end gap-2.5">
          {!paired && <button type="button" onClick={() => void openPairing().catch(e => notify(said(e), 'error'))} className="pill-outline h-11 px-5 text-[15px]">Try again</button>}
          <button type="button" onClick={() => close.current()} className="pill-brand h-11 px-5 text-[15px]">Done</button>
        </div>
      </div>
    );
  } else {
    body = (
      <>
        <div className="flex flex-col gap-6 sm:flex-row sm:items-center">
          {state?.link ? <QrCode text={state.link} /> : <div aria-hidden className="h-[220px] w-[220px] shrink-0 rounded-[14px] bg-s2" />}
          <div className="flex min-w-0 flex-1 flex-col gap-4">
            <ol className="flex flex-col gap-3">
              {['Install PhotoSense from the App Store or Google Play.', 'On the phone, choose Connect your computer.', 'Point the phone at this code.'].map((t, i) => (
                <li key={t} className="flex gap-3 text-[14.5px] text-t1"><span className="w-4 shrink-0 font-semibold text-t2">{i + 1}</span>{t}</li>
              ))}
            </ol>
            <div className="flex items-center justify-between gap-4 rounded-xl bg-s2 px-4 py-3">
              <span className="text-[13.5px] text-t2">Or enter this code on the phone</span>
              <span aria-label="Code to type on the phone" className="shrink-0 whitespace-nowrap font-mono text-[20px] font-semibold tracking-[.12em] text-t1">{code || '…'}</span>
            </div>
          </div>
        </div>
        <p className="text-[13px] text-t2">The phone and this computer need to be on the same Wi-Fi. The code changes every 5 minutes.</p>
      </>
    );
  }

  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={() => close.current()}>
      <div role="dialog" aria-modal="true" aria-label="Pair a phone" className="flex max-h-full w-[640px] max-w-full flex-col gap-5 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <div className="flex items-center justify-between">
          <h2 className="text-[19px] font-semibold">Pair a phone</h2>
          <button type="button" onClick={() => close.current()} aria-label="Close" className="pill-quiet h-[34px] w-[34px] text-[17px] leading-none">×</button>
        </div>
        {body}
      </div>
    </Scrim>
  );
}
