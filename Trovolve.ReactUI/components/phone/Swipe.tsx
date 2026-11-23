import React, { useEffect, useRef, useState } from 'react';
import { decideSwipe, deleteSwiped, organizeImageUrl, organizeThumbnailUrl, spareSwipe, undoSwipe } from '../../lib/apiClient';
import { useBackLayer } from '../../lib/backStack';
import { formatBytes, formatDuration } from '../../lib/format';
import { items, type Library } from '../../lib/library';
import { placeInFull, shortDate } from '../../lib/organize';
import { deviceWord, usePhone } from '../../lib/phone';
import type { ResultCard } from '../../lib/shareCard';
import {
  bytesOf, cardLine, dayLetter, dayName, kept, marked, markedAll, nextCard, overNotice, reviewed, runningLine, streakLine, summaryFoot, summaryKicker, swipeLimit,
} from '../../lib/swipe';
import type { OrganizeFileDto, SwipeCardDto, SwipeFreedDto, SwipeStateDto } from '../../types';
import type { ToastKind } from '../Toaster';
import { PhoneButton } from './Controls';
import { CheckIcon, CloseIcon, ShareIcon, TrashIcon, UndoIcon } from './Icons';
import { ShareRow } from './Share';
import { usePlanGate } from './ProSheet';

type Notify = (message: string, kind: ToastKind) => void;

const said = (e: unknown) => (e instanceof Error ? e.message : String(e));

/** How far a card is dragged before letting go of it decides: left deletes, right keeps. */
export const DECIDE_AT = 100;

/** The library's file for a card, by its id, once Library has read it. */
const fileOf = (library: Library, id: string) => library.files.find(f => f.id === id);

/** A card's small picture, or, for a video, which has none, a dark tile with a play mark. */
function Thumb({ file, id, className }: { readonly file?: OrganizeFileDto; readonly id: string; readonly className: string }) {
  if (file?.isVideo) return <span aria-hidden className={`grid place-items-center bg-[#1c2124] ${className}`}><Play /></span>;
  return <img src={organizeThumbnailUrl(id)} alt="" loading="lazy" className={`bg-fill object-cover ${className}`} />;
}

const Play = () => <span aria-hidden className="ml-[2px] h-0 w-0 border-y-[7px] border-l-[11px] border-y-transparent border-l-white" />;

/** The last seven days as dots, each filled when something was swiped on it, today last. */
function Week({ state, size }: { readonly state: SwipeStateDto; readonly size: number }) {
  return (
    <ol aria-label="The last seven days" className="flex gap-1.5">
      {state.week.map(d => (
        <li key={d.date} aria-label={dayName(d)} style={{ width: size, height: size }}
          className={`grid shrink-0 place-items-center rounded-full text-[11px] font-semibold ${d.swiped ? 'bg-acc text-on-acc' : 'border-2 border-fill text-t2'}`}>
          <span aria-hidden>{dayLetter(d)}</span>
        </li>
      ))}
    </ol>
  );
}

interface PanelProps {
  readonly state?: SwipeStateDto;
  readonly library: Library;
  /** Opens swiping, or the summary when the set is through and deletions wait. */
  onOpen(where: 'cards' | 'summary'): void;
}

/** 12: Swipe to clean on Clean up: three of today's photos fanned out, how far through today's set, the week's streak, and Start, Continue or Review deletions. */
export function SwipePanel({ state, library, onOpen }: PanelProps) {
  if (!state) {
    return <section aria-label="Swipe to clean" className="rounded-[24px] bg-cell p-4"><p role="status" className="text-[15px] text-t2">Drawing today&apos;s set…</p></section>;
  }
  const line = cardLine(state);
  const fan = state.set.filter(c => c.delete === null).slice(0, 3);
  return (
    <section aria-label="Swipe to clean" className="overflow-hidden rounded-[24px] bg-cell">
      <div aria-hidden className="relative grid h-[118px] place-items-center overflow-hidden bg-fill">
        {[-14, 0, 14].map((turn, i) => (
          <span key={turn} className="absolute h-[92px] w-[70px] overflow-hidden rounded-[12px] border-2 border-cell bg-fill2"
            style={{ transform: `translateX(${(i - 1) * 62}px) rotate(${turn}deg) translateY(${turn === 0 ? -6 : 8}px)` }}>
            {fan[i] && <Thumb file={fileOf(library, fan[i].id)} id={fan[i].id} className="h-full w-full" />}
          </span>
        ))}
      </div>
      <div className="flex flex-col gap-3 p-4">
        <div>
          <h2 className="text-[18px] font-semibold">Swipe to clean</h2>
          <p className="text-[14px] leading-[1.4] text-t2">{line.text}</p>
        </div>
        <div className="flex flex-wrap items-center gap-2.5">
          <Week state={state} size={24} />
          <span className="text-[14px] font-semibold">{streakLine(state.streak)}</span>
        </div>
        {line.button && <PhoneButton onClick={() => onOpen(line.button === 'Review deletions' ? 'summary' : 'cards')}>{line.button}</PhoneButton>}
      </div>
    </section>
  );
}

/** A layer over everything, tabs and all, that Back, a swipe from the edge and Escape take away. */
function Cover({ label, onClose, beside, children }: { readonly label: string; onClose(): void; /** On a tablet, a panel beside the column (T3). */ readonly beside?: React.ReactNode; readonly children: React.ReactNode }) {
  const close = useRef(onClose);
  useEffect(() => { close.current = onClose; });
  useBackLayer(true, () => close.current());
  return (
    <section role="dialog" aria-modal aria-label={label}
      className="fixed inset-0 z-40 flex animate-fade-in flex-col bg-bg px-4 pb-[calc(var(--safe-b)+12px)] pt-[calc(var(--safe-t)+8px)] font-ui text-t1">
      {/* A column as wide as a phone's, centred on a tablet, as the design's card column is, with its panel beside it. */}
      <div className="mx-auto flex min-h-0 w-full flex-1 justify-center gap-6">
        <div className="flex min-h-0 w-full max-w-[480px] flex-1 flex-col">{children}</div>
        {beside}
      </div>
    </section>
  );
}

/**
 * T3: beside a tablet's swiping, what is marked to delete and what it frees, the latest 15 marked, the week's streak, and
 * the free plan's removals left today.
 */
function MarkedPanel({ state, library, limit }: { readonly state: SwipeStateDto; readonly library: Library; readonly limit: { readonly text: string; readonly over: boolean } | null }) {
  const going = markedAll(state);
  const latest = going.slice(-15).reverse();
  return (
    <aside aria-label="Marked to delete" className="mt-[calc(var(--ib)+12px)] flex w-[320px] shrink-0 flex-col gap-3 self-start rounded-[20px] bg-cell p-4">
      <div>
        <p className="text-[13px] font-semibold text-t2">Marked to delete</p>
        <p className="font-title text-[30px] font-bold tabular-nums">{formatBytes(bytesOf(going))}</p>
        <p className="text-[13px] text-t2">{going.length === 1 ? '1 item' : `${going.length.toLocaleString()} items`} so far · confirmed at the end</p>
      </div>
      {latest.length > 0 && (
        <ul aria-label="Latest marked" className="grid grid-cols-5 gap-[2px] overflow-hidden rounded-[12px]">
          {latest.map(c => (
            <li key={c.id} className="aspect-square bg-fill"><Thumb file={fileOf(library, c.id)} id={c.id} className="h-full w-full" /></li>
          ))}
        </ul>
      )}
      <div className="flex items-center gap-2.5 border-t border-sep pt-3">
        <Week state={state} size={20} />
        <span className="text-[13.5px] font-semibold">{streakLine(state.streak)}</span>
      </div>
      {limit && <p className={`text-[13px] leading-[1.45] ${limit.over ? 'text-amber' : 'text-t2'}`}>{limit.text}</p>}
    </aside>
  );
}

const roundButton = 'grid h-[var(--ib)] w-[var(--ib)] shrink-0 place-items-center rounded-full bg-fill text-t1 disabled:opacity-35';

interface SessionProps {
  readonly state: SwipeStateDto;
  readonly library: Library;
  /** The computer backups go to, for the flag on a photo it holds. */
  readonly computer?: string | null;
  notify: Notify;
  /** Closes swiping before the set is through. */
  onClose(): void;
  /** The set is through: the summary follows. */
  onDone(): void;
}

/**
 * 13: today's set, one photo at a time: dragged left past {@link DECIDE_AT} pixels it is marked to delete, right it is
 * kept, or Delete and Keep below it; Undo takes back the last. Nothing is deleted here, and no offer interrupts: the
 * foot says what is marked so far and what the free plan has left today.
 */
export function SwipeSession({ state, library, computer, notify, onClose, onDone }: SessionProps) {
  const { words, tablet } = usePhone();
  const gate = usePlanGate();
  const [drag, setDrag] = useState<{ x: number; y: number; from?: { x: number; y: number } }>({ x: 0, y: 0 });
  const [busy, setBusy] = useState(false);
  const card = nextCard(state);
  const n = state.set.length, done = reviewed(state);
  const limit = swipeLimit(state, gate.plan?.leftToday);

  async function decide(del: boolean) {
    if (!card || busy) return;
    setBusy(true);
    try {
      const after = await decideSwipe(card.id, del);
      setDrag({ x: 0, y: 0 });
      if (!nextCard(after)) onDone();
    } catch (e) {
      notify(said(e), 'error');
      setDrag({ x: 0, y: 0 });
    } finally {
      setBusy(false);
    }
  }

  async function undo() {
    setBusy(true);
    try { await undoSwipe(); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }

  const latest = useRef({ decide, onClose });
  useEffect(() => { latest.current = { decide, onClose }; });
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'ArrowLeft') void latest.current.decide(true);
      else if (e.key === 'ArrowRight') void latest.current.decide(false);
      else if (e.key === 'Escape') latest.current.onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  const file = card && fileOf(library, card.id);
  const place = file?.place == null ? undefined : library.listing?.places[file.place];
  const meta = file && [place && placeInFull(place), file.isVideo ? `Video${file.durationSeconds == null ? '' : ` ${formatDuration(file.durationSeconds)}`}` : 'Photo', formatBytes(card.bytes)].filter(Boolean).join(' · ');
  const leaning = Math.max(-1, Math.min(1, drag.x / 90));

  return (
    <Cover label="Swipe to clean" onClose={onClose} beside={tablet && <MarkedPanel state={state} library={library} limit={limit} />}>
      <div className="flex items-center gap-3 pb-3">
        <button type="button" aria-label="Close" onClick={onClose} className={roundButton}><CloseIcon size={18} /></button>
        <div className="flex min-w-0 flex-1 flex-col items-center gap-1.5">
          <p className="text-[14px] font-semibold tabular-nums">{Math.min(done + 1, n).toLocaleString()} of {n.toLocaleString()}</p>
          <div role="progressbar" aria-label="Today's set" aria-valuemin={0} aria-valuemax={n} aria-valuenow={done} className="h-1 w-20 overflow-hidden rounded-full bg-fill">
            <div className="h-full rounded-full bg-acc" style={{ width: `${n > 0 ? (done / n) * 100 : 0}%` }} />
          </div>
        </div>
        <button type="button" aria-label="Undo" disabled={busy || done === 0} onClick={() => void undo()} className={roundButton}><UndoIcon size={18} /></button>
      </div>

      {card && (
        <div className="relative min-h-0 flex-1">
          <div role="img" aria-label={file ? `${file.name}, ${shortDate(file.date)}` : 'A photo'}
            onPointerDown={e => { e.currentTarget.setPointerCapture?.(e.pointerId); setDrag({ x: 0, y: 0, from: { x: e.clientX, y: e.clientY } }); }}
            onPointerMove={e => { if (drag.from) setDrag({ ...drag, x: e.clientX - drag.from.x, y: e.clientY - drag.from.y }); }}
            onPointerUp={() => (Math.abs(drag.x) >= DECIDE_AT ? void decide(drag.x < 0) : setDrag({ x: 0, y: 0 }))}
            onPointerCancel={() => setDrag({ x: 0, y: 0 })}
            className={`absolute inset-0 touch-none select-none overflow-hidden rounded-[28px] bg-[#1c2124] ${drag.from ? '' : 'transition-transform duration-200'}`}
            style={{ transform: `translate(${drag.x}px, ${drag.y * 0.3}px) rotate(${drag.x / 18}deg)` }}>
            {file?.isVideo
              ? <span className="grid h-full w-full place-items-center"><Play /></span>
              : (
                <>
                  <img src={organizeThumbnailUrl(card.id)} alt="" draggable={false} className="absolute inset-0 h-full w-full object-cover" />
                  <img src={organizeImageUrl(card.id)} alt="" draggable={false} className="absolute inset-0 h-full w-full object-cover" />
                </>
              )}
            {card.onComputer && computer && (
              <span className="absolute left-3 top-3 flex items-center gap-1.5 rounded-full bg-black/55 px-2.5 py-1 text-[12.5px] font-semibold text-white">
                <span aria-hidden className="h-[7px] w-[7px] rounded-full bg-[#30D158]" />On {computer}
              </span>
            )}
            <span aria-hidden className="absolute right-4 top-5 rotate-12 rounded-lg border-[3px] border-[#30D158] px-2 text-[22px] font-black tracking-[.06em] text-[#30D158]" style={{ opacity: Math.max(0, leaning) }}>KEEP</span>
            <span aria-hidden className="absolute left-4 top-5 -rotate-12 rounded-lg border-[3px] border-[#FF453A] px-2 text-[22px] font-black tracking-[.06em] text-[#FF453A]" style={{ opacity: Math.max(0, -leaning) }}>DELETE</span>
            {file && (
              <div className="absolute inset-x-0 bottom-0 bg-gradient-to-t from-black/75 to-transparent px-4 pb-4 pt-10 text-white">
                <p className="text-[22px] font-bold">{shortDate(file.date)}</p>
                <p className="text-[13.5px] text-white/80">{meta}</p>
              </div>
            )}
          </div>
        </div>
      )}

      <div className="flex justify-center gap-10 pt-4">
        <button type="button" disabled={busy || !card} onClick={() => void decide(true)} className="flex flex-col items-center gap-1.5 text-[13px] font-semibold text-t2 disabled:opacity-40">
          <span aria-hidden className="grid h-[68px] w-[68px] place-items-center rounded-full bg-danger-soft text-danger-t"><TrashIcon size={26} /></span>Delete
        </button>
        <button type="button" disabled={busy || !card} onClick={() => void decide(false)} className="flex flex-col items-center gap-1.5 text-[13px] font-semibold text-t2 disabled:opacity-40">
          <span aria-hidden className="grid h-[68px] w-[68px] place-items-center rounded-full bg-keep-soft text-keep"><CheckIcon size={26} /></span>Keep
        </button>
      </div>
      <div className="flex flex-col items-center pt-3 text-center text-[13px] leading-[1.45]">
        {/* On a tablet the panel beside says these. */}
        {!tablet && <p className="text-t2">{runningLine(state)}</p>}
        {!tablet && limit && <p className={limit.over ? 'text-amber' : 'text-t2'}>{limit.text}</p>}
        <p className="sr-only">{`Deleted photos wait in ${words.bin}. Nothing is deleted until you confirm at the end.`}</p>
      </div>
    </Cover>
  );
}

interface SummaryProps {
  readonly state: SwipeStateDto;
  readonly library: Library;
  notify: Notify;
  onClose(): void;
  /** Once deleted, opens the share card. */
  onShare?(card: ResultCard): void;
}

/**
 * 14 and 15: what the session marked and what it would free, the streak, Reviewed, Kept and To delete, the marked photos
 * (tap one to keep it), and the one confirmation that deletes them, to the phone's own bin after its question. Over the
 * free plan's removals left today, the largest that fit go now and the rest stay marked; Pro deletes them all.
 */
export function SwipeSummary({ state, library, notify, onClose, onShare }: SummaryProps) {
  const { words, platform, tablet } = usePhone();
  const gate = usePlanGate();
  const [busy, setBusy] = useState(false);
  const [freed, setFreed] = useState<SwipeFreedDto>();
  const going = marked(state), all = markedAll(state);
  const left = gate.plan?.leftToday ?? null;
  const notice = overNotice(going.length, left, gate.plan?.removalsPerDay ?? 50);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  async function remove(atMost?: number) {
    setBusy(true);
    try { setFreed(await deleteSwiped(atMost)); } catch (e) { notify(said(e), 'error'); } finally { setBusy(false); }
  }
  // More than the free plan has left today: the Pro sheet, whose purchase deletes them all and whose other button the largest that fit.
  const withPro = () => gate.remove(going.length, 'items', allowed => void remove(allowed < going.length ? allowed : undefined), true);
  async function spare(card: SwipeCardDto) {
    try { await spareSwipe(card.id, !card.spared); } catch (e) { notify(said(e), 'error'); }
  }

  // The share card of what was deleted: the amount as the summary says it, the streak and how many.
  const cardOf = (done: SwipeFreedDto): ResultCard => ({ amount: formatBytes(done.bytes), device: deviceWord(platform, tablet), streak: state.streak, items: done.deleted });
  const stats: [string, number][] = freed
    ? [['Reviewed', reviewed(state)], ['Kept', kept(state)], ['Deleted', freed.deleted]]
    : [['Reviewed', reviewed(state)], ['Kept', kept(state)], ['To delete', going.length]];
  return (
    <Cover label="Swipe summary" onClose={onClose}>
      <div className="pb-2"><button type="button" aria-label="Close" onClick={onClose} className={roundButton}><CloseIcon size={18} /></button></div>
      <div className="flex min-h-0 flex-1 flex-col gap-4 overflow-y-auto">
        <div className="px-1">
          <p className="text-[14px] font-semibold text-t2">{freed ? 'Space freed' : summaryKicker(state)}</p>
          <p className="font-title text-[44px] font-bold leading-tight tabular-nums">{freed ? `${formatBytes(freed.bytes)} freed` : formatBytes(bytesOf(going))}</p>
          <p className="text-[15px] text-t2">
            {freed
              ? freed.left > 0 ? `${items(freed.left)} stay marked for tomorrow.` : nextCard(state) ? 'The rest of today’s set is still waiting.' : 'A new set is ready tomorrow.'
              : `ready to free from ${items(going.length)}`}
          </p>
        </div>
        <section aria-label="Streak" className="flex flex-col gap-3 rounded-[20px] bg-cell p-4">
          <p className="text-[16px] font-semibold">{streakLine(state.streak)}</p>
          <Week state={state} size={28} />
        </section>
        <dl className="grid grid-cols-3 gap-2">
          {stats.map(([label, value]) => (
            <div key={label} className="flex flex-col-reverse rounded-[16px] bg-cell px-3.5 py-3">
              <dt className="text-[13px] text-t2">{label}</dt>
              <dd className="text-[22px] font-bold tabular-nums">{value.toLocaleString()}</dd>
            </div>
          ))}
        </dl>
        {!freed && all.length > 0 && (
          <section aria-label="Marked to delete" className="flex flex-col gap-2">
            <div className="flex items-baseline justify-between px-1">
              <h2 className="text-[16px] font-semibold">Marked to delete</h2>
              <span className="text-[13px] text-t2">Tap any to keep it</span>
            </div>
            <ul className="grid grid-cols-6 gap-[3px] overflow-hidden rounded-[12px]">
              {all.map(card => {
                const file = fileOf(library, card.id);
                return (
                  <li key={card.id}>
                    <button type="button" aria-pressed={!card.spared} aria-label={`${file?.name ?? 'A photo'}: ${card.spared ? 'kept' : 'marked to delete'}`} onClick={() => void spare(card)}
                      className="relative block aspect-square w-full">
                      <Thumb file={file} id={card.id} className={`h-full w-full ${card.spared ? 'opacity-35' : ''}`} />
                      {card.spared && <span aria-hidden className="absolute inset-x-0 bottom-0.5 text-center text-[10px] font-bold text-t1">KEPT</span>}
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>
        )}
        {freed && freed.deleted > 0 && onShare && <ShareRow card={cardOf(freed)} onOpen={() => onShare(cardOf(freed))} />}
        {!freed && notice && <p role="note" className="rounded-[16px] bg-amber-soft px-4 py-3 text-[14.5px] leading-[1.4]">{notice}</p>}
      </div>
      <div className="flex flex-col gap-1 pt-3">
        {freed && freed.deleted > 0 && onShare ? (
          <div className="flex gap-3">
            <PhoneButton kind="secondary" onClick={() => onShare(cardOf(freed))}><span className="flex items-center justify-center gap-2"><ShareIcon size={18} />Share</span></PhoneButton>
            <PhoneButton onClick={onClose}>Done</PhoneButton>
          </div>
        ) : freed || going.length === 0 ? <PhoneButton onClick={onClose}>{freed ? 'Done' : 'Finish'}</PhoneButton>
          : notice && left === 0 ? <PhoneButton disabled={busy} onClick={withPro}>Get Pro</PhoneButton>
            : notice ? <>
              <PhoneButton disabled={busy} onClick={() => void remove(left!)}>Delete {left!.toLocaleString()} now</PhoneButton>
              <PhoneButton kind="text" disabled={busy} onClick={withPro}>Delete all {going.length.toLocaleString()} with Pro</PhoneButton>
            </>
              : <PhoneButton disabled={busy} onClick={() => void remove()}>Delete {items(going.length)}</PhoneButton>}
        {!freed && going.length > 0 && <p className="px-2 pt-1 text-center text-[12.5px] leading-[1.4] text-t2">{summaryFoot(going.length, left, words.bin)}</p>}
      </div>
    </Cover>
  );
}
