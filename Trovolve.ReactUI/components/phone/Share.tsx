import React, { useState } from 'react';
import { createPortal } from 'react-dom';
import { appIconUrl, shareImage } from '../../lib/apiClient';
import { CARD_COLOURS, cardPills, cardPng, drawResultCard, type CardFormat, type ResultCard } from '../../lib/shareCard';
import type { ToastKind } from '../Toaster';
import { PhoneButton, PhoneSwitch, Segmented } from './Controls';
import { ShareIcon } from './Icons';
import { Sheet } from './Sheet';

type Notify = (message: string, kind: ToastKind) => void;

const FORMATS: readonly { readonly id: CardFormat; readonly label: string }[] = [{ id: 'square', label: 'Square' }, { id: 'story', label: 'Story' }];

/** The share card as the sheet shows it: Trovolve's mark, the amount freed and on what, and the pills when shown. */
export function ResultCardView({ card, format, withPills }: { readonly card: ResultCard; readonly format: CardFormat; readonly withPills: boolean }) {
  return (
    <figure aria-label="Share card" className={`mx-auto flex flex-col justify-center gap-2 rounded-[18px] p-5 ${format === 'square' ? 'aspect-square w-[240px]' : 'aspect-[9/16] w-[190px]'}`}
      style={{ background: CARD_COLOURS.background, color: CARD_COLOURS.text }}>
      <span className="flex items-center gap-2 text-[13px] font-bold">
        <img src={appIconUrl} alt="" className="h-6 w-6 rounded-[6px]" />Trovolve
      </span>
      <span className="mt-auto font-title text-[46px] font-bold leading-none tracking-tight">{card.amount}</span>
      <span className="text-[13px] font-medium" style={{ color: CARD_COLOURS.line }}>freed on my {card.device}</span>
      <span className={`flex flex-col items-start gap-1.5 ${format === 'story' ? 'mb-auto' : ''}`}>
        {withPills && cardPills(card).map(p => <span key={p} className="rounded-full px-2.5 py-1 text-[11px] font-semibold" style={{ background: CARD_COLOURS.pill }}>{p}</span>)}
      </span>
    </figure>
  );
}

/**
 * 25: Share your result. The card, square or as tall as a story, with or without the streak and how many items; Share
 * draws it and hands it to the phone's share sheet. None of the person's photos is on it.
 */
export function ShareSheet({ card, notify, onClose }: { readonly card: ResultCard; notify: Notify; onClose(): void }) {
  const [format, setFormat] = useState<CardFormat>('square');
  const [withPills, setWithPills] = useState(true);
  const [busy, setBusy] = useState(false);
  async function share() {
    setBusy(true);
    try {
      await shareImage(await cardPng(format, (pen, icon) => drawResultCard(pen, card, format, withPills, icon), appIconUrl), 'Trovolve.png');
      onClose();
    } catch (e) {
      notify(e instanceof Error ? e.message : String(e), 'error');
    } finally {
      setBusy(false);
    }
  }
  return createPortal(
    <Sheet title="Share your result" bare onClose={onClose}>
      <div className="flex flex-col gap-4 px-5 pb-2 pt-3">
        <div>
          <h2 className="font-title text-[20px] font-bold">Share your result</h2>
          <p className="text-[14px] text-t2">None of your photos are on the card.</p>
        </div>
        <Segmented<CardFormat> label="Format" options={FORMATS} value={format} onChange={setFormat} />
        <div className="py-2"><ResultCardView card={card} format={format} withPills={withPills} /></div>
        <PhoneSwitch label="Show streak and item count" on={withPills} onChange={setWithPills} />
        <PhoneButton disabled={busy} onClick={() => void share()}><span className="flex items-center justify-center gap-2"><ShareIcon size={18} />Share</span></PhoneButton>
        <button type="button" onClick={onClose} className="py-1.5 text-center text-[15px] font-semibold text-acc">Not now</button>
      </div>
    </Sheet>,
    document.body,
  );
}

/** The row that offers the share card on a result screen: "I freed 28.1 GB with Trovolve". */
export function ShareRow({ card, onOpen }: { readonly card: ResultCard; onOpen(): void }) {
  return (
    <button type="button" onClick={onOpen} className="flex items-center gap-3 rounded-cell bg-cell p-4 text-left">
      <img src={appIconUrl} alt="" className="h-11 w-11 shrink-0 rounded-[11px]" />
      <span className="min-w-0">
        <span className="block text-[15.5px] font-semibold">I freed {card.amount} with Trovolve</span>
        <span className="block text-[13px] text-t2">Share card. None of your photos are on it.</span>
      </span>
    </button>
  );
}
