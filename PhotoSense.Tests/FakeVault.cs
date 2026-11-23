using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>A phone's Keychain or Keystore holding the hidden vault's recovery key, in memory.</summary>
internal sealed class FakeVaultKeys : IVaultKeyStore
{
    /// <summary>The recovery key kept; null before the vault is turned on.</summary>
    public string? Kept { get; set; }

    /// <inheritdoc />
    public Task<string?> RecoveryKeyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Kept);
    }

    /// <inheritdoc />
    public Task SaveRecoveryKeyAsync(string recoveryKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Kept = recoveryKey;
        return Task.CompletedTask;
    }
}

/// <summary>The phone's Face ID or fingerprint question, answered as the test says.</summary>
internal sealed class FakeUnlocker : IUnlocker
{
    /// <summary>Whether the person passes the next question.</summary>
    public bool Passes { get; set; } = true;

    /// <summary>What each question said it was for.</summary>
    public List<string> Asked { get; } = [];

    /// <inheritdoc />
    public Task<bool> UnlockAsync(string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Asked.Add(reason);
        return Task.FromResult(Passes);
    }
}

/// <summary>The phone's home screen widgets, keeping what they were last handed.</summary>
internal sealed class FakeWidgets : IWidgets
{
    /// <summary>What the widgets show now; null until they were handed anything.</summary>
    public WidgetSnapshot? Shown { get; private set; }

    /// <inheritdoc />
    public void Show(WidgetSnapshot snapshot) => Shown = snapshot;
}

/// <summary>The hidden vault's own files, in memory, as a phone keeps them inside the app.</summary>
internal sealed class MemoryVaultFiles : IVaultFiles
{
    private readonly Dictionary<string, (byte[] Bytes, DateTime Written)> _files = new(StringComparer.Ordinal);
    private DateTime _clock = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>When set, what a file is read as, given its name and what it holds: a disk that changed it.</summary>
    public Func<string, byte[], byte[]>? Reading { get; set; }

    /// <inheritdoc />
    public IReadOnlyList<VaultFileInfo> List()
    {
        lock (_files) return [.. _files.OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => new VaultFileInfo(f.Key, f.Value.Bytes.LongLength, f.Value.Written))];
    }

    /// <inheritdoc />
    public async Task WriteAsync(string name, Func<Stream, Task> write, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        lock (_files) if (_files.ContainsKey(name)) throw new IOException($"The hidden vault has a file called {name} already.");
        using var output = new MemoryStream();
        await write(output);
        lock (_files) _files[name] = (output.ToArray(), _clock = _clock.AddMinutes(1));
    }

    /// <inheritdoc />
    public Stream OpenRead(string name)
    {
        lock (_files)
        {
            if (!_files.TryGetValue(name, out var file)) throw new FileNotFoundException(name);
            return new MemoryStream(Reading?.Invoke(name, file.Bytes) ?? file.Bytes, writable: false);
        }
    }

    /// <inheritdoc />
    public void Delete(string name)
    {
        lock (_files) _files.Remove(name);
    }
}
