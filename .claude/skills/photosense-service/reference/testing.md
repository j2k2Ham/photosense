# Testing the service (TUnit)

## Rules

- Await every assertion (`await Assert.That(x).IsEqualTo(y)`): one that is not awaited is never checked. CS4014 is an error in the test project.
- Compare collections with `IsEquivalentTo(expected, CollectionOrdering.Matching)`, or with `.Any`.
- `Assert.ThrowsAsync<T>` and `Assert.Throws<T>` return `T?`, and accept a derived exception.
- Tests run in parallel, even within one class. Shared static state needs `[NotInParallel("key")]`; there is none in the service now.
- No test touches the database but the LiteDB stores' own, which use `new LiteDatabase(new MemoryStream())` through `Stores`. Everything else registers `AddStandInStores()` (after a phone's setup, or as `PhotoSenseServer.Build`'s `replace`) or uses a stand-in directly (`MemoryBatchStore`, `MemoryMediaDetailsStore`, `MemoryScanHistory`, `MemoryAudit`, `MemoryPairs`, `InMemoryPhotoRepository`). A stand-in copies what it keeps, as a database would.
- Tests use only what each project makes public. An endpoint is asked through the router (`TestApi` and its address wrappers), a store is made by `AddPhotoSenseCore` (`Stores`), the server by `PhotoSenseServer.Build` (`ServerFixture`).
- Test names are sentences of what happens: `A_Copy_Is_Removed_Into_The_Album_Of_Removed_Pictures`. They are the test's documentation; a test class, fixture, fake or helper has a `<summary>` (CLAUDE.md).
- An async test takes `CancellationToken ct` last, which TUnit hands it, and passes it to every call that takes one. A helper that awaits takes one too. A test of what happens when a caller has gone away passes `new CancellationToken(canceled: true)`.
- A test reads no clock: a time to stamp comes from `TestClock` (fixed, or moving on by a step each time it is read), and a file's time is set from its own (`File.GetLastWriteTimeUtc(path).AddMinutes(5)`).
- A synchronous hook that has to wait (a reader held while a test looks) waits with the test's token: `hold.Wait(ct)`. A hook that needs the store mid-move reads it straight from the database (`ChangesFixture.Stored`), never through `.Result`.
- One `HttpClient` serves every test of a class (`PhotoSenseServerTests.Client`), asking each program by its whole address.
- A fake decoder says a picture cannot be decoded with `UnreadableImageException`, as the real ones do.

## Traps already met

- `IsEquivalentTo` compares objects member by member through reflection. Some types throw when read that way (an `IPAddress`'s `ScopeId` does), and one holding a `Func` cannot be compared. Compare their strings or keys instead.
- coverlet counts more branches than the source shows. A `?.` is one, and a list pattern (`parts is [a, b, ..]`) checks for null and for length, so one that can never be null leaves a branch no test can reach. Write `parts.Length >= 2 && …`, or a `!` when the value cannot be null.
- A branch that depends on the OS cannot be covered on one machine. Put the OS's answer behind a seam (see `patterns.md`).
- A test needing a network port listens on port 0, and reads back the port it was given.
- A test that plays one end of a connection keeps that end open until the other side is done (`PairingTests.Holding`). A stream nothing refers to any more is closed by the garbage collector in a full run, and the other side's read fails with "forcibly closed".
- A test that holds a file open to make it fail has only ever run on Windows; prefer `FailingDisk`.

## Helpers

| Helper | Where | What it gives |
|---|---|---|
| `OrganizeFixture` | `Tests/Application/OrganizeTesting.cs` | A temporary folder, an in-memory repository, the Organize services, `Mover()` and `Removed()` |
| `Http.Get/Post/Delete(...)` with `.FromClient()` | `Tests/Endpoints/ApiTesting.cs` | Requests for endpoint tests; `.Json()` and `.Text()` read an answer |
| `ServerFixture` | `Tests/Server/ServerTesting.cs` | The program as `PhotoSenseServer.Build` makes it, with its stores in memory; `StartedAsync` starts it on a port of its own |
| `TestApi` with `AboutApi`, `PhotosApi`, `ScanApi`, `OrganizeApi`, `TransferApi` | `Tests/Endpoints/TestApi.cs` | Every address through the router, on stand-ins (stores and previews in memory, a still clock), with the test's own services registered in their place; the wrappers take the endpoints' method names and build the real address |
| `Stores` | `Tests/Stores.cs` | The LiteDB stores as `AddPhotoSenseCore` makes them, on a database in memory the test holds (`db.Photos()`, `db.Batches()`, …): for the stores' own tests only |
| `AddStandInStores`, `MemoryBatchStore` and the rest | `Tests/StandInStores.cs` | Every store in memory, for every other test; `MemoryBatchStore.Stored(id)` is what is kept at that moment, for a hook mid-move |
| `InMemoryPhotoRepository`, `InMemoryThumbnailStore` | `Tests/` | Scans' records and previews in memory |
| `FailingDisk` | `Tests/Application/ChangesTesting.cs` | The real `DiskFileSystem`, except that "move", "copy", "make", "remove folder", "delete", "erase", "read" or "create" fail for the paths named, on any OS |
| `InMemoryFileSystem` | `Tests/InMemoryFileSystem.cs` | Photos in memory only; `PhotosNotOnDiskTests` runs Clean up and Organize on it end to end |
| `FakePhotoLibrary` | `Tests/FakePhotoLibrary.cs` | An iPhone's library in memory, `Fetching` standing in for iCloud; `PhotosOnAPhoneTests` runs the iPhone stack through `InProcessApi`, with Magick.NET standing in for ImageIO |
| `FakeMediaStore` | `Tests/FakeMediaStore.cs` | An Android phone's media store in memory; `PhotosOnAndroidTests` runs the Android stack |
| `MediaSamples` | `Tests/MediaSamples.cs` | A JPEG with an Apple maker note, and a QuickTime movie with the phone's metadata |
| `TestFiles` | `Tests/TestFiles.cs` | `Disk`, `LinkFolder`, `DenyAccess`, `Remove` |
| `MemoryPairs`, `Loopback`, `ScriptedStream`, `HurriedClock` | `Tests/TransferTesting.cs` | Pairings in memory; two ends of a connection over this computer's own network; a stream that answers what was written down; a clock whose timers go off sooner (`HurriedClock.Minute`: a minute in two seconds) |
| `FakeAdvertiser` | `Tests/FakeAdvertiser.cs` | What a phone would tell the network |
