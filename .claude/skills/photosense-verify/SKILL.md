---
name: photosense-verify
description: Use when checking a PhotoSense change end to end the way Jamie runs it, running a scratch copy of the program, reproducing something Jamie reported, or looking at Jamie's data or photos. Also covers the Windows shell traps that break builds and scripts here.
---

# Checking it as Jamie runs it

## Jamie's app

- **Starting it:** Jamie runs `./dev-start.ps1` from the repo root (`pwsh ./dev-start.ps1` on a Mac). It builds and starts the service from `bin/Debug` on port 7071, and the page with `next dev` on port 3000. `-Stop` stops it.
- **Restarts:** the page reloads changes by itself; the service needs a restart.
- **Data:** `%LOCALAPPDATA%\PhotoSense` (`photosense.db` and thumbnails), never under `bin`.
- **Before building, cleaning or deleting under `bin`, `obj` or `.next`:** run `Get-NetTCPConnection -LocalPort 7071,3000 -State Listen`. While it runs:
  - build in Release;
  - never run `next build` or a second `next dev` in `PhotoSense.ReactUI`;
  - never stop Jamie's processes.

## A scratch copy of the program

Use one to try anything that moves or deletes files, or that would disturb Jamie's app.

- **The simplest:** the end-to-end suite's own program. `cd e2e && npm run app`, then:

  ```powershell
  $env:PhotoStorage__DatabasePath = "<scratch>\data\photosense.db"; $env:PhotoStorage__ThumbnailPath = "<scratch>\data\thumbs"
  dotnet e2e\.cache\server\PhotoSense.Server.dll --urls http://localhost:7099
  ```

  It serves the page itself, at http://localhost:7099, with no map key in it.
- **A page that reloads as it changes:** robocopy `PhotoSense.ReactUI` to the scratchpad, excluding `node_modules`, `.next`, `out` and `.env.local`. Give it its own `npm ci` (never a junction to the repo's `node_modules`), then run `npx next dev -p 3100` with `NEXT_PUBLIC_API_BASE=http://localhost:7099/api`. Start the service with `--Cors:Origins:0=http://localhost:3100`.
- **Driving it:** `playwright-core` with `chromium.launch({ channel: 'chrome' })` from a folder of its own in the scratchpad. Take screenshots in both themes (the class `light` on `html`) and at 390 px wide, and wait for colour transitions to settle before judging one.
- **Google's maps:** start the scratch page with a made-up key, and strip `key=` from the Maps script request in Playwright to get Google's unbilled development-mode map. Never use or commit Jamie's key from `.env.local`.

## Jamie's photos and data

- **Read-only:** `C:\Users\jsham\Phone Pictures` and `F:\PC_backup` may be scanned and read, never moved, deleted or organized. Copy a few files to a scratch folder and test on the copy.
- **Jamie's database:** copy `photosense.db` while the service is stopped. Point a scratch service at the copy, and abort every non-GET request in Playwright.
- **A report of odd behaviour:** reproduce it, or read a copy of the database, before changing any code.
- **Size:** try a new feature on the largest, slowest real case: `F:\PC_backup`, about 48,000 files on an external disk.

## Saying what was checked

Name what was tried, on what data and how, and what was not tried. Try a command or key press before telling Jamie to use it. An override of the data path once hid a total failure, so check with the same settings and start-up Jamie uses.

Before running a multi-line command, or one with switches, quotes or backslashes, read `reference/windows-traps.md`.
