namespace PhotoSense.Tests;

/// <summary>A clock that tells the time the test sets, and moves on by a step each time it is read.</summary>
internal sealed class TestClock(DateTimeOffset now, TimeSpan step = default) : TimeProvider
{
    public static readonly DateTimeOffset Day = new(2026, 10, 15, 9, 30, 0, TimeSpan.Zero);

    public DateTimeOffset Now { get; set; } = now;

    public TestClock() : this(Day) { }

    public override DateTimeOffset GetUtcNow()
    {
        var at = Now;
        Now += step;
        return at;
    }
}
