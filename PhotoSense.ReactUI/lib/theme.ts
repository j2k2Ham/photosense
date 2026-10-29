import { useCallback, useEffect, useState } from 'react';
import { THEME_KEY } from './themeKey';

export type Theme = 'dark' | 'light';


function saved(): Theme {
  try { return localStorage.getItem(THEME_KEY) === 'light' ? 'light' : 'dark'; } catch { return 'dark'; }
}

/** The theme in use, and a way to change it. Dark unless the person chose light. */
export function useTheme(): [Theme, (theme: Theme) => void] {
  const [theme, setTheme] = useState<Theme>('dark');
  // Read after mounting: the server has no saved choice to render with.
  useEffect(() => { setTheme(saved()); }, []);

  const choose = useCallback((next: Theme) => {
    setTheme(next);
    document.documentElement.classList.toggle('light', next === 'light');
    // A browser that refuses storage still gets the theme for this visit.
    try { localStorage.setItem(THEME_KEY, next); } catch { /* not remembered */ }
  }, []);

  return [theme, choose];
}
