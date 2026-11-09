using System.Reflection;

namespace PhotoSense.Api.About;

/// <summary>The program answering, which each host registers: its name, its version and the notices built into it.</summary>
/// <param name="Program">Which program it is: "server", "iphone" or "android".</param>
/// <param name="Version">Its version, as About shows it.</param>
/// <param name="Notices">Reads the third-party notices built into it.</param>
public sealed record ProgramInfo(string Program, string Version, Func<string> Notices)
{
    /// <summary>The name of the third-party notices each program builds in (from notices/, made by tools/notices.mjs).</summary>
    public const string NoticesResource = "PhotoSense.ThirdPartyNotices.txt";

    /// <summary>The program an assembly is, with the version and notices built into it.</summary>
    /// <param name="assembly">The program's own assembly.</param>
    /// <param name="program">Which program it is.</param>
    public static ProgramInfo From(Assembly assembly, string program) => new(
        program,
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version!.ToString(),
        () =>
        {
            using var notices = assembly.GetManifestResourceStream(NoticesResource) ?? throw new InvalidOperationException($"{program} was built without its third-party notices.");
            return new StreamReader(notices).ReadToEnd();
        });
}
