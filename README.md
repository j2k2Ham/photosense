# PhotoSense

Cross-platform photo duplicate & near-duplicate detection.

## Projects

| Project | Purpose |
|---------|---------|
| `PhotoSense.Domain` | Core entities, value objects & services abstractions |
| `PhotoSense.Application` | Application use-cases / orchestration (scanning, grouping) |
| `PhotoSense.Infrastructure` | Persistence, hashing & metadata extraction implementations |
| `PhotoSense.Functions` | Azure Functions HTTP endpoints / background scan |
| `PhotoSense.BlazorServer` | Existing Blazor Server UI (legacy / secondary) |
| `PhotoSense.ReactUI` | New Next.js (React) web client (primary UI) |
| `PhotoSense.Tests` | Automated test suite (unit + perf) |

## Dual UI Strategy

The solution now contains two UI fronts:

1. **React (Next.js) UI (`PhotoSense.ReactUI`)** – Primary. Fetches data via REST endpoints (backed by Functions or Blazor acting as an API host). SWR handles polling for scan progress & group listings.
2. **Blazor Server (`PhotoSense.BlazorServer`)** – Secondary. Can be retired later or kept for purely .NET hosting scenarios.

Both UIs rely on the *same* domain/application layers. Communication contracts are simple JSON DTOs that mirror domain objects (e.g. `DuplicateGroup`, `Photo`, `ScanProgressSnapshot`). The React UI keeps its own `types.ts` file aligned with backend naming. When adding new fields, update both the Application layer DTO & the React `types.ts`.

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
| `/scan/start` | POST | Start a scan. Body `{ primaryLocation, secondaryLocation?, recursive }`; returns `{ instanceId }`, or 400 `{ error }` when a folder is not found on the server |
| `/scan/progress/{instanceId}` | GET | Current `ScanProgressSnapshot` |
| `/scan/groups?mode=duplicates\|similar&page=&pageSize=&q=&hideKept=` | GET | Groups: a keeper (the best copy) and the photos matched against it |
| `/photos/{id}/thumbnail` | GET | Small JPEG preview (cached at scan time); pictures only |
| `/photos/{id}/image` | GET | The picture for viewing; HEIC and TIFF are converted to JPEG on the fly |
| `/photos/{id}/video` | GET | The video for the in-app player, sent in pieces of up to 4 MB as the player asks for them (`Range` requests) |
| `/photos/{id}/open` | POST | Open the file in the default application of the computer the service runs on |
| `/photos/{id}/keep?kept=true\|false` | POST | Mark a copy to keep (bulk removal skips it) |
| `/photos/{id}?physical=true` | DELETE | Remove one file, with the sidecars and Live Photo video that belong to it alone |
| `/photos/bulk/remove-duplicates?group=` | POST | Remove the duplicates of one group, or of all groups |

Requests that change or remove photos must carry the `x-photosense-client` header (the React client sends it). Other web sites cannot add it, because the Functions host only grants cross-origin access to the UI's address, set under `Host:CORS` in `PhotoSense.Functions/local.settings.json`. If you serve the UI from another port, add that address there.

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

Clicking a picture or a thumbnail opens it in a floating window. A video plays in the page, in the review panel and in the floating window alike. What plays there depends on the browser: MP4 and most iPhone MOV files do; where one does not, the player says so. Either way **Open in default player** (or **Open in default viewer** for a picture) hands the file to the operating system, as double-clicking it would: the file itself through the Windows shell, `open` on macOS, `xdg-open` on Linux. The program starts on the computer the service runs on, so this is for running PhotoSense on your own machine.

Removing never erases anything. Files are moved to a `_PhotoSense_Removed` folder inside the scanned folder, keeping their relative path; delete that folder to free the space, or move a file back to restore it. A file is left alone if it, or the copy being kept, changed since the scan.

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

## Swapping UIs

Because UIs are isolated projects with no code-level coupling to each other, deployment choice is just a matter of which project you publish. The backend contract stays stable. In multi-environment deployments you can host both side-by-side until the React UI fully supersedes the Blazor experience.

## Development Notes

- TailwindCSS powers the new React UI styling.
- `swr` provides lightweight polling + cache for scan progress & groups.
- Components are intentionally stateless where possible; future real-time updates (WebSockets / SignalR / Azure Web PubSub) can push into a simple event bus hook.

## Tests & Coverage

Both halves are tested to 100% of branches.

### Service (.NET)

From the repo root:

```bash
dotnet test PhotoSense.Tests/PhotoSense.Tests.csproj --configuration Release /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

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
./dev-start.ps1             # build solution + start Functions + React UI
./dev-start.ps1 -Open       # also open browser to http://localhost:3000
./dev-start.ps1 -FunctionsPort 7072
./dev-start.ps1 -NoBuild    # skip build for faster restart
./dev-start.ps1 -Clean      # clean then build
./dev-start.ps1 -IncludeBlazor -UseWatch  # (optional) also start legacy Blazor in watch mode
```

Creates local photo folders under `PhotoSense.Functions/photos/primary` & `.../secondary` if missing. Press Ctrl+C to stop all processes.

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

### React Hook: `useScanLogs`

Located at `PhotoSense.ReactUI/lib/useScanLogs.ts`.

Features:

- Tries SignalR negotiation first (`/api/scan/logs/negotiate`).
- Falls back automatically to REST polling (`/api/scan/logs`).
- Maintains a bounded rolling buffer (`maxItems`, default 500).
- Provides status lifecycle: `idle | connecting | streaming | polling | error | paused`.
- Exposes `pause()`, `resume()`, and `clear()` helpers.
- Handles 429 (rate limit) by temporary backoff (doubling interval for one cycle).

Usage example:

```tsx
import { useScanLogs } from '../lib/useScanLogs';

export function LiveLogsPanel() {
  const { logs, status, error, pause, resume, clear } = useScanLogs({ maxItems: 800, restLimit: 300 });

  return (
    <div className="space-y-2">
      <div className="flex gap-2 items-center text-sm">
        <span>Status: <strong>{status}</strong></span>
        {error && <span className="text-red-500">{error}</span>}
        {status !== 'paused' ? (
          <button onClick={pause} className="px-2 py-1 border rounded">Pause</button>
        ) : (
          <button onClick={resume} className="px-2 py-1 border rounded">Resume</button>
        )}
        <button onClick={clear} className="px-2 py-1 border rounded">Clear</button>
      </div>
      <pre className="text-xs bg-black text-green-300 p-2 max-h-96 overflow-auto rounded">
        {logs.map(l => `${l.timestamp} [${l.level}] ${l.message}`).join('\n')}
      </pre>
    </div>
  );
}
```

Options:

| Option | Default | Description |
|--------|---------|-------------|
| `maxItems` | 500 | Rolling buffer size (oldest dropped). |
| `pollIntervalMs` | 1500 | Interval between REST polls when not streaming. |
| `restLimit` | 200 | `limit` query value per poll request. |
| `disableSignalR` | false | Force REST polling (testing). |
| `startPaused` | false | Initialize in paused state. |

Returned state fields: `logs`, `status`, `error`, `pause()`, `resume()`, `clear()`.

### Backwards Compatibility Helper

`connectLogStream` in `lib/apiClient.ts` remains (now marked deprecated) and internally mirrors the hook logic (SignalR then REST) but lacks controls and buffer management. Migrate existing components to the hook for richer state handling.

### Future Enhancements

- Optional SSE endpoint for lighter fallback vs polling.
- Per-instance filtering (`?instanceId=...`).
- Persist recent logs to durable storage if needed for diagnostics.


