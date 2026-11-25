namespace Trovolve.Domain.Entities;

/// <summary>One use of a feature of Trovolve, as it is kept and sent when usage metrics are on.</summary>
/// <param name="Sequence">Its place among every use on this device, from 1, so that the order of uses is known even when two share a time.</param>
/// <param name="At">When it was used, in UTC.</param>
/// <param name="Feature">Which feature: the address it was used by, without its values, such as "POST scan/start".</param>
/// <param name="Status">How it ended: the answer's HTTP status, 200 for done, 400 and above for refused or failed.</param>
/// <param name="Session">The run of the program it was used in, new each time the program starts.</param>
public sealed record FeatureUse(long Sequence, DateTimeOffset At, string Feature, int Status, Guid Session);

/// <summary>The program that noted the uses, as the usage server is told it.</summary>
/// <param name="Program">Which program: "server" on a computer, "iphone" or "android".</param>
/// <param name="Version">Its version.</param>
/// <param name="System">The system it runs on, as .NET describes it, such as "Microsoft Windows 10.0.26300".</param>
public sealed record UsageSource(string Program, string Version, string System);

/// <summary>What is sent to the usage server at once.</summary>
/// <param name="Install">This installation of Trovolve: a random id made the first time uses are noted, which names no person or device.</param>
/// <param name="Source">The program that noted them.</param>
/// <param name="Uses">The uses, oldest first.</param>
public sealed record UsageReport(Guid Install, UsageSource Source, IReadOnlyList<FeatureUse> Uses);
