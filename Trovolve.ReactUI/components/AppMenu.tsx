import React, { useEffect, useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import { FeatureIcon } from './FeatureIcons';
import { binName } from '../lib/format';
import type { ThemeChoice } from '../lib/theme';
import { useDismiss } from '../lib/useDismiss';

interface Props {
  /** What the person chose: dark, light, or the device's own setting. */
  readonly theme: ThemeChoice;
  readonly errorCount: number;
  onTheme(theme: ThemeChoice): void;
  onErrors(): void;
  /** Opens the support page. */
  onSupport(): void;
  /** Goes to the scan screen; left out where there are no results to change from. */
  onChangeFolders?(): void;
  /** Asks to forget the results; left out where there are none. */
  onClearResults?(): void;
  onSettings(): void;
  onHelp(): void;
  onAbout(): void;
  /** Asks to erase what has been removed; nothing goes until that is confirmed. */
  onDeletePermanently(): void;
  onClose(): void;
}

const themes: { id: ThemeChoice; name: string; swatch: string }[] = [
  { id: 'dark', name: 'Dark', swatch: '#0c0f11' },
  { id: 'light', name: 'Light', swatch: '#f5f7f9' },
  { id: 'auto', name: 'Follow device setting', swatch: 'linear-gradient(135deg, #0c0f11 50%, #f5f7f9 50%)' },
];

/** The two items that open a menu of their own beneath them. */
type Submenu = 'theme' | 'support';
const SUBMENUS: readonly Submenu[] = ['theme', 'support'];

/** The menu in the bar: the theme (opening beneath itself), the folders scanned, settings, Support (errors and the support page), help, About and Move to Recycle Bin (Move to Trash on a Mac). */
export function AppMenu({ theme, errorCount, onTheme, onErrors, onSupport, onChangeFolders, onClearResults, onSettings, onHelp, onAbout, onDeletePermanently, onClose }: Props) {
  const ref = useDismiss<HTMLDivElement>(onClose);
  // Choosing something from the menu puts the menu away.
  const pick = (action: () => void) => () => { action(); onClose(); };
  const item = 'flex w-full items-center rounded-[10px] px-3 py-2.5 text-left text-[14px] hover:bg-s2';
  // Theme and Support each open beneath themselves, one at a time, as a menu of its own: the right arrow opens it on
  // its first item, the left goes back.
  const [open, setOpen] = useState<Submenu | null>(null);
  const themeItem = useRef<HTMLButtonElement>(null);
  const supportItem = useRef<HTMLButtonElement>(null);
  const submenu = useRef<HTMLDivElement>(null);
  const toggle = (which: Submenu) => () => setOpen(now => (now === which ? null : which));
  // As in any menu: it opens on its first item, the arrow keys, Home and End go through the rest, and once it closes
  // focus is back on what opened it, which a window opened from the menu then gives focus back to in turn.
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>('[role^="menuitem"]')?.focus();
    return () => { if (opener?.isConnected) opener.focus(); };
  }, [ref]);
  function onKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
    const opener = (which: Submenu) => (which === 'theme' ? themeItem : supportItem).current!;
    const opening = SUBMENUS.find(which => document.activeElement === opener(which));
    if (e.key === 'ArrowRight' && opening) {
      e.preventDefault();
      // Drawn at once, so that its first item is there to be focused.
      flushSync(() => setOpen(opening));
      submenu.current!.querySelector<HTMLElement>('[role^="menuitem"]')!.focus();
      return;
    }
    if (e.key === 'ArrowLeft' && open && submenu.current?.contains(document.activeElement)) {
      e.preventDefault();
      setOpen(null);
      opener(open).focus();
      return;
    }
    const items = [...e.currentTarget.querySelectorAll<HTMLElement>('[role^="menuitem"]')];
    const at = items.indexOf(document.activeElement as HTMLElement);
    const to: Record<string, number> = { ArrowDown: at + 1, ArrowUp: (at <= 0 ? items.length : at) - 1, Home: 0, End: items.length - 1 };
    if (!(e.key in to)) return;
    e.preventDefault();
    items[to[e.key] % items.length].focus();
  }
  const chevron = (which: Submenu) => (
    <svg aria-hidden width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round"
      className={`ml-2 shrink-0 text-t2 transition-transform ${open === which ? 'rotate-90' : ''}`}><path d="M9 5l7 7-7 7" /></svg>
  );
  const beneath = 'mb-1 ml-3 border-l border-line pl-2';

  return (
    <div ref={ref} role="menu" aria-label="Menu" tabIndex={-1} onKeyDown={onKeyDown} className="fixed right-3 top-[58px] z-40 w-[min(300px,calc(100vw-24px))] rounded-2xl sm:right-6 border border-line bg-pop p-2 shadow-[0_20px_50px_var(--shadow)]">
      <button ref={themeItem} type="button" role="menuitem" aria-haspopup="menu" aria-expanded={open === 'theme'} onClick={toggle('theme')} className={item}>
        Theme<span className="ml-auto text-[13px] text-t3">{themes.find(t => t.id === theme)!.name}</span>{chevron('theme')}
      </button>
      {open === 'theme' && (
        <div ref={submenu} role="menu" aria-label="Theme" className={beneath}>
          {themes.map(t => (
            <button key={t.id} type="button" role="menuitemradio" aria-checked={theme === t.id} onClick={() => onTheme(t.id)}
              className={`${item} gap-2.5 ${theme === t.id ? 'font-semibold text-t1' : 'text-t2'}`}>
              <span aria-hidden className="h-3 w-3 shrink-0 rounded-full border border-line" style={{ background: t.swatch }} />{t.name}
              {theme === t.id && (
                <svg aria-hidden width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.6" strokeLinecap="round" strokeLinejoin="round"
                  className="ml-auto shrink-0 text-ident"><path d="M5 12l5 5L20 7" /></svg>
              )}
            </button>
          ))}
        </div>
      )}
      <div role="separator" className="my-1 border-t border-line" />
      {onChangeFolders && <button type="button" role="menuitem" onClick={pick(onChangeFolders)} className={item}>Change folders or rescan</button>}
      {onClearResults && <button type="button" role="menuitem" onClick={pick(onClearResults)} className={item}>Clear results</button>}
      <button type="button" role="menuitem" onClick={pick(onSettings)} className={item}>Settings</button>
      <button ref={supportItem} type="button" role="menuitem" aria-haspopup="menu" aria-expanded={open === 'support'} onClick={toggle('support')} className={item}>
        Support
        <span className="ml-auto font-mono text-[12px] text-rose-t">{errorCount > 0 && `${errorCount} ${errorCount === 1 ? 'error' : 'errors'}`}</span>{chevron('support')}
      </button>
      {open === 'support' && (
        <div ref={submenu} role="menu" aria-label="Support" className={beneath}>
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
