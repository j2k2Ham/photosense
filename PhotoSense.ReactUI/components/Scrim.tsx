import React, { useEffect, useRef } from 'react';

const STOPS = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

// Only the window on top keeps Tab to itself: a question raised over a window has it while the question is up.
const open: HTMLElement[] = [];

interface Props {
  readonly className: string;
  readonly children: React.ReactNode;
  /** Puts the window away, as its Escape does. */
  onDismiss(): void;
}

/**
 * The dimmed layer a window or question stands on. Pressing it, outside the window, puts the window away. While the
 * window is up, Tab and Shift+Tab go round its own controls rather than the page behind it, and once it goes, focus goes
 * back to whatever had it when the window came.
 */
export function Scrim({ className, children, onDismiss }: Props) {
  const layer = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const me = layer.current!;
    const before = document.activeElement as HTMLElement | null;
    open.push(me);
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Tab' || open[open.length - 1] !== me) return;
      const stops = [...me.querySelectorAll<HTMLElement>(STOPS)];
      const at = stops.indexOf(document.activeElement as HTMLElement);
      if (stops.length === 0) { e.preventDefault(); return; }
      if (e.shiftKey && at <= 0) { e.preventDefault(); stops[stops.length - 1].focus(); }
      else if (!e.shiftKey && (at === -1 || at === stops.length - 1)) { e.preventDefault(); stops[0].focus(); }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
      open.splice(open.indexOf(me), 1);
      if (before?.isConnected) before.focus();
    };
  }, []);

  return (
    <div ref={layer} role="presentation" className={className} onMouseDown={e => { if (e.target === e.currentTarget) onDismiss(); }}>
      {children}
    </div>
  );
}
