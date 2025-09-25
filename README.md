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

```
cd PhotoSense.ReactUI
npm install
npm run dev
```

Environment vars:

```
NEXT_PUBLIC_API_BASE=http://localhost:7071/api   # Azure Functions / API base
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

```
dotnet test PhotoSense.Tests/PhotoSense.Tests.csproj --configuration Release /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

Coverage thresholds (CI enforced): Line ≥ 90%, Branch ≥ 85%.
