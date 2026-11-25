---
name: trovolve-page
description: Use before changing or testing Trovolve's React page in Trovolve.ReactUI (app/, components/, lib/, types.ts, tests/). Covers its commands, the rules every change keeps, and its Vitest tests at 100% branch coverage.
---

# Changing the page

Next.js 16 (a static export), React 19, SWR, Tailwind 3, Vitest with Testing Library and jsdom.

## Commands, in Trovolve.ReactUI

```sh
npm test                      # Vitest
npm run test:coverage         # fails below 100% of branches, lines, functions and statements
npx tsc --noEmit              # then delete the tsconfig.tsbuildinfo it leaves
npm run lint                  # ESLint 9, Next's rules and jsx-a11y strict: no errors and no warnings
```

- **Run Vitest from a folder written with a capital drive letter** (PowerShell's `C:\dev\…`). From Git Bash (`/c/dev/…`, which Node sees as `c:/dev/…`) Vite loads Vitest twice and every file fails with "Vitest failed to find the current suite".
- **Every test starts with the service saying the note asking for a backup was accepted**: `tests/setup.ts` answers `/terms` for the real `fetch`. A test of the note stands in `useTerms`, `acceptTerms` and `termsNow` through the API client's mock, with a store the page subscribes to (`tests/components/phone/firstRun.test.tsx`).
- **`npm run build`** catches what tsc does not, such as `app/layout.tsx` importing a module that uses SWR. Never run it, or a second `next dev`, in `Trovolve.ReactUI` while Jamie's app is running (`Get-NetTCPConnection -LocalPort 7071,3000`): build in a copy instead (`trovolve-verify`).
- **Lint has no warnings, and stays so.** React's `set-state-in-effect` and `refs` rules are errors (Jamie, 2026-11-17). Instead of setting state straight from an effect: read browser storage with `useStored` (`lib/stored.ts`, on `useSyncExternalStore`); keep an answer with the question it answers and drop it when the question changes (`MovePreview`); adjust state while rendering when something it follows changes (`app/page.tsx`, the scan followed); or clear it in the effect's cleanup (`useOrganizeReading`). A callback an effect hands to something outside React is a `useEffectEvent`, not a ref written while rendering (`GoogleFolderMap`).

## Every change keeps these

- **Requests** go through `lib/apiClient.ts`. One that changes files revalidates the SWR keys it affects (`refreshGroups`, `refreshOrganize`). `lib/apiBase.ts` holds the service's address; `app/layout.tsx` may import it but not `apiClient`.
- **Types:** `types.ts` mirrors `Trovolve.Contracts`. Change both together.
- **TSDoc:** every export says what it is for in a `/** … */` just above it (CLAUDE.md). `tests/documentation.test.ts` fails on one that does not.
- **Wording:** text names what the person sees, in short plain sentences. A button says exactly what happens ("Delete 3 files"), and the message after says what happened. A removal question names the file that goes and the file that stays.
- **Themes:** use the theme tokens (`text-t2`, `bg-pop`, `border-line`, the `pill-*` buttons), never raw colours, and check both themes.
- **The user guide:** `lib/guide.ts`, shown by Help, changes whenever something on screen is renamed, added or removed. So does the README when behaviour changes.
- **Phones:** `platformOf(useAbout().data?.program)` (`lib/phone.ts`) is `ios`, `android`, or none on a computer. On a phone or tablet the page is laid out by `components/phone/` instead of the computer's bar and menu (`PhoneApp`, `PhoneShell`), in the skin `useSkin` sets (`data-skin`, `app/globals.css`) and the theme `useTheme` gives (Auto, Dark, Light). The design is `docs/design_handoff_trovolve_mobile` (untracked); `STEPS.md` has the steps still to build.
  - A phone's parts read where they are from `usePhone()`: the platform, phone or tablet (768 px wide or more), the platform's words (`album` or `folder`), and what stands at the foot.
  - Anything opened over a screen (a pushed screen, a sheet, a full-screen view) calls `useBackLayer(open, close)`, so that Android's Back and iOS's edge swipe close it. One opened as another closes takes over its history entry; going back is never done at once, or it would close the new one.
  - A sheet or full-screen view opened from inside a tab is drawn with `createPortal(…, document.body)`: the shell's screens are a fixed layer of their own, and anything inside them stays under the tab bar whatever its z-index (`Library.tsx`).
  - Colours come from the skin's own tokens (`bg-cell`, `text-acc`, `bg-bar`, `text-danger-t`); sizes that differ by platform from its variables (`h-[var(--btn-h)]`, `rounded-cell`).

Before building a window, dialog, message or setting, read `reference/patterns.md`: z-order, `Scrim`, `ConfirmDialog`, Escape, toasts, settings and the icon.

Before writing a test, read `reference/testing.md`: mocking the API client, what Istanbul counts, and the traps already met.
