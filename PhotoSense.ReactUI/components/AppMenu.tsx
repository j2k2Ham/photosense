import React from 'react';
import type { Theme } from '../lib/theme';
import { useDismiss } from '../lib/useDismiss';

interface Props {
  readonly theme: Theme;
  readonly errorCount: number;
  onTheme(theme: Theme): void;
  onErrors(): void;
  onChangeFolders(): void;
  onClearResults(): void;
  onClose(): void;
}

const themes: { id: Theme; name: string; swatch: string }[] = [
  { id: 'dark', name: 'Dark', swatch: '#0c0f11' },
  { id: 'light', name: 'Light', swatch: '#f5f7f9' },
];

export function AppMenu({ theme, errorCount, onTheme, onErrors, onChangeFolders, onClearResults, onClose }: Props) {
  const ref = useDismiss<HTMLDivElement>(onClose);
  // Choosing something from the menu puts the menu away.
  const pick = (action: () => void) => () => { action(); onClose(); };
  const item = 'flex w-full items-center rounded-[10px] px-3 py-2.5 text-left text-[14px] hover:bg-s2';

  return (
    <div ref={ref} role="menu" aria-label="Menu" className="fixed right-6 top-[58px] z-40 w-[300px] rounded-2xl border border-line bg-pop p-2 shadow-[0_20px_50px_var(--shadow)]">
      <div className="label-caps px-3 pb-2 pt-2">Theme</div>
      <div className="mx-2 mb-2 flex rounded-full bg-s2 p-1" role="group" aria-label="Theme">
        {themes.map(t => (
          <button key={t.id} type="button" aria-pressed={theme === t.id} onClick={() => onTheme(t.id)}
            className={`pill h-8 flex-1 text-[13px] ${theme === t.id ? 'bg-pop font-semibold text-t1' : 'font-medium text-t2'}`}>
            <span aria-hidden className="h-2.5 w-2.5 rounded-full border border-line" style={{ background: t.swatch }} />{t.name}
          </button>
        ))}
      </div>
      <div className="my-1 border-t border-line" />
      <button type="button" role="menuitem" onClick={pick(onErrors)} className={item}>
        Errors<span className="ml-auto font-mono text-[12px] text-t3">{errorCount}</span>
      </button>
      <button type="button" role="menuitem" onClick={pick(onChangeFolders)} className={item}>Change folders or rescan</button>
      <button type="button" role="menuitem" onClick={pick(onClearResults)} className={item}>Clear results</button>
      <div className="my-1 border-t border-line" />
      <p className="px-3 py-2 text-[12.5px] text-t3">Removed files are in <span className="font-mono">_PhotoSense_Removed</span> inside each scanned folder.</p>
    </div>
  );
}
