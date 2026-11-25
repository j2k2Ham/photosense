import React, { useEffect, useRef } from 'react';
import { Scrim } from './Scrim';

interface Props {
  /** Keeps the person's answer: true to continue where they left off, false to start fresh each time. */
  onAnswer(carryOn: boolean): void;
  /** Puts the question away unanswered, so that it is asked again after the next scan. */
  onClose(): void;
}

/**
 * Asked once, after the first scan on a computer, until answered: whether Trovolve opens next time where the person
 * left off (Clean up's results, Organize's folder) or fresh, the results forgotten and nothing chosen.
 */
export function ContinueQuestion({ onAnswer, onClose }: Props) {
  const carryOn = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    carryOn.current?.focus();
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <Scrim className="fixed inset-0 z-[45] flex items-center justify-center bg-scrim p-3 sm:p-6" onDismiss={onClose}>
      <div role="alertdialog" aria-modal="true" aria-label="Continue where you left off?" className="flex max-h-full w-[480px] max-w-full flex-col gap-4 overflow-y-auto rounded-[20px] border border-line bg-pop p-5 shadow-dialog sm:p-7">
        <h2 className="text-[21px] font-semibold">Continue where you left off?</h2>
        <div className="space-y-1.5 text-[15px] text-t2">
          <p>Next time Trovolve opens, it can show your last results in Clean up and your last folder in Organize.</p>
          <p>Or it can start fresh each time: the last results forgotten, and no folder chosen. Your photos are never touched.</p>
        </div>
        <div className="flex flex-wrap justify-end gap-2.5 pt-1">
          <button type="button" onClick={() => onAnswer(false)} className="pill-outline h-11 px-5 text-[15px]">Start fresh each time</button>
          <button ref={carryOn} type="button" onClick={() => onAnswer(true)} className="pill-brand h-11 px-5 text-[15px]">Continue where I left off</button>
        </div>
      </div>
    </Scrim>
  );
}
