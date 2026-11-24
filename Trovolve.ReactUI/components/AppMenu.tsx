import React, { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { FeatureIcon } from './FeatureIcons';
import { binName } from '../lib/format';
import type { Theme } from '../lib/theme';
import { useDismiss } from '../lib/useDismiss';

interface Props {
  readonly theme: Theme;
  readonly errorCount: number;
  onTheme(theme: Theme): void;
  onErrors(): void;
  /** Opens the support page. */
  onSupport(): void;
  onChangeFolders(): void;
  onClearResults(): void;
  onSettings(): void;
  onHelp(): void;
  onAbout(): void;
  /** Asks to erase what has been removed; nothing goes until that is confirmed. */
  onDeletePermanently(): void;
  onClose(): void;
}

const themes: { id: Theme; name: string; swatch: string }[] = [
  { id: 'dark', name: 'Dark', swatch: '#0c0f11' },
  { id: 'light', name: 'Light', swatch: '#f5f7f9' },
];

/** The menu in the bar: the theme, the folders scanned, settings, Support (errors and the support page), help, About and Move to Recycle Bin (Move to Trash on a Mac). */
export function AppMenu({ theme, errorCount, onTheme, onErrors, onSupport, onChangeFolders, onClearResults, onSettings, onHelp, onAbout, onDeletePermanently, onClose }: Props) {
  const ref = useDismiss<HTMLDivElement>(onClose);
  // Choosing something from the menu puts the menu away.
  const pick = (action: () => void) => () => { action(); onClose(); };
  const item = 'flex w-full items-center rounded-[10px] px-3 py-2.5 text-left text-[14px] hover:bg-s2';
  // Support opens beneath itself, as a menu of its own: the right arrow opens it on its first item, the left goes back.
  const [support, setSupport] = useState(false);
  const supportItem = useRef<HTMLButtonElement>(null);
  const supportMenu = useRef<HTMLDivElement>(null);
  // As in any menu: it opens on its first item, the arrow keys, Home and End go through the rest, and once it closes
  // focus is back on what opened it, which a window opened from the menu then gives focus back to in turn.
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>('[role^="menuitem"]')?.focus();
    return () => { if (opener?.isConnected) opener.focus(); };
  }, [ref]);
  function onKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    if (e.key === 'ArrowRight' && document.activeElement === supportItem.current) {
      e.preventDefault();
      // Drawn at once, so that its first item is there to be focused.
      flushSync(() => setSupport(true));
      supportMenu.current!.querySelector<HTMLElement>('[role="menuitem"]')!.focus();
      return;
    }
    if (e.key === 'ArrowLeft' && supportMenu.current?.contains(document.activeElement)) {
      e.preventDefault();
      setSupport(false);
      supportItem.current!.focus();
      return;
    }
    const items = [...e.currentTarget.querySelectorAll<HTMLElement>('[role^="menuitem"]')];
    const at = items.indexOf(document.activeElement as HTMLElement);
    const to: Record<string, number> = { ArrowDown: at + 1, ArrowUp: (at <= 0 ? items.length : at) - 1, Home: 0, End: items.length - 1 };
    if (!(e.key in to)) return;
    e.preventDefault();
    items[to[e.key] % items.length].focus();
  }

  return (
    <div ref={ref} role="menu" aria-label="Menu" tabIndex={-1} onKeyDown={onKeyDown} className="fixed right-3 top-[58px] z-40 w-[min(300px,calc(100vw-24px))] rounded-2xl sm:right-6 border border-line bg-pop p-2 shadow-[0_20px_50px_var(--shadow)]">
      <div className="label-caps px-3 pb-2 pt-2">Theme</div>
      <div className="mx-2 mb-2 flex rounded-full bg-s2 p-1" role="group" aria-label="Theme">
        {themes.map(t => (
          <button key={t.id} type="button" role="menuitemradio" aria-checked={theme === t.id} onClick={() => onTheme(t.id)}
            className={`pill h-8 flex-1 text-[13px] ${theme === t.id ? 'bg-pop font-semibold text-t1' : 'font-medium text-t2'}`}>
            <span aria-hidden className="h-2.5 w-2.5 rounded-full border border-line" style={{ background: t.swatch }} />{t.name}
          </button>
        ))}
      </div>
      <div role="separator" className="my-1 border-t border-line" />
      <button type="button" role="menuitem" onClick={pick(onChangeFolders)} className={item}>Change folders or rescan</button>
      <button type="button" role="menuitem" onClick={pick(onClearResults)} className={item}>Clear results</button>
      <button type="button" role="menuitem" onClick={pick(onSettings)} className={item}>Settings</button>
      <button ref={supportItem} type="button" role="menuitem" aria-haspopup="menu" aria-expanded={support} onClick={() => setSupport(on => !on)} className={item}>
        Support
        {errorCount > 0 && <span className="ml-auto font-mono text-[12px] text-rose-t">{errorCount} {errorCount === 1 ? 'error' : 'errors'}</span>}
        <svg aria-hidden width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"
          className={`${errorCount > 0 ? 'ml-2' : 'ml-auto'} shrink-0 text-t2 transition-transform ${support ? 'rotate-90' : ''}`}><path d="M9 5l7 7-7 7" /></svg>
      </button>
      {support && (
        <div ref={supportMenu} role="menu" aria-label="Support" className="mb-1 ml-3 border-l border-line pl-2">
          <button type="button" role="menuitem" onClick={pick(onErrors)} className={item}>
            Errors<span className="ml-auto font-mono text-[12px] text-t3">{errorCount}</span>
          </button>
          <button type="button" role="menuitem" onClick={pick(onSupport)} className={item}>Support page</button>
        </div>
      )}
      <button type="button" role="menuitem" onClick={pick(onHelp)} className={item}>Help</button>
      <button type="button" role="menuitem" onClick={pick(onAbout)} className={item}>About Trovolve</button>
      <div role="separator" className="my-1 border-t border-line" />
      <button type="button" role="menuitem" onClick={pick(onDeletePermanently)} className={`${item} gap-2.5 text-rose-t hover:bg-rose-bg`}
        title={`Removed files wait in _Trovolve_Removed inside each folder they were removed from. Move to ${binName()} sends them there.`}><FeatureIcon name="removed" size={18} />Move to {binName()}…</button>
    </div>
  );
}
