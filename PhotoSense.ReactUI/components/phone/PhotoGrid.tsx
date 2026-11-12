import React from 'react';
import { usePhone } from '../../lib/phone';
import { AddToFolderIcon, SendIcon, TickIcon, TrashIcon } from './Icons';

/** One picture or video in the grid. */
export interface GridItem {
  readonly id: string;
  /** Its name, which a screen reader says for it. */
  readonly name: string;
  /** Its preview; absent for a video, which has none. */
  readonly thumbnail?: string;
  /** How long a video plays, as shown on it ("0:42"); absent for a picture. */
  readonly duration?: string;
}

interface GridProps {
  readonly items: readonly GridItem[];
  /** Tapping chooses rather than opens. */
  readonly selecting?: boolean;
  readonly selected?: ReadonlySet<string>;
  onTap(id: string): void;
}

/**
 * Pictures and videos as square tiles, edge to edge with 2 px between: three across on a phone; on an iPad six, or
 * eight with its sidebar hidden; eight on an Android tablet. While choosing, each has a circle, filled when chosen.
 */
export function PhotoGrid({ items, selecting, selected, onTap }: GridProps) {
  const { ios, tablet, sidebarHidden } = usePhone();
  const columns = !tablet ? 'grid-cols-3' : ios && !sidebarHidden ? 'grid-cols-6' : 'grid-cols-8';
  return (
    <ul className={`grid gap-[2px] ${columns}`}>
      {items.map(item => {
        const on = !!selected?.has(item.id);
        // Android draws a chosen tile smaller, its corners round; an iPhone only fills its circle.
        const shrunk = on && !ios ? 'left-2 top-2 h-[calc(100%-16px)] w-[calc(100%-16px)] rounded-[14px]' : 'left-0 top-0 h-full w-full';
        return (
          <li key={item.id}>
            <button type="button" aria-label={item.name} aria-pressed={selecting ? on : undefined} onClick={() => onTap(item.id)}
              className="relative block aspect-square w-full overflow-hidden bg-fill">
              {item.thumbnail
                ? <img src={item.thumbnail} alt="" loading="lazy" className={`absolute object-cover transition-all duration-150 ${shrunk}`} />
                : <span aria-hidden className={`absolute grid place-items-center bg-[#1c2124] transition-all duration-150 ${shrunk}`}><span className="ml-1 h-0 w-0 border-y-[9px] border-l-[14px] border-y-transparent border-l-white" /></span>}
              {item.duration && <span className="absolute bottom-1 right-1.5 text-[12px] font-semibold text-white [text-shadow:0_1px_3px_rgba(0,0,0,.6)]">{item.duration}</span>}
              {selecting && (
                <span aria-hidden className={`absolute grid h-6 w-6 place-items-center rounded-full border-[1.5px] border-white ${on ? 'bg-acc' : 'bg-black/20'} ${ios ? 'bottom-1.5 right-1.5' : 'left-1.5 top-1.5'}`}>
                  {on && <TickIcon />}
                </span>
              )}
            </button>
          </li>
        );
      })}
    </ul>
  );
}

/** A month's header over its part of the grid: the month, and how many it holds. */
export function MonthHeader({ month, count }: { readonly month: string; readonly count: string }) {
  return (
    <div className="flex items-baseline justify-between gap-3 px-4 pb-2 pt-3.5">
      <h2 className="font-title text-[17px] font-semibold">{month}</h2>
      <span className="text-[13px] text-t2">{count}</span>
    </div>
  );
}

interface SelectionBarProps {
  readonly count: number;
  onSend(): void;
  onPut(): void;
  onDelete(): void;
}

/**
 * What can be done with what is chosen, in place of the tab bar: Send, put in an album or folder, and Delete. Faint
 * while nothing is chosen. Floating glass on an iPhone, a bar across the foot on Android, floating in the middle on a tablet.
 */
export function SelectionBar({ count, onSend, onPut, onDelete }: SelectionBarProps) {
  const { ios, tablet, words } = usePhone();
  const place = tablet
    ? 'bottom-7 inset-x-0 mx-auto w-[min(420px,calc(100%-32px))] rounded-full p-1 shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px] backdrop-saturate-[1.8]'
    : ios
      ? 'bottom-[22px] inset-x-4 rounded-full p-1 shadow-bar [border:var(--bar-bd)] backdrop-blur-[24px] backdrop-saturate-[1.8]'
      : 'bottom-[var(--safe-b)] inset-x-0';
  const none = count === 0;
  const action = 'flex flex-col items-center justify-center gap-[3px] rounded-full text-[11.5px] font-semibold disabled:opacity-40';
  return (
    <div role="toolbar" aria-label="What is selected" className={`fixed z-20 grid h-16 animate-toast-in grid-cols-3 bg-bar ${place}`}>
      <button type="button" disabled={none} onClick={onSend} className={`${action} text-t1`}><SendIcon size={22} />Send</button>
      <button type="button" disabled={none} onClick={onPut} className={`${action} text-t1`}><AddToFolderIcon size={22} />{words.put}</button>
      <button type="button" disabled={none} onClick={onDelete} className={`${action} text-danger-t`}><TrashIcon size={22} />Delete</button>
    </div>
  );
}
