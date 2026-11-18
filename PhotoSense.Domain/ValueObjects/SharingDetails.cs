using System.Globalization;
using System.Text.RegularExpressions;

namespace PhotoSense.Domain.ValueObjects;

/// <summary>
/// Which of a picture's or video's details say where it was taken, and which say what took it, for a copy shared
/// without them: by their EXIF names on Android, and by their XMP paths and QuickTime keys on an iPhone.
/// </summary>
public static partial class SharingDetails
{
    /// <summary>The EXIF details that say where a picture was taken, as Android's <c>ExifInterface</c> names them.</summary>
    public static IReadOnlyList<string> ExifPlace { get; } =
    [
        "GPSVersionID", "GPSLatitudeRef", "GPSLatitude", "GPSLongitudeRef", "GPSLongitude", "GPSAltitudeRef", "GPSAltitude",
        "GPSTimeStamp", "GPSSatellites", "GPSStatus", "GPSMeasureMode", "GPSDOP", "GPSSpeedRef", "GPSSpeed", "GPSTrackRef",
        "GPSTrack", "GPSImgDirectionRef", "GPSImgDirection", "GPSMapDatum", "GPSDestLatitudeRef", "GPSDestLatitude",
        "GPSDestLongitudeRef", "GPSDestLongitude", "GPSDestBearingRef", "GPSDestBearing", "GPSDestDistanceRef",
        "GPSDestDistance", "GPSProcessingMethod", "GPSAreaInformation", "GPSDateStamp", "GPSDifferential", "GPSHPositioningError",
        // XMP can repeat where it was taken, in words or numbers, so it goes too.
        "Xmp",
    ];

    /// <summary>The EXIF details that say what took a picture and how: the camera, its lens, its software and settings.</summary>
    public static IReadOnlyList<string> ExifCamera { get; } =
    [
        "Make", "Model", "Software", "LensMake", "LensModel", "LensSerialNumber", "LensSpecification", "BodySerialNumber",
        "CameraOwnerName", "MakerNote", "FNumber", "ApertureValue", "ExposureTime", "ShutterSpeedValue", "ExposureProgram",
        "ExposureBiasValue", "PhotographicSensitivity", "FocalLength", "FocalLengthIn35mmFilm", "Flash", "MeteringMode", "WhiteBalance",
    ];

    /// <summary>The EXIF details a picture made again as a JPEG keeps from its original: when it was taken.</summary>
    public static IReadOnlyList<string> ExifWhen { get; } =
    [
        "DateTime", "DateTimeOriginal", "DateTimeDigitized", "OffsetTime", "OffsetTimeOriginal", "OffsetTimeDigitized",
        "SubSecTime", "SubSecTimeOriginal", "SubSecTimeDigitized",
    ];

    private static readonly HashSet<string> Camera = new(ExifCamera.Concat(["HostComputer"]), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IptcPlace = new(StringComparer.Ordinal)
    {
        "photoshop:City", "photoshop:State", "photoshop:Country", "Iptc4xmpCore:Location", "Iptc4xmpCore:CountryCode",
    };

    /// <summary>
    /// Whether a detail, by its EXIF name, XMP path ("<c>exif:GPSLatitude</c>") or QuickTime key
    /// ("<c>mdta/com.apple.quicktime.location.ISO6709</c>"), says where it was taken.
    /// </summary>
    public static bool IsPlace(string detail)
        => Last(detail).StartsWith("GPS", StringComparison.OrdinalIgnoreCase) || detail.Contains("location", StringComparison.OrdinalIgnoreCase)
           || detail.Contains("©xyz", StringComparison.Ordinal) || IptcPlace.Contains(detail);

    /// <summary>Whether a detail, named as for <see cref="IsPlace"/>, says what took it: the camera, its lens, its software and settings.</summary>
    public static bool IsCamera(string detail)
    {
        var last = Last(detail);
        return Camera.Contains(last) || last.Contains("Lens", StringComparison.OrdinalIgnoreCase) || last.Contains("SerialNumber", StringComparison.OrdinalIgnoreCase)
               || detail.Contains("camera", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The latitude and longitude of a place written as ISO 6709 does, as a video keeps it ("<c>+46.0038-112.5348/</c>");
    /// null when there is none or it says something else.
    /// </summary>
    public static (double Latitude, double Longitude)? Iso6709(string? place)
    {
        if (place is null) return null;
        var found = Iso6709Pattern().Match(place);
        if (!found.Success) return null;
        return (double.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(found.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    // Its own name, after the namespace or key that comes first.
    private static string Last(string detail) => detail[(detail.LastIndexOfAny([':', '/', '.']) + 1)..];

    [GeneratedRegex(@"^([+-]\d+(?:\.\d+)?)([+-]\d+(?:\.\d+)?)")]
    private static partial Regex Iso6709Pattern();
}
