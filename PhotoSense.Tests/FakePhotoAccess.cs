using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>A phone's photo access as a test sets it, with a picker that lets it see as much as the test says.</summary>
public sealed class FakePhotoAccess : IPhotoAccess
{
    /// <inheritdoc />
    public PhotoAccess Current { get; set; } = PhotoAccess.Everything;

    /// <summary>What the person lets PhotoSense see once the picker is put away.</summary>
    public PhotoAccess AfterPicker { get; set; } = PhotoAccess.Everything;

    /// <summary>How often the system's picker was shown.</summary>
    public int PickersShown { get; private set; }

    /// <summary>How often the system's settings were opened.</summary>
    public int SettingsOpened { get; private set; }

    /// <summary>What the person answers to the system's question.</summary>
    public PhotoAccess Answer { get; set; } = PhotoAccess.Everything;

    /// <summary>How often the system's question was asked.</summary>
    public int Asked { get; private set; }

    /// <inheritdoc />
    public Task AskAsync(CancellationToken ct)
    {
        Asked++;
        Current = Answer;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ChooseMoreAsync(CancellationToken ct)
    {
        PickersShown++;
        Current = AfterPicker;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void OpenSettings() => SettingsOpened++;
}
