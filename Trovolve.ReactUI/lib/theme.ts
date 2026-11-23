import { useCallback, useEffect, useSyncExternalStore } from 'react';
import { useStored } from './stored';
import { SYSTEM_LIGHT, THEME_KEY } from './themeKey';

/** The page's colours: dark or light. */
export type Theme = 'dark' | 'light';

/** What the person chose: dark, light, or Auto, which follows the system's own setting. */
export type ThemeChoice = Theme | 'auto';

const CHOICES: readonly string[] = ['dark', 'light', 'auto'];

// Nothing saved, or what is saved is not a theme: undefined, so that the caller's own choice applies.
const choiceIn = (kept: string | null): ThemeChoice | undefined => (kept !== null && CHOICES.includes(kept) ? (kept as ThemeChoice) : undefined);

function watchSystem(changed: () => void) {
  const query = window.matchMedia(SYSTEM_LIGHT);
  query.addEventListener('change', changed);
  return () => query.removeEventListener('change', changed);
}
const systemIsLight = () => window.matchMedia(SYSTEM_LIGHT).matches;
// The page is made ahead on the server, which has no system to ask.
const lightOnTheServer = () => false;

/**
 * The theme in use, a way to change it, and what was chosen. Auto follows the system's own setting, and changes
 * with it. With nothing chosen yet it is `unchosen`: dark on a computer, Auto on a phone.
 */
export function useTheme(unchosen: ThemeChoice = 'dark'): [Theme, (choice: ThemeChoice) => void, ThemeChoice] {
  // Null until the saved choice is read: the server has none to render with, and the page must not change theme
  // before it knows, or a page saved light would flash dark.
  const [kept, keepChoice] = useStored(THEME_KEY);
  const chosen = kept === undefined ? null : choiceIn(kept);
  const systemLight = useSyncExternalStore(watchSystem, systemIsLight, lightOnTheServer);

  const choice = chosen ?? unchosen;
  const theme: Theme = choice === 'auto' ? (systemLight ? 'light' : 'dark') : choice;
  const known = chosen !== null;
  useEffect(() => {
    if (known) document.documentElement.classList.toggle('light', theme === 'light');
  }, [known, theme]);

  // A browser that refuses storage still gets the theme for this visit.
  const choose = useCallback((next: ThemeChoice) => keepChoice(() => next), [keepChoice]);

  return [theme, choose, choice];
}
