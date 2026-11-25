---
name: trovolve-service
description: Use before changing or testing Trovolve's C# service, meaning anything in Trovolve.Domain, Application, Infrastructure, Desktop, Api, Server, Contracts or Trovolve.Tests. Covers where code goes, adding an endpoint, building, running TUnit tests and measuring 100% branch coverage.
---

# Changing the service

The generic .NET skills of installed plugins (minimal APIs, EF Core, xUnit, FluentAssertions, Testcontainers) do not apply here. This project uses its own endpoint table, LiteDB, TUnit and Moq.

## Where code goes

| What | Where |
|---|---|
| An entity, value object, an interface to storage or the OS, or a rule that needs nothing else (matching, file names) | `Trovolve.Domain` |
| What Trovolve decides: scanning, grouping, removal, Organize, erasing, sending | `Trovolve.Application` (`Scanning/`, `Organizing/`, `Changes/`, `Removal/`, `Transfer/`), registered in `AddTrovolveApplication` |
| An adapter both hosts run: LiteDB stores, hashing, metadata, place names, disks, a phone's library as folders, finding phones | `Trovolve.Infrastructure`, registered in `AddTrovolveCore` |
| What only a computer has: Magick.NET, the folder browser, `ShellSystemViewer` | `Trovolve.Desktop`, registered in `AddTrovolveComputer` (`Trovolve.Server/TrovolveServices.cs`) |
| An endpoint | `Trovolve.Api/Endpoints`, registered in `AddTrovolveApi`, its address in `TrovolveRoutes` |
| What the page is sent | `Trovolve.Contracts`, mirrored by hand in `Trovolve.ReactUI/types.ts` |

- **Layers:** Domain ← Application ← Infrastructure. `Trovolve.Api` references Domain, Application and Contracts only, never Infrastructure or ASP.NET Core, because the phone apps serve the same endpoints in-process.
- **Photos:** go through `IPhotoFileSystem` only. Domain, Application and Api never use `File`, `Directory`, `FileInfo` or `FileStream` on a photo. Trovolve's own data (LiteDB, thumbnails) stays on the program's own disk.

## Adding an endpoint

1. A method on an endpoint class that takes an `ApiRequest` and returns an `ApiResponse`. Not a minimal API or a controller.
2. Check `RequestGuard.Allows(req.Headers)` first if it reveals or changes anyone's files.
3. Add the address to `TrovolveRoutes`, a literal address before one a parameter could match.
4. Register the class in `AddTrovolveApi`, with a factory lambda when its constructor has optional parameters.
5. Add DTOs to `Trovolve.Contracts` and their mirror to `types.ts`.
6. Add the address to the README's API table.

Before writing the code, read `reference/patterns.md`: registering, the request guard, answers, LiteDB, paths, ids, test seams, scan state and phones.

## Build and test

```sh
dotnet build Trovolve.sln -c Release -nodeReuse:false -warnaserror     # as CI builds; dashes, not slashes, in Git Bash
dotnet run --project Trovolve.Tests -c Release                         # every test
dotnet run --project Trovolve.Tests -c Release -- --treenode-filter "/*/*/ClassName/*"
```

- **Coverage:** append `-- --coverlet --coverlet-output-format opencover --coverlet-include "[Trovolve.*]*" --coverlet-exclude "[Trovolve.Tests]*" --coverlet-exclude-by-file "**/*.g.cs" --results-directory Trovolve.Tests/coverage`, then `python -I .claude/skills/trovolve-service/scripts/coverage_gaps.py Trovolve.Tests/coverage`. It prints the totals and every line and branch not yet covered, by file. Report the measured totals.
- **Jamie's app:** if it is running (`Get-NetTCPConnection -LocalPort 7071,3000`), build in Release only and never touch its `bin/Debug`.
- **Before writing a test,** read `reference/testing.md` for TUnit's rules here, the test helpers and the traps already met.
- **For sending photos between a phone and a computer** (`Transfer/`), read `reference/transfer.md`.
