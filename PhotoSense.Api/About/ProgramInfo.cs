using System.Reflection;

namespace PhotoSense.Api.About;

/// <summary>The program answering, which each host registers: its name, its version and the notices built into it.</summary>
public sealed record ProgramInfo(string Program, string Version, Func<string> Notices)
{
    /// <summary>The name of the third-party notices each program builds in (from notices/, made by tools/notices.mjs).</summary>
    public const string NoticesResource = "PhotoSense.ThirdPartyNotices.txt";

    public static ProgramInfo From(Assembly assembly, string program) => new(
        program,
        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version!.ToString(),
        () =>
        {
            using var notices = assembly.GetManifestResourceStream(NoticesResource) ?? throw new InvalidOperationException($"{program} was built without its third-party notices.");
            return new StreamReader(notices).ReadToEnd();
        });
}
