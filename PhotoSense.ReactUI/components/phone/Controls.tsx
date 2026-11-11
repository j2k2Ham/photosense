import React from 'react';
import { usePhone } from '../../lib/phone';
import { ChevronIcon } from './Icons';

/** What a button does: the screen's main thing, something that removes files, something else, or a plain link. */
export type ButtonKind = 'primary' | 'danger' | 'secondary' | 'text';

const buttonColours: Record<ButtonKind, string> = {
  primary: 'bg-acc text-on-acc',
  danger: 'bg-danger text-on-danger',
  secondary: 'bg-btn2-bg text-btn2-fg',
  text: 'text-acc !font-semibold',
};

interface ButtonProps {
  readonly kind?: ButtonKind;
  readonly children: React.ReactNode;
  readonly disabled?: boolean;
  readonly className?: string;
  onClick(): void;
}

/** A button as each platform draws one: a pill the width of the screen, its label on one line, ending in "…" when long. */
export function PhoneButton({ kind = 'primary', children, disabled, className = '', onClick }: ButtonProps) {
  return (
    <button type="button" disabled={disabled} onClick={onClick}
      className={`flex h-[var(--btn-h)] w-full min-w-0 items-center justify-center rounded-full px-5 text-[length:var(--btn-fs)] font-[number:var(--btn-fw)] transition disabled:opacity-40 ${buttonColours[kind]} ${className}`}>
      <span className="truncate">{children}</span>
    </button>
  );
}

/** The foot a screen's main button stands in, kept in view and fading the screen out above it. */
export function StickyFooter({ children }: { readonly children: React.ReactNode }) {
  return (
    <div className="sticky bottom-0 z-10 mt-auto flex flex-col gap-2 bg-[linear-gradient(to_bottom,transparent,var(--bg)_26%)] px-4 pb-[calc(var(--safe-b)+12px)] pt-4">
      {children}
    </div>
  );
}

interface SegmentedProps<T extends string> {
  readonly label: string;
  readonly options: readonly { readonly id: T; readonly label: string }[];
  readonly value: T;
  readonly className?: string;
  onChange(value: T): void;
}

/** One of a few choices side by side: on an iPhone a pill sliding inside a groove, on Android outlined segments. */
export function Segmented<T extends string>({ label, options, value, className = '', onChange }: SegmentedProps<T>) {
  const { ios } = usePhone();
  return (
    <div role="radiogroup" aria-label={label}
      className={`flex h-[var(--seg-h)] overflow-hidden rounded-full ${ios ? 'bg-seg-bg p-[2px]' : 'border border-seg-bd'} ${className}`}>
      {options.map((o, i) => {
        const on = o.id === value;
        const shape = ios ? `rounded-full ${on ? 'shadow-seg' : ''}` : `${i > 0 ? 'border-l border-seg-bd' : ''} ${i === 0 ? 'rounded-l-full' : ''} ${i === options.length - 1 ? 'rounded-r-full' : ''}`;
        return (
          <button key={o.id} type="button" role="radio" aria-checked={on} onClick={() => onChange(o.id)}
            className={`min-w-0 flex-1 truncate px-3 text-[14px] font-semibold transition ${shape} ${on ? 'bg-seg-on text-seg-on-fg' : 'text-t1'}`}>
            {o.label}
          </button>
        );
      })}
    </div>
  );
}

interface ChipProps {
  readonly label: string;
  readonly count?: string;
  readonly on: boolean;
  onClick(): void;
}

/** A filter or a choice in a row of them, with how many it holds after its name. */
export function Chip({ label, count, on, onClick }: ChipProps) {
  return (
    <button type="button" aria-pressed={on} onClick={onClick}
      className={`flex h-[34px] shrink-0 items-center gap-[7px] whitespace-nowrap rounded-chip border px-[14px] text-[14px] font-medium ${on ? 'border-transparent bg-chip-on-bg text-chip-on-fg' : 'border-chip-bd bg-chip-bg text-t1'}`}>
      {label}
      {/* A space, so that a screen reader says "All 6,941" rather than one word; the gap draws it. */}
      {count !== undefined && ' '}
      {count !== undefined && <span className={`text-[12.5px] tabular-nums ${on ? 'text-chip-on-fg opacity-75' : 'text-t2'}`}>{count}</span>}
    </button>
  );
}

interface GroupProps {
  readonly header?: string;
  /** What stands at the right of the header, such as a count. */
  readonly trailing?: React.ReactNode;
  /** In a sheet, where rows stand out from the sheet's own colour. */
  readonly inSheet?: boolean;
  readonly children: React.ReactNode;
}

/**
 * Rows that belong together, under a header: on an iPhone one rounded panel with lines between its rows, on Android
 * a row each, close together, the group's outer corners round.
 */
export function ListGroup({ header, trailing, inSheet, children }: GroupProps) {
  const { ios } = usePhone();
  // Written out whole, as Tailwind makes only the classes it finds as they are.
  const cell = ios ? (inSheet ? 'bg-sheet-cell' : 'bg-cell') : (inSheet ? '[&>li]:bg-sheet-cell' : '[&>li]:bg-cell');
  return (
    <section aria-label={header} className="flex flex-col gap-2">
      {header && (
        <div className="flex items-baseline justify-between px-1">
          <h3 className={ios ? 'text-[13px] font-semibold uppercase tracking-[.02em] text-t2' : 'text-[14px] font-medium text-acc'}>{header}</h3>
          {trailing && <span className="text-[13px] tabular-nums text-t2">{trailing}</span>}
        </div>
      )}
      <ul className={ios
        ? `overflow-hidden rounded-cell ${cell} [&>li+li]:border-t-[0.5px] [&>li+li]:border-sep`
        : `flex flex-col gap-[2px] overflow-hidden rounded-[20px] [&>li]:rounded-[4px] ${cell}`}>
        {children}
      </ul>
    </section>
  );
}

interface RowProps {
  readonly children: React.ReactNode;
  /** What the row says on its right, before any chevron. */
  readonly detail?: React.ReactNode;
  /** The row leads to something further in, and says so with a chevron. */
  readonly chevron?: boolean;
  readonly danger?: boolean;
  readonly disabled?: boolean;
  onClick?(): void;
}

/** One row of a list: pressed when it does something, with a chevron when it leads somewhere. */
export function ListRow({ children, detail, chevron, danger, disabled, onClick }: RowProps) {
  const inside = (
    <>
      <span className={`min-w-0 flex-1 ${danger ? 'text-danger-t' : ''}`}>{children}</span>
      {/* Spaced, so that a screen reader says "Errors None" rather than one word. */}
      {detail !== undefined && ' '}
      {detail !== undefined && <span className="shrink-0 text-[15px] tabular-nums text-t2">{detail}</span>}
      {chevron && <span className="shrink-0 text-t3"><ChevronIcon /></span>}
    </>
  );
  const shape = 'flex min-h-[52px] w-full items-center gap-3 px-4 py-[11px] text-left text-[16px]';
  return (
    <li>
      {onClick
        ? <button type="button" disabled={disabled} onClick={onClick} className={`${shape} disabled:opacity-40`}>{inside}</button>
        : <div className={shape}>{inside}</div>}
    </li>
  );
}

interface HeaderButtonProps {
  readonly label: string;
  readonly children: React.ReactNode;
  onClick(): void;
}

/** A round button in a screen's header: glass on an iPhone, plain on Android. */
export function IconButton({ label, children, onClick }: HeaderButtonProps) {
  const { ios } = usePhone();
  return (
    <button type="button" aria-label={label} title={label} onClick={onClick}
      className={`grid h-[var(--ib)] w-[var(--ib)] shrink-0 place-items-center rounded-full text-t1 ${ios ? 'bg-ib-bg shadow-ib backdrop-blur-[12px] [border:var(--ib-bd)]' : ''}`}>
      {children}
    </button>
  );
}

/** A word in a screen's header, such as Select or Cancel: a glass pill on an iPhone, a tonal one on Android. */
export function HeaderTextButton({ children, onClick }: { readonly children: React.ReactNode; onClick(): void }) {
  const { ios } = usePhone();
  return (
    <button type="button" onClick={onClick}
      className={`flex shrink-0 items-center rounded-full bg-ib-bg text-tb-fg ${ios ? 'h-11 px-[18px] text-[16px] font-semibold shadow-ib backdrop-blur-[12px] [border:var(--ib-bd)]' : 'h-10 px-[14px] text-[14.5px] font-medium'}`}>
      {children}
    </button>
  );
}
