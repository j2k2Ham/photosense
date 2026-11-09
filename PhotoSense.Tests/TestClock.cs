namespace PhotoSense.Tests;

/// <summary>A clock that tells the time the test sets, and moves on by a step each time it is read.</summary>
internal sealed class TestClock(DateTimeOffset now, TimeSpan step = default) : TimeProvider
{
    /// <summary>The day it tells unless the test sets another.</summary>
    public static readonly DateTimeOffset Day = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    /// <summary>The time it tells next.</summary>
    public DateTimeOffset Now { get; set; } = now;

    /// <summary>A clock standing still at <see cref="Day"/>.</summary>
    public TestClock() : this(Day) { }

    /// <summary>The time, moving on by the step each time it is read.</summary>
    public override DateTimeOffset GetUtcNow()
    {
        var at = Now;
        Now += step;
        return at;
    }
}
