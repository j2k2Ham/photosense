# PhotoSense

Cross-platform photo duplicate & near-duplicate detection.

## Projects

| Project | Purpose |
|---------|---------|
| `PhotoSense.Domain` | Core entities, value objects & services abstractions |
| `PhotoSense.Application` | Application use-cases / orchestration (scanning, grouping) |
| `PhotoSense.Infrastructure` | Persistence, hashing & metadata extraction implementations |
| `PhotoSense.Functions` | (Planned) Azure Functions HTTP endpoints / background processing |
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

> For local experimentation before the HTTP API exists, the UI uses mock polling (empty lists). Implement the following endpoints in Functions / Blazor to back the UI:

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/scan/start` | POST | Start a scan; returns `{ instanceId }` |
| `/scan/groups` | GET | Returns duplicate/near-duplicate groups |
| `/scan/progress/{instanceId}` | GET | Returns current `ScanProgressSnapshot` |

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

Run tests (with coverage) from repo root:

```bash
dotnet test PhotoSense.Tests/PhotoSense.Tests.csproj --configuration Release /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

Coverage thresholds (CI enforced): Line ≥ 90%, Branch ≥ 85%.

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


