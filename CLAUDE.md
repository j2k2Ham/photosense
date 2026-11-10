# PhotoSense — notes for Claude

PhotoSense is a photo and video tidying tool that runs on the machine holding the photos. **Clean up** scans folders, groups exact duplicates and similar shots, keeps the best copy of each and lets the rest be removed after review. **Organize** sorts a folder's files into folders by place and date, with a preview before anything moves. The owner, Jamie, uses it on their own iPhone library (HEIC, JPEG, Live Photo MOVs, `.AAE` sidecars) on Windows. The README describes behaviour in detail; the in-app user guide (`PhotoSense.ReactUI/lib/guide.ts`) describes it for users.

**What Jamie has asked for and what is left to do** are in `STEPS.md`, imported here so that every session starts from it. Keep it current: tick a step once it is done and verified, add new steps, questions and call-outs as they come up, and move a question into the step it settles once Jamie answers.

@STEPS.md

## Skills

Each kind of work has a skill in `.claude/skills`. Load it before starting; its reference files are read only when the skill says to.

| Skill | For |
|---|---|
| `photosense-service` | The C# service and its TUnit tests: where code goes, endpoints, patterns, coverage |
| `photosense-page` | The React page and its Vitest tests |
| `photosense-e2e` | The Playwright end-to-end tests in `e2e/` |
| `photosense-phone-apps` | The iPhone and Android apps, and their GitHub checks |
| `photosense-verify` | Running it as Jamie does, scratch copies, reproducing a report, Jamie's data, Windows traps |
| `photosense-build-tools` | Versions, third-party notices, the web site, `product.json`, the icon, adding a package |
| `photosense-finish` | Before saying work is done, committing or pushing: checks, documents, the report, git |

## Layout

| Folder | What is in it |
|---|---|
| `PhotoSense.Domain` | Entities, value objects, store and service interfaces (`IPhotoFileSystem`), `PhotoStorageOptions`, and the rules that need nothing else: what counts as the same picture (`Matching/`), file names (`NameRules`, `PhotoNaming`), which files a scan takes in. No packages |
| `PhotoSense.Application` | Scanning and grouping (`Scanning/`), Organize (`Organizing/`), changes made and undone whole (`Changes/`), removing and erasing (`Removal/`), sending from a phone to a computer and its protocol (`Transfer/`); registered by `AddPhotoSenseApplication` |
| `PhotoSense.Infrastructure` | What every host runs: LiteDB stores, hashing, metadata, place names, the computer's disks, a phone's photos as folders (`Library/`), finding phones (`Transfer/`); registered by `AddPhotoSenseCore`, with the phones' composition (`AddPhotoSensePhone`, `AddPhotoSenseAndroid`) |
| `PhotoSense.Desktop` | What only a computer has: Magick.NET, the folder browser, `ShellSystemViewer` |
| `PhotoSense.Api` | The endpoints apart from any host, the route table `PhotoSenseRoutes`, `InProcessApi` for the phones |
| `PhotoSense.Server` | The service as one ASP.NET Core program, serving the page too when built in; `PhotoSenseServer.Build` is its one public entry |
| `PhotoSense.iOS`, `PhotoSense.Android` | The phone apps: the page in a web view, with the thin layer over each phone's frameworks. Not in `PhotoSense.sln` |
| `PhotoSense.Contracts` | JSON DTOs sent to the page; `PhotoSense.ReactUI/types.ts` mirrors them by hand |
| `PhotoSense.ReactUI` | The page: Next.js 16, React 19, SWR, Tailwind 3 |
| `PhotoSense.Tests`, `e2e`, `PhotoSense.Benchmarks` | TUnit tests of all but the page; Playwright tests of the whole program; BenchmarkDotNet runs of matching |
| `tools/`, `version.json`, `product.json`, `notices/`, `site/pages/`, `branding/` | Versions, notices, the web site, who makes PhotoSense, the one icon |
| `docs/design_handoff_photosense_redesign` | Jamie's design handoff. Leave it alone and out of commits unless asked |

## Architecture

- **Clean Architecture:** Domain ← Application ← Infrastructure. `PhotoSense.Api` never references Infrastructure or ASP.NET Core, because the phone apps answer the same endpoints in-process. New storage or OS access goes behind an interface in Domain.
  - Each layer registers its own services: `AddPhotoSenseApplication` (Application), `AddPhotoSenseCore` (Infrastructure), `AddPhotoSenseApi` (Api). Each host calls them all, with what only it has.
  - Logic that needs no outside world goes in Domain; logic over Domain's interfaces in Application. Infrastructure holds only adapters to LiteDB, MetadataExtractor, the disk, the network and the phones.
- **Photos go through `IPhotoFileSystem` only:** `DiskFileSystem` on a computer, `PhotoLibraryFileSystem` on an iPhone, `MediaFileSystem` on Android. Domain, Application and Api never use `File`, `Directory`, `FileInfo` or `FileStream` on a photo.
- **The stack:**
  - .NET 10 with one ASP.NET Core program. Endpoints are methods taking an `ApiRequest`: no minimal APIs and no controllers.
  - LiteDB in one file: no EF Core and no migrations. No auth: `RequestGuard` is the only gate.
  - TUnit with Moq and coverlet: no xUnit, FluentAssertions or Testcontainers.
  - Magick.NET on a computer, the phone's own decoder on a phone. Generic .NET advice that assumes another stack does not apply.
- **Checks on GitHub:**
  - `ci.yml` runs on Ubuntu, Windows and macOS, for `main` and pull requests only, so it has never run on the feature branch.
  - `iphone.yml`, `android.yml` and `e2e.yml` also run on pushes to `feature/mobile-and-mac`.

## Rules

1. **100% branch coverage in all three parts.** Every change ships with tests that keep the service, the UI and the build tools (`tools/`) at 100% of branches. Report the measured figures when done, not a guess.
2. **Nothing is ever replaced, and nothing is erased except by "Delete permanently".**
   - Removing a file moves it into `_PhotoSense_Removed` inside the scanned or organized folder, keeping its relative path, and is undoable where the UI offers Undo.
   - The only code that erases files is `RemovedFiles.EraseAsync`, through `IPhotoFileSystem.Erase`, which nothing else calls. It only empties folders named `_PhotoSense_Removed`.
   - The one exception, Jamie's decision of 2026-10-29: on a phone, after sending to a computer, `PhoneSending.DeleteSent` sends to the phone's Recently Deleted or bin (`IPhotoChangesTogether.EraseAll`) the pictures every file of which the computer has confirmed by its SHA-256, once the person confirms or chose Move. Nothing on a computer is erased that way.
   - A file arriving under a taken name gets a number.
   - The last copy of a picture is never removed as a duplicate.
3. **Every removal question and message names the file that goes and the file that stays**, by folder when the names are alike. Jamie has twice taken two files for one.
4. **Jamie's real photos are read-only to us.** `C:\Users\jsham\Phone Pictures` and `F:\PC_backup` may be scanned and read. Never move, delete or organize files there when testing: copy a few files to a scratch folder and test on the copy.
5. **Leave the running app alone.** Before building, cleaning or deleting anything under `bin`, `obj` or `.next`, check whether Jamie's app is running (`Get-NetTCPConnection -LocalPort 7071,3000`). While it runs:
   - build in Release;
   - do not run `next build` or a second `next dev` in `PhotoSense.ReactUI`;
   - never stop Jamie's processes.

   "It's locked, so it will refuse" is not a safeguard for a folder: a recursive delete removes everything it can first.
6. **Never use or commit Jamie's Google Maps key.** It is in `PhotoSense.ReactUI/.env.local`, which git ignores; every build made for testing leaves it out.
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
10. **No anti-patterns unless there is no better way, and then only with Jamie's say-so** (Jamie, 2026-11-09).
    - Not used: blocking on a task (`.Result`, `.Wait()`, `GetAwaiter().GetResult()`), `async void`, `new HttpClient()` per call, `DateTime.Now`/`UtcNow` where a `TimeProvider` can be given, an empty `catch`, a catch of every exception, a bare `catch { …; throw; }` (use a `finally` that lets go of what was not handed over).
    - When one seems the best solution, ask Jamie before writing it, saying why nothing else works, and record the answer here.
    - Accepted by Jamie on 2026-11-09:
      - `catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)`: a filter naming the types, which the detector misreads as a catch of everything.
      - Catches of every error at the edges where nothing else would catch it, each saying so: `InProcessApi.AnswerAsync` and the phones' `PageSchemeHandler` and `PageClient` (the page would wait for ever), and `ScanRunner` (a scan in the background would stop unseen).
      - One catch of every error around MetadataExtractor, in `MetadataFiles.TryRead`: it throws undocumented errors on damaged files, and one damaged file must not stop a scan of thousands. Each picture decoder instead turns its own errors into `UnreadableImageException`.
    - Accepted by Jamie on 2026-11-10: Android's `PageClient.Ask` blocks on the answer. `ShouldInterceptRequest` must answer synchronously, with the status known, so there is no non-blocking way; the block is on one of WebView's own threads, where it cannot deadlock; a local web server or a second way of calling for the page would cost more than it saves.
11. **Every async method takes a `CancellationToken`, last, and uses it** (Jamie, 2026-11-09).
    - The token is required, not `= default`, so that no caller can forget it; a test method takes the one TUnit gives it. CA2016 (pass it on) and CA1068 (last) are warnings, so errors in CI.
    - Using it means passing it to every call that takes one and checking it in long loops. A method that answers at once from memory or LiteDB checks it first (`ct.ThrowIfCancellationRequested()`).
    - Where work must not stop part way, `CancellationToken.None` is passed on purpose, with a comment saying why: once a change's files have moved it is finished and written down (`FileChanges`), erasing once begun is finished and recorded (`RemovedFiles`), a folder's reading is shared by everyone asking (`OrganizeLibrary`), what was done is recorded whatever happens (the audit after a change).
    - Endpoints take the token the router hands them (`ApiRequest.Aborted`).

## How things are written here

- **XML documentation** (Jamie, 2026-11-09):
  - Every public or internal class, record, struct, interface, enum, delegate, method, property, field, constant and event has a `<summary>`, in the plain English of the rest of the code, saying what it is for; a positional record documents each parameter with `<param>`. A member that implements an interface or overrides one says `<inheritdoc />` when that says it all.
  - A private method has one when its purpose or intent is not plain from its name and body.
  - The compiler checks what other projects can see (CS1591, with `GenerateDocumentationFile` in `Directory.Build.props`); internal and private members are kept documented by hand.
  - In `PhotoSense.Tests`, test methods are exempt, as their names are sentences; every test class, fixture, fake and helper is documented.
  - The page's TypeScript follows the same rule with TSDoc (`/** … */`): every exported function, component, hook, type, interface and constant (Jamie, 2026-11-09).
- **Comments** say *why*, in plain English, usually one line. They never say what the code does or narrate a change. A line that does something easy to misread gets one. Names describe what the user would recognise.
- **Public is each project's front door; the tests use nothing else** (Jamie, 2026-11-09: no `InternalsVisibleTo`).
  - Public: what another project uses, Domain's rules and interfaces, Application's services, an adapter a program could put together itself, the sending protocol (the contract between two programs), the HTTP vocabulary (`ApiRequest`, `RequestGuard`, `ByteRange`), and each project's registrations. `PhotoSense.Server` has `PhotoSenseServer.Build` only.
  - Internal: endpoints, the LiteDB stores (their constructors take LiteDB's own types), mappers and helpers. Tests reach them through the front door: endpoints through the router (`TestApi`), stores through `AddPhotoSenseCore` (`Stores`), the server through `PhotoSenseServer.Build`.
  - When a test seems to need something internal, the structure is usually wrong: move the logic to where it belongs, or give the class a constructor that takes what it asks of the outside world (as `ShellSystemViewer` and `FileSystemFolderBrowser` do).
- **UI text:**
  - Name what the person sees, in short plain sentences, without jargon or apologies.
  - Buttons say exactly what happens ("Delete 3 files"), and the message after says what happened.
  - Errors say what went wrong and what to do.
- **The README** documents behaviour and the API table. Update it with any endpoint or user-visible change.
- **Reports to Jamie:** say what changed, what was verified and how, what was not verified, and any decision Jamie has not confirmed. Use they/them for Jamie.
