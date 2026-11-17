using PhotoSense.Domain.Repositories;
using PhotoSense.Domain.ValueObjects;

namespace PhotoSense.Tests;

/// <summary>The phones backing up to a computer, what it holds for each and what happened, in memory.</summary>
internal sealed class MemoryBackupStore : IBackupStore
{
    private readonly List<BackupPhone> _phones = [];
    private readonly Dictionary<(string, string), HeldFile> _held = [];
    private readonly List<BackupEvent> _events = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupPhone>> PhonesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_phones) return Task.FromResult<IReadOnlyList<BackupPhone>>([.. _phones.OrderBy(p => p.Paired)]);
    }

    /// <inheritdoc />
    public Task SavePhoneAsync(BackupPhone phone, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_phones)
        {
            _phones.RemoveAll(p => p.Id == phone.Id);
            _phones.Add(phone);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<HeldFile>> HeldAsync(string phoneId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_held) return Task.FromResult<IReadOnlyList<HeldFile>>([.. _held.Values.Where(f => f.PhoneId == phoneId)]);
    }

    /// <inheritdoc />
    public Task HoldAsync(HeldFile file, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_held) _held[(file.PhoneId, file.Sha256)] = file;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ForgetAsync(string phoneId, IReadOnlyCollection<string> sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_held) foreach (var sha in sha256) _held.Remove((phoneId, sha));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task NoteAsync(BackupEvent happened, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_events) _events.Add(happened);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupEvent>> EventsAsync(string phoneId, int atMost, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_events) return Task.FromResult<IReadOnlyList<BackupEvent>>([.. _events.Where(e => e.PhoneId == phoneId).Reverse().OrderByDescending(e => e.At).Take(atMost)]);
    }
}

/// <summary>A phone's own record of its backups, in memory.</summary>
internal sealed class MemoryBackupLedger : IBackupLedger
{
    private BackupComputer? _computer;
    private BackupSettings _settings = BackupSettings.Default;
    private readonly HashSet<string> _onComputer = [];
    private readonly Dictionary<string, (long Size, DateTime Modified, string Sha256)> _checksums = [];

    /// <inheritdoc />
    public Task<BackupComputer?> ComputerAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_computer);
    }

    /// <inheritdoc />
    public Task SaveComputerAsync(BackupComputer computer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer)
        {
            if (_computer is not null && _computer.ComputerId != computer.ComputerId) _onComputer.Clear();
            _computer = computer;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BackupSettings> SettingsAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_settings);
    }

    /// <inheritdoc />
    public Task SaveSettingsAsync(BackupSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _settings = settings;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> OnComputerAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer) return Task.FromResult<IReadOnlySet<string>>(_onComputer.ToHashSet());
    }

    /// <inheritdoc />
    public Task KeepOnComputerAsync(IReadOnlyCollection<string> sha256, bool all, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer)
        {
            if (all) _onComputer.Clear();
            _onComputer.UnionWith(sha256);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> ChecksumAsync(string path, long size, DateTime modifiedUtc, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_checksums)
            return Task.FromResult(_checksums.TryGetValue(PhotoPath.Key(path), out var kept) && kept.Size == size && kept.Modified == modifiedUtc ? kept.Sha256 : null);
    }

    /// <inheritdoc />
    public Task KeepChecksumAsync(string path, long size, DateTime modifiedUtc, string sha256, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_checksums) _checksums[PhotoPath.Key(path)] = (size, modifiedUtc, sha256);
        return Task.CompletedTask;
    }
}
