# Service patterns

- **Registering:** endpoint classes get their services through the constructor. Application's services go in `AddPhotoSenseApplication`, Infrastructure's adapters in `AddPhotoSenseCore`, endpoint classes in `AddPhotoSenseApi`. A host's own go in `AddPhotoSenseComputer`, `AddPhotoSensePhone` (iPhone) or `AddPhotoSenseAndroid`, each of which calls `AddPhotoSenseApplication().AddPhotoSenseCore()`.
  - Endpoint classes, the LiteDB stores and helpers are `internal`; DI makes them through their public constructors. `PhotoSenseRoutes` has an internal constructor and is made by a factory in `AddPhotoSenseApi`. No project has `InternalsVisibleTo` (CLAUDE.md, "Public is each project's front door").
  - A time is never read from `DateTime.UtcNow`: a service that stamps one takes a `TimeProvider` (registered once in `AddPhotoSenseCore`). The audit and pairing stores stamp what they keep; `OrganizeMover` and `DuplicateRemovalService` stamp each change as it starts.
- **About and the program:** `ProductInfo` (`product.json`, built into `PhotoSense.Api`) is the same for every program. Each host registers its own `ProgramInfo`: its name (`server`, `iphone`, `android`), its version from its assembly, and its notices file. `IPhotoAccess` says what a phone has let PhotoSense see; on a computer, `FolderAccess` is always everything.
- **Request guard:** an endpoint that reveals or changes someone's files checks `RequestGuard.Allows(req.Headers)` first. That is the `x-photosense-client` header, which other web pages cannot send.
  - The server answers only under this machine's own names (`AllowedHosts`, `localhost;127.0.0.1;[::1]`, set in `PhotoSenseServer.Build`), so a page renamed to this machine by DNS rebinding cannot send the header as its own.
  - A refusal carries a plain-text reason, and a status that says whose fault it is: 400, 401, 404 or 409.
- **Endpoints and tokens:** an endpoint method is `XAsync(ApiRequest req, …, CancellationToken ct)`; the router hands it `req.Aborted`. A JSON body is read with `req.ReadJsonAsync<T>(options, ct)`, which gives null for what is not JSON of that shape, and the endpoint says so.
- **Answers:** `ApiResponse` keeps the status it is given, before or after writing. It writes JSON with property names as declared, not camelCased, which is what the page expects (`WriteAsJsonAsync(value, ct)`, `WriteStringAsync(text, ct)`).
- **Errors:** a file that cannot be reached is an `IOException` or `UnauthorizedAccessException`, caught together with a `when` filter. A picture that cannot be decoded is an `UnreadableImageException`: each `IImageAnalyzer` turns its decoder's own errors into one. What a file says about itself is read through `MetadataFiles.TryRead`, the one catch of every error (CLAUDE.md rule 10). A failure that is no loss is a `Try` method that returns whether it worked (`EmptyFolders.TakeAway`), never an empty catch.
- **LiteDB:** one shared `LiteDatabase` in one file, and no migrations. Each store's constructor calls `LiteDbMapping.Prepare<T>()` for every type it maps, before first use; LiteDB 5 otherwise fails now and then when two stores are first made together. A document class with a string key marks it `[BsonId]`.
- **Paths:** compare file paths with `PhotoPath.Key` and `PhotoPath.Comparer`, which ignore case on Windows and macOS and respect it on Linux.
- **Organize's file ids:** an HMAC of the path, with a key made for each run of the service, so a request can never name a path of its own choosing.
- **Test seams:** code that would ask the OS or the disk takes that answer as an argument, so that every branch runs on one OS: `PhotoPath.Key(path, ignoreCase)`, `UsbmuxPhoneFinder.Muxer(windows)`, `MdnsPhoneFinder.Networks(networks)`. A Desktop class takes the OS's answers in a public constructor beside its everyday one: `ShellSystemViewer(runningOn, start)`, `FileSystemFolderBrowser(folder, drives, windows, exists)`. Every class that touches photos takes an `IPhotoFileSystem`. Prefer a seam like that to a test that depends on Windows file locking.
- **Coverage exclusions:** only what no test may or can reach, with its `Justification`: `Bundle.Open` (a file the build left out), `MagickImageAnalyzer.Required` (Magick.NET handing back no pixels) and `OpenDatabase` (no test opens a database file). `Program.Main` only starts the server.
- **Scan state:** scans run in the background through `ScanRunner` (Application), one at a time, with progress held in memory (`InMemoryScanProgressStore`) and the log's lines in the registered `IScanLogSink`, which `/scan/logs` takes them from. A scan cut short by a stop is not resumed. `/scan/status` reports the scan running now.
- **Changes:** every move, copy and removal is an `OrganizeBatch`, written down before any file moves, carried out and undone all or nothing, and put back at start-up by `FileChanges.RecoverAsync`. A file arriving under a taken name gets a number (`FileChanges.FreePlace`); nothing is ever replaced. A token stops a change only before its first file moves; after that it is finished and written down with `CancellationToken.None`.

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
