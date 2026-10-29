import React, { useEffect, useRef } from 'react';

interface Props {
  /** What the window is, for a screen reader. */
  readonly label: string;
  /** What goes in the bar across the top, before the Esc hint and the close button. */
  readonly header: React.ReactNode;
  /** The stage on the left and the panel on the right; on a narrow screen, the stage above the panel. */
  readonly children: React.ReactNode;
  /** How wide the panel on the right is, as a Tailwind grid template for a wide screen ("lg:grid-cols-[…]"). */
  readonly columns: string;
  /** Raised above another window of the same kind that is open beneath it. */
  readonly raised?: boolean;
  onClose(): void;
}

/** The large window the comparison screens share: a scrim, a header, a stage and a panel beside it. */
export function WindowShell({ label, header, children, columns, raised = false, onClose }: Props) {
  const panel = useRef<HTMLDivElement>(null);
  // Focus comes into the window, so that its keys work without a click first.
  useEffect(() => { panel.current?.focus(); }, []);

  return (
    // On a narrow screen the window is the whole screen, and its parts stand one under another and scroll together.
    <div className={`fixed inset-0 bg-scrim lg:p-10 ${raised ? 'z-40' : 'z-30'}`} onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={panel} tabIndex={-1} role="dialog" aria-modal="true" aria-label={label} className="flex h-full flex-col overflow-hidden border-line bg-s1 shadow-dialog outline-none lg:rounded-[18px] lg:border">
        {/* The header's own parts wrap among themselves on a narrow screen; the close button stays at the top right. */}
        <div className="flex min-h-[60px] shrink-0 items-start gap-3.5 border-b border-line py-2.5 pl-4 pr-3 lg:h-[72px] lg:items-center lg:py-0 lg:pl-7 lg:pr-[18px]">
          <div className="flex min-h-[38px] min-w-0 flex-1 flex-wrap items-center gap-x-3.5 gap-y-2 lg:flex-nowrap">{header}</div>
          <kbd className="hidden rounded-md border border-line px-2 py-0.5 font-mono text-[12px] text-t3 lg:inline">Esc</kbd>
          <button type="button" onClick={onClose} aria-label="Close" className="pill-quiet h-[38px] w-[38px] shrink-0 text-[18px] leading-none">×</button>
        </div>
        {/* Narrow, each part takes the height it needs and the window scrolls; wide, the parts share the height there is. */}
        <div className={`grid min-h-0 flex-1 auto-rows-max grid-cols-1 overflow-y-auto lg:auto-rows-auto lg:grid-rows-[minmax(0,1fr)] lg:overflow-hidden ${columns}`}>{children}</div>
      </div>
    </div>
  );
}

/** A small key cap, for saying which key does something. */
export const Key = ({ children }: { readonly children: React.ReactNode }) => <kbd className="rounded border border-line px-1.5 font-mono">{children}</kbd>;
