# PhotoSense

Cross-platform photo duplicate & near-duplicate detection.

## Projects

| Project | Purpose |
|---------|---------|
| `PhotoSense.Domain` | Core entities, value objects & services abstractions |
| `PhotoSense.Application` | Application use-cases / orchestration (scanning, grouping) |
| `PhotoSense.Infrastructure` | Persistence, hashing & metadata extraction implementations |
| `PhotoSense.Functions` | Azure Functions HTTP endpoints / background scan |
| `PhotoSense.Contracts` | The JSON shapes the service sends to the UI |
| `PhotoSense.ReactUI` | Next.js (React) web client |
| `PhotoSense.Tests` | Automated tests of everything above the UI |
| `PhotoSense.Benchmarks` | A benchmark of the duplicate analysis, run by hand |

## The UI and the service

The React UI (`PhotoSense.ReactUI`) fetches everything over REST from the Functions service, with SWR polling for scan progress and group listings. What they exchange is plain JSON mirroring the DTOs in `PhotoSense.Contracts`; the UI keeps its own `types.ts` in step with them. When adding a field, update both.

## Running the React UI

```bash
cd PhotoSense.ReactUI
npm install
npm run dev
```

Environment vars:

```bash
export NEXT_PUBLIC_API_BASE=http://localhost:7071/api   # Azure Functions / API base
```

The API the UI uses (Functions host, `http://localhost:7071/api`):

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/folders?path=` | GET | The folders inside a folder of the computer the service runs on, each with its full path; without `path`, the places to start from (Pictures, the home folder, each disk) |
| `/scan/start` | POST | Start a scan. Body `{ primaryLocation, secondaryLocation?, recursive, startOver? }`; returns `{ instanceId }`, or 400 `{ error }` when a folder is not found on the server |
| `/scan/reset` | POST | Forget everything earlier scans recorded, previews included; refused while a scan is running. The photos are not touched |
| `/scan/progress/{instanceId}` | GET | Current `ScanProgressSnapshot` |
| `/scan/groups?mode=duplicates\|similar&page=&pageSize=&q=&hideKept=` | GET | Groups: a keeper (the best copy) and the photos matched against it |
| `/photos/{id}/thumbnail` | GET | Small JPEG preview (cached at scan time); pictures only |
| `/photos/{id}/image` | GET | The picture for viewing; HEIC and TIFF are converted to JPEG on the fly |
| `/photos/{id}/video` | GET | The video for the in-app player, sent in pieces of up to 4 MB as the player asks for them (`Range` requests) |
| `/photos/{id}/open` | POST | Open the file in the default application of the computer the service runs on |
| `/photos/{id}/keep?kept=true\|false` | POST | Mark a copy to keep (bulk removal skips it) |
| `/photos/{id}?physical=true` | DELETE | Remove one file, with the sidecars and Live Photo video that belong to it alone |
| `/photos/bulk/remove-duplicates?group=` | POST | Remove the duplicates of one group, or of all groups |

Requests that change or remove photos, and the folder listing, must carry the `x-photosense-client` header (the React client sends it). Other web sites cannot add it, because the Functions host only grants cross-origin access to the UI's address, set under `Host:CORS` in `PhotoSense.Functions/local.settings.json`. If you serve the UI from another port, add that address there.

### Choosing the folders to scan

A scan needs each folder's full path as the service sees it. The **Browse** button beside a path opens a folder browser that lists the folders of the computer the service runs on and fills in the full path of the one chosen; a path can also be typed or pasted. (A browser's own folder dialog is no use here: it tells a web page the name of the folder that was picked and never where it is.)

One folder is enough: copies inside it, in the folder itself or in its subfolders, are found. The secondary folder is for comparing a second place with the first, a backup for instance. Giving the same folder as both changes nothing: it is scanned once, the setup screen says so, and a file is never matched with itself.

### Scanning again, and starting over

A scan builds on the one before it: files that have not changed since are skipped, files that have gone are forgotten at the end, and copies marked keep stay marked. That makes a second scan of a large library take seconds rather than minutes.

To run a scan from nothing instead, tick **Start over** on the setup screen before pressing Scan: everything recorded by earlier scans is forgotten first, and every file is read again. **Clear results**, in the menu, does the forgetting on its own, without scanning. Either way only PhotoSense's record goes, with its previews and keep marks; the photos, and anything already moved to `_PhotoSense_Removed`, stay where they are.

### How long a scan will take

While a scan runs, the setup screen says how long is left. The service works that out and sends it with the progress (`secondsLeft` on `GET /api/scan/progress/{id}`):

- At first it goes by earlier scans: what a file took to read in the last three scans that read at least 50 files, for every file this scan has not seen before. Files already on record are mostly skipped and cost next to nothing.
- Once the scan has run 10 seconds and has 50 files behind it, it goes by its own pace instead.
- With no earlier scan and too little of this one, it says it is still working it out.

What each scan took is kept with the rest of the data, and is not forgotten by **Start over** or **Clear results**.

### Where the data is kept

The database (`photosense.db`) and the thumbnail cache are kept in a folder of your own, outside the program:

| System | Folder |
|--------|--------|
| Windows | `%LOCALAPPDATA%\PhotoSense` |
| macOS | `~/Library/Application Support/PhotoSense` |
| Linux | `~/.local/share/PhotoSense` |

Set `PhotoStorage__DatabasePath` to an absolute path to keep the database elsewhere, and `PhotoStorage__ThumbnailPath` for the thumbnails (by default a folder beside the database). A path that is not absolute is taken from the folder above. Deleting the folder forgets every scan; the photos themselves are not touched.

The data must not be inside the folder the service runs from (`PhotoSense.Functions/bin/...`), and the service refuses to start a database there. The Functions host watches that folder and restarts itself when a folder appears in it, as a thumbnail cache does on a scan's first picture; after such a restart it answers every request with an error while the scan carries on unseen.

If the service does stop answering, the page says so in a bar under the header, with a Retry button, since what is already on screen stays there. Every error is also kept in the Errors panel (the menu, or the red chip in the header) until it is cleared.

## How duplicates are found

A scan reads every picture (JPEG, PNG, GIF, BMP, TIFF, WebP, HEIC/HEIF) and video (MOV, MP4, M4V, 3GP) under the chosen folders and keeps one record per file path, so scanning again never makes a file a duplicate of itself. Unchanged files are skipped on later scans.

Videos are matched only as identical files and are never decoded. Identical files have identical sizes, so a video is read in full only when another video has exactly its size; the rest are recorded from their headers alone.

Matches are sorted by how certain they are:

| Tier | What it is | How it is decided | Removed in bulk |
|------|------------|-------------------|-----------------|
| Identical file | Same bytes (pictures and videos) | SHA-256 of the file | Yes |
| Same picture | One shot saved again: converted, resized or re-compressed | A 64-bit DCT perceptual hash nominates pairs (compared bit by bit); each pair is then checked directly: same shape, near-identical pixels at 32×32, and the same capture instant when both files record one | Yes |
| Similar | Burst frames and edited versions | Looks alike but fails a check above | No, review only |

Within a group the keeper is chosen by resolution first, then format, then intact capture details, JPEG quality, and file size. The order lives in `PhotoRanking`, which also words the reason shown in the review screen. Every other member of a group was compared with the keeper itself, never chained through a third photo. The thresholds live in `PhotoMatcher` with the measurements they came from.

### HEIC or JPEG

When the same picture exists at the same resolution as both a HEIC and a JPEG, the JPEG is kept, because it opens on anything. A lossless file (PNG, TIFF, BMP) is kept ahead of either.

To keep the HEIC instead, set `PhotoStorage__KeepFormat` to `CameraOriginal` (under `Values` in `PhotoSense.Functions/local.settings.json`, or as an environment variable) and restart the service. The HEIC is the file the phone wrote, the JPEG a conversion made from it, and the HEIC is usually about half the size. The default is `WidelyCompatible`. Nothing needs scanning again; the choice is applied when groups are worked out.

PhotoSense shows HEIC pictures itself, whatever the computer can open. Outside it, Windows opens HEIC files only once the free "HEIF Image Extensions" and the "HEVC Video Extensions" are installed from the Microsoft Store; without both, Photos and Explorer previews show nothing. macOS and iOS open them as they are.

### Looking at a file before deciding

The look and layout follow the design handoff in `docs/design_handoff_photosense_redesign` (dark and light themes, chosen from the menu and remembered in the browser). The review desk shows the original beside one copy, with a table of what differs between them; clicking either opens the comparison window, where Space flips between the copy and the original in the same spot.


A copy and its original are always two files, and each is shown under its own name with the folder it is in: under the two pictures on the review desk, under each thumbnail, and in the heading of the comparison window ("`IMG_0648.JPG`, a copy of `IMG_0377.JPG`"). Two files holding the very same picture can sit side by side in one folder under unrelated names, and look like one file until the names are read.

Hovering over a duplicate lists what sets it apart from the original: its name, folder, format, pixel size, file size or capture date, whichever differ. Two files of one picture can share a name, a folder and a date, as a phone's `IMG_4299.HEIC` and the `IMG_4299.JPG` made from it do, and would otherwise look like one file listed twice. Each thumbnail also carries its format in the corner.

Clicking a picture or a thumbnail opens it in a floating window. A video plays in the page, in the review panel and in the floating window alike. What plays there depends on the browser: MP4 and most iPhone MOV files do; where one does not, the player says so. Either way **Open in default player** (or **Open in default viewer** for a picture) hands the file to the operating system, as double-clicking it would: the file itself through the Windows shell, `open` on macOS, `xdg-open` on Linux. The program starts on the computer the service runs on, so this is for running PhotoSense on your own machine.

Removing never erases anything. Files are moved to a `_PhotoSense_Removed` folder inside the scanned folder, keeping their relative path; delete that folder to free the space, or move a file back to restore it. Every question asked before a removal, and the message after it, names the file that goes and the file that stays.

The last copy of a picture is never removed as a duplicate. A copy goes only while the original it was matched with is still on disk exactly as it was scanned, and the original can be removed "instead" only while one of its copies is. If that file has been deleted, moved or edited since the scan, by PhotoSense or by anything else, the removal is refused and the file is left where it is; scan again to bring the results up to date. The same holds if two records turn out to be one file reached by two paths (a linked folder, or a drive letter standing for a folder): the file is put straight back.

### Sidecars and Live Photos

An iPhone item can be several files: `IMG_1234.HEIC` (the picture), `IMG_1234.MOV` (its Live Photo video), `IMG_E1234.HEIC` (an edited version) and `IMG_1234.AAE` / `IMG_O1234.AAE` (the record of the edits). When a picture is removed, the files that belonged to it alone go with it:

- Nothing goes while another picture of the same item stays in the folder (the other format, or the edited version).
- A video goes only if it carries the same Live Photo identifier as the picture. The name is not enough: unrelated videos do end up with the same number as a picture.
- Sidecars go once no picture or video of the item is left.

A Live Photo's video is also left out of duplicate matching while its picture is beside it, so it can only ever leave together with that picture.

### Place names

Where a picture was taken is shown as the nearest town ("Buxton, North Carolina, US", or "Near Anaconda, Montana, US" when the town is more than 3 km away; nothing beyond 80 km). The lookup uses a list bundled with the application, so positions are never sent anywhere. The list is an extract of [GeoNames](https://www.geonames.org/) data, licensed CC BY 4.0; see `PhotoSense.Infrastructure/Places/README.md`.

Decoding is done with Magick.NET. iPhone HEIC files decode slowly (about a second each), so a first scan of a few thousand photos takes several minutes.

## Adding New API Fields

1. Add property in the relevant Application DTO / event.
2. Update serialization in the API layer (Functions controller or minimal API endpoint).
3. Update `PhotoSense.ReactUI/types.ts` & adjust component rendering.

## Development Notes

- TailwindCSS powers the new React UI styling.
- `swr` provides lightweight polling + cache for scan progress & groups.
- Components are intentionally stateless where possible; future real-time updates (WebSockets / SignalR / Azure Web PubSub) can push into a simple event bus hook.

## Tests & Coverage

Both halves are tested to 100% of branches.

### Service (.NET)

The tests are written with [TUnit](https://tunit.dev), which builds them as a program of their own. From the repo root, to run them:

```bash
dotnet run --project PhotoSense.Tests -c Release
```

and with coverage (options for the test program go after the `--`):

```bash
dotnet run --project PhotoSense.Tests -c Release -- --coverlet --coverlet-output-format opencover --coverlet-include "[PhotoSense.*]*" --coverlet-exclude "[PhotoSense.Tests]*" --coverlet-exclude-by-file "**/*.g.cs" --results-directory PhotoSense.Tests/coverage
```

`dotnet test --project PhotoSense.Tests -c Release` runs them too on the .NET 10 SDK, which `global.json` tells to use the new test platform. One test can be picked out with `--treenode-filter "/*/*/ClassName/*"`.

In a test, every assertion is awaited (`await Assert.That(actual).IsEqualTo(expected)`); one that is not awaited is never checked, so the build treats a forgotten `await` as an error. Tests run side by side, including those of one class; the few that share the in-memory log queue are marked `[NotInParallel("ScanLogQueue")]`.

Coverage thresholds (CI enforced): Line ≥ 90%, Branch ≥ 85%. Measured on Windows the .NET code stands at 100% of lines and 100% of branches. Generated code (`*.g.cs`) and the service's start-up class are left out of the measurement.

Some code is shaped so that every branch can be exercised from one operating system: `PhotoPath.Key(path, ignoreCase)`, `ShellSystemViewer.DesktopOf` and `BasicExifMetadataExtractor.Read(photo, directories)` take as an argument what they would otherwise ask the system or a file for. Two guards against a library handing back nothing (`OutboxIntegrationEventPublisher.NameOf`, `MagickImageAnalyzer.Required`) are callable on their own for the same reason.

### UI (React)

From `PhotoSense.ReactUI`:

```bash
npm test                # once
npm run test:watch      # again on every change
npm run test:coverage   # once, with coverage; fails below 100%
```

The tests are in `PhotoSense.ReactUI/tests`, laid out like the code they cover (`app`, `components`, `lib`). They run in Vitest with Testing Library, against jsdom rather than a real browser, so nothing needs to be running: the service is stood in for by `tests/fixtures.ts` and the log hub by `tests/fakeSignalR.ts`.

Coverage is measured with Istanbul over `app`, `components` and `lib`, and `vitest.config.ts` fails the run when statements, branches, functions or lines fall below 100%. CI runs it as well, before building the UI. Istanbul counts each arm of an `if` or a ternary and each operand of `&&`, `||` and `??` that is reached; `?.` is not counted as a branch. The HTML report is written to `PhotoSense.ReactUI/coverage`.

## Quick Local Run (Functions + React)

Use the PowerShell helper script to spin up the Azure Functions host and the React (Next.js) UI:

```powershell
./dev-start.ps1             # build + start Functions + React UI
./dev-start.ps1 -Open       # also open browser to http://localhost:3000
./dev-start.ps1 -FunctionsPort 7072
./dev-start.ps1 -NoBuild    # skip build for faster restart
./dev-start.ps1 -Clean      # clean then build
```

To stop PhotoSense, press Ctrl+C in the terminal it was started in, or run `./dev-start.ps1 -Stop` from any terminal. To restart it, run `./dev-start.ps1` again: it first stops a copy that is still running, since two cannot share the ports and a running service keeps its program files locked against the build. Only PhotoSense's own processes are stopped; if some other program has the UI's port, the UI takes the next free one and the service is told to accept it there.

The scan runs as a Durable Functions activity, which needs the Azurite storage emulator. The script starts it when nothing is listening on port 10000 and `azurite` is on PATH (`npm install -g azurite`); `docker compose -f docker-compose.azurite.yml up` works too.

## VS Code Tasks

Common tasks are defined in `.vscode/tasks.json`:

| Task | Purpose |
|------|---------|
| `build:solution` | Build all projects |
| `run:functions (core tools)` | Start Functions host (port 7071) |
| `run:react` | Start React UI (Next.js dev server) |
| `run:functions (core tools)` | Start Functions host (port 7071) |
| `run:functions+react` | Combined startup via dev-start script |
| `dev:start` | Same as combined (opens browser) |

Run via the VS Code command palette: `Tasks: Run Task`.

## Contracts NuGet Package

The shared DTOs live in `PhotoSense.Contracts`. A pre-release package is produced on build:

Artifact: `PhotoSense.Contracts.0.1.0-alpha.nupkg`

To publish manually:

```powershell
dotnet nuget push PhotoSense.Contracts\bin\Debug\PhotoSense.Contracts.0.1.0-alpha.nupkg -s https://api.nuget.org/v3/index.json -k <API_KEY>
```

Next version: bump `<Version>` inside `PhotoSense.Contracts.csproj` (semantic versioning recommended).

## SignalR (Planned Real-time Logs)

There are two mechanisms for consuming scan log lines:

1. REST Polling (always on): `GET /api/scan/logs` returns and drains queued log events.
2. SignalR (optional): Behind `ENABLE_SIGNALR`, a negotiation endpoint `POST|GET /api/scan/logs/negotiate` plus a timer-driven broadcast (`BroadcastScanLogs`) attempts to push logs to hub `scanlogs` every 5s.

The broadcast is switched off in `local.settings.json` (`AzureWebJobs.BroadcastScanLogs.Disabled`). Without an Azure SignalR connection string it fails every 5 seconds and throws away the log lines it took from the queue, so the polling endpoint would show only some of them. Remove that setting once `AzureSignalRConnectionString` is configured.

If the SignalR broadcast build fails (API drift in extension package), the REST polling endpoint remains a stable fallback. The broadcast currently uses a tentative `[SignalROutput]` pattern returning an array of message objects `{ target, arguments }`.

To experiment:

1. Add `ENABLE_SIGNALR` constant in Functions project.
2. Run `func start` and hit negotiate to retrieve connection info.
3. From React UI, establish a HubConnection to the `scanlogs` hub and listen to the `log` target.

### Log Retrieval API (Polling)

Endpoint: `GET /api/scan/logs?limit=NN`

Response shape:

```json
{
  "items": [
    { "instanceId": "worker-1", "timestamp": "2025-10-14T12:34:56.789Z", "level": "Info", "message": "Scanning file X" }
  ],
  "count": 1
}
```

Behavior:

- Each call drains up to `limit` pending log entries (default 200, hard cap 1000).
- Drained entries are removed from the in-memory queue, so subsequent calls only return new lines.
- Global rate limiting: simple token bucket (capacity 20, refill 1/sec). Exceeding this returns HTTP 429 with text body.
- Clients should treat 429 as a signal to back off (e.g. double poll interval briefly).

### In the UI

`connectLogStream` in `PhotoSense.ReactUI/lib/apiClient.ts` follows the log for the setup screen: over SignalR where the negotiate call succeeds, otherwise by asking the REST endpoint again every few seconds.

### Future Enhancements

- Optional SSE endpoint for lighter fallback vs polling.
- Per-instance filtering (`?instanceId=...`).
- Persist recent logs to durable storage if needed for diagnostics.


