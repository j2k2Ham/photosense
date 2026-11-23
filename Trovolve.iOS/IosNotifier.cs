using Trovolve.Domain.Services;
using UserNotifications;

namespace Trovolve.iOS;

/// <summary>
/// Trovolve's notifications on an iPhone or iPad, after the person allows them; shown while the app is open too, and
/// one tapped tells the page what to open.
/// </summary>
public sealed class IosNotifier : UNUserNotificationCenterDelegate, INotifier
{
    private const string OpensKey = "opens";
    private static string? _opened;

    /// <summary>The one notifier, which iOS tells of notifications tapped from the moment the app starts.</summary>
    public static IosNotifier Shared { get; } = new();

    private static UNUserNotificationCenter Center => UNUserNotificationCenter.Current;

    /// <inheritdoc />
    public async Task<bool> AllowedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var settings = await Center.GetNotificationSettingsAsync();
        return settings.AuthorizationStatus is UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral;
    }

    /// <inheritdoc />
    public async Task<bool> AskAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        // iOS asks only once; after that the answer stands until the person changes it in Settings.
        var answer = await Center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert | UNAuthorizationOptions.Sound);
        return answer.Item1;
    }

    /// <inheritdoc />
    public void Show(Notice notice) => Center.AddNotificationRequest(UNNotificationRequest.FromIdentifier(notice.Id, Content(notice), null), null);

    /// <inheritdoc />
    public void Schedule(Notice notice, DateTimeOffset at)
    {
        // iOS waits at least a second.
        var seconds = Math.Max(1, (at - DateTimeOffset.UtcNow).TotalSeconds);
        Center.AddNotificationRequest(UNNotificationRequest.FromIdentifier(notice.Id, Content(notice), UNTimeIntervalNotificationTrigger.CreateTrigger(seconds, false)), null);
    }

    /// <inheritdoc />
    public void Cancel(string id)
    {
        Center.RemovePendingNotificationRequests([id]);
        Center.RemoveDeliveredNotifications([id]);
    }

    /// <inheritdoc />
    public string? TakeOpened() => Interlocked.Exchange(ref _opened, null);

    /// <summary>
    /// A widget or the app icon's menu tapped: notes what it opens, as a notification would, and tells the page. Only
    /// what Trovolve opens is taken.
    /// </summary>
    public static void Open(string? opens)
    {
        if (opens is not ("backup" or "swipe" or "search" or "free-up-space")) return;
        _opened = opens;
        PageViewController.Shown?.TellNotice();
    }

    /// <summary>Shown as a banner while Trovolve is open too.</summary>
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification, Action<UNNotificationPresentationOptions> completionHandler)
        => completionHandler(UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List);

    /// <summary>Tapped: notes what it opens, and tells the page.</summary>
    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response, Action completionHandler)
    {
        if (response.Notification.Request.Content.UserInfo[OpensKey] is NSString opens) _opened = opens;
        PageViewController.Shown?.TellNotice();
        completionHandler();
    }

    private static UNMutableNotificationContent Content(Notice notice) => new()
    {
        Title = notice.Title, Body = notice.Body, Sound = UNNotificationSound.Default,
        UserInfo = notice.Opens is { } opens ? NSDictionary.FromObjectAndKey(new NSString(opens), new NSString(OpensKey)) : new NSDictionary(),
    };
}
