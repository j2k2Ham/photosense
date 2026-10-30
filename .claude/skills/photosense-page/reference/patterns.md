# Page patterns

- **Windows:** large ones use `WindowShell` (z-30, or z-40 when raised over another). The sending windows (`TransferParts.TransferWindow`) are z-40. The folder picker is z-[46].
- **Questions** use `ConfirmDialog` at z-[45], rose for anything that removes files. `small` is for a second question; `ready={false}` is for one still waiting for its data; `note` is the box under the body.
- **Escape:** each window handles its own. A window under a question ignores keys while the question is up (its `asking` or `frozen` prop), or one Escape closes both.
- **`Scrim`:** every modal window stands on one. It closes the window on a press outside it, keeps Tab inside the topmost window, and gives focus back when the window goes.
- **Messages:** `useToasts().push(message, kind, action?)` puts them in the one `role="log"` region, always on the page. They go after 4.5 s (`TOAST_MS`), or 9 s with an action such as Undo.
- **Styling:**
  - Colours are tokens in `app/globals.css` (`--bg`, `--s1` to `--s3`, `--t1` to `--t3`, `--brand`, `--rose`, `--line`, `--stage`), with the light theme under `.light`. Faint text (`--t3`) must still reach 4.5:1 on its background.
  - Buttons use `pill-brand`, `pill-outline`, `pill-quiet` and `pill-rose`. These are `whitespace-nowrap`; a long label that must fit a phone's width gets `min-h-11 max-w-full whitespace-normal`.
- **Settings** live in `lib/settings.ts` (the browser's localStorage). Remembered folders live in `lib/lastScan.ts`: `photosense-last-scan` and `photosense-organize-root`.
- **The icon** is `branding/app-icon.png` only, shown from `appIconUrl` (`/api/about/icon`). Never copy it into the page or an app.
- **Maps:** Google's only with `NEXT_PUBLIC_GOOGLE_MAPS_KEY` (in `.env.local`, which git ignores; never use or commit Jamie's key); otherwise the plain map. Markers are `AdvancedMarkerElement`, with `NEXT_PUBLIC_GOOGLE_MAPS_ID` or `DEMO_MAP_ID`.
- **Organize's state:** arrangements not yet carried out live in the page only. What was moved is recorded by the service (`organize_batches`), so undo survives a reload.
- **Android:** its web view hands the app no request bodies, so `apiClient`'s `json()` passes each body to `window.PhotoSenseAndroid.keep`, and the request carries `x-photosense-body`. Every request with a body goes through `json()`.
