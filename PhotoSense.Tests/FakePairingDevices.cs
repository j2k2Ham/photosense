using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>A phone's camera that sees what the test says: a code's text, or nothing, as when the person puts it away.</summary>
internal sealed class FakeCodeScanner : ICodeScanner
{
    /// <summary>What the camera sees next; null as if put away.</summary>
    public string? Sees { get; set; }

    /// <summary>How many times the camera was opened.</summary>
    public int Opened { get; private set; }

    /// <summary>Why the camera cannot be used; null when it can.</summary>
    public string? Broken { get; set; }

    /// <inheritdoc />
    public Task<string?> ScanAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Opened++;
        return Broken is null ? Task.FromResult(Sees) : Task.FromException<string?>(new IOException(Broken));
    }
}

/// <summary>The computers a phone finds on the Wi-Fi, as the test lists them.</summary>
internal sealed class FakeComputerFinder : IComputerFinder
{
    /// <summary>The computers found, in order.</summary>
    public List<FoundComputer> Computers { get; } = [];

    /// <inheritdoc />
    public Task<IReadOnlyList<FoundComputer>> FindAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<FoundComputer>>([.. Computers]);
    }
}
