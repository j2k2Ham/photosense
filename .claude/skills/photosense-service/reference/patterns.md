# Service patterns

- **Registering:** endpoint classes get their services through the constructor. Register services both hosts run in `AddPhotoSenseCore`. A host's own go in `AddPhotoSenseComputer`, `AddPhotoSensePhone` (iPhone) or `AddPhotoSenseAndroid`, and endpoint classes in `AddPhotoSenseApi`.
- **About and the program:** `ProductInfo` (`product.json`, built into `PhotoSense.Api`) is the same for every program. Each host registers its own `ProgramInfo`: its name (`server`, `iphone`, `android`), its version from its assembly, and its notices file. `IPhotoAccess` says what a phone has let PhotoSense see; on a computer, `FolderAccess` is always everything.
- **Request guard:** an endpoint that reveals or changes someone's files checks `RequestGuard.Allows(req.Headers)` first. That is the `x-photosense-client` header, which other web pages cannot send.
  - The server answers only under this machine's own names (`AllowedHosts`, `localhost;127.0.0.1;[::1]`, set in `PhotoSenseServer.Build`), so a page renamed to this machine by DNS rebinding cannot send the header as its own.
  - A refusal carries a plain-text reason, and a status that says whose fault it is: 400, 401, 404 or 409.
- **Answers:** `ApiResponse` keeps the status it is given, before or after writing. It writes JSON with property names as declared, not camelCased, which is what the page expects.
- **LiteDB:** one shared `LiteDatabase` in one file, and no migrations. Each store's constructor calls `LiteDbMapping.Prepare<T>()` for every type it maps, before first use; LiteDB 5 otherwise fails now and then when two stores are first made together. A document class with a string key marks it `[BsonId]`.
- **Paths:** compare file paths with `PhotoPath.Key` and `PhotoPath.Comparer`, which ignore case on Windows and macOS and respect it on Linux.
- **Organize's file ids:** an HMAC of the path, with a key made for each run of the service, so a request can never name a path of its own choosing.
- **Test seams:** code that would ask the OS or the disk takes that answer as an argument, so that every branch runs on one OS: `PhotoPath.Key(path, ignoreCase)`, `UsbmuxPhoneFinder.Muxer(windows)`, `MdnsPhoneFinder.Networks(networks)`. Every class that touches photos takes an `IPhotoFileSystem`. Prefer a seam like that to a test that depends on Windows file locking.
- **Scan state:** scans run in the background through `ScanRunner`, one at a time, with progress held in memory (`InMemoryScanProgressStore`). A scan cut short by a stop is not resumed. `/scan/status` reports the scan running now.
- **Changes:** every move, copy and removal is an `OrganizeBatch`, written down before any file moves, carried out and undone all or nothing, and put back at start-up by `FileChanges.RecoverAsync`. A file arriving under a taken name gets a number (`FileChanges.FreePlace`); nothing is ever replaced.

## Phones as file systems

| | iPhone: `PhotoLibraryFileSystem` over `IPhotoLibrary` | Android: `MediaFileSystem` over `IMediaStore` |
|---|---|---|
| Root | `/Photo Library` (`LibraryRoot`) | `/Phone` (`StorageRoot`) |
| A folder | An album PhotoSense made; `A\B` is folder A holding album B | A real folder, only inside DCIM, Pictures or Movies; a top-level one is refused |
| Move | Changes which of PhotoSense's albums a picture is in; the person's own albums are left alone | Changes the folder Android keeps it in, and may rename it |
| Remove | Into the album **PhotoSense Removed**, shown as `_PhotoSense_Removed` | Into `_PhotoSense_Removed` inside the folder; for the whole phone, into `Pictures/PhotoSense Removed` |
| Erase (Delete permanently) | Recently Deleted | Android's bin |
| Copy | Refused (`NoCopies`) | Refused (`NoCopies`) |
| An emptied folder | The album stays: iOS asks before an app deletes one | Forgotten when PhotoSense only made it for later |

Both implement `IPhotoChangesTogether`, so `FileChanges` and `RemovedFiles` hand each change over whole: one question from the phone, all or nothing. Copying only ever happens from a phone to a computer (Jamie, 2026-10-30).
