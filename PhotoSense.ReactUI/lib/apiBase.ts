// Where the service answers. The page a program hands out itself (the service's own, the phone app's) is built with /api.
// Kept apart from apiClient so that the page's frame (app/layout.tsx), which is drawn ahead of time, can use it too.
export const API_BASE = process.env.NEXT_PUBLIC_API_BASE ?? 'http://localhost:7071/api';

/** PhotoSense's icon, kept once for every program (branding/app-icon.png) and handed out by the program itself. */
export const appIconUrl = `${API_BASE}/about/icon`;
