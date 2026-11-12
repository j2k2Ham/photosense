using System.Security.AccessControl;
using System.Security.Principal;
using PhotoSense.Domain.Services;

namespace PhotoSense.Tests;

/// <summary>Stand-ins and file-system helpers shared by tests.</summary>
internal sealed class InMemoryThumbnailStore : IThumbnailStore
{
    /// <summary>The previews kept, by hash.</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> Saved { get; } = new();
    /// <inheritdoc />
    public Task SaveAsync(string contentHash, byte[] jpeg, CancellationToken ct) { Saved[contentHash] = jpeg; return Task.CompletedTask; }
    /// <inheritdoc />
    public Task<byte[]?> GetAsync(string contentHash, CancellationToken ct) => Task.FromResult(Saved.GetValueOrDefault(contentHash));
    /// <inheritdoc />
    public Task ClearAsync(CancellationToken ct) { Saved.Clear(); return Task.CompletedTask; }
}

/// <summary>File-system helpers shared by tests.</summary>
internal static class TestFiles
{
    /// <summary>The computer's own disks, as the service has them.</summary>
    public static PhotoSense.Infrastructure.Files.DiskFileSystem Disk { get; } = new();

    /// <summary>
    /// Deletes a test's folder. A database file closed a moment ago can still be held for an instant, by a
    /// virus scanner for one, and on Windows that now and then makes the first attempt fail.
    /// </summary>
    public static void Remove(DirectoryInfo folder)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                folder.Delete(true);
                return;
            }
            catch (Exception ex) when (attempt < 5 && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }

    /// <summary>
    /// Makes <paramref name="link"/> a second path to the folder <paramref name="target"/> until disposed.
    /// Windows asks for a privilege to create a symbolic link, which this account may lack, and for none to
    /// create a junction. The link is taken away again because deleting a folder that holds one can fail.
    /// </summary>
    public static IDisposable LinkFolder(string link, string target)
    {
        if (OperatingSystem.IsWindows())
        {
            using var mklink = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", ["/c", "mklink", "/J", link, target])
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
            mklink.WaitForExit();
            if (!Directory.Exists(link)) throw new IOException($"Could not link {link} to {target}");
        }
        else Directory.CreateSymbolicLink(link, target);
        return new Restore(() => Directory.Delete(link));
    }

    /// <summary>
    /// Takes away this user's access to a file or folder until disposed, so that code which must cope with
    /// "access denied" can be shown to. Returns null where that cannot be arranged (running as root).
    /// </summary>
    public static IDisposable? DenyAccess(string path)
    {
        if (OperatingSystem.IsWindows()) return WindowsDenial.Apply(path);
        return Environment.UserName == "root" ? null : UnixDenial.Apply(path);
    }

    /// <summary>Takes away the right to read a folder on Linux and macOS, and gives it back.</summary>
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static class UnixDenial
    {
        /// <summary>Takes the right away; disposing the result gives it back.</summary>
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

    /// <summary>Takes away the right to read a folder on Windows, and gives it back.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static class WindowsDenial
    {
        /// <summary>Takes the right away; disposing the result gives it back.</summary>
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
