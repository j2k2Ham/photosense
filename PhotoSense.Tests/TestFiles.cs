using System.Security.AccessControl;
using System.Security.Principal;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>Stand-ins and file-system helpers shared by tests.</summary>
internal sealed class InMemoryThumbnailStore : IThumbnailStore
{
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Saved { get; } = new();
    public Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct = default) { Saved[contentHash] = jpeg; return Task.CompletedTask; }
    public Task<byte[]?> GetAsync(string contentHash, CancellationToken ct = default) => Task.FromResult(Saved.GetValueOrDefault(contentHash));
}

internal static class TestFiles
{
    /// <summary>
    /// Takes away this user's access to a file or folder until disposed, so that code which must cope with
    /// "access denied" can be shown to. Returns null where that cannot be arranged (running as root).
    /// </summary>
    public static IDisposable? DenyAccess(string path)
    {
        if (OperatingSystem.IsWindows()) return WindowsDenial.Apply(path);
        return Environment.UserName == "root" ? null : UnixDenial.Apply(path);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static class UnixDenial
    {
        public static IDisposable Apply(string path)
        {
            var before = File.GetUnixFileMode(path);
            File.SetUnixFileMode(path, UnixFileMode.None);
            return new Restore(() => Reset(path, before));
        }

        private static void Reset(string path, UnixFileMode mode) => File.SetUnixFileMode(path, mode);
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static class WindowsDenial
    {
        public static IDisposable Apply(string path)
        {
            var user = WindowsIdentity.GetCurrent().User!;
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                var rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory | FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories | FileSystemRights.Write, AccessControlType.Deny);
                var security = directory.GetAccessControl();
                security.AddAccessRule(rule);
                directory.SetAccessControl(security);
                return new Restore(() => { var s = directory.GetAccessControl(); s.RemoveAccessRule(rule); directory.SetAccessControl(s); });
            }
            else
            {
                var file = new FileInfo(path);
                var rule = new FileSystemAccessRule(user, FileSystemRights.Read | FileSystemRights.Write, AccessControlType.Deny);
                var security = file.GetAccessControl();
                security.AddAccessRule(rule);
                file.SetAccessControl(security);
                return new Restore(() => { var s = file.GetAccessControl(); s.RemoveAccessRule(rule); file.SetAccessControl(s); });
            }
        }
    }
}
