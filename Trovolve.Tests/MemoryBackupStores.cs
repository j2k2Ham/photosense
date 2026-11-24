using Trovolve.Domain.Repositories;
using Trovolve.Domain.ValueObjects;

namespace Trovolve.Tests;

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
    private readonly HashSet<string> _foldersMade = new(StringComparer.Ordinal);

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
            if (_computer is not null && _computer.ComputerId != computer.ComputerId)
            {
                _onComputer.Clear();
                _foldersMade.Clear();
            }
            _computer = computer;
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ForgetComputerAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer)
        {
            _onComputer.Clear();
            _foldersMade.Clear();
            _computer = null;
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

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> FoldersMadeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer) return Task.FromResult<IReadOnlySet<string>>(_foldersMade.ToHashSet(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task KeepFolderMadeAsync(string folder, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_onComputer) _foldersMade.Add(folder);
        return Task.CompletedTask;
    }
}

/// <summary>Swipe to clean's day, what was swiped, what waits to be deleted and the days swiped, in memory.</summary>
internal sealed class MemorySwipeStore : ISwipeStore
{
    private readonly object _lock = new();
    private readonly HashSet<string> _swiped = new(StringComparer.Ordinal);
    private readonly HashSet<DateOnly> _days = [];
    private SwipeDay? _day;
    private IReadOnlyList<string> _marked = [];

    /// <inheritdoc />
    public Task<SwipeDay?> DayAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_day);
    }

    /// <inheritdoc />
    public Task SaveDayAsync(SwipeDay day, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) _day = day;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<string>> SwipedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult<IReadOnlySet<string>>(_swiped.ToHashSet(StringComparer.Ordinal));
    }

    /// <inheritdoc />
    public Task SetSwipedAsync(IEnumerable<string> keys, bool swiped, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            foreach (var key in keys)
                if (swiped) _swiped.Add(key);
                else _swiped.Remove(key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> MarkedAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult(_marked);
    }

    /// <inheritdoc />
    public Task SaveMarkedAsync(IReadOnlyList<string> keys, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) _marked = [.. keys];
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<DateOnly>> DaysAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock) return Task.FromResult<IReadOnlySet<DateOnly>>(_days.ToHashSet());
    }

    /// <inheritdoc />
    public Task SetDayAsync(DateOnly day, bool swiped, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_lock)
            if (swiped) _days.Add(day);
            else _days.Remove(day);
        return Task.CompletedTask;
    }
}
