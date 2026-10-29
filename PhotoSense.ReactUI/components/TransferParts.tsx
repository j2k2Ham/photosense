import React, { useEffect } from 'react';
import { Scrim } from './Scrim';
import { formatBytes } from '../lib/format';

export const messageOf = (e: unknown) => (e instanceof TypeError ? 'Cannot reach the PhotoSense server.' : e instanceof Error ? e.message : String(e));

export const Named = ({ children }: { readonly children: string }) => <span className="font-semibold text-t1 [overflow-wrap:anywhere]">{children}</span>;
export const Where = ({ children }: { readonly children: string }) => <span className="font-mono text-[13.5px] [overflow-wrap:anywhere]">{children}</span>;

interface WindowProps {
  readonly title: string;
  /** A question or a folder picker is up over it: its keys are that one's. */
  readonly frozen: boolean;
  readonly children: React.ReactNode;
  onClose(): void;
}

/** The window sending and receiving photos happen in. */
export function TransferWindow({ title, frozen, children, onClose }: WindowProps) {
  useEffect(() => {
    if (frozen) return;
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [frozen, onClose]);

  return (
    <Scrim className="fixed inset-0 z-40 flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={onClose}>
      <div role="dialog" aria-modal="true" aria-label={title} className="flex max-h-full w-[520px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">{title}</h2>
        {children}
      </div>
    </Scrim>
  );
}

/** The buttons along the foot of the window. */
export const Buttons = ({ children }: { readonly children: React.ReactNode }) => <div className="flex flex-wrap justify-end gap-2.5 pt-1">{children}</div>;

interface CompareProps {
  readonly code: string;
  /** The other device, by the name it goes by. */
  readonly other: string;
  readonly busy: boolean;
  onAnswer(same: boolean): void;
}

/** The number both devices show the first time they connect, and the person's answer to whether it is the same on both. */
export function Compare({ code, other, busy, onAnswer }: CompareProps) {
  return (
    <>
      <p className="text-[15px] text-t2">Does <Named>{other}</Named> show this number?</p>
      <p className="rounded-2xl bg-s2 py-5 text-center font-mono text-[40px] font-semibold tracking-[0.12em]">{code}</p>
      <p className="text-[13.5px] text-t2">This is asked the first time only. If the numbers are not the same, something else may be trying to connect, so choose They don’t match.</p>
      <Buttons>
        <button type="button" disabled={busy} onClick={() => onAnswer(false)} className="pill-outline h-11 px-5 text-[15px]">They don’t match</button>
        <button type="button" disabled={busy} onClick={() => onAnswer(true)} className="pill-brand h-11 px-5 text-[15px]">They match</button>
      </Buttons>
    </>
  );
}

interface ProgressProps {
  /** What is going on, for a screen reader: "Sending to DESKTOP-1". */
  readonly label: string;
  readonly done: number;
  readonly total: number;
  readonly bytes: number;
  readonly totalBytes: number;
}

/** How far the files have got, by their size, and how many of them are through. */
export function Progress({ label, done, total, bytes, totalBytes }: ProgressProps) {
  const percent = totalBytes > 0 ? Math.min(100, Math.round((bytes * 100) / totalBytes)) : 0;
  return (
    <div className="flex flex-col gap-2">
      <div role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={100} aria-valuenow={percent} className="h-2 overflow-hidden rounded-full bg-s3">
        <div className="h-full rounded-full bg-brand transition-[width]" style={{ width: `${percent}%` }} />
      </div>
      <p className="text-[13.5px] text-t2">{done.toLocaleString()} of {total.toLocaleString()} {total === 1 ? 'file' : 'files'} · {formatBytes(bytes)} of {formatBytes(totalBytes)}</p>
    </div>
  );
}
