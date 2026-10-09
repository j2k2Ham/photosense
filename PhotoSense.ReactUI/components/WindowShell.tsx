import React, { useEffect, useRef } from 'react';

interface Props {
  /** What the window is, for a screen reader. */
  readonly label: string;
  /** What goes in the bar across the top, before the Esc hint and the close button. */
  readonly header: React.ReactNode;
  /** The stage on the left and the panel on the right. */
  readonly children: React.ReactNode;
  /** How wide the panel on the right is, as a Tailwind grid template. */
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
    <div className={`fixed inset-0 bg-scrim p-10 ${raised ? 'z-40' : 'z-30'}`} onMouseDown={e => { if (e.target === e.currentTarget) onClose(); }}>
      <div ref={panel} tabIndex={-1} role="dialog" aria-modal="true" aria-label={label} className="flex h-full flex-col overflow-hidden rounded-[18px] border border-line bg-s1 shadow-dialog outline-none">
        <div className="flex h-[72px] shrink-0 items-center gap-3.5 border-b border-line pl-7 pr-[18px]">
          {header}
          <kbd className="rounded-md border border-line px-2 py-0.5 font-mono text-[12px] text-t3">Esc</kbd>
          <button type="button" onClick={onClose} aria-label="Close" className="pill-quiet h-[38px] w-[38px] text-[18px] leading-none">×</button>
        </div>
        <div className={`grid min-h-0 flex-1 grid-rows-[minmax(0,1fr)] ${columns}`}>{children}</div>
      </div>
    </div>
  );
}

/** A small key cap, for saying which key does something. */
export const Key = ({ children }: { readonly children: React.ReactNode }) => <kbd className="rounded border border-line px-1.5 font-mono">{children}</kbd>;
