import React, { useEffect } from 'react';
import type { Theme } from '../lib/theme';
import { useDismiss } from '../lib/useDismiss';

interface Props {
  readonly theme: Theme;
  readonly errorCount: number;
  onTheme(theme: Theme): void;
  onErrors(): void;
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

export function AppMenu({ theme, errorCount, onTheme, onErrors, onChangeFolders, onClearResults, onSettings, onHelp, onAbout, onDeletePermanently, onClose }: Props) {
  const ref = useDismiss<HTMLDivElement>(onClose);
  // Choosing something from the menu puts the menu away.
  const pick = (action: () => void) => () => { action(); onClose(); };
  const item = 'flex w-full items-center rounded-[10px] px-3 py-2.5 text-left text-[14px] hover:bg-s2';
  // As in any menu: it opens on its first item, the arrow keys, Home and End go through the rest, and once it closes
  // focus is back on what opened it, which a window opened from the menu then gives focus back to in turn.
  useEffect(() => {
    const opener = document.activeElement as HTMLElement | null;
    ref.current?.querySelector<HTMLElement>('[role^="menuitem"]')?.focus();
    return () => { if (opener?.isConnected) opener.focus(); };
  }, [ref]);
  function onKeyDown(e: React.KeyboardEvent<HTMLDivElement>) {
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
      <button type="button" role="menuitem" onClick={pick(onErrors)} className={item}>
        Errors<span className="ml-auto font-mono text-[12px] text-t3">{errorCount}</span>
      </button>
      <button type="button" role="menuitem" onClick={pick(onChangeFolders)} className={item}>Change folders or rescan</button>
      <button type="button" role="menuitem" onClick={pick(onClearResults)} className={item}>Clear results</button>
      <button type="button" role="menuitem" onClick={pick(onSettings)} className={item}>Settings</button>
      <button type="button" role="menuitem" onClick={pick(onHelp)} className={item}>Help</button>
      <button type="button" role="menuitem" onClick={pick(onAbout)} className={item}>About PhotoSense</button>
      <div role="separator" className="my-1 border-t border-line" />
      <button type="button" role="menuitem" onClick={pick(onDeletePermanently)} className={`${item} text-rose-t hover:bg-rose-bg`}
        title="Removed files wait in _PhotoSense_Removed inside each folder they were removed from. Delete permanently erases them.">Delete permanently</button>
    </div>
  );
}
