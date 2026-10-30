# Testing the service (TUnit)

## Rules

- Await every assertion (`await Assert.That(x).IsEqualTo(y)`): one that is not awaited is never checked. CS4014 is an error in the test project.
- Compare collections with `IsEquivalentTo(expected, CollectionOrdering.Matching)`, or with `.Any`.
- `Assert.ThrowsAsync<T>` and `Assert.Throws<T>` return `T?`, and accept a derived exception.
- Tests run in parallel, even within one class. Shared static state needs `[NotInParallel("key")]`; anything that scans uses `"ScanLogQueue"`.
- Test names are sentences of what happens: `A_Copy_Is_Removed_Into_The_Album_Of_Removed_Pictures`.

## Traps already met

- `IsEquivalentTo` compares objects member by member through reflection. Some types throw when read that way (an `IPAddress`'s `ScopeId` does), and one holding a `Func` cannot be compared. Compare their strings or keys instead.
- coverlet counts more branches than the source shows. A `?.` is one, and a list pattern (`parts is [a, b, ..]`) checks for null and for length, so one that can never be null leaves a branch no test can reach. Write `parts.Length >= 2 && …`, or a `!` when the value cannot be null.
- A branch that depends on the OS cannot be covered on one machine. Put the OS's answer behind a seam (see `patterns.md`).
- A test needing a network port listens on port 0, and reads back the port it was given.
- A test that holds a file open to make it fail has only ever run on Windows; prefer `FailingDisk`.

## Helpers

| Helper | Where | What it gives |
|---|---|---|
| `OrganizeFixture` | `Tests/Application/OrganizeTesting.cs` | A temporary folder, an in-memory repository, the Organize services, `Mover()` and `Removed()` |
| `Http.Get/Post/Delete(...)` with `.FromClient()` | `Tests/Endpoints/ApiTesting.cs` | Requests for endpoint tests; `.Json()` and `.Text()` read an answer |
| `ServerFixture` | `Tests/Endpoints` | The service's registrations on a data folder of their own |
| `FailingDisk` | `Tests/Application/ChangesTesting.cs` | The real `DiskFileSystem`, except that "move", "copy", "make", "remove folder", "delete", "erase", "read" or "create" fail for the paths named, on any OS |
| `InMemoryFileSystem` | `Tests/InMemoryFileSystem.cs` | Photos in memory only; `PhotosNotOnDiskTests` runs Clean up and Organize on it end to end |
| `FakePhotoLibrary` | `Tests/FakePhotoLibrary.cs` | An iPhone's library in memory; `PhotosOnAPhoneTests` runs the iPhone stack through `InProcessApi`, with Magick.NET standing in for ImageIO |
| `FakeMediaStore` | `Tests/FakeMediaStore.cs` | An Android phone's media store in memory; `PhotosOnAndroidTests` runs the Android stack |
| `MediaSamples` | `Tests/MediaSamples.cs` | A JPEG with an Apple maker note, and a QuickTime movie with the phone's metadata |
| `TestFiles` | `Tests/TestFiles.cs` | `Disk`, `LinkFolder`, `DenyAccess`, `Remove` |
| `MemoryPairs`, `Loopback`, `ScriptedStream` | `Tests/TransferTesting.cs` | Pairings in memory; two ends of a connection over this computer's own network; a stream that answers what was written down |
| `FakeAdvertiser` | `Tests/FakeAdvertiser.cs` | What a phone would tell the network |
