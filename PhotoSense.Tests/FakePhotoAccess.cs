using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>A phone's photo access as a test sets it, with a picker that lets it see as much as the test says.</summary>
public sealed class FakePhotoAccess : IPhotoAccess
{
    public PhotoAccess Current { get; set; } = PhotoAccess.Everything;

    /// <summary>What the person lets PhotoSense see once the picker is put away.</summary>
    public PhotoAccess AfterPicker { get; set; } = PhotoAccess.Everything;

    public int PickersShown { get; private set; }

    public int SettingsOpened { get; private set; }

    public Task ChooseMoreAsync(CancellationToken ct)
    {
        PickersShown++;
        Current = AfterPicker;
        return Task.CompletedTask;
    }

    public void OpenSettings() => SettingsOpened++;
}
