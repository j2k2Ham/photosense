namespace Trovolve.Domain.Services;

/// <summary>What the phone's home screen widgets show (35), written out as they say it, so that both phones say the same.</summary>
/// <param name="PercentFull">How full the phone's storage is, in percent.</param>
/// <param name="Freeable">How much Free up space can free: "19.8 GB".</param>
/// <param name="BackupLine">How backing up stands: "380 items waiting", "All on Studio iMac", or what to do first.</param>
/// <param name="BackupButton">The Storage widget's button: "Back up now", or "Pair" before a computer is paired.</param>
/// <param name="Ready">How many of today's set to swipe are left.</param>
/// <param name="ReadyLine">What that number is: "photos ready today", or that today's set is done.</param>
/// <param name="Week">The last seven days, today last: whether something was swiped on each.</param>
/// <param name="StreakLine">"6-day streak", or how to start one.</param>
public sealed record WidgetSnapshot(int PercentFull, string Freeable, string BackupLine, string BackupButton, int Ready, string ReadyLine, IReadOnlyList<bool> Week, string StreakLine);

/// <summary>
/// The phone's home screen widgets: the Storage widget and the Swipe to clean widget. They cannot ask Trovolve
/// anything themselves, so the app hands them what to show.
/// </summary>
public interface IWidgets
{
    /// <summary>Shows this on every Trovolve widget on the home screen, and keeps it for those added later.</summary>
    void Show(WidgetSnapshot snapshot);
}
