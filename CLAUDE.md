# PhotoSense — notes for Claude

PhotoSense is a photo and video tidying tool that runs on the machine holding the photos. **Clean up** scans folders, groups exact duplicates and similar shots, keeps the best copy of each and lets the rest be removed after review. **Organize** sorts a folder's files into folders by place and date, with a preview before anything moves. The owner, Jamie, uses it on their own iPhone library (HEIC, JPEG, Live Photo MOVs, `.AAE` sidecars) on Windows. The README describes behaviour in detail; the in-app user guide (`PhotoSense.ReactUI/lib/guide.ts`) describes it for users.

## Layout

| Folder | What is in it |
|---|---|
| `PhotoSense.Domain` | Entities, value objects, store and service interfaces, `PhotoStorageOptions` |
| `PhotoSense.Application` | Scanning, grouping and removal (`Scanning/`), Organize (`Organizing/`), erasing removed files (`Removal/`) |
| `PhotoSense.Infrastructure` | What both hosts run: LiteDB stores, hashing, metadata, place names (GeoNames), the computer's disks (`Files/DiskFileSystem`), companions, a phone's library as a folder (`Library/`), and the shared DI (`AddPhotoSenseCore`; `AddPhotoSensePhone` for a phone) |
| `PhotoSense.Desktop` | What only a computer has: Magick.NET imaging, the folder browser, `ShellSystemViewer` |
| `PhotoSense.Api` | The endpoints, apart from any host: `Endpoints/`, `Scanning/` (with `ScanRunner`), the route table `PhotoSenseRoutes`, `Http/` (`ApiRequest`, `ApiResponse`, `ApiRouter`), `Hosting/` (`InProcessApi`, `PageFiles`) and `AddPhotoSenseApi` |
| `PhotoSense.Server` | The service as one ASP.NET Core program: `PhotoSenseServer` (CORS, the bundled page, `/api` to `ApiBridge`), `AddPhotoSenseComputer` in `PhotoSenseServices.cs` |
| `PhotoSense.iOS` | The iPhone/iPad app: a `WKWebView` whose `app://` requests `PageSchemeHandler` answers in-process, `PhotoKitLibrary`, `ImageIOAnalyzer`, `SelfCheck`. Builds only on a Mac (`.github/workflows/iphone.yml`); not in `PhotoSense.sln` |
| `PhotoSense.Contracts` | JSON DTOs sent to the UI; `PhotoSense.ReactUI/types.ts` mirrors them by hand |
| `PhotoSense.ReactUI` | Next.js 16 / React 19 / SWR / Tailwind 3 UI: `app/page.tsx`, `components/`, `components/organize/`, `lib/` |
| `tools/` | Node scripts the build runs, tested in `tools/tests`: `version.mjs` (each program's version, from `version.json` and the git history), `notices.mjs` (the third-party notices in `notices/`), `site.mjs` (the web site from `site/pages`) |
| `version.json`, `product.json` | The next kind of release and whether the programs share a version; who makes PhotoSense and how to reach them (built into `PhotoSense.Api`) |
| `site/pages/`, `branding/app-icon.png` | The privacy policy, terms and support page, shown by About and made into the web site; the one copy of the icon |
| `PhotoSense.Tests` | TUnit tests of everything except the UI |
| `PhotoSense.Benchmarks` | BenchmarkDotNet runs of near-duplicate matching |
| `docs/design_handoff_photosense_redesign` | Jamie's design handoff. Leave it alone and out of commits unless asked |

## Architecture and stack

- **Clean Architecture:** Domain ← Application ← Infrastructure, with `PhotoSense.Server` as the host that wires them together. `PhotoSense.Api` depends on Domain, Application and Contracts only, never on Infrastructure or ASP.NET Core, because the phone app serves the same endpoints. Domain and Application never reference Infrastructure or a host; new storage or OS access goes behind an interface in Domain.
- **Where photos live:** everything that lists, reads, moves, copies or erases photos goes through `IPhotoFileSystem` (Domain): `DiskFileSystem` on a computer, `PhotoLibraryFileSystem` over `IPhotoLibrary` on a phone. Domain, Application and Api never use `File`, `Directory`, `FileInfo` or `FileStream` on photos. PhotoSense's own data (LiteDB, thumbnails) stays on the program's own disk. On a phone a folder is an album PhotoSense made (`PhotoLibraryFileSystem`): a move changes which of its albums a picture is in, removal puts it in the **PhotoSense Removed** album, erasing sends it to Recently Deleted, and copies are refused (`NoCopies`). A file system that takes many changes as one implements `IPhotoChangesTogether`, and `FileChanges` and `RemovedFiles` then hand each change over whole.
- **.NET 10**, one ASP.NET Core program (`PhotoSense.Server`), no Azure Functions, Azurite or SignalR. Not minimal APIs or controllers: endpoints are methods taking an `ApiRequest` and returning an `ApiResponse`, and every address is listed in `PhotoSenseRoutes`.
- **Storage:** LiteDB in one file. No EF Core, no SQL server, no migrations.
- **Auth:** none. The service runs on the user's own machine; `RequestGuard` is the only gate (see Service patterns). No JWT, OIDC or cookies.
- **Tests:** TUnit with Moq and coverlet (`coverlet.MTP`), not xUnit, FluentAssertions or Testcontainers.
- **Imaging and metadata:** Magick.NET (Q8) on a computer, ImageIO on a phone, both measuring through `ImageFingerprint`; MetadataExtractor on both.
- **The phone app:** plain .NET for iOS (not MAUI), only the thin layer over Apple's frameworks; anything that decides goes in shared code with tests. It is checked on GitHub's simulator by `SelfCheck` and on a device by hand, as Jamie agreed.
- Generic .NET advice that assumes another stack (EF Core, minimal APIs, xUnit) does not apply here; follow this file.

## Running and testing

- **Run the app the way Jamie does:** `./dev-start.ps1` from the repo root. It builds and starts the service (7071) and the UI (3000); on a Mac it runs in PowerShell 7 as `pwsh ./dev-start.ps1`. `./dev-start.ps1 -Stop` stops a running copy; Ctrl+C in its terminal also stops everything. The UI hot-reloads source changes; the service does not, so service changes need a restart.
- **Data** lives in `%LOCALAPPDATA%\PhotoSense` (`photosense.db`, thumbnails), never under `bin`. The options validator refuses a data folder inside the program folder.
- **Build:** `dotnet build PhotoSense.sln -c Release -nodeReuse:false -warnaserror`. CI builds with warnings as errors, so build that way locally.
- **Service tests (TUnit, a program of its own):** `dotnet run --project PhotoSense.Tests -c Release`. One class: append `-- --treenode-filter "/*/*/ClassName/*"`. Coverage: append `-- --coverlet --coverlet-output-format opencover --coverlet-include "[PhotoSense.*]*" --coverlet-exclude "[PhotoSense.Tests]*" --coverlet-exclude-by-file "**/*.g.cs" --results-directory PhotoSense.Tests/coverage`.
- **UI tests (Vitest + Testing Library + jsdom):** in `PhotoSense.ReactUI`, `npm test`, or `npm run test:coverage`, which fails below 100%. Type-check with `npx tsc --noEmit`, then delete the `tsconfig.tsbuildinfo` it leaves. `npm run lint` (ESLint 9, Next's rules and jsx-a11y's strict set) must report no errors; the 17 `react-hooks` compiler warnings are known. `npm run build` catches what tsc does not, such as a server-rendered file (`app/layout.tsx`) importing a module that uses SWR.
- **Build-tool tests (node:test):** from the root, `node --test --experimental-test-coverage --test-coverage-include="tools/*.mjs" --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 tools/tests/`. They also check `notices/` is current, so restore and `npm ci` first.
- **After adding or updating a package** (NuGet or the page's), run `node tools/notices.mjs` and commit `notices/`; CI fails while they are out of date. A package that names a licence without its text needs `tools/licenses/<SPDX>.txt` or a `NAMED_ONLY` entry; a font added to `app/layout.tsx` needs its licence in `tools/fonts`.
- **CI** (`.github/workflows/ci.yml`) runs on Ubuntu for pushes and pull requests to `main`/`master`: build, UI tests with coverage, `npm run build`, service tests with coverage; a second job builds and tests the service on Windows and macOS, publishes it as one program (`publish.ps1`), starts it and asks it for the page and `/api/health`. The feature branches have never been through it. Some service tests that hold a file open to make it fail were only ever run on Windows.

## Rules

1. **100% branch coverage in all three parts.** Every change ships with tests that keep the service, the UI and the build tools (`tools/`) at 100% of branches. Report the measured figures when done, not a guess.
2. **Nothing is ever replaced, and nothing is erased except by "Delete permanently".**
   - Removing a file moves it into `_PhotoSense_Removed` inside the scanned or organized folder, keeping its relative path, and is undoable where the UI offers Undo.
   - The only code that erases files is `RemovedFiles.EraseAsync`, through `IPhotoFileSystem.Erase`, which nothing else calls. It only empties folders named `_PhotoSense_Removed`.
   - A file arriving under a taken name gets a number.
   - The last copy of a picture is never removed as a duplicate.
3. **Every removal question and message names the file that goes and the file that stays**, by folder when the names are alike. Jamie has twice taken two files for one.
4. **Jamie's real photos are read-only to us.** `C:\Users\jsham\Phone Pictures` and `F:\PC_backup` may be scanned and read. Never move, delete or organize files there when testing: copy a few files to a scratch folder and test on the copy.
5. **Leave the running app alone.** Before building, cleaning or deleting anything under `bin`, `obj` or `.next`, check whether Jamie's app is running (`Get-NetTCPConnection -LocalPort 7071,3000`). While it runs:
   - build in Release;
   - do not run `next build` or a second `next dev` in `PhotoSense.ReactUI`;
   - never stop Jamie's processes.

   "It's locked, so it will refuse" is not a safeguard for a folder: a recursive delete removes everything it can first.
6. **Never use or commit Jamie's Google Maps key.** It is in `PhotoSense.ReactUI/.env.local`, which git ignores. For map checks, start a scratch UI with a made-up key, and strip `key=` from the Maps script request in Playwright to get Google's unbilled development-mode map.
7. **Git:**
   - Commit or push only when Jamie asks, and ask each time; there is no standing go-ahead.
   - Do not use `git stash`: Jamie keeps uncommitted work of their own in the tree.
   - Do not stage deletions with `git rm`: a partial commit once captured only the deletions.
   - Leave build output and coverage reports out of commits.
8. **Check before claiming.**
   - Try a command or key press before telling Jamie to use it.
   - Verify end to end with default settings and the same start-up path Jamie uses. An override of the data path once hid a total failure.
   - Try new features on the largest, slowest real case, such as the 48,000-file external disk.
   - When Jamie reports odd behaviour, reproduce it, or read a copy of the database first (copy `photosense.db` while the service is stopped), before changing code. Say plainly what was and was not verified.
9. **Ask before guessing on product decisions that change what gets deleted.** Make a reasonable choice when the request is clear enough, and name any unconfirmed decision in the summary.

## Service patterns

- **Registering:** endpoint classes get their services through the constructor. Register services both hosts run in `AddPhotoSenseCore`, a host's own in `AddPhotoSenseComputer` or `AddPhotoSensePhone`, and endpoint classes in `AddPhotoSenseApi`; a factory lambda is used where a constructor has optional parameters. Add each address to `PhotoSenseRoutes`, a literal address before one a parameter could match (`scan/progress/latest` would go before `scan/progress/{instanceId}`).
- **About and the program:** `ProductInfo` (`product.json`, built into `PhotoSense.Api`) is the same for every program; each host registers its own `ProgramInfo` (its name, its version from its assembly, its notices file). `IPhotoAccess` says what a phone has let PhotoSense see (`FolderAccess` on a computer, always everything).
- **Request guard:** an endpoint that reveals or changes someone's files checks `RequestGuard.Allows(req.Headers)` first (the `x-photosense-client` header, which other web pages cannot send). The server answers only under this machine's own names (`AllowedHosts`, by default `localhost;127.0.0.1;[::1]`, set in `PhotoSenseServer.Build`), so a page renamed to this machine by DNS rebinding cannot send the header as its own. Refusals carry a plain-text reason and a status that says whose fault it is.
- **Answers:** `ApiResponse` keeps the status it is given, before or after writing, and writes JSON with property names as declared (not camelCase), as the Functions host did and the page expects.
- **LiteDB:** one shared `LiteDatabase`. Each store's constructor calls `LiteDbMapping.Prepare<T>()` for every mapped type before first use; LiteDB 5 otherwise fails now and then when two stores are first made together.
- **Paths:** compare file paths with `PhotoPath.Key` / `PhotoPath.Comparer`, which are case-blind on Windows and macOS and case-sensitive on Linux.
- **Organize file ids:** an HMAC of the path with a key made per run of the service, so a request can never name a path of its own choosing.
- **Testability seams:** code that would ask the OS or the disk takes that as an argument so every branch runs on one OS, as in `PhotoPath.Key(path, ignoreCase)`, and every class that touches photos takes an `IPhotoFileSystem`. Prefer a seam like that to a test that depends on Windows file locking.
- **Scan state:** scans run in the background through `ScanRunner`, one at a time, with progress held in memory (`InMemoryScanProgressStore`); one cut short by a stop is not resumed. `/scan/status` reports the scan running now, and the page follows a running scan even after a reload.

## UI patterns

- **API calls** all go through `lib/apiClient.ts`. Calls that change files revalidate the SWR keys they affect (`refreshGroups`, `refreshOrganize`). The service's address is in `lib/apiBase.ts`, which the server-rendered `app/layout.tsx` may import (`apiClient` it may not: SWR's server build has no `mutate`).
- **The icon** is `branding/app-icon.png` only; the page shows it from `appIconUrl` (`/api/about/icon`). Never copy it into the page or the app.
- **Types:** `types.ts` mirrors `PhotoSense.Contracts`. Update both when a field changes.
- **Styling:** colours are theme tokens in `app/globals.css` (`--bg`, `--t1`, `--brand`, `--rose`, ...), used through Tailwind classes such as `bg-pop`, `text-t2` and `border-line`. Buttons use the `pill-*` classes. Every screen must work in both the dark and the light theme.
- **Windows and dialogs:** large windows use `WindowShell` (z-30, or z-40 when raised over another). Questions use `ConfirmDialog` (z-[45], rose for anything that removes files); `small` and `ready` cover a second question and a question still waiting for data. Each window handles its own Escape. A window under a question must ignore keys while the question is up (the `asking` / `frozen` props), or one Escape closes both. Every modal window stands on `Scrim`, which closes it on a press outside it, keeps Tab inside the topmost window and gives focus back when it goes. Messages (`Toaster`) go into one `role="log"` region that is always on the page.
- **Settings** live in `lib/settings.ts` (browser localStorage). Remembered folders live in `lib/lastScan.ts`.
- **The user guide** (`lib/guide.ts`, shown by `components/HelpWindow.tsx` from Help in the menu) must change whenever something on screen is renamed, added or removed. Its tests check its structure.
- **Organize state:** arrangements that have not been carried out live in the page only. What was moved is recorded in the service (`organize_batches`), so undo survives a reload.

## Test patterns

- **TUnit:**
  - Every assertion is awaited (`await Assert.That(x).IsEqualTo(y)`); an un-awaited one is never checked. CS4014 is an error in the test project.
  - Collections compare with `IsEquivalentTo(expected, CollectionOrdering.Matching)` or `.Any`.
  - `Assert.ThrowsAsync<T>` returns `T?`.
  - Tests run in parallel, even within a class; shared static state needs `[NotInParallel("key")]`.
- **Service helpers:**
  - `OrganizeFixture` (`Tests/Application/OrganizeTesting.cs`) gives a temp folder, an in-memory repository, the Organize services, `Mover()` and `Removed()`.
  - `Http.Get/Post/Delete(...)` with `.FromClient()` (`Tests/Endpoints/ApiTesting.cs`) build requests for endpoint tests; `ServerFixture` gives the service's registrations on a data folder of its own.
  - `FailingDisk` (`Tests/Application/ChangesTesting.cs`) is the real `DiskFileSystem` except that a test makes "move", "copy", "make", "remove folder", "delete", "erase" or "read" fail for the paths it names, on any system. `ChangesFixture` and `OrganizeFixture` use one (`Disk`).
  - `InMemoryFileSystem` (`Tests/InMemoryFileSystem.cs`) keeps photos in memory only; `PhotosNotOnDiskTests` runs Clean up and Organize on it end to end.
  - `FakePhotoLibrary` (`Tests/FakePhotoLibrary.cs`) is a phone's library in memory; `PhotosOnAPhoneTests` runs the phone's whole stack (`AddPhotoSensePhone`, requests through `InProcessApi`) on it, with Magick.NET standing in for ImageIO.
  - The iOS app cannot be built here: the Mono toolchain pack for the current runtime is missing, and installing it needs admin. Its compile check is the `iPhone app` workflow, which runs on pushes, so each try needs Jamie's go-ahead to push.
  - `TestFiles` has `Disk` (the real disk), `LinkFolder` and `DenyAccess`.
- **UI tests:**
  - Tests live in `tests/`, mirroring `app`, `components` and `lib`.
  - `lib/apiClient` is mocked with `vi.hoisted` plus `vi.mock(..., importOriginal)`. Fixtures are in `tests/fixtures.ts`; `deferred` is in `tests/deferred.ts`.
  - Istanbul counts each arm of `if`, ternaries, `&&`, `||`, `??` and default parameter values, but not `?.`.
  - Test names are plain sentences that say what the user sees ("asks first, then takes the files off the page at once and offers to put them back").

## How things are written here

- **Comments** say *why*, in plain English, usually one line. They never say what the code does or narrate a change. Names describe what the user would recognise.
- **UI text:**
  - Name what the person sees, in short plain sentences, without jargon or apologies.
  - Buttons say exactly what happens ("Delete 3 files"), and the message after says what happened.
  - Errors say what went wrong and what to do.
- **The README** documents behaviour and the API table. Update it with any endpoint or user-visible change.
- **Reports to Jamie:** say what changed, what was verified and how, what was not verified, and any decision Jamie has not confirmed. Use they/them for Jamie.

## Environment traps (Windows)

- Git Bash rewrites MSBuild switches written with a slash (`/p:`, `/warnaserror`) into paths. Write them with a dash.
- Idle MSBuild workers keep DLLs locked and break the next build (MSB3027). Build with `-nodeReuse:false`.
- Multi-line heredocs with quotes or backslashes often break in the Bash tool. Write the script to the session scratchpad and run it with `python -I path`.
- `rm -rf` follows junctions. Never link a scratch UI copy to the repo's `node_modules`; give it its own `npm ci`, and remove a junction only with `[IO.Directory]::Delete(path)`.
- **Scratch copy of the app for end-to-end checks:**
  - Service: copy `PhotoSense.Server/bin/Release/net10.0` to the scratchpad and run `dotnet PhotoSense.Server.dll --urls http://localhost:7099 --Cors:Origins:0=http://localhost:3100`, with `PhotoStorage__DatabasePath` and `PhotoStorage__ThumbnailPath` pointing at scratch. Or `./publish.ps1 -Output <scratch>` and run the program there, which serves the page itself.
  - UI: robocopy `PhotoSense.ReactUI` to the scratchpad, excluding `node_modules`, `.next` and `.env.local`, run its own `npm ci`, then `npx next dev -p 3100` with `NEXT_PUBLIC_API_BASE=http://localhost:7099/api`.
  - Drive it with `playwright-core` and `chromium.launch({ channel: 'chrome' })`.
  - To see Jamie's data without touching it, point the scratch service at a *copy* of the database, and abort every non-GET request in Playwright.
