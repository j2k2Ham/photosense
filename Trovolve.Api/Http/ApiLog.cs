using Microsoft.Extensions.Logging;

namespace Trovolve.Api.Http;

/// <summary>What the service's addresses write to the program's log, with event ids from 2000.</summary>
internal static partial class ApiLog
{
    /// <summary>A route answered: written at Debug, as the page asks some every second.</summary>
    [LoggerMessage(EventId = 2000, Level = LogLevel.Debug, Message = "{Route} answered {Status} in {Milliseconds:0.0} ms")]
    public static partial void Answered(ILogger log, string route, int status, double milliseconds);

    /// <summary>A request failed on an error nothing else caught, where no web server stands behind the addresses to say so.</summary>
    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "{Method} {Path} failed on an error nothing expected")]
    public static partial void Failed(ILogger log, Exception error, string method, string path);
}
