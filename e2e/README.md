# End-to-end tests

PhotoSense as one program, the way `publish.ps1` makes it, with its page in a browser, driven as a person would use it: scanning, Clean up, Organize, Delete permanently, the menu, Help and About, and the phone's and the computer's halves of sending photos between them.

Each test starts a program of its own, on a data folder of its own and a free port, and makes the photos it works on in a temporary folder of its own: JPEGs carrying the date, place and camera a phone writes, exact copies, similar shots, PNG exports, Live Photos (a picture and its video naming one identifier) and edit files (`fixtures/media.ts`). Tests share nothing, so they run side by side. Nobody's own photos are ever touched.

## Running them

```sh
cd e2e
npm ci
npx playwright install chromium webkit
npm run app      # builds the page and the service into .cache (again after changing either)
npm test
```

- `npm run app` builds the page in a copy of `PhotoSense.ReactUI`, without that folder's `.env.local`, so a page server running there is left alone and no map key gets into the program; the service's build files go into `.cache` as well, never into the repository's `bin` and `obj`.
- `npm test -- --project computer` runs Chrome at a computer's size only; `PHOTOSENSE_E2E_CHROME=1` uses the Chrome installed on this computer instead of Playwright's own.
- `npm run report` opens what the last run found, with a trace of each test that failed.

## What runs where

| Project | Browser | Tests |
|---|---|---|
| `computer` | Chrome, 1440 × 900 | every test |
| `phone` | WebKit, as an iPhone 13 | those marked `@phone`: what has to work at a phone's size and in the engine the iPhone app shows the page in |

The program the tests start is a computer's. What only a phone or another device answers (that it is the iPhone app, how much of its library it may see, a phone found on the network and sending to it) is stood in for in the browser (`fixtures/phone.ts`); everything else is the program's own, on real files. The phones' own apps are checked by their workflows on GitHub's simulator and emulator (`iphone.yml`, `android.yml`).

`accessibility.spec.ts` checks each screen against the WCAG 2.1 A and AA rules axe-core can find for itself, in both themes.

## Writing one

- `test`, `expect` and `message` come from `fixtures/app.ts`: the `program`, `photos` (the test's folder, with `picture`, `livePhoto`, `movie`, `sidecar`, `copy`, `files` and `exists`) and `service` (to set a test up through the program, as `service.scan(photos.root)` does).
- `fixtures/screens.ts` gets to the parts of the page, by the names a screen reader would read: `openOrganize`, `menu`, `tile`, `preview`, `question`, `drag`.
- A test says what a person sees and what is on the disk afterwards; a removal checks `_PhotoSense_Removed`, an undo that every file is back.
- Mark it `@phone` in its name when it should also run at a phone's size.
