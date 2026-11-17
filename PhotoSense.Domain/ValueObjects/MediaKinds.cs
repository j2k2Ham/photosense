namespace PhotoSense.Domain.ValueObjects;

/// <summary>Kinds of picture told from where a file is and what it is called: screenshots, and what messaging apps saved.</summary>
public static class MediaKinds
{
    /// <summary>What a video is larger than to be a large one: 200 MB.</summary>
    public const long LargeVideoBytes = 200L * 1024 * 1024;

    /// <summary>What a picture's sharpest part (<see cref="Services.ImageFingerprint.Sharpness"/>) is below to be blurry.</summary>
    public const double BlurryBelow = 400;

    private static readonly string[] MessagingApps = ["WhatsApp", "Telegram"];

    /// <summary>
    /// Whether a file is a picture of the screen by its name or folder, as phones and computers save them: "Screenshot",
    /// "Screen Shot" or "Screenshot_…" at the start of its name, or in a folder named Screenshots.
    /// </summary>
    public static bool IsScreenshot(string path)
    {
        var name = Path.GetFileName(path);
        if (name.StartsWith("Screenshot", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Screen Shot", StringComparison.OrdinalIgnoreCase)) return true;
        return Segments(path).Any(s => s.Equals("Screenshots", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether a file was saved by WhatsApp or Telegram, by a folder of theirs it is in, as Android keeps them.</summary>
    public static bool IsFromMessaging(string path)
        => Segments(path).SkipLast(1).Any(s => MessagingApps.Any(app => s.Contains(app, StringComparison.OrdinalIgnoreCase)));

    private static string[] Segments(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
}
