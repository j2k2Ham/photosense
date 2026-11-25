using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trovolve.Domain.Configuration;
using Trovolve.Domain.Entities;
using Trovolve.Domain.Repositories;
using Trovolve.Domain.Services;

namespace Trovolve.Application.Usage;

/// <summary>
/// Notes which of Trovolve's features are used, when and in what order, and sends them to the usage server, so that
/// those who make Trovolve know what people use. Off unless the settings turn it on (<see cref="UsageMetricsOptions"/>):
/// while off, nothing is noted, kept or sent.
/// </summary>
/// <remarks>
/// What is noted waits on the device until the server takes it, the oldest forgotten past
/// <see cref="UsageMetricsOptions.MaxKept"/>; a server that cannot be reached loses nothing until then. Uses are sent at
/// most once every <see cref="UsageMetricsOptions.SendEvery"/>, one batch after another, oldest first.
/// </remarks>
public sealed class UsageMetrics
{
    private readonly IUsageStore _store;
    private readonly IUsageSender _sender;
    private readonly IOptions<UsageMetricsOptions> _options;
    private readonly UsageSource _source;
    private readonly TimeProvider _time;
    private readonly ILogger<UsageMetrics> _log;
    // This run of the program, which every use noted in it carries.
    private readonly Guid _session = Guid.NewGuid();
    // One sending at a time: a second would send the same uses again.
    private readonly SemaphoreSlim _sending = new(1, 1);
    private DateTimeOffset? _lastSent;

    /// <summary>Uses noted in <paramref name="store"/> and sent by <paramref name="sender"/>, as <paramref name="options"/> say.</summary>
    /// <param name="store">Where uses wait until they are sent.</param>
    /// <param name="sender">Sends them to the usage server.</param>
    /// <param name="options">Whether uses are noted and sent, where to, and how often.</param>
    /// <param name="source">The program that notes them.</param>
    /// <param name="time">When each use happened, and when uses are next due to be sent.</param>
    /// <param name="log">The program's log, told when uses are sent and when the server cannot be reached.</param>
    public UsageMetrics(IUsageStore store, IUsageSender sender, IOptions<UsageMetricsOptions> options, UsageSource source, TimeProvider time, ILogger<UsageMetrics> log)
    {
        _store = store;
        _sender = sender;
        _options = options;
        _source = source;
        _time = time;
        _log = log;
    }

    /// <summary>Whether uses are noted at all.</summary>
    public bool On => _options.Value.Enabled;

    /// <summary>Notes that a feature was used, and how it ended; nothing at all while usage metrics are off.</summary>
    /// <param name="feature">Which feature: the address it was used by, without its values, such as "POST scan/start".</param>
    /// <param name="status">How it ended: the answer's HTTP status.</param>
    /// <param name="ct">Checked before anything is noted.</param>
    public async Task RecordAsync(string feature, int status, CancellationToken ct)
    {
        if (!On) return;
        await _store.AddAsync(_time.GetUtcNow(), feature, status, _session, Math.Max(1, _options.Value.MaxKept), ct);
    }

    /// <summary>
    /// Sends what waits, oldest first, when it is due: on, with a server to send to, and the last sending at least
    /// <see cref="UsageMetricsOptions.SendEvery"/> ago. A server that cannot be reached is said in the log, and what was not
    /// sent waits for the next time.
    /// </summary>
    /// <param name="ct">Stops the sending between batches; what was sent is forgotten, the rest waits.</param>
    /// <returns>How many uses were sent.</returns>
    public async Task<int> SendDueAsync(CancellationToken ct)
    {
        var options = _options.Value;
        if (options.SendTo() is not { } to) return 0;
        // Another sending is going on: it sends what this one would.
        if (!await _sending.WaitAsync(TimeSpan.Zero, ct)) return 0;
        try
        {
            var now = _time.GetUtcNow();
            if (_lastSent is { } last && now - last < options.SendEvery) return 0;
            _lastSent = now;
            var batch = Math.Max(1, options.BatchSize);
            var install = await _store.InstallAsync(ct);
            var sent = 0;
            while (await _store.OldestAsync(batch, ct) is { Count: > 0 } uses)
            {
                try
                {
                    await _sender.SendAsync(to, new UsageReport(install, _source, uses), ct);
                }
                catch (IOException ex)
                {
                    TrovolveLog.UsageNotSent(_log, ex, await _store.CountAsync(ct));
                    break;
                }
                await _store.ForgetThroughAsync(uses[^1].Sequence, ct);
                sent += uses.Count;
                if (uses.Count < batch) break;
            }
            if (sent > 0) TrovolveLog.UsageSent(_log, sent);
            return sent;
        }
        finally
        {
            _sending.Release();
        }
    }

    /// <summary>
    /// Sends what waits whenever it is due, for as long as the program runs: the computer's program runs this from its
    /// start; a phone sends when it wakes instead (<see cref="SendDueAsync"/>). Returns at once while usage metrics are off.
    /// </summary>
    /// <param name="stop">Stops it, as the program stops.</param>
    /// <exception cref="OperationCanceledException">The program stopped while it was sending or waiting to.</exception>
    public async Task SendWhileRunningAsync(CancellationToken stop)
    {
        if (!On) return;
        while (!stop.IsCancellationRequested)
        {
            await SendDueAsync(stop);
            // A minute at least, so that a setting of nothing never keeps the program busy.
            await Task.Delay(TimeSpan.FromTicks(Math.Max(TimeSpan.TicksPerMinute, _options.Value.SendEvery.Ticks)), _time, stop);
        }
    }
}
