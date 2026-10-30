---
name: photosense-page
description: Use before changing or testing PhotoSense's React page in PhotoSense.ReactUI (app/, components/, lib/, types.ts, tests/). Covers its commands, the rules every change keeps, and its Vitest tests at 100% branch coverage.
---

# Changing the page

Next.js 16 (a static export), React 19, SWR, Tailwind 3, Vitest with Testing Library and jsdom.

## Commands, in PhotoSense.ReactUI

```sh
npm test                      # Vitest
npm run test:coverage         # fails below 100% of branches, lines, functions and statements
npx tsc --noEmit              # then delete the tsconfig.tsbuildinfo it leaves
npm run lint                  # ESLint 9, Next's rules and jsx-a11y strict: 0 errors; the 17 react-hooks warnings are known
```

- **`npm run build`** catches what tsc does not, such as `app/layout.tsx` importing a module that uses SWR. Never run it, or a second `next dev`, in `PhotoSense.ReactUI` while Jamie's app is running (`Get-NetTCPConnection -LocalPort 7071,3000`): build in a copy instead (`photosense-verify`).
- **A change that adds lint warnings is not done.** The known 17 stay 17. Most new ones come from calling `setState` synchronously in an effect; set state in a promise's callback instead.

## Every change keeps these

- **Requests** go through `lib/apiClient.ts`. One that changes files revalidates the SWR keys it affects (`refreshGroups`, `refreshOrganize`). `lib/apiBase.ts` holds the service's address; `app/layout.tsx` may import it but not `apiClient`.
- **Types:** `types.ts` mirrors `PhotoSense.Contracts`. Change both together.
- **Wording:** text names what the person sees, in short plain sentences. A button says exactly what happens ("Delete 3 files"), and the message after says what happened. A removal question names the file that goes and the file that stays.
- **Themes:** use the theme tokens (`text-t2`, `bg-pop`, `border-line`, the `pill-*` buttons), never raw colours, and check both themes.
- **The user guide:** `lib/guide.ts`, shown by Help, changes whenever something on screen is renamed, added or removed. So does the README when behaviour changes.
- **Phones:** `PHONES` (`iphone`, `android`, from `useAbout().data?.program`) decides what only a phone or only a computer shows.

Before building a window, dialog, message or setting, read `reference/patterns.md`: z-order, `Scrim`, `ConfirmDialog`, Escape, toasts, settings and the icon.

Before writing a test, read `reference/testing.md`: mocking the API client, what Istanbul counts, and the traps already met.
