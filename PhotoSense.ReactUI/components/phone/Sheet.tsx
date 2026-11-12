import React, { useEffect, useLayoutEffect, useRef } from 'react';
import { useBackLayer } from '../../lib/backStack';
import { usePhone } from '../../lib/phone';
import { Scrim } from '../Scrim';
import { PhoneButton } from './Controls';

interface SheetProps {
  readonly title: string;
  /** What the button that puts it away says: Done, or Close. */
  readonly done?: string;
  /** Leaves out the title and its button, for a question that has its own (`ConfirmSheet`). */
  readonly bare?: boolean;
  readonly children: React.ReactNode;
  onClose(): void;
}

/**
 * A sheet over the screen: on a phone rising from the bottom, the width of the screen, with a grabber; on a tablet a
 * card in the middle. Pressing outside it, Escape, Back or its own button puts it away.
 */
export function Sheet({ title, done = 'Done', bare, children, onClose }: SheetProps) {
  const { ios, tablet } = usePhone();
  const close = useRef(onClose);
  useLayoutEffect(() => { close.current = onClose; });
  useBackLayer(true, () => close.current());
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') close.current(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const place = tablet ? 'items-center p-10' : 'items-end';
  const shape = tablet
    ? 'w-[560px] max-w-full max-h-[calc(100%-40px)] rounded-[28px] pb-[14px]'
    : `w-full rounded-t-sheet ${ios ? 'max-h-[calc(100%-56px)] pb-[calc(var(--safe-b)+12px)]' : 'max-h-[calc(100%-24px)] pb-[calc(var(--safe-b)+16px)]'}`;
  return (
    <Scrim className={`fixed inset-0 z-[45] flex animate-fade-in justify-center bg-scrim ${place}`} onDismiss={() => close.current()}>
      <div role="dialog" aria-modal aria-label={title}
        className={`animate-sheet-in overflow-y-auto bg-sheet text-t1 shadow-[0_-8px_40px_rgba(0,0,0,.25)] ${shape}`}>
        {!tablet && (
          <div className="flex justify-center pt-2"><div className="h-[5px] w-[var(--grab-w)] rounded-[3px] bg-grab" /></div>
        )}
        {!bare && (
          <div className="flex items-center gap-2 pb-2 pl-5 pr-4 pt-3">
            <h2 className="min-w-0 flex-1 font-title text-[20px] font-bold">{title}</h2>
            <button type="button" onClick={() => close.current()} className="h-9 shrink-0 rounded-full bg-btn2-bg px-[14px] text-[15px] font-semibold text-btn2-fg">{done}</button>
          </div>
        )}
        {children}
      </div>
    </Scrim>
  );
}

interface ConfirmProps {
  readonly title: string;
  /** What will happen, a sentence a line: what goes, what stays, and how it comes back. */
  readonly lines: readonly React.ReactNode[];
  readonly confirmLabel: string;
  readonly busy?: boolean;
  /** What the question leads to: danger for anything that removes files. */
  readonly kind?: 'danger' | 'primary';
  readonly children?: React.ReactNode;
  onConfirm(): void;
  onCancel(): void;
}

/** A question in a sheet: its title, what will happen, the button that does it, and Cancel. */
export function ConfirmSheet({ title, lines, confirmLabel, busy, kind = 'danger', children, onConfirm, onCancel }: ConfirmProps) {
  return (
    <Sheet title={title} bare onClose={onCancel}>
      <div className="flex flex-col gap-2 px-5 pb-2 pt-4">
        <h2 className="font-title text-[21px] font-bold [overflow-wrap:anywhere]">{title}</h2>
        {lines.map((line, i) => <p key={i} className="text-[15px] leading-[1.45] text-t2 [overflow-wrap:anywhere]">{line}</p>)}
        {children}
      </div>
      <div className="flex flex-col gap-2 px-4 pt-3">
        <PhoneButton kind={kind} disabled={busy} onClick={onConfirm}>{confirmLabel}</PhoneButton>
        <PhoneButton kind="secondary" onClick={onCancel}>Cancel</PhoneButton>
      </div>
    </Sheet>
  );
}
