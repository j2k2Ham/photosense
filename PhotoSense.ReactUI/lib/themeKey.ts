/** Where the chosen theme is remembered between visits. Kept apart from the hook so the page's layout, which runs on the server, can use it. */
export const THEME_KEY = 'photosense-theme';

/** The question a browser answers when its system is set to light, which the Auto theme follows. */
export const SYSTEM_LIGHT = '(prefers-color-scheme: light)';
