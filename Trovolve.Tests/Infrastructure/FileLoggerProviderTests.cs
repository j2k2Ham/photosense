using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trovolve.Infrastructure;
using Trovolve.Infrastructure.Logging;

namespace Trovolve.Tests.Infrastructure;

/// <summary>The program's log, written to a file a day in a folder of Trovolve's own, the last week kept.</summary>
public class FileLoggerProviderTests
{
    // The file as a reader sees it while the program still writes to it.
    private static string Read(string path)
    {
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return new StreamReader(file).ReadToEnd();
    }

    [Test]
    public async Task Each_Line_Says_When_How_Serious_And_From_Where_With_An_Error_Written_After_It()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            using var provider = new FileLoggerProvider(Path.Combine(folder.FullName, "logs"), new TestClock());
            var log = provider.CreateLogger("Trovolve.Scan");
            foreach (var level in new[] { LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Critical })
                log.Log(level, "said at {Level}", level);
            log.LogError(new IOException("The disk is full."), "A change could not be carried out");
            // Nothing is written at None, which is no level at all; and the log keeps no scopes.
            log.Log(LogLevel.None, "never");
            await Assert.That(log.IsEnabled(LogLevel.None)).IsFalse();
            await Assert.That(log.BeginScope("scope")).IsNull();

            await Assert.That(provider.Today).IsEqualTo(Path.Combine(folder.FullName, "logs", "trovolve-2026-10-15.log"));
            var lines = Read(provider.Today).Split(Environment.NewLine);
            await Assert.That(lines[..6]).IsEquivalentTo([
                "2026-10-15T09:30:00.000Z [TRC] Trovolve.Scan: said at Trace",
                "2026-10-15T09:30:00.000Z [DBG] Trovolve.Scan: said at Debug",
                "2026-10-15T09:30:00.000Z [INF] Trovolve.Scan: said at Information",
                "2026-10-15T09:30:00.000Z [WRN] Trovolve.Scan: said at Warning",
                "2026-10-15T09:30:00.000Z [CRT] Trovolve.Scan: said at Critical",
                "2026-10-15T09:30:00.000Z [ERR] Trovolve.Scan: A change could not be carried out",
            ], CollectionOrdering.Matching);
            await Assert.That(lines[6]).IsEqualTo("System.IO.IOException: The disk is full.");
            await Assert.That(string.Join("\n", lines)).DoesNotContain("never");
        }
        finally
        {
            TestFiles.Remove(folder);
        }
    }

    [Test]
    public async Task A_New_Day_Begins_A_New_File_And_Files_Older_Than_A_Week_Are_Deleted(CancellationToken ct)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            // Eight days before, seven days before, and files of other names, which are never touched.
            foreach (var name in new[] { "trovolve-2026-10-07.log", "trovolve-2026-10-09.log", "trovolve-notes.log", "notes.txt" })
                await File.WriteAllTextAsync(Path.Combine(folder.FullName, name), "kept", ct);
            var clock = new TestClock();
            using var provider = new FileLoggerProvider(folder.FullName, clock);
            var log = provider.CreateLogger("Trovolve");

            log.LogInformation("first");
            log.LogInformation("second");
            await Assert.That(Directory.GetFiles(folder.FullName).Select(f => Path.GetFileName(f)!).Order()).IsEquivalentTo(
                ["notes.txt", "trovolve-2026-10-09.log", "trovolve-2026-10-15.log", "trovolve-notes.log"], CollectionOrdering.Matching);

            clock.Now = clock.Now.AddDays(1);
            log.LogInformation("third");
            await Assert.That(Directory.GetFiles(folder.FullName).Select(f => Path.GetFileName(f)!).Order()).IsEquivalentTo(
                ["notes.txt", "trovolve-2026-10-15.log", "trovolve-2026-10-16.log", "trovolve-notes.log"], CollectionOrdering.Matching);
            await Assert.That(Read(Path.Combine(folder.FullName, "trovolve-2026-10-15.log"))).Contains("first").And.Contains("second").And.DoesNotContain("third");
            await Assert.That(Read(provider.Today)).Contains("third");
        }
        finally
        {
            TestFiles.Remove(folder);
        }
    }

    [Test]
    public async Task A_Line_That_Cannot_Be_Written_Is_Dropped_And_The_Next_One_Is_Written_Once_It_Can_Be()
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            using var provider = new FileLoggerProvider(folder.FullName, new TestClock());
            var log = provider.CreateLogger("Trovolve");
            // Today's file cannot be made: a folder has its name.
            Directory.CreateDirectory(provider.Today);
            log.LogWarning("dropped");
            await Assert.That(Directory.Exists(provider.Today)).IsTrue();

            Directory.Delete(provider.Today);
            log.LogWarning("written");
            await Assert.That(Read(provider.Today)).Contains("written").And.DoesNotContain("dropped");

            // Closed, as the program stops, and opened again should anything still write.
            provider.Dispose();
            provider.Dispose();
            log.LogWarning("after");
            await Assert.That(Read(provider.Today)).Contains("after");
        }
        finally
        {
            TestFiles.Remove(folder);
        }
    }

    [Test]
    public async Task A_Program_That_Keeps_A_Log_File_Writes_Every_Service_Log_Into_The_Folder_It_Names(CancellationToken ct)
    {
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            await using (var services = new ServiceCollection().AddSingleton<TimeProvider>(new TestClock()).AddLogging()
                .AddTrovolveLogFile(_ => Path.Combine(folder.FullName, "logs")).BuildServiceProvider())
                services.GetRequiredService<ILogger<FileLoggerProviderTests>>().LogInformation("Kept in the file");

            var written = await File.ReadAllTextAsync(Path.Combine(folder.FullName, "logs", "trovolve-2026-10-15.log"), ct);
            await Assert.That(written).Contains($"[INF] {typeof(FileLoggerProviderTests).FullName}: Kept in the file");
        }
        finally
        {
            TestFiles.Remove(folder);
        }
    }
}
