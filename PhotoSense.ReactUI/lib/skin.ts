import { useEffect } from 'react';
import { platformOf } from './phone';
import type { Theme } from './theme';

/** How a phone or tablet draws the page: its own platform's look (iOS 26, or Material 3 on Android), dark or light. */
export type Skin = `${'ios' | 'and'}-native-${Theme}`;

/** The skin for the program the page runs in, or none on a computer, which keeps its own look. */
export function skinOf(program: string | undefined, theme: Theme): Skin | undefined {
  const platform = platformOf(program);
  if (!platform) return undefined;
  return `${platform === 'ios' ? 'ios' : 'and'}-native-${theme}`;
}

/**
 * Puts the skin for the program and theme on the page's root, as `data-skin`, where `app/globals.css` gives it its
 * colours, type and sizes; on a computer there is none. Returns the skin.
 */
export function useSkin(program: string | undefined, theme: Theme): Skin | undefined {
  const skin = skinOf(program, theme);
  useEffect(() => {
    const root = document.documentElement;
    if (skin) root.dataset.skin = skin;
    else delete root.dataset.skin;
  }, [skin]);
  return skin;
}
