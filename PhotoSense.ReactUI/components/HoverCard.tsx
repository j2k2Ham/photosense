import React, { useEffect, useId, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';

/** How long the pointer rests on a tile before its card shows: passing over a gallery shows nothing. */
export const HOVER_DELAY = 350;
/** How long a card stays once the pointer has left its tile, so that the pointer can be moved onto it. */
const GRACE = 150;
const GAP = 8, EDGE = 12;

interface Shown<T> { key: string; item: T; tile: DOMRect; }

/**
 * A card of details for whichever tile of a gallery is pointed at or has the keyboard's focus. It is drawn on the page
 * body, against the window, so that nothing that scrolls cuts it off. Esc puts it away, the pointer can rest on it to
 * read or select what it says, and a touch never opens it: on a phone a tap is a tap.
 */
export function useHoverCard<T>() {
  const [shown, setShown] = useState<Shown<T>>();
  const timer = useRef<ReturnType<typeof setTimeout>>(undefined);
  // A tile is being pressed: the focus that comes with the press is not the keyboard's.
  const pressed = useRef(false);
  const id = useId();
  const later = (ms: number, then: () => void) => { clearTimeout(timer.current); timer.current = setTimeout(then, ms); };
  const hide = () => { clearTimeout(timer.current); setShown(undefined); };

  useEffect(() => () => clearTimeout(timer.current), []);
  const up = shown !== undefined;
  useEffect(() => {
    if (!up) return undefined;
    // Before any window's own Escape: the first press puts the card away and nothing else.
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') { e.stopPropagation(); setShown(undefined); } };
    // A card left where its tile was is about another tile once the gallery has moved.
    const gone = () => setShown(undefined);
    window.addEventListener('keydown', onKey, true);
    window.addEventListener('scroll', gone, true);
    window.addEventListener('resize', gone);
    return () => { window.removeEventListener('keydown', onKey, true); window.removeEventListener('scroll', gone, true); window.removeEventListener('resize', gone); };
  }, [up]);

  return {
    /** What a tile is given: it shows its card when pointed at for a moment, or at once when reached by the keyboard. */
    on(key: string, item: T) {
      return {
        'aria-describedby': shown?.key === key ? id : undefined,
        onPointerEnter: (e: React.PointerEvent<HTMLElement>) => {
          if (e.pointerType === 'touch') return;
          const tile = e.currentTarget;
          // Once a card is up, the next tile's comes at once.
          later(shown ? 0 : HOVER_DELAY, () => setShown({ key, item, tile: tile.getBoundingClientRect() }));
        },
        onPointerLeave: () => later(GRACE, () => setShown(undefined)),
        // A press is for doing, not reading: the card goes, and the focus that comes with the press does not bring it back.
        onPointerDown: () => { pressed.current = true; hide(); },
        onFocus: (e: React.FocusEvent<HTMLElement>) => {
          if (!pressed.current) setShown({ key, item, tile: e.currentTarget.getBoundingClientRect() });
          pressed.current = false;
        },
        onBlur: hide,
      };
    },
    /** The card of the tile pointed at, if any, made by `render`. */
    card(render: (item: T) => React.ReactNode, width = 340) {
      if (!shown) return null;
      return createPortal(
        <Card id={id} tile={shown.tile} width={width} onEnter={() => clearTimeout(timer.current)} onLeave={() => later(GRACE, () => setShown(undefined))}>
          {render(shown.item)}
        </Card>,
        document.body);
    },
  };
}

interface CardProps { readonly id: string; readonly tile: DOMRect; readonly width: number; readonly children: React.ReactNode; onEnter(): void; onLeave(): void; }

/** Above the tile when there is room there, otherwise below it; never wider than the window, and held inside it. */
function Card({ id, tile, width, children, onEnter, onLeave }: CardProps) {
  const self = useRef<HTMLDivElement>(null);
  useLayoutEffect(() => {
    const card = self.current!, wide = Math.min(width, window.innerWidth - 2 * EDGE), tall = card.offsetHeight;
    const fits = (top: number) => top >= EDGE && top + tall <= window.innerHeight - EDGE;
    const top = [tile.top - GAP - tall, tile.bottom + GAP].find(fits) ?? Math.max(EDGE, window.innerHeight - EDGE - tall);
    const left = Math.max(EDGE, Math.min(tile.left + tile.width / 2 - wide / 2, window.innerWidth - EDGE - wide));
    Object.assign(card.style, { width: `${wide}px`, left: `${left}px`, top: `${top}px`, visibility: 'visible' });
  }, [tile, width]);
  return (
    // Drawn first where it cannot be seen, so that it can be measured before it is put in its place.
    <div ref={self} id={id} role="tooltip" style={{ left: 0, top: 0, width, visibility: 'hidden' }} onPointerEnter={onEnter} onPointerLeave={onLeave}
      className="fixed z-50 rounded-[14px] border border-line bg-pop p-4 text-[13.5px] shadow-pop">
      {children}
    </div>
  );
}

/** The details on a card, a label beside each. */
export function Facts({ rows }: { readonly rows: readonly (readonly [string, React.ReactNode, boolean?])[] }) {
  return (
    <dl className="grid grid-cols-[4.5rem_minmax(0,1fr)] gap-x-2 gap-y-0.5 text-t2">
      {rows.map(([label, value, mono]) => (
        <React.Fragment key={label}>
          <dt className="text-t3">{label}</dt>
          <dd className={mono ? 'font-mono text-[12.5px] [overflow-wrap:anywhere]' : '[overflow-wrap:anywhere]'}>{value}</dd>
        </React.Fragment>
      ))}
    </dl>
  );
}
