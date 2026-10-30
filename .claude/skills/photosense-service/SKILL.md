---
name: photosense-service
description: Use before changing or testing PhotoSense's C# service, meaning anything in PhotoSense.Domain, Application, Infrastructure, Desktop, Api, Server, Contracts or PhotoSense.Tests. Covers where code goes, adding an endpoint, building, running TUnit tests and measuring 100% branch coverage.
---

# Changing the service

The generic .NET skills of installed plugins (minimal APIs, EF Core, xUnit, FluentAssertions, Testcontainers) do not apply here. This project uses its own endpoint table, LiteDB, TUnit and Moq.

## Where code goes

| What | Where |
|---|---|
| An entity, value object, or an interface to storage or the OS | `PhotoSense.Domain` |
| What PhotoSense decides: scanning, grouping, removal, Organize, erasing, sending | `PhotoSense.Application` (`Scanning/`, `Organizing/`, `Changes/`, `Removal/`, `Transfer/`) |
| What both hosts run: LiteDB stores, hashing, metadata, place names, disks, a phone's library as folders, finding phones | `PhotoSense.Infrastructure`, registered in `AddPhotoSenseCore` |
| What only a computer has: Magick.NET, the folder browser, `ShellSystemViewer` | `PhotoSense.Desktop`, registered in `AddPhotoSenseComputer` (`PhotoSense.Server/PhotoSenseServices.cs`) |
| An endpoint | `PhotoSense.Api/Endpoints`, registered in `AddPhotoSenseApi`, its address in `PhotoSenseRoutes` |
| What the page is sent | `PhotoSense.Contracts`, mirrored by hand in `PhotoSense.ReactUI/types.ts` |

- **Layers:** Domain ← Application ← Infrastructure. `PhotoSense.Api` references Domain, Application and Contracts only, never Infrastructure or ASP.NET Core, because the phone apps serve the same endpoints in-process.
- **Photos:** go through `IPhotoFileSystem` only. Domain, Application and Api never use `File`, `Directory`, `FileInfo` or `FileStream` on a photo. PhotoSense's own data (LiteDB, thumbnails) stays on the program's own disk.

## Adding an endpoint

1. A method on an endpoint class that takes an `ApiRequest` and returns an `ApiResponse`. Not a minimal API or a controller.
2. Check `RequestGuard.Allows(req.Headers)` first if it reveals or changes anyone's files.
3. Add the address to `PhotoSenseRoutes`, a literal address before one a parameter could match.
4. Register the class in `AddPhotoSenseApi`, with a factory lambda when its constructor has optional parameters.
5. Add DTOs to `PhotoSense.Contracts` and their mirror to `types.ts`.
6. Add the address to the README's API table.

Before writing the code, read `reference/patterns.md`: registering, the request guard, answers, LiteDB, paths, ids, test seams, scan state and phones.

## Build and test

```sh
dotnet build PhotoSense.sln -c Release -nodeReuse:false -warnaserror     # as CI builds; dashes, not slashes, in Git Bash
dotnet run --project PhotoSense.Tests -c Release                         # every test
dotnet run --project PhotoSense.Tests -c Release -- --treenode-filter "/*/*/ClassName/*"
```

- **Coverage:** append `-- --coverlet --coverlet-output-format opencover --coverlet-include "[PhotoSense.*]*" --coverlet-exclude "[PhotoSense.Tests]*" --coverlet-exclude-by-file "**/*.g.cs" --results-directory PhotoSense.Tests/coverage`, then `python -I .claude/skills/photosense-service/scripts/coverage_gaps.py PhotoSense.Tests/coverage`. It prints the totals and every line and branch not yet covered, by file. Report the measured totals.
- **Jamie's app:** if it is running (`Get-NetTCPConnection -LocalPort 7071,3000`), build in Release only and never touch its `bin/Debug`.
- **Before writing a test,** read `reference/testing.md` for TUnit's rules here, the test helpers and the traps already met.
- **For sending photos between a phone and a computer** (`Transfer/`), read `reference/transfer.md`.
