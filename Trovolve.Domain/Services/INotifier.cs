namespace Trovolve.Domain.Services;

/// <summary>A notification of Trovolve's own, as the phone shows it.</summary>
/// <param name="Id">Which notification it is; one of the same id takes the place of the last.</param>
/// <param name="Title">Its first line.</param>
/// <param name="Body">What it says under its title.</param>
/// <param name="Opens">What Trovolve opens when it is tapped (<see cref="Notices.FreeUpSpace"/>), or null for where the person was.</param>
public sealed record Notice(string Id, string Title, string Body, string? Opens);

/// <summary>The notifications Trovolve sends, and what a tapped one opens.</summary>
public static class Notices
{
    /// <summary>The low-storage alert's id.</summary>
    public const string LowStorage = "low-storage";

    /// <summary>The reminder before a free trial ends.</summary>
    public const string TrialReminder = "trial-reminder";

    /// <summary>What the low-storage alert opens: Free up space.</summary>
    public const string FreeUpSpace = "free-up-space";

    /// <summary>What the trial's reminder opens: the subscription.</summary>
    public const string Subscription = "subscription";
}

/// <summary>A phone's own notifications: asking to send them, sending one now or later, and what one tapped asked to open.</summary>
public interface INotifier
{
    /// <summary>Whether the person lets Trovolve send notifications.</summary>
    Task<bool> AllowedAsync(CancellationToken ct);

    /// <summary>Asks the person, with the phone's own question, to let Trovolve send notifications; answers whether they did.</summary>
    Task<bool> AskAsync(CancellationToken ct);

    /// <summary>Shows a notification now.</summary>
    void Show(Notice notice);

    /// <summary>Shows a notification at <paramref name="at"/>, in place of any of the same id still to come.</summary>
    void Schedule(Notice notice, DateTimeOffset at);

    /// <summary>Takes back a notification still to come, and takes away one shown.</summary>
    void Cancel(string id);

    /// <summary>What the last notification tapped asked to open, once; null when none has been tapped since.</summary>
    string? TakeOpened();
}
