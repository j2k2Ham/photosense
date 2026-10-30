# PhotoSense

Cross-platform photo duplicate & near-duplicate detection.

## Projects

| Project | Purpose |
|---------|---------|
| `PhotoSense.Domain` | Core entities, value objects & services abstractions |
| `PhotoSense.Application` | Application use-cases / orchestration (scanning, grouping) |
| `PhotoSense.Infrastructure` | What both programs run: persistence, hashing, metadata, place names, the computer's disks (`DiskFileSystem`), a phone's photo library as a folder (`Library/`), and the services wired together (`AddPhotoSenseCore`) |
| `PhotoSense.Desktop` | What only a computer has: Magick.NET to decode pictures, the folder browser, opening a file in another program |
| `PhotoSense.Api` | The service's endpoints, apart from any one program that serves them, and what a program without a web server needs to answer the page (`Hosting/`) |
| `PhotoSense.Server` | The service as one program for a Windows or Mac computer (ASP.NET Core) |
| `PhotoSense.iOS` | The app for an iPhone or iPad, built on GitHub's Macs; not part of `PhotoSense.sln` |
| `PhotoSense.Contracts` | The JSON shapes the service sends to the UI |
| `PhotoSense.ReactUI` | Next.js (React) web client |
| `PhotoSense.Tests` | Automated tests of everything above the UI |
| `PhotoSense.Benchmarks` | A benchmark of the duplicate analysis, run by hand |

Beside them:

| Folder or file | What it is |
|---|---|
| `version.json` | Which kind of release comes next, and whether the programs share one version (see *Versions*) |
| `product.json` | Who makes PhotoSense and how to reach them: the publisher, the copyright line, the support address, the web addresses, the law that governs the terms |
| `branding/app-icon.png` | The icon, the one copy every program uses |
| `site/pages/` | The privacy policy, the terms of use and the support page, which the About window shows and `tools/site.mjs` makes into the web site |
| `notices/` | Each program's third-party notices, written by `tools/notices.mjs` |
| `tools/` | What the build runs: `version.mjs`, `notices.mjs`, `site.mjs`, with their tests in `tools/tests` |

## The user guide

How to use PhotoSense is told in the app itself: **Help**, in the menu, opens the user guide. It has a topic for each part of the app (scanning, reviewing duplicates and similar shots, Organize, deleting permanently, what to do when something looks wrong), can be searched, is read from one topic to the next, and most topics have a button that goes to the place they describe. Its text is in `PhotoSense.ReactUI/lib/guide.ts`; when something on screen is renamed or added, the guide is changed there.

## Phones and narrow windows

The page fits whatever window it is in, and follows a window as it is resized. From 1024 pixels wide it is laid out as on a desktop. Narrower, it switches to a layout for a phone:

- **Clean up** shows the groups; tapping one shows its review in their place, with **All groups** to go back.
- **Organize** has **Files** and **Folders** at the top to switch between the gallery and the panel of folders; tapping a folder shows its files.
- The large windows (comparing, previewing a move, names already taken, the guide, the map) fill the screen, with their details under the picture instead of beside it.
- The header keeps the logo, **Clean up | Organize**, the errors and the menu; the scanned folders' chip is in the menu as **Change folders or rescan**.

Opening PhotoSense from a phone needs more than the layout: as set up, the UI asks for the service at `http://localhost:7071/api`, which on a phone is the phone itself, and the service lets only `http://localhost:3000` call it (`Cors:Origins` in `PhotoSense.Server/appsettings.json`). Reaching it from another device means serving both on the computer's network address, with `NEXT_PUBLIC_API_BASE`, the CORS list and `AllowedHosts` (below) changed to match, and `PHOTOSENSE_API_KEY` set so that other devices on the network cannot use the service: it has no other login.

## The UI and the service

The React UI (`PhotoSense.ReactUI`) fetches everything over REST from the service, with SWR polling for scan progress and group listings. What they exchange is plain JSON mirroring the DTOs in `PhotoSense.Contracts`; the UI keeps its own `types.ts` in step with them. When adding a field, update both.

The service is one ASP.NET Core program, `PhotoSense.Server`. It hands every request below `/api` to the endpoints in `PhotoSense.Api` (`PhotoSenseRoutes` lists every address), and serves the page itself when the page has been built into it (see *One program for a computer* below). The endpoints know nothing of the program that serves them: they read an `ApiRequest` and write an `ApiResponse`, so that the app on a phone answers the same page at the same addresses with the same code (see *The iPhone app*). What both programs run is wired together once (`AddPhotoSenseCore`, `AddPhotoSenseApi`); each adds only what is its own (`AddPhotoSenseComputer`, `AddPhotoSensePhone`).

A scan runs in the background of the program, one at a time (`ScanRunner`); its progress is held in memory, as before. A scan that the program's stopping cuts short is not carried on when it starts again: scan again, and the files already read are skipped.

### Where the photos live

Everything that lists, reads, moves, copies or erases photos does it through one interface, `IPhotoFileSystem` in `PhotoSense.Domain`: folders that hold files and other folders, and for each file its size and when it last changed. On a computer that is `DiskFileSystem`, the computer's own disks. On a phone it is the phone's photo library (`PhotoLibraryFileSystem`), so that scanning, Clean up, Organize, the record of changes (below), undo and **Delete permanently** work there as they are. Nothing in `PhotoSense.Domain`, `PhotoSense.Application` or `PhotoSense.Api` reaches a disk any other way, and one test runs a scan, Clean up and its undo, an Organize move and its undo, a removal and erasing on photos held only in memory (`PhotosNotOnDiskTests`). What PhotoSense keeps of its own, the database and the thumbnails, stays on the computer's disk, as does the folder browser.

The interface promises what the rules below rest on: a move or copy never replaces a file, a file can be read while it is being moved or removed, and erasing (`Erase`) is a separate operation that only **Delete permanently** asks for.

## Running the React UI

```bash
cd PhotoSense.ReactUI
npm install
npm run dev
```

Environment vars:

```bash
export NEXT_PUBLIC_API_BASE=http://localhost:7071/api   # where the service is
```

The API the UI uses (the service, `http://localhost:7071/api`):

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/about` | GET | Who makes PhotoSense, how to reach them, and the program answering: `{ name, version, program, publisher, copyright, supportEmail, website, privacyPolicyUrl, termsUrl, supportUrl }`, a value not yet set in `product.json` as `null` |
| `/about/icon` | GET | The icon (`branding/app-icon.png`) |
| `/about/{privacy\|terms\|support\|notices}` | GET | The privacy policy, the terms of use or the support page in Markdown, with `product.json`'s details filled in; or the program's third-party notices as plain text |
| `/library/access` | GET | How much of a phone's photos PhotoSense may see: `{ access: "everything" \| "selected" \| "nothing" }`; on a computer always `everything` |
| `/library/choose-more` | POST | Show the phone's own picker for choosing more photos; answers once it is put away, with the access then |
| `/library/open-settings` | POST | Open PhotoSense's page of the phone's settings, where its access to the photos is changed; 204 |
| `/folders?path=` | GET | The folders inside a folder of the computer the service runs on, each with its full path; without `path`, the places to start from (Pictures, the home folder, each disk) |
| `/scan/start` | POST | Start a scan. Body `{ primaryLocation, secondaryLocation?, recursive, startOver? }`; returns `{ instanceId }`, or 400 `{ error }` when a folder is not found on the server |
| `/scan/reset` | POST | Forget everything earlier scans recorded, previews included; refused while a scan is running. The photos are not touched |
| `/scan/progress/{instanceId}` | GET | Current `ScanProgressSnapshot` |
| `/scan/groups?mode=duplicates\|similar&page=&pageSize=&q=&hideKept=` | GET | Groups: a keeper (the best copy) and the photos matched against it |
| `/photos/{id}/thumbnail` | GET | Small JPEG preview (cached at scan time); pictures only |
| `/photos/{id}/image` | GET | The picture for viewing; HEIC and TIFF are converted to JPEG on the fly |
| `/photos/{id}/video` | GET | The video for the in-app player, sent in pieces of up to 4 MB as the player asks for them (`Range` requests) |
| `/photos/{id}/open` | POST | Open the file in the default application of the computer the service runs on |
| `/organize/files?root=` | GET | Every picture and video under a folder, with when and where each was taken; no scan is needed. Each file has an `id` that stands for it in the calls below |
| `/organize/progress?root=&from=&places=` | GET | What has been read of a folder while `/organize/files` is still answering: `{ reading, readingId, total, done, from, more, files, places }`. `from` and `places` say how many files and places the page already has, so each answer holds only what was read since (at most 4,000 files; `more` says to ask again at once) |
| `/organize/plan` | POST | Where a set of files would go and which names are already taken there. Body `{ basePath, folderName, direct, files: [{ id, subfolder }] }`; `subfolder` puts a file in a folder inside the destination (`2022`, `2022\Butte`). Nothing is moved |
| `/organize/apply` | POST | Move or copy the files. The same body plus `mode` (`move` or `copy`), `companions`, `label` and a `name` for each file; returns `{ batchId, done, renamed, skipped, ... }` |
| `/organize/remove` | POST | Take files out of the folder being organized. Body `{ root, files: [{ id }] }`. They go to `_PhotoSense_Removed` inside the root, with the Live Photo videos and edit files that belong to them alone; the answer has the same shape as for `apply`, and names every file asked for that went, one that went along with its picture among them |
| `/organize/undo/{batchId}` | POST | Take one move, copy or removal back, Organize's or Clean up's, all of it or none; returns `{ restored, skipped, problems }`. 404 when it was undone already or never done |
| `/organize/batches` | GET | The latest moves, copies and removals that can still be undone, Clean up's among them |
| `/organize/files/{id}/thumbnail`, `/image` | GET | A file's preview, and the picture for viewing |
| `/organize/files/{id}/open` | POST | Open the file in the default application of the computer the service runs on |
| `/photos/{id}/keep?kept=true\|false` | POST | Mark a copy to keep (bulk removal skips it) |
| `/photos/{id}?physical=true` | DELETE | Remove one file, with the sidecars and Live Photo video that belong to it alone; returns `{ heldAt, companions, batchId }`, and `batchId` undoes it. A file that cannot be moved answers 409 with the reason, and nothing is moved |
| `/photos/bulk/remove-duplicates?group=` | POST | Remove the duplicates of one group, of several (`group=` once for each, or in the body as `{ "groups": [...] }` when there are more than an address holds), or of all groups when none is named. With `&mode=similar`, the similar shots of the groups named instead; those are never removed without a group being named. It is one change: when one file cannot be moved, none is. Returns `{ removed, bytes, skipped, companions, problems, batchId }` |
| `/removed?root=&root=` | GET | The `_PhotoSense_Removed` folders that hold files, with how much is in each: `{ folders: [{ path, files, bytes }], files, bytes }`. Looked for at any depth inside the folders given and inside every folder a scan on record covered, and wherever Organize put removed files |
| `/send/start` | POST | On a phone: offer files to a computer. Body `{ files: [id] }`, the ids `/organize/files` gave; their Live Photo videos and edit files go with them. The phone waits for a computer, on the network and on a cable, until stopped. Returns the state below; 404 when none of the files is there any more, and on a computer always 404 |
| `/send/state` | GET | Where sending has got to: `{ stage, code, computer, done, total, bytes, totalBytes, deletable, where, phone, problem }`. `stage` is `idle`, `waiting`, `comparing`, `sending`, `sent` or `failed`; `code` is the number to compare while comparing; `deletable` names the pictures and videos every file of which is safely on the computer |
| `/send/confirm` | POST | Body `{ same }`: whether the computer shows the same number |
| `/send/stop` | POST | Stop offering; what was sent stays known, to be deleted |
| `/send/delete-sent` | POST | Delete from the phone what `deletable` names, after the phone's own question, into its Recently Deleted; returns `{ deleted }`, or 409 with the reason when the person said no |
| `/phones` | GET | On a computer: the phones offering photos now, found on the network (Bonjour) and on a cable (Apple's device service): `{ phones: [{ key, name, via }] }`, `via` being `wifi` or `usb`. On a phone always 404 |
| `/receive/start` | POST | Receive what a phone found offers. Body `{ phone, folder }`: a `key` from `/phones` and a folder of this computer. 400 without them, 404 when the phone is no longer found or the folder is not there, 409 while already receiving |
| `/receive/state` | GET | Where receiving has got to: `{ stage, code, phone, done, total, bytes, totalBytes, alreadyThere, folder, problem }`. `stage` is `idle`, `connecting`, `comparing`, `receiving`, `done` or `failed` |
| `/receive/confirm`, `/receive/stop` | POST | As for sending: `{ same }`, and stopping. A file still arriving when stopped is not kept |
| `/removed/erase` | POST | Erase everything in those folders, and the folders. Body `{ folders: [path] }`, the paths as `/removed` listed them; returns `{ erased, bytes, skipped, problems }`. This is the one request that erases files. A path that is not a folder named `_PhotoSense_Removed` is refused with 400, and nothing is erased then |

Every request above that shows where files are or changes anything must carry the `x-photosense-client` header (the React client sends it): the folder listing, starting a scan, the groups, the scan log (`/scan/logs`), as well as everything that changes or removes photos. Requests without it get 401. Only `/health`, `/about` and its documents, `/library/access`, `/scan/status`, `/scan/progress` and the thumbnails, images and videos answer without it: those carry no file names, or are loaded by `<img>` and `<video>` tags, which cannot send a header. Other web sites cannot add it, because the service only grants cross-origin access to the UI's address, listed under `Cors:Origins` in `PhotoSense.Server/appsettings.json` (or given as `--Cors:Origins:0=<address>`). `dev-start.ps1` tells the service the port it gave the UI. A page the service serves itself is at its own address and needs no listing.

The service also answers only under this computer's own names, `localhost`, `127.0.0.1` and `[::1]`; under any other name a request gets 400. A web site can have its own name pointed at this computer (DNS rebinding), and its page would then be at the service's own address, free to send the header. Other names can be allowed with the `AllowedHosts` setting, separated by semicolons (`--AllowedHosts=localhost;photos-pc.local`), which replaces the three.

### Choosing the folders to scan

A scan needs each folder's full path as the service sees it. The **Browse** button beside a path opens a folder browser that lists the folders of the computer the service runs on and fills in the full path of the one chosen; a path can also be typed or pasted. (A browser's own folder dialog is no use here: it tells a web page the name of the folder that was picked and never where it is.)

A photo program's own library is never offered, looked inside or scanned, even when the folder it is in is: "Photos Library.photoslibrary" in a Mac's Pictures folder, an iPhoto or Aperture library, Photo Booth's. What is inside belongs to that program's database, and a file moved or removed from outside it would break the library.

On a Mac, the disks offered are the whole disk ("Computer") and the disks under `/Volumes`, plugged in or shared; the system's own volumes are left out.

One folder is enough: copies inside it, in the folder itself or in its subfolders, are found. The secondary folder is for comparing a second place with the first, a backup for instance. Giving the same folder as both changes nothing: it is scanned once, the setup screen says so, and a file is never matched with itself.

### Scanning again, and starting over

A scan builds on the one before it: files that have not changed since are skipped, files that have gone are forgotten at the end, and copies marked keep stay marked. That makes a second scan of a large library take seconds rather than minutes.

To run a scan from nothing instead, tick **Start over** on the setup screen before pressing Scan: everything recorded by earlier scans is forgotten first, and every file is read again. **Clear results**, in the menu, does the forgetting on its own, without scanning. Either way only PhotoSense's record goes, with its previews and keep marks; the photos, and anything already moved to `_PhotoSense_Removed`, stay where they are.

A scan runs in the service, not in the page. A page that is loaded again while one is running, or opened in another tab, shows that scan's progress and says when it has finished, as the page that started it does.

When a scan finds no duplicates, the Duplicates tab, which is the one that opens, is empty; it then offers a button to the similar shots when there are any (and the Similar tab does the same the other way about).

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

The data must not be inside the folder the service runs from (`PhotoSense.Server/bin/...`, or the folder of a published program), and the service refuses to start a database there: a rebuild, or a newer copy of the program put in its place, would take the data with it.

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

To keep the HEIC instead, set `PhotoStorage__KeepFormat` to `CameraOriginal` (as an environment variable, or under `PhotoStorage` in `PhotoSense.Server/appsettings.json`) and restart the service. The HEIC is the file the phone wrote, the JPEG a conversion made from it, and the HEIC is usually about half the size. The default is `WidelyCompatible`. Nothing needs scanning again; the choice is applied when groups are worked out.

PhotoSense shows HEIC pictures itself, whatever the computer can open. Outside it, Windows opens HEIC files only once the free "HEIF Image Extensions" and the "HEVC Video Extensions" are installed from the Microsoft Store; without both, Photos and Explorer previews show nothing. macOS and iOS open them as they are.

### Looking at a file before deciding

The look and layout follow the design handoff in `docs/design_handoff_photosense_redesign` (dark and light themes, chosen from the menu and remembered in the browser). The review desk shows the original beside one copy, with a table of what differs between them; clicking either opens the comparison window, where Space flips between the copy and the original in the same spot. When a group has several copies, the window says which one is showing ("Copy 2 of 3") and the arrows over the copy's picture, or the left and right arrow keys, move to the one before or after it. Deleting a copy from the window closes it when that was the group's only copy; otherwise the copy that took its place is shown.


A copy and its original are always two files, and each is shown under its own name with the folder it is in: under the two pictures on the review desk, under each thumbnail, and in the heading of the comparison window ("`IMG_0648.JPG`, a copy of `IMG_0377.JPG`"). Two files holding the very same picture can sit side by side in one folder under unrelated names, and look like one file until the names are read.

Hovering over a duplicate lists what sets it apart from the original: its name, folder, format, pixel size, file size or capture date, whichever differ. Two files of one picture can share a name, a folder and a date, as a phone's `IMG_4299.HEIC` and the `IMG_4299.JPG` made from it do, and would otherwise look like one file listed twice. Each thumbnail also carries its format in the corner.

Every thumbnail, in Clean up and in Organize, has a card of details for the file it shows: its name, when and where it was taken, its folder, its pixel size, format and size, and in Clean up the camera. The card comes when the pointer rests on the thumbnail for a moment, or at once when the thumbnail is reached with Tab, and screen readers read it as the thumbnail's description. The pointer can be moved onto it to read or select what it says; Esc, a click, or scrolling puts it away. A touch never opens it, so on a phone a tap does what it always did.

Clicking a picture or a thumbnail opens it in a floating window. A video plays in the page, in the review panel and in the floating window alike. What plays there depends on the browser: MP4 and most iPhone MOV files do; where one does not, the player says so. Either way **Open in default player** (or **Open in default viewer** for a picture) hands the file to the operating system, as double-clicking it would: the file itself through the Windows shell, `open` on macOS, `xdg-open` on Linux. The program starts on the computer the service runs on, so this is for running PhotoSense on your own machine.

On either tab the review desk offers **Delete this copy**, which takes the one file shown beside the original (or the best shot) and nothing else, and, when a group has more than one, **Delete all N copies**, which takes every copy in that group and leaves the original. On the Similar tab the files are different shots or edited versions and not copies of one file; the question asked first says so. **Delete all duplicates** never takes them.

Groups can also be selected: each tile has a tick box, and **Select all**, above the tiles, ticks every group on the page (ticks stay over a turn of the page, and go when the other tab is turned to). A bar under the tiles says how many groups are ticked and what their copies come to, and deletes the duplicates, or on the Similar tab the similar shots, of all of them together after asking. The best of each group stays, and copies marked keep are skipped.

Removing never erases anything. Files are moved to a `_PhotoSense_Removed` folder inside the scanned folder, keeping their relative path. **Undo**, on the message after a removal, puts every file it took back where it was, and the copies are listed in their groups again; Organize's **Recently moved** lists Clean up's removals too, for undoing later. **Delete permanently** (below) frees the space. Every question asked before a removal, and the message after it, names the file that goes and the file that stays.

### Every change happens in full or not at all

Each move, copy or removal, in Clean up or in Organize, and each undo of one, is written down in the database before any file is touched, with every file it is to move and where to. If one file cannot go (open in another program, say), the files that had already gone are put back, and the message names the file that stopped it: "IMG_2.JPG could not be moved: ... Nothing was moved: every file is where it was." Files left out before anything moves, because they have changed since the scan, have gone, or would be the last copy, are still left out and reported, as before; the rest go.

If the service stops part of the way (closed, crashed, the computer turned off), it puts the change back the next time it starts, before anything else can move files, and records what it did in its audit log (the `audit` collection in `photosense.db`). The page does not yet say so. It looks at each file rather than trusting the record: a file that had moved goes back, a copy that was being written is deleted only while it holds nothing but what the original begins with, and a file it finds where it expected nothing is left alone. An undo cut short is taken back the same way, so the change it was undoing stands and can be undone again.

When an undo finds a file's old name taken by another file, it puts it back under that name with a number. A file that is no longer where the change put it is left, and the message says so. A copy is deleted by an undo only while it is still exactly as it was written and what it was copied from is still there; besides **Delete permanently**, that and the half-written copies of a change being put back are the only files PhotoSense ever deletes, and each is a copy it made itself.

### Deleting permanently

**Delete permanently**, in the menu, erases everything that Clean up and Organize have removed and that is still waiting in a `_PhotoSense_Removed` folder. It is the one thing in PhotoSense that erases files: they do not go to the Recycle Bin, and a removal whose files are erased can no longer be undone (it leaves Recently moved).

It first looks for those folders: inside the folders last scanned and the folder being organized, inside every folder a scan on record covered, and wherever Organize put a removed file. A warning then lists the folders it found with the number of files in each and what they come to. Confirming it brings a second, small question, **Are you sure?**, and only that one erases. The second question has a switch, **Do not ask me this second time again**; with it on, the warning alone is asked from then on. **Settings**, in the menu, turns the second question back on. Settings are kept in the browser.

A file that cannot be erased (in use, say) is left, with the folders it is in, and the message says so. Only folders named `_PhotoSense_Removed` are ever emptied. A link, or anything else Windows marks as standing in for a file kept elsewhere (the files OneDrive keeps online, for one), is neither counted, followed nor erased, and the folder holding it stays.

The last copy of a picture is never removed as a duplicate. A copy goes only while the original it was matched with is still on disk exactly as it was scanned, and the original can be removed "instead" only while one of its copies is. If that file has been deleted, moved or edited since the scan, by PhotoSense or by anything else, the removal is refused and the file is left where it is; scan again to bring the results up to date. The same holds if two records turn out to be one file reached by two paths (a linked folder, or a drive letter standing for a folder): the file is put straight back.

### Organize

The **Clean up / Organize** switch in the header leads to a second area that sorts the files of a folder into folders. It starts with the folder last scanned; **Choose root folder** takes any other. Only dates and positions are read, never the pictures themselves. Files a scan has been through are listed from what the scan recorded. Any other file is read once, and what was read is kept in the database, so a folder is slow to list only the first time: about 30 files a second on an external hard disk, which for 16,000 unscanned files is some nine minutes, and a few seconds after that. While a folder is being read the page shows how many of its files have been gone through, and shows the files as they are read: it asks every second or so for the ones read since it last asked. They come in the order they were read; the whole list, latest first, takes their place when the reading is over. Files can be selected and put in folders of your own meanwhile, but a folder can only be previewed, and so moved, once every file is in.

- **Suggested folders** group the files by place or by date, and then, inside each folder, by the other. **Group by Place** makes a folder for each place, with a subfolder for each year or each month inside it, or none (`Butte\2022`). **Group by Date** makes a folder for each year or each month, with a subfolder for each place inside it, or none (`2022\Butte`); a file with no location stays in the year's or month's own folder. Places within 1, 5, 15, 25 or 50 miles of each other share a folder (or every place gets its own), named after the place with the most files: by its landmark or area where one is known, otherwise by its town, or as "Town, state", or as a state folder with the place inside. When the folders are by place, the map shows them as dots; clicking a dot or a card shows that folder's files alone. The folder picked is named above the files, with a × to let it go; **Clear all**, beside it, puts away the folder, the search, the filter and the kind together, so that every file shows again.
- Above the files, **All**, **Not organized** and **No location** filter them, and **Photos & videos**, **Photos** and **Videos** show both kinds or only one. The two combine (**Not organized** with **Videos** is the videos still to sort), and each count is within what the other row leaves. The kind shown is also what the folders take: with **Photos** on, the suggested folders and your own folders hold and move only pictures, with **Videos** only videos, and the files of the other kind stay where they are for later. A Live Photo's video is left out under **Videos**: it goes with its picture (`livePhoto` in the `/organize/files` listing marks it once the whole folder is read).
- **The map** is Google's when a Google Maps key is set (see below): streets, or satellite pictures with **Satellite**, to zoom and move about in. Without a key, or when Google cannot be reached or refuses the key, a plain map drawn by the page is shown instead, and the page asks Google for nothing. A click on the map itself, or on the button in its corner, opens it large in a window of its own, where it does all that it does small (on Google's map, the whole screen as well). Clicking a dot there shows that folder's files and closes the window; Esc closes it too.
- **Deleting.** As soon as a file is selected, a bin appears in the bar at the foot of the files. It asks first, then takes every selected file out of the folder, with the Live Photo videos and edit files that belong to them alone, and the selection is over. Nothing is erased: the files go to `_PhotoSense_Removed` inside the root folder, the same folder Clean up uses, each keeping its place relative to the root. **Undo** (in the message, or under Recently moved) puts them back; **Delete permanently**, in the menu, frees the space. What a scan recorded of a deleted file is dropped, and an undo brings it back.
- **Select all**, above the files, selects every file being shown, on every page, and turned off lets them go; with a folder picked, a search or a filter on, that is the files those leave.
- **Your folders** are folders of your own. **New folder** (in the panel), **New folder from these** (in the bar under selected files) and a drop on **New folder** open one window for it, with two choices. **New folder** takes a name and is made inside the folder being organized, or wherever **Change** (beside **Made inside**) chooses. **Folder already there** browses the folders, starting in the folder being organized, and the files go straight into the one picked: a folder made earlier, say, that new pictures belong in. Nothing in it is replaced; a name already taken there gets a number. A folder is set up once: picking it again, or naming a new folder where one of yours already is, is refused. Then select files (click, Shift-click for a range) or drag them onto it. Files in your folders are left out of the suggestions. Files can be dragged onto a suggested folder as well. A card says where its folder is when that is not a new folder in the folder being organized ("Already there", "In D:\Archive"), with the whole path on hover; its preview starts there.
- **Preview** shows every file a folder would take before anything moves: click a file to leave it where it is, rename the folder (`\` makes subfolders), choose another place for it, and choose between moving and copying. Files already in the folder they would go to are left alone.
- **Nothing is ever replaced.** A file going where its name is taken is given a number, as in `IMG_6435 (1).JPG`. **Review each name** shows the file beside whatever has the name, says when the two are byte for byte the same, and lets each be given a number, another name, or left where it is. The service applies the same rule again when the files are moved, whatever it is asked. Under each of the files shown there is a small bin, for when the two are the same file or one is not wanted: it asks first, naming the file that goes and the one that stays by their folders, and then removes that one file as **Deleting** above does. The window goes on to the next name, or closes when that was the last, and the file is gone from the preview and from the page.
- A picture's Live Photo video and edit files can be brought along with it (see below); they follow the picture's new name.
- **Recently moved**, a button at the foot of the panel that says how many can be undone, opens a window listing what was done, Clean up's removals among it, each with **Undo**: moved files go back under the names they had, copies are removed, and the folders that were made for them go again if that leaves them empty. A copy that has changed since it was made is left; a file whose old place has been taken by another goes back under its name with a number.

#### A Google Maps key

The map of the suggested folders uses the Google Maps JavaScript API, which needs a key from a Google Cloud project with billing enabled (Google gives a monthly allowance of free map loads). Make one under *APIs & Services > Credentials* with the *Maps JavaScript API* enabled, restrict it to `http://localhost:3000/*`, and put it in `PhotoSense.ReactUI/.env.local` (which git ignores):

```
NEXT_PUBLIC_GOOGLE_MAPS_KEY=your-key
```

The dots are Google's advanced markers, which need a map ID. Until one is set, the page uses Google's `DEMO_MAP_ID`, which Google documents for trying maps out. For release, make a map ID in the Cloud console (*Google Maps Platform > Map management*, type *JavaScript*) and set it beside the key:

```
NEXT_PUBLIC_GOOGLE_MAPS_ID=your-map-id
```

Start the UI again afterwards: the key is read when it starts. With a key set, the page fetches the map from Google, so Google sees which parts of the world are looked at. The pictures, their names and their positions are not sent; the dots are drawn by the page. The large map is a second map as far as Google's count of map loads goes: one more each time it is opened.

What was arranged but not carried out (your folders, files dropped on a suggestion, the names given to suggested folders) lives in the page and is gone when it is reloaded; what was moved is on record in the service and can still be undone after a reload.

### Sidecars and Live Photos

An iPhone item can be several files: `IMG_1234.HEIC` (the picture), `IMG_1234.MOV` (its Live Photo video), `IMG_E1234.HEIC` (an edited version) and `IMG_1234.AAE` / `IMG_O1234.AAE` (the record of the edits). When a picture is removed, the files that belonged to it alone go with it:

- Nothing goes while another picture of the same item stays in the folder (the other format, or the edited version).
- A video goes only if it carries the same Live Photo identifier as the picture. The name is not enough: unrelated videos do end up with the same number as a picture.
- Sidecars go once no picture or video of the item is left.

A Live Photo's video is also left out of duplicate matching while its picture is beside it, so it can only ever leave together with that picture.

### Place names

Organize also names the landmark or area a picture was taken at, when the position is at one: a sight, a park, a beach, a district ("Old Faithful Geyser", "Caras Park", "Rehoboth Beach"). The list gives every landmark a single point, so a picture counts as taken there only within a short distance of that point (200 m to 1 km, by kind). A large park is therefore named only near the point the list has for it, and most places are named after their town; the card says so when it is.

Where a picture was taken is shown as the nearest town ("Buxton, North Carolina, US", or "Near Anaconda, Montana, US" when the town is more than 3 km away; nothing beyond 80 km). The lookup uses a list bundled with the application, so positions are never sent anywhere. The list is an extract of [GeoNames](https://www.geonames.org/) data, licensed CC BY 4.0; see `PhotoSense.Infrastructure/Places/README.md`.

Decoding is done with Magick.NET. iPhone HEIC files decode slowly (about a second each), so a first scan of a few thousand photos takes several minutes.

## Adding New API Fields

1. Add property in the relevant Application DTO / event.
2. Fill it in in the endpoint in `PhotoSense.Api` that sends it.
3. Update `PhotoSense.ReactUI/types.ts` & adjust component rendering.

## Development Notes

- TailwindCSS powers the new React UI styling.
- `swr` provides lightweight polling + cache for scan progress & groups.
- Components are intentionally stateless where possible.

## Tests & Coverage

The service, the UI and the build tools are each tested to 100% of branches.

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

Some code is shaped so that every branch can be exercised from one operating system: `PhotoPath.Key(path, ignoreCase)`, `ShellSystemViewer.DesktopOf` and `BasicExifMetadataExtractor.Read(photo, directories)` take as an argument what they would otherwise ask the system or a file for. Everything that touches photos takes the file system it works on (`IPhotoFileSystem`), so that a file that will not move, will not go back, cannot be read or cannot be erased, and a change cut short part of the way, are all tested on any system with `FailingDisk` in the tests (the real disk, except where a test says it fails), and the whole of Clean up and Organize is tested on photos in memory with `InMemoryFileSystem`. A guard against a library handing back nothing (`MagickImageAnalyzer.Required`) is callable on its own for the same reason.

### UI (React)

From `PhotoSense.ReactUI`:

```bash
npm test                # once
npm run test:watch      # again on every change
npm run test:coverage   # once, with coverage; fails below 100%
```

The tests are in `PhotoSense.ReactUI/tests`, laid out like the code they cover (`app`, `components`, `lib`). They run in Vitest with Testing Library, against jsdom rather than a real browser, so nothing needs to be running: the service is stood in for by `tests/fixtures.ts`, and `tests/deferred.ts` holds a request open for as long as a test needs.

Coverage is measured with Istanbul over `app`, `components` and `lib`, and `vitest.config.ts` fails the run when statements, branches, functions or lines fall below 100%. CI runs it as well, before building the UI. Istanbul counts each arm of an `if` or a ternary and each operand of `&&`, `||` and `??` that is reached; `?.` is not counted as a branch. The HTML report is written to `PhotoSense.ReactUI/coverage`.

### Build tools (Node)

From the repo root:

```bash
node --test --experimental-test-coverage --test-coverage-include="tools/*.mjs" --test-coverage-lines=100 --test-coverage-branches=100 --test-coverage-functions=100 tools/tests/
```

The tests of `tools/notices.mjs` also check that the notices in `notices/` are up to date, so they need a restore (`dotnet restore PhotoSense.sln`) and the page's packages (`npm ci` in `PhotoSense.ReactUI`) first.

## Quick Local Run (service + React)

Needs the .NET 10 SDK (every project targets `net10.0`, and `global.json` asks for a 10.0 SDK) and Node. Nothing else: no emulator, no Functions tools. On a Mac, the script needs PowerShell 7 (`brew install --cask powershell`) and is started as `pwsh ./dev-start.ps1`.

The PowerShell helper script builds the service and starts it with the React (Next.js) UI:

```powershell
./dev-start.ps1             # build + start the service + React UI
./dev-start.ps1 -Open       # also open browser to http://localhost:3000
./dev-start.ps1 -ServicePort 7072
./dev-start.ps1 -NoBuild    # skip build for faster restart
./dev-start.ps1 -Clean      # clean then build
```

To stop PhotoSense, press Ctrl+C in the terminal it was started in, or run `./dev-start.ps1 -Stop` from any terminal. To restart it, run `./dev-start.ps1` again: it first stops a copy that is still running, since two cannot share the ports and a running service keeps its program files locked against the build. Only PhotoSense's own processes are stopped; if some other program has the UI's port, the UI takes the next free one and the service is told to accept it there.

`dotnet run --project PhotoSense.Server` starts the service on its own, at `http://localhost:7071`.

## One program for a computer (Windows or Mac)

`publish.ps1` builds PhotoSense as one program: the service, with .NET inside it so that nothing need be installed, and the page beside it in `wwwroot`. Start the program and open `http://localhost:7071`; the page and the service are at the one address, and no other server is needed.

```powershell
./publish.ps1                        # for the system this runs on
./publish.ps1 -Runtime osx-arm64     # a Mac with Apple silicon (M1 and later)
./publish.ps1 -Runtime osx-x64       # an Intel Mac
./publish.ps1 -Runtime win-x64       # Windows
```

The program goes to `publish/<runtime>`: `PhotoSense.Server.exe` on Windows, `PhotoSense.Server` on a Mac. A Mac build can be made on Windows. It is not signed, so the first time macOS asks before running it: Control-click it in Finder and choose Open, or clear the mark with `xattr -dr com.apple.quarantine <the folder>`.

CI builds and tests the service on Windows and on macOS as well as on Linux, and on both of those publishes the program, starts it and asks it for the page and for `/api/health`.

## The iPhone app

`PhotoSense.iOS` is PhotoSense on an iPhone or iPad: the same page, in a web view that fills the screen, with every request the page makes answered inside the app by the same endpoints the service uses. There is no web server on the phone. The page's own files are handed out from the app, and its requests below `/api` go to `PhotoSenseRoutes` (`PageSchemeHandler`, through `InProcessApi` and `PageFiles` in `PhotoSense.Api/Hosting`). What the app records is kept among its own files, and the previews where the phone may clear them when it runs short of space.

The first time it opens, it asks to see the photos: all of them, those picked, or none. The library is then offered as one folder, **Photo Library**, holding every picture and video once under the name it was taken with; a Live Photo is two files there, its picture and its video. Pictures taken under the same name are told apart by the start of each one's own id (`IMG_0001 (1A2B3C4D).HEIC`), so that a name never comes to stand for another picture. Pictures are decoded by the phone itself (`ImageIOAnalyzer`), since Magick.NET has no build for a phone, and measured the same way as on a computer (`ImageFingerprint`).

A scan finds copies and similar shots, and Clean up and Organize work on the phone's library as they do on a folder, with its albums as folders:

- **The library is a folder** (`/Photo Library`) holding every picture and video that is in none of the albums PhotoSense made. Each of those albums is a folder inside it, under the folders it is in: Organize's `Butte\2022` is a Butte folder in the Photos app holding a 2022 album. The person's own albums are never shown, changed or taken away; PhotoSense knows its own by the ids it keeps (`albums.txt` among the app's files).
- **Moving** a picture puts it in the album of its folder, made as it arrives, and takes it out of every other album PhotoSense made. The picture itself never moves and keeps its name. There are no copies on a phone, so Organize offers Move only. An album left empty, by **Undo** or by moving everything out of it, stays: iOS asks the person before an app takes an album away, and an empty album does no harm, so it is left for the person to delete in Photos.
- **Removing** puts the picture, with its Live Photo video, in an album called **PhotoSense Removed** (shown to the rest of PhotoSense as `_PhotoSense_Removed`), where **Undo** takes it back. It is not hidden: with the Hidden album locked, as iOS has it by default since iOS 16, apps cannot see hidden photos at all, so PhotoSense could neither put them back nor delete them.
- **Delete permanently** sends what is in that album to the phone's **Recently Deleted**, which keeps it for 30 days.
- **One change, one question.** Each change is handed to the phone whole (`IPhotoChangesTogether`), as one PhotoKit request: all of it happens or none does, and when iOS asks the person first, as it does before deleting, it asks once. A change the person declines leaves everything as it was, and the message says so.

When only some photos were allowed, the scan screen says so, with **Choose more photos** for the phone's own picker and **Open Settings** for allowing all of them (`/library/...`, `PhotoKitAccess`). A link to anywhere outside the app (a web site, an email address) opens in Safari or Mail.

Only the parts that talk to Apple's frameworks are in `PhotoSense.iOS`: the photo library (`PhotoKitLibrary`), the decoder, the web view and its requests. Everything that decides anything is shared and tested with the rest (`PhotoLibraryFileSystemTests`, `InProcessApiTests`, and `PhotosOnAPhoneTests`, which runs the phone's whole stack but Apple's code on a library held in memory).

### Sending photos from a phone to a computer

On the phone, **Send to computer** under the selected files in Organize offers them, with their Live Photo videos and edit files, to PhotoSense on a computer; on the computer, **Receive from phone** in the menu takes them into a folder (`PhoneSending` and `PhoneReceiving` in `PhotoSense.Application/Transfer`).

- **How the two find each other.** The phone waits for a connection on port 47615, or on any free port should another app have that one, and says so on the Wi-Fi with Bonjour (`_photosense._tcp`, with the phone's id and name; `BonjourAdvertiser` in the app). The computer asks the networks it is on (`MdnsPhoneFinder`), and Apple's device service for an iPhone or iPad on a cable (`UsbmuxPhoneFinder`: the Apple Mobile Device Service on Windows, which Apple Devices or iTunes installs, and the system's own on a Mac), and connects. Only while the person has the phone offering photos is anything listening; nothing goes through the internet.
- **Pairing, once.** Each connection agrees on a key of its own (elliptic-curve Diffie-Hellman, P-256). The first time, both show a six-digit number worked out from everything said, and the person says on both whether it is the same; someone in between would make them differ, and the phone commits to its key before it sees the computer's random number, so that no one can choose one to make them match. The two then keep a secret (`IPairingStore`, in the database) and prove they have it each time after, with nothing asked. Everything said after that is sealed with AES-GCM, a key for each direction and a number for each frame (`Pairing`, `SecureChannel`, `Wire`).
- **Nothing replaced, nothing damaged.** Each file is written under a hidden name of PhotoSense's own (`.IMG_0001.JPG.<id>.photosense-part`) until all of it is there and its SHA-256 is the one the phone sent, and is then given its own name, numbered when that is taken. A file arriving damaged, or cut short by a stop, is not kept. One the very same as a file already in the folder is not written again, and counts as safely there. A name the phone gives is made a plain name of that folder, never a path elsewhere.
- **Copy or move.** The person chooses on the phone. A file counts as safely on the computer only once the computer has said so with the SHA-256 the phone sent. With **Move**, once sending is through, the phone deletes from itself the pictures and videos every file of which is there, into its own Recently Deleted (`IPhotoChangesTogether.EraseAll`), where they stay for 30 days; iOS asks first, and the message after says what went where. With **Copy**, the phone offers the same afterwards, naming what goes and that the copies on the computer stay. Nothing is ever copied onto the phone.
- **One at a time.** A phone sends to one computer at a time; another that connects meanwhile is told the phone is busy.

### Building it

The app builds only where Apple's tools are. The `iPhone app` workflow (`.github/workflows/iphone.yml`) builds it on GitHub's Macs on every push: first the page, with `NEXT_PUBLIC_API_BASE=/api`, then the app for the simulator. It then starts a simulator, puts a photo, a copy of it and a JPEG of it in the simulator's library, and starts the app with `PHOTOSENSE_SELF_CHECK=1`. The app then makes the page's own requests from inside its web view: the library offered, a scan started and finished, the copies found, a preview and a whole picture shown, Organize's listing, a removal refused. Each step is reported as a note on the run. On a Mac with Xcode the same build is:

```bash
cd PhotoSense.ReactUI && npm ci && NEXT_PUBLIC_API_BASE=/api npm run build && cd ..
dotnet workload install ios
dotnet build PhotoSense.iOS/PhotoSense.iOS.csproj -c Release -r iossimulator-arm64
```

### Putting it on an iPhone or iPad

The workflow's second job signs the app and sends it to TestFlight, once these are in the repository's secrets (Settings > Secrets and variables > Actions). Until then it is skipped.

| Secret | What it is |
|---|---|
| `APPLE_CERTIFICATE_P12` | An Apple Distribution certificate with its private key, as a `.p12` file in base64 |
| `APPLE_CERTIFICATE_PASSWORD` | The password the `.p12` file was saved with |
| `APPLE_PROFILE` | An App Store provisioning profile for the app's id, `com.j2k2ham.photosense`, in base64 |
| `APPSTORE_KEY_ID`, `APPSTORE_ISSUER_ID`, `APPSTORE_KEY_P8` | An App Store Connect API key (Users and Access > Integrations), its issuer, and the `.p8` file's text |

Without a Mac, the certificate's request can be made with OpenSSL, which comes with Git for Windows:

```bash
openssl req -new -newkey rsa:2048 -nodes -keyout photosense.key -out photosense.csr -subj "/CN=PhotoSense/C=US"
# upload photosense.csr at developer.apple.com > Certificates > Apple Distribution, and download distribution.cer
openssl x509 -inform der -in distribution.cer -out distribution.pem
openssl pkcs12 -export -legacy -inkey photosense.key -in distribution.pem -out distribution.p12
base64 -w0 distribution.p12   # the value of APPLE_CERTIFICATE_P12
```

Keep `photosense.key` and the `.p12` file out of the repository. In App Store Connect the app needs a record with the same id; each build then appears in TestFlight once Apple has processed it.

`PhotoSense.iOS/PrivacyInfo.xcprivacy` is what Apple asks every app to declare: PhotoSense tracks no one and collects nothing, and the only "required reason" uses are the .NET runtime's own (file dates, free space, time since start). It must change if the app ever sends anything off the phone, a map included.

## The Android app

`PhotoSense.Android` is PhotoSense on an Android phone (Android 11 or later): the same page in a web view, with its requests answered inside the app (`PageClient`, through `InProcessApi` and `PageFiles`), as on the iPhone. The page is shown at `https://appassets.androidplatform.net/`, an address Android keeps for an app's own files. Android's web view hands the app no request's body, so there the page hands each body over first (`window.PhotoSenseAndroid.keep`) and the request carries the number it was given (`x-photosense-body`).

The phone's storage is one folder, **Phone** (`/Phone`), holding its own folders as Android keeps them: DCIM, Pictures, Movies and the rest (`MediaFileSystem`, over Android's media store through `IMediaStore`).

- **Folders.** Android keeps pictures and videos only inside DCIM, Pictures or Movies, so a folder of them is made inside one of those; a folder at the top of the phone's storage is refused with a message that says so.
- **Moving** a photo changes the folder Android keeps it in, and may give it a numbered name, as on a computer. Nothing is copied onto the phone: photos are copied to a computer.
- **Removing** puts the photo in `_PhotoSense_Removed` inside the folder being cleaned up or organized, keeping its place below it, where **Undo** takes it back. For the phone's storage as a whole, which can hold no folder of its own, that is `Pictures/PhotoSense Removed`, shown to the rest of PhotoSense as `/Phone/_PhotoSense_Removed`.
- **Delete permanently** sends what was removed to Android's bin, which keeps it for 30 days.
- **Android asks first.** A photo another app made (the camera's, say) is moved or put in the bin only after the person says yes to Android's own question, asked once for all the photos changed together; should one move fail, those already moved go back. Photos PhotoSense made itself it may change without asking.
- **Sending to a computer** works over Wi-Fi, told to the network with Android's own Bonjour (`NsdAdvertiser`); the cable is Apple's alone.

Only the parts that talk to Android are in `PhotoSense.Android`: the media store (`AndroidMediaStore`), the decoder (`AndroidImageAnalyzer`), what PhotoSense may see (`AndroidAccess`), the web view and its requests. Everything that decides anything is shared and tested with the rest (`MediaFileSystemTests`, and `PhotosOnAndroidTests`, which runs the phone's whole stack but Android's own code on a phone's storage held in memory).

### Building it

The `android` workload, a JDK and Android's SDK build it (Visual Studio installs all three). First build the page for the app, as for the iPhone: in `PhotoSense.ReactUI`, `NEXT_PUBLIC_API_BASE=/api npm run build`. A page built elsewhere can be named instead with `-p:PhotoSensePage=<folder>`; that keeps this computer's own `.env.local` out of the app. Then `dotnet build PhotoSense.Android -c Release`.

The check is `.github/workflows/android.yml`: it builds the app for an emulator with three sample pictures built in (`-p:PhotoSenseSamples`), starts Android 14 on GitHub's machine, and the app checks itself (`SelfCheck`, started with the extra `selfcheck`), each step written on the run.

## VS Code Tasks

Common tasks are defined in `.vscode/tasks.json`:

| Task | Purpose |
|------|---------|
| `build:solution` | Build all projects |
| `build (service)` | Build the service (the default build task) |
| `run:service` | Start the service (port 7071) |
| `run:react` | Start React UI (Next.js dev server) |
| `run:service+react` | Combined startup via dev-start script |
| `dev:start` | Same as combined (opens browser) |
| `publish:program` | Build the one program for this computer (`publish.ps1`) |

Run via the VS Code command palette: `Tasks: Run Task`. `.vscode/launch.json` starts the service under the debugger, or attaches to one that runs.

## Versions

Every program is numbered as part of its build, from `version.json` and the repository's history (`tools/version.mjs`, run by `Directory.Build.targets`):

- `release` says what the next release is: `major`, `minor` or `patch`. It is the one thing to change before a release.
- The last release is the highest release tag: `v1.2.0` for all the programs at once.
- An ordinary build is the next release followed by the number of commits so far: `1.3.0.57` after `v1.2.0` with `release` set to `minor`. Every build of the same commit gets the same number, on any machine, so the programs built from it match; the number only rises, as Apple's build numbers and Windows' file versions need.
- A release build is the release alone, `1.3.0`: `dotnet build -p:PhotoSenseRelease=true`, or any build of a commit that carries its release tag. Once it has shipped, tag that commit (`git tag v1.3.0`).

With `linked` set to `false`, each program is numbered on its own: by its own entry under `parts` (`server`, `iphone`), and by its own tags as well as the shared ones (`iphone-v1.0.3`). Linking them again carries on above the highest release of any of them. A program names itself with `<PhotoSensePart>` in its project file; the libraries every program shares take the linked version.

On an iPhone the version people see is the three numbers (`1.3.0`), and the build number Apple asks to rise with every upload is the commit count. `node tools/version.mjs --part iphone` prints a program's version.

## About, the privacy policy and the support page

**About PhotoSense**, in the menu, shows the version of the program answering, who makes it and how to reach them (`/about`), and holds the support page, the privacy policy, the terms of use and the acknowledgements of what others made that PhotoSense uses.

- Who makes PhotoSense, the copyright line, the support address, the web addresses and the governing law are in `product.json` alone, built into `PhotoSense.Api`. A value left blank there is shown as not set yet.
- The documents are Markdown in `site/pages`, with `{{name}}`, `{{publisher}}`, `{{supportEmail}}` and `{{governingLaw}}` filled in from `product.json`. A `###` heading that ends in a question mark is a frequently asked question: the app and the site show each as a row with a plus, which opens its answer (whatever follows the heading, up to the next one) under it and turns into a minus. The support page's questions are written that way. The app shows them as they are (`lib/markdown.ts` reads them); `node tools/site.mjs` writes the same pages as a web site in `site/out`, for the App Store listing and the app to point to. It refuses while the support address or the governing law is not set; `--draft` writes a marked draft.
- The third-party notices are written by `node tools/notices.mjs`, a file for each program (`notices/server.txt`, `notices/iphone.txt`) from what it actually ships: its NuGet packages (from the restore), the page's own packages and Tailwind's styles (from `package-lock.json`), the page's fonts (`tools/fonts`) and the place names (GeoNames). Each licence is given in full, from the package itself or, where a package only names its licence, from `tools/licenses`. Run it after adding or updating a package; CI fails while the notices are out of date. Each program builds its own file in.

## The icon

`branding/app-icon.png` (1024 by 1024, no transparency) is the only copy of the icon. The iPhone app takes it as Apple's single-size icon, from which Xcode makes every size; `PhotoSense.Api` builds it in and hands it out at `/about/icon`, which the page shows as its tab icon, in its header and under About. To change the icon, replace that file.

## Accessibility

- The page is linted with every rule of `eslint-plugin-jsx-a11y`'s strict set (`npm run lint`, run in CI), besides Next's own.
- Each window and question stands on `Scrim`: Tab and Shift+Tab go round its own controls, Escape or a press outside it puts it away, and focus goes back to what had it before. A question raised over a window has Tab to itself.
- The menu is a menu: it opens on its first item, and the arrow keys, Home and End go through it. The theme is chosen among its items.
- The header, the page's content (`<main>`) and the menu are landmarks a screen reader can jump between. Messages are added to one region that is always there (`role="log"`), so each is read out; an error says it is one.
- The keyboard's place is always shown, in the brand's colour on both themes, and a text field drawn without an outline shows it on its frame. With less motion asked for, nothing slides, sweeps or fades.
- Every screen was checked with [axe](https://github.com/dequelabs/axe-core) in Chrome, in both themes, with nothing found: the results, a group, Organize, the scan screen, the menu, Help, Settings and About.

The page is in English only.

## The scan log

The setup screen shows the scan's log as it comes in, by asking the service for the lines logged since it last asked. That request answers only requests that carry the `x-photosense-client` header.

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

`connectLogStream` in `PhotoSense.ReactUI/lib/apiClient.ts` follows the log for the setup screen, asking the endpoint above every second and a half.
