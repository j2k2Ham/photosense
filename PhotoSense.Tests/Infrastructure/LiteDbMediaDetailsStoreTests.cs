using LiteDB;
using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.Services;
using PhotoSense.Infrastructure.Persistence;

namespace PhotoSense.Tests.Infrastructure;

public class LiteDbMediaDetailsStoreTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "photosense-kept");
    private static string In(params string[] parts) => Path.Combine([Root, .. parts]);

    [Test]
    public async Task Keeps_What_Was_Read_From_Each_File_And_Hands_It_Back_By_Path_Or_By_Folder()
    {
        using var db = new LiteDatabase(new MemoryStream());
        var store = new LiteDbMediaDetailsStore(db);
        await Assert.That(store.At(In("IMG_1.JPG"))).IsNull();
        await Assert.That(store.Under(Root)).IsEmpty();

        var written = new DateTime(2023, 6, 9, 18, 3, 22, 123, DateTimeKind.Utc).AddTicks(4567);
        var taken = new DateTime(2023, 6, 9, 14, 3, 22, DateTimeKind.Unspecified).AddTicks(89);
        var picture = new StoredMediaDetails(In("IMG_1.JPG"), 6_093_000, written, new MediaDetails(taken, 4032, 3024, null, 46.1283, -112.9423, "LIVE-1"));
        var video = new StoredMediaDetails(In("2023", "clip.MOV"), 102_760_448, written.AddDays(1), new MediaDetails(null, 1920, 1080, 84.5, null, null));
        var elsewhere = new StoredMediaDetails(Path.Combine(Root + " old", "IMG_9.JPG"), 1, written, new MediaDetails(null, 0, 0, null, null, null));
        store.Save([picture, video, elsewhere]);
        store.Save([]);

        // Exactly as kept, to the tick, and with the time it was taken still carrying no zone.
        var back = store.At(In("IMG_1.JPG"))!;
        await Assert.That(back).IsEqualTo(picture);
        await Assert.That((back.ModifiedUtc.Kind, back.Details.TakenOn!.Value.Kind)).IsEqualTo((DateTimeKind.Utc, DateTimeKind.Unspecified));
        await Assert.That(store.At(In("2023", "clip.MOV"))).IsEqualTo(video);

        // A folder gives everything inside it, however deep, and nothing from a folder whose name merely begins the same.
        await Assert.That(store.Under(Root).Select(k => k.Path).Order()).IsEquivalentTo(new[] { picture.Path, video.Path }.Order(), CollectionOrdering.Matching);
        await Assert.That(store.Under(Root + Path.DirectorySeparatorChar).Count).IsEqualTo(2);
        await Assert.That(store.Under(In("2023")).Single()).IsEqualTo(video);
        await Assert.That(store.Under(Root + " old").Single()).IsEqualTo(elsewhere);

        // Kept again for the same path, it takes the place of what was there; another store on the database sees it.
        var changed = picture with { SizeBytes = 7, Details = picture.Details with { Width = 1 } };
        store.Save([changed]);
        await Assert.That(new LiteDbMediaDetailsStore(db).At(In("IMG_1.JPG"))).IsEqualTo(changed);
        await Assert.That(store.Under(Root).Count).IsEqualTo(2);
    }
}
