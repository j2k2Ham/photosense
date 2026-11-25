using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests.Domain;

/// <summary>Which of a picture's or video's details a shared copy leaves out for its place and for its camera, and a video's place read.</summary>
public class SharingDetailsTests
{
    [Test]
    public async Task Each_Exif_Detail_Of_A_Place_Or_A_Camera_Is_Known_For_What_It_Says(CancellationToken ct)
    {
        foreach (var detail in SharingDetails.ExifPlace.Where(d => d != "Xmp"))
            await Assert.That(SharingDetails.IsPlace(detail) && !SharingDetails.IsCamera(detail)).IsTrue().Because(detail);
        foreach (var detail in SharingDetails.ExifCamera)
            await Assert.That(SharingDetails.IsCamera(detail) && !SharingDetails.IsPlace(detail)).IsTrue().Because(detail);
        // When it was taken stays, whatever is left out.
        foreach (var detail in SharingDetails.ExifWhen)
            await Assert.That(SharingDetails.IsPlace(detail) || SharingDetails.IsCamera(detail)).IsFalse().Because(detail);
    }

    [Test]
    public async Task A_Pictures_Xmp_Paths_And_A_Videos_Keys_Are_Known_By_Their_Own_Names(CancellationToken ct)
    {
        string[] places = ["exif:GPSLatitude", "photoshop:City", "Iptc4xmpCore:CountryCode", "Iptc4xmpExt:LocationShown",
            "mdta/com.apple.quicktime.location.ISO6709", "common/location", "udta/©xyz"];
        foreach (var place in places) await Assert.That(SharingDetails.IsPlace(place)).IsTrue().Because(place);
        string[] cameras = ["tiff:Make", "tiff:Model", "tiff:Software", "xmp:HostComputer", "exifEX:LensModel", "aux:SerialNumber", "exif:FNumber",
            "mdta/com.apple.quicktime.make", "mdta/com.apple.quicktime.software", "mdta/com.apple.quicktime.camera.focal_length"];
        foreach (var camera in cameras) await Assert.That(SharingDetails.IsCamera(camera)).IsTrue().Because(camera);
        string[] neither = ["tiff:Orientation", "exif:DateTimeOriginal", "xmp:CreateDate", "mdta/com.apple.quicktime.creationdate", "photoshop:Headline"];
        foreach (var other in neither) await Assert.That(SharingDetails.IsPlace(other) || SharingDetails.IsCamera(other)).IsFalse().Because(other);
    }

    [Test]
    public async Task A_Videos_Place_Is_Read_As_Iso_6709_Writes_It(CancellationToken ct)
    {
        await Assert.That(SharingDetails.Iso6709("+46.0038-112.5348/")).IsEqualTo((46.0038, -112.5348));
        await Assert.That(SharingDetails.Iso6709("-33.8688+151.2093+012.000/")).IsEqualTo((-33.8688, 151.2093));
        await Assert.That(SharingDetails.Iso6709("+46-112/")).IsEqualTo((46d, -112d));
        await Assert.That(SharingDetails.Iso6709(null)).IsNull();
        await Assert.That(SharingDetails.Iso6709("somewhere")).IsNull();
    }
}
