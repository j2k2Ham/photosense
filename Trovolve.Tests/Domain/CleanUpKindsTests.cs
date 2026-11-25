using Trovolve.Domain.Entities;
using Trovolve.Domain.Matching;
using Trovolve.Domain.Services;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests.Domain;

/// <summary>The kinds of picture Clean up's categories are made of: screenshots, messaging apps' pictures, bursts, and how sharp a picture is.</summary>
public class CleanUpKindsTests
{
    [Test]
    [Arguments("C:/Pictures/Screenshot 2026-11-17 at 9.41.07.png", true)]
    [Arguments("/storage/emulated/0/Pictures/Screenshots/IMG_1.jpg", true)]
    [Arguments("C:\\Users\\Jamie\\Desktop\\Screen Shot 2019-03-02.png", true)]
    [Arguments("/storage/emulated/0/DCIM/Screenshots/Screenshot_20261117-094107.jpg", true)]
    [Arguments("C:/Pictures/screenshot_1.PNG", true)]
    [Arguments("C:/Pictures/IMG_0001.JPG", false)]
    [Arguments("C:/Pictures/My screenshots of maps/IMG_0001.JPG", false)]
    [Arguments("/", false)]
    public async Task A_Screenshot_Is_Told_By_Its_Name_Or_Its_Folder(string path, bool screenshot, CancellationToken ct)
        => await Assert.That(MediaKinds.IsScreenshot(path)).IsEqualTo(screenshot);

    [Test]
    [Arguments("/storage/emulated/0/Android/media/com.whatsapp/WhatsApp/Media/WhatsApp Images/IMG-20261117-WA0001.jpg", true)]
    [Arguments("/storage/emulated/0/Pictures/Telegram/photo_1.jpg", true)]
    [Arguments("/storage/emulated/0/DCIM/Camera/WhatsApp-like name.jpg", false)]
    [Arguments("/storage/emulated/0/DCIM/Camera/IMG_1.jpg", false)]
    public async Task What_A_Messaging_App_Saved_Is_Told_By_Its_Folder_Not_Its_Name(string path, bool messaging, CancellationToken ct)
        => await Assert.That(MediaKinds.IsFromMessaging(path)).IsEqualTo(messaging);

    private static Photo Shot(string name, DateTime? at, double? sharpness = null, string? camera = "iPhone 15 Pro", string? burst = null, int width = 4032, bool screenshot = false)
    {
        var photo = TestPhotos.Make(name, taken: at, width: width);
        photo.Sharpness = sharpness;
        photo.CameraModel = camera;
        photo.BurstId = burst;
        photo.IsScreenshot = screenshot;
        return photo;
    }

    private static readonly DateTime At = new(2026, 3, 3, 14, 31, 7, 100);

    [Test]
    public async Task Three_Or_More_Shots_Of_One_Camera_Less_Than_A_Second_Apart_Are_A_Burst_And_The_Sharpest_Is_Kept(CancellationToken ct)
    {
        var burst = Bursts.Of([
            Shot("A1.JPG", At, 900), Shot("A2.JPG", At.AddMilliseconds(400), 2400), Shot("A3.JPG", At.AddMilliseconds(1300), 1200),
            // A second and a half later: another moment, alone.
            Shot("A4.JPG", At.AddMilliseconds(2800), 3000),
            // Another camera at the same moment is no part of it, and two shots are not a burst.
            Shot("B1.JPG", At, 100, camera: "Pixel 9"), Shot("B2.JPG", At.AddMilliseconds(200), 100, camera: "Pixel 9"),
            // Videos, screenshots and shots with no time are never part of one.
            TestPhotos.Video("A.MOV"), Shot("S1.PNG", At.AddMilliseconds(100), screenshot: true), Shot("N1.JPG", null),
        ]).Single();

        await Assert.That(burst.Shots.Select(s => s.FileName)).IsEquivalentTo(["A1.JPG", "A2.JPG", "A3.JPG"], CollectionOrdering.Matching);
        await Assert.That(burst.Keeper.FileName).IsEqualTo("A2.JPG");
        await Assert.That(burst.Extra.Select(s => s.FileName)).IsEquivalentTo(["A1.JPG", "A3.JPG"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task A_Burst_The_Phone_Marks_Is_One_However_Few_Or_Far_Apart_And_The_Latest_Comes_First(CancellationToken ct)
    {
        var bursts = Bursts.Of([
            Shot("M1.JPG", At, burst: "B-1"), Shot("M2.JPG", At.AddSeconds(5), burst: "B-1"),
            // Marked alone, it is no burst.
            Shot("L1.JPG", At.AddDays(1), burst: "B-2"),
            Shot("T1.JPG", At.AddDays(2), camera: null), Shot("T2.JPG", At.AddDays(2).AddMilliseconds(300), camera: null), Shot("T3.JPG", At.AddDays(2).AddMilliseconds(600), camera: null),
        ]);

        await Assert.That(bursts.Select(b => b.Shots[0].FileName)).IsEquivalentTo(["T1.JPG", "M1.JPG"], CollectionOrdering.Matching);
    }

    [Test]
    public async Task With_None_Sharper_The_Largest_Is_Kept_Then_The_First(CancellationToken ct)
    {
        await Assert.That(Bursts.KeeperOf([Shot("1.JPG", At, width: 3000), Shot("2.JPG", At.AddMilliseconds(1), width: 4000)]).FileName).IsEqualTo("2.JPG");
        await Assert.That(Bursts.KeeperOf([Shot("1.JPG", At), Shot("2.JPG", At.AddMilliseconds(1))]).FileName).IsEqualTo("1.JPG");
        // A shot measured is sharper than one not measured.
        await Assert.That(Bursts.KeeperOf([Shot("1.JPG", At), Shot("2.JPG", At.AddMilliseconds(1), 10)]).FileName).IsEqualTo("2.JPG");
    }

    // A picture of solid squares a few pixels wide, or the same with every square's edge softened into the next.
    private static byte[] Squares(int width, int height, int blur)
    {
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var value = ((x / 8 + y / 8) % 2) * 255.0;
                if (blur > 0)
                {
                    // Averaged over a square around it: what a lens out of focus does.
                    double sum = 0; var n = 0;
                    for (var dy = -blur; dy <= blur; dy++)
                        for (var dx = -blur; dx <= blur; dx++) { sum += (((x + dx + 64) / 8 + (y + dy + 64) / 8) % 2) * 255.0; n++; }
                    value = sum / n;
                }
                rgb[(y * width + x) * 3] = rgb[(y * width + x) * 3 + 1] = rgb[(y * width + x) * 3 + 2] = (byte)value;
            }
        return rgb;
    }

    [Test]
    public async Task A_Picture_Out_Of_Focus_Measures_Far_Softer_Than_The_Same_In_Focus_And_A_Plain_One_Not_At_All(CancellationToken ct)
    {
        var sharp = ImageFingerprint.Sharpness(Squares(64, 48, 0), 64, 48);
        var soft = ImageFingerprint.Sharpness(Squares(64, 48, 3), 64, 48);
        await Assert.That(sharp).IsGreaterThan(soft * 10);
        await Assert.That(soft).IsGreaterThan(0);
        await Assert.That(ImageFingerprint.Sharpness(new byte[64 * 48 * 3], 64, 48)).IsEqualTo(0);
        // Too small to have tiles with an inside.
        await Assert.That(ImageFingerprint.Sharpness(new byte[11 * 48 * 3], 11, 48)).IsEqualTo(0);
        await Assert.That(ImageFingerprint.Sharpness(new byte[64 * 11 * 3], 64, 11)).IsEqualTo(0);
    }

    [Test]
    public async Task Its_Sharpest_Part_Counts_So_A_Portrait_With_Its_Background_Softened_Is_Not_Blurry(CancellationToken ct)
    {
        // Soft all over, but for one sharp quarter.
        var picture = Squares(64, 64, 3);
        var sharp = Squares(64, 64, 0);
        for (var y = 0; y < 16; y++) Array.Copy(sharp, y * 64 * 3, picture, y * 64 * 3, 16 * 3);
        await Assert.That(ImageFingerprint.Sharpness(picture, 64, 64)).IsGreaterThan(ImageFingerprint.Sharpness(Squares(64, 64, 3), 64, 64) * 5);
    }
}
