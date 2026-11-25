using Microsoft.Extensions.Logging;

namespace Trovolve.Infrastructure.Logging;

/// <summary>
/// The program's log, written to a file a day in a folder of Trovolve's own (<c>trovolve-2026-11-25.log</c>), the last
/// <see cref="DaysKept"/> days kept. A phone has no console to read, so this is where what happened on it can be found.
/// </summary>
/// <remarks>
/// Its levels are set in the settings' <c>Logging:File</c> section, as the console's are in <c>Logging:Console</c>;
/// Information and above unless they say otherwise. A line that cannot be written is dropped: a log must never stop
/// what it describes.
/// </remarks>
[ProviderAlias("File")]
public sealed class FileLoggerProvider : ILoggerProvider
{
    /// <summary>How many days of log files are kept, today's among them.</summary>
    public const int DaysKept = 7;

    private const string Prefix = "trovolve-";
    private const string Extension = ".log";
    private readonly string _folder;
    private readonly TimeProvider _time;
    // One writer for every logger, so that lines from many threads never interleave.
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private DateOnly _day;

    /// <summary>Writes the log into <paramref name="folder"/>, made when the first line is written, each line stamped by <paramref name="time"/>.</summary>
    /// <param name="folder">Where the log files go.</param>
    /// <param name="time">The clock each line is stamped with, and which says when a new day's file begins.</param>
    public FileLoggerProvider(string folder, TimeProvider time)
    {
        _folder = folder;
        _time = time;
    }

    /// <summary>The file today's lines go into.</summary>
    public string Today => FileFor(DateOnly.FromDateTime(_time.GetUtcNow().UtcDateTime));

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    /// <summary>Closes today's file.</summary>
    public void Dispose()
    {
        lock (_gate) Close();
    }

    private string FileFor(DateOnly day) => Path.Combine(_folder, $"{Prefix}{day:yyyy-MM-dd}{Extension}");

    private void Write(string category, LogLevel level, string message, Exception? exception)
    {
        var now = _time.GetUtcNow();
        var line = $"{now:yyyy-MM-dd'T'HH:mm:ss.fff'Z'} [{Short(level)}] {category}: {message}";
        lock (_gate)
        {
            try
            {
                var writer = WriterFor(DateOnly.FromDateTime(now.UtcDateTime));
                writer.WriteLine(line);
                if (exception is not null) writer.WriteLine(exception.ToString());
            }
            // A full disk, or a folder taken away: the line is dropped, and the file opened again for the next one.
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Close();
            }
        }
    }

    // Today's file, opened when the day changes, when the old days past those kept are deleted.
    private StreamWriter WriterFor(DateOnly day)
    {
        if (_writer is not null && day == _day) return _writer;
        Close();
        Directory.CreateDirectory(_folder);
        ForgetDaysBefore(day.AddDays(1 - DaysKept));
        // Shared for reading, so that the log can be read, or sent, while the program runs.
        var stream = new FileStream(FileFor(day), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        (_writer, _day) = (new StreamWriter(stream) { AutoFlush = true }, day);
        return _writer;
    }

    // Lets go of the day's file, when one is open; called under the gate.
    private void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    private void ForgetDaysBefore(DateOnly first)
    {
        foreach (var file in Directory.EnumerateFiles(_folder, $"{Prefix}*{Extension}"))
        {
            var name = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
            if (DateOnly.TryParseExact(name, "yyyy-MM-dd", out var day) && day < first) File.Delete(file);
        }
    }

    private static string Short(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        _ => "CRT",
    };

    /// <summary>One category's lines, written through the provider's one file.</summary>
    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        /// <inheritdoc />
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel)) provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
