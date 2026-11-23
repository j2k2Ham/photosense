import React, { useEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { appIconUrl, shareImage } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { deviceWord, usePhone } from '../../lib/phone';
import { CARD_COLOURS, cardPng, drawYearSlide, yearLine, yearSlides, type YearSlide } from '../../lib/shareCard';
import type { YearReviewDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { CloseIcon, ShareIcon } from './Icons';

type Notify = (message: string, kind: ToastKind) => void;

/** 41: the card in Library that opens Year in review, in December and January. */
export function YearCard({ review, onWatch, inRow }: { readonly review: YearReviewDto; onWatch(): void; /** In a tablet's row of cards, Watch under the words. */ readonly inRow?: boolean }) {
  return (
    <section aria-label="Year in review" className={`flex gap-3 rounded-[20px] px-4 py-4 ${inRow ? 'min-w-0 flex-1 flex-col items-start' : 'mx-4 mb-3 items-center'}`} style={{ background: 'oklch(0.28 0.05 205)', color: CARD_COLOURS.text }}>
      <div className="min-w-0 flex-1">
        <p className="text-[12.5px] font-semibold" style={{ color: CARD_COLOURS.line }}>Year in review</p>
        <p className="text-[18px] font-bold">Your {review.year} in photos</p>
        <p className="text-[13px] opacity-85">{yearLine(review)}</p>
      </div>
      <button type="button" onClick={onWatch} className="h-9 shrink-0 rounded-full bg-white px-4 text-[14.5px] font-semibold text-black">Watch</button>
    </section>
  );
}

/** One card's content, as the screen shows it. */
function Slide({ slide }: { readonly slide: YearSlide }) {
  return (
    <div className="flex flex-col gap-3">
      <p className="text-[15px] font-bold" style={{ color: CARD_COLOURS.line }}>{slide.kicker}</p>
      <h2 className="font-title text-[56px] font-bold leading-none tracking-tight">{slide.big}</h2>
      {slide.line && <p className="text-[17px] leading-[1.35]">{slide.line}</p>}
      {slide.rows && slide.rows.length > 0 && (
        <dl className="mt-2 flex flex-col gap-2">
          {slide.rows.map(([figure, what]) => (
            <div key={figure + what} className="flex items-baseline gap-3">
              <dt className="min-w-[96px] text-[22px] font-bold tabular-nums">{figure}</dt>
              {what && <dd className="text-[15px]">{what}</dd>}
            </div>
          ))}
        </dl>
      )}
      {slide.months && (
        <div aria-label="Photos by month" role="img" className="mt-4 flex h-[110px] items-end gap-1.5">
          {slide.months.counts.map((n, i) => {
            const most = Math.max(1, ...slide.months!.counts);
            return (
              <div key={i} className="flex flex-1 flex-col items-center gap-1">
                <span className="w-full rounded-[4px]" style={{ height: `${Math.max(6, (n / most) * 90)}px`, background: i + 1 === slide.months!.busiest ? CARD_COLOURS.line : 'rgba(255,255,255,.3)' }} />
                <span className="text-[10px] font-semibold">{'JFMAMJJASOND'[i]}</span>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}

/**
 * 42–44: Year in review, five cards one after another, over everything. A tap on the right 70% goes on, on the left
 * 30% goes back; the arrow keys too, and Escape or the cross closes. Each card's Share hands it, drawn as a story, to
 * the phone's share sheet. No photo is on any card.
 */
export function YearStories({ review, notify, onClose }: { readonly review: YearReviewDto; notify: Notify; onClose(): void }) {
  const { platform, tablet } = usePhone();
  const slides = yearSlides(review, deviceWord(platform, tablet));
  const [at, setAt] = useState(0);
  const [busy, setBusy] = useState(false);
  const close = useRef(onClose);
  useEffect(() => { close.current = onClose; });
  useBackLayer(true, () => close.current());
  const go = (by: number) => setAt(i => Math.min(slides.length - 1, Math.max(0, i + by)));
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') close.current();
      else if (e.key === 'ArrowRight') setAt(i => Math.min(slides.length - 1, i + 1));
      else if (e.key === 'ArrowLeft') setAt(i => Math.max(0, i - 1));
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [slides.length]);
  const slide = slides[at];

  async function share() {
    setBusy(true);
    try {
      await shareImage(await cardPng('story', (pen, icon) => drawYearSlide(pen, slide, icon), appIconUrl), `PhotoSense ${review.year}.png`);
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    } finally {
      setBusy(false);
    }
  }

  return createPortal(
    <section role="dialog" aria-modal aria-label="Year in review" className="fixed inset-0 z-40 flex animate-fade-in flex-col font-ui"
      style={{ background: slide.background, color: CARD_COLOURS.text }}>
      <div className="mx-auto flex min-h-0 w-full max-w-[480px] flex-1 flex-col px-4 pb-[calc(var(--safe-b)+16px)] pt-[calc(var(--safe-t)+8px)]">
        <div role="progressbar" aria-label="Cards" aria-valuemin={1} aria-valuemax={slides.length} aria-valuenow={at + 1} className="flex gap-1">
          {slides.map((s, i) => <span key={s.kicker} className={`h-[3px] flex-1 rounded-full ${i <= at ? 'bg-white' : 'bg-white/30'}`} />)}
        </div>
        <div className="flex items-center gap-2 pt-4">
          <img src={appIconUrl} alt="" className="h-7 w-7 rounded-[7px]" />
          <span className="flex-1 text-[14px] font-bold">PhotoSense</span>
          <button type="button" aria-label="Close" onClick={() => close.current()} className="grid h-9 w-9 place-items-center rounded-full"><CloseIcon size={20} /></button>
        </div>
        <div className="relative flex min-h-0 flex-1 flex-col justify-center">
          <Slide slide={slide} />
          {/* Taps beside the card's text: back on the left 30%, on on the rest. */}
          <button type="button" aria-label="Previous card" disabled={at === 0} onClick={() => go(-1)} className="absolute inset-y-0 left-0 w-[30%] opacity-0" />
          <button type="button" aria-label="Next card" disabled={at === slides.length - 1} onClick={() => go(1)} className="absolute inset-y-0 right-0 w-[70%] opacity-0" />
        </div>
        <div className="flex flex-col items-center gap-2">
          <button type="button" disabled={busy} onClick={() => void share()} className="flex h-11 items-center gap-2 rounded-full bg-white px-6 text-[15px] font-semibold text-black disabled:opacity-60">
            <ShareIcon size={18} />Share
          </button>
          <p className="text-[12.5px] font-semibold">No photos are on these cards.</p>
        </div>
      </div>
    </section>,
    document.body,
  );
}
