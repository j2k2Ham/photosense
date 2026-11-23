using System.Runtime.InteropServices;
using System.Text.Json;
using PhotoSense.Domain.Services;

namespace PhotoSense.iOS;

/// <summary>
/// The home screen widgets on an iPhone or iPad (35), drawn by PhotoSense's widget extension (Native/Widgets). What
/// they show is written into the App Group the app and the extension share, and WidgetKit is asked to draw them again.
/// </summary>
internal sealed class IosWidgets : IWidgets
{
    /// <summary>The App Group the app and its widgets share.</summary>
    public const string Group = "group.com.j2k2ham.photosense";

    /// <inheritdoc />
    /// <remarks>Without the App Group, as in a build signed without it, there is nowhere to write, and nothing is shown.</remarks>
    public void Show(WidgetSnapshot snapshot)
    {
        if (NSFileManager.DefaultManager.GetContainerUrl(Group)?.Path is not { } shared) return;
        File.WriteAllText(Path.Combine(shared, "widgets.json"), JsonSerializer.Serialize(snapshot));
        photosense_widgets_reload();
    }

    [DllImport("__Internal")] private static extern void photosense_widgets_reload();
}
