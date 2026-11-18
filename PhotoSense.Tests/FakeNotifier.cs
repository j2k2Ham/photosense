using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>A phone's notifications as the test sets them: whether they are allowed, what the person answers when asked, and what was sent.</summary>
internal sealed class FakeNotifier : INotifier
{
    /// <summary>The phone lets PhotoSense send notifications.</summary>
    public bool Allowed { get; set; }

    /// <summary>What the person answers to the phone's own question.</summary>
    public bool Answer { get; set; } = true;

    /// <summary>How many times the phone's question was asked.</summary>
    public int Asked { get; private set; }

    /// <summary>The notifications shown, in order.</summary>
    public List<Notice> Shown { get; } = [];

    /// <summary>The notifications still to come, by id, with when.</summary>
    public Dictionary<string, (Notice Notice, DateTimeOffset At)> Scheduled { get; } = [];

    /// <summary>What a notification tapped asked to open, until it is taken.</summary>
    public string? Opened { get; set; }

    /// <inheritdoc />
    public Task<bool> AllowedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Allowed);
    }

    /// <inheritdoc />
    public Task<bool> AskAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Asked++;
        Allowed = Answer;
        return Task.FromResult(Allowed);
    }

    /// <inheritdoc />
    public void Show(Notice notice) => Shown.Add(notice);

    /// <inheritdoc />
    public void Schedule(Notice notice, DateTimeOffset at) => Scheduled[notice.Id] = (notice, at);

    /// <inheritdoc />
    public void Cancel(string id) => Scheduled.Remove(id);

    /// <inheritdoc />
    public string? TakeOpened()
    {
        var opened = Opened;
        Opened = null;
        return opened;
    }
}

/// <summary>A phone's share sheet that keeps what it was handed, or fails as the test says.</summary>
internal sealed class FakeShareSheet : IShareSheet
{
    /// <summary>The pictures shared, with their names, in order.</summary>
    public List<(byte[] Png, string Name)> Shared { get; } = [];

    /// <summary>When set, the next share fails, and this is why.</summary>
    public string? Fail { get; set; }

    /// <inheritdoc />
    public Task ShareImageAsync(byte[] png, string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (Fail is { } why)
        {
            Fail = null;
            throw new IOException(why);
        }
        Shared.Add((png, name));
        return Task.CompletedTask;
    }
}
