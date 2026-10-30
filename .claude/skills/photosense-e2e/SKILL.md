---
name: photosense-e2e
description: Use before running, writing or fixing PhotoSense's Playwright end-to-end tests in e2e/, or when a user-visible change needs checking in a real browser against the whole program.
---

# The end-to-end tests

`e2e/README.md` describes the suite and its fixtures; read it before writing a test. In short:

- Each test starts its own program (`.cache/server`, built by `npm run app`) on its own data folder and port.
- Each test makes its own photos in a temporary folder.
- Nothing is shared, and nobody's real photos are touched.

## Running

```sh
cd e2e
npm ci && npx playwright install chromium webkit     # once
npm run app                                           # again after any change to the page or the service
npx playwright test --project computer                # Chrome at a computer's size: every test
npx playwright test --project phone                   # WebKit as an iPhone 13: the tests named @phone
npx playwright test tests/organize-own.spec.ts -g "dragged"
npm run typecheck
```

- **Chrome:** `PHOTOSENSE_E2E_CHROME=1` uses the installed Chrome instead of Playwright's download.
- **Jamie's app:** `npm run app` builds the page in a copy, without `.env.local`, and publishes the service into `.cache` through `--artifacts-path`. It is safe while Jamie's app runs.
- **After a failure:** read `test-results/<test>/error-context.md`. It holds the page as a screen reader saw it at that moment: what was on screen, and the exact names.
- **A WebKit install that hangs on unpacking:** stop it, delete the half-made `%LOCALAPPDATA%\ms-playwright\webkit-<n>` and `__dirlock`, and install again.

## Writing a test

- **Imports:** `test`, `expect` and `message` from `fixtures/app.ts`; helpers from `fixtures/screens.ts` (`openOrganize`, `menu`, `tile`, `preview`, `question`, `drag`, `photoFiles`); photos from `fixtures/media.ts` (`PLACES`, `picture`, `png`, `movie`, `sidecar`).
- **Setting up:** through the program, as in `await service.scan(photos.root)`, then `page.goto('/')`. Organize needs `openOrganize(page, root)`: a scan through the service does not tell the page which folder it was.
- **Selectors:** by role and accessible name, as a screen reader reads them. To find a name, print `await page.getByRole('main').ariaSnapshot()` in a throwaway test.
- **What to check:** what the person sees, and what is on the disk afterwards (`photos.files()`, `photos.exists()`). A removal checks `_PhotoSense_Removed`; an undo checks that every file is back.
- **What only a phone or another device answers:** stand in for it with `fixtures/phone.ts` (`Phone`, `PhoneNearby`). Everything else stays the program's own.
- **`@phone`:** add it to a test's name when it must also work at a phone's size and in WebKit.

## Traps already met

- **Which copy is kept** is PhotoSense's choice, not the order the test made the files in. Check that one whole set stays, not which folder it is in.
- **Messages** go after 4.5 s. Check one straight after the action that shows it.
- **Switches and boxes drawn over a hidden input** (`sr-only`) are clicked by their label, as a person does.
- **HTML drag and drop** is not started by Playwright's mouse here; use `drag(page, from, onto)`.
- **Organize's first listing** with no scan behind it can take tens of seconds while the program loads place names. `openOrganize` allows a minute.
- **Organize's search** knows month names ("February") and short dates ("Feb 2, 2023"), not "Feb 2023".
- **Delete permanently's "do not ask again"** is kept only when the question is confirmed.
- **The accessibility check** (`accessibility.spec.ts`) fails on real contrast problems. Fix the token, do not loosen the rule.
