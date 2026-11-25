namespace Trovolve.Domain.Configuration;

/// <summary>
/// Whether Trovolve notes which of its features are used, and where it sends them: the <c>UsageMetrics</c> section of
/// the settings. Off unless turned on; while off, nothing is noted, kept or sent.
/// </summary>
/// <remarks>
/// A use is the feature's name, when it was used, in what order, and whether it worked: never a file, a folder, a
/// photo or anything the person typed. Turning this on needs the privacy policy and the FAQ to say so first, as they
/// say today that nothing leaves the device.
/// </remarks>
public sealed class UsageMetricsOptions
{
    /// <summary>The settings' section these are read from.</summary>
    public const string Section = "UsageMetrics";

    /// <summary>Whether uses are noted and sent. Off until the usage server is set up.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The usage server's address, which uses are sent to as JSON. HTTPS, or HTTP to this machine itself for testing;
    /// blank or anything else, uses are noted and kept but not sent.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>How often what was noted is sent.</summary>
    public TimeSpan SendEvery { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The most uses sent in one request.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>The most uses kept while the server cannot be reached; past it, the oldest are forgotten.</summary>
    public int MaxKept { get; set; } = 10_000;

    /// <summary>Where uses are sent, when they are to be: on, with an address that is HTTPS or this machine's own.</summary>
    public Uri? SendTo()
        => Enabled && Uri.TryCreate(Endpoint, UriKind.Absolute, out var to) && (to.Scheme == Uri.UriSchemeHttps || (to.Scheme == Uri.UriSchemeHttp && to.IsLoopback)) ? to : null;
}
