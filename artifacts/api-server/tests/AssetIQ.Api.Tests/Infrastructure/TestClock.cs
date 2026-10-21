using Microsoft.Extensions.Time.Testing;

namespace AssetIQ.Api.Tests.Infrastructure;

/// <summary>
/// The API's clock in tests, like Vitest's <c>vi.setSystemTime</c>: it can be set to any instant,
/// backwards included (each test starts again on Wednesday 10:00). Timers (e.g. the simulated
/// Business Central sync) run on an inner <see cref="FakeTimeProvider"/> and fire only through
/// <see cref="Advance"/>.
/// </summary>
public sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private readonly FakeTimeProvider _timers = new();
    private readonly object _gate = new();
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    /// <summary>Jump to <paramref name="instant"/>; pending timers are not affected.</summary>
    public void SetUtcNow(DateTimeOffset instant)
    {
        lock (_gate) _now = instant;
    }

    /// <summary>Move forward, firing any timers that fall due.</summary>
    public void Advance(TimeSpan delta)
    {
        lock (_gate) _now += delta;
        _timers.Advance(delta);
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        _timers.CreateTimer(callback, state, dueTime, period);

    public override long GetTimestamp() => _timers.GetTimestamp();

    public override long TimestampFrequency => _timers.TimestampFrequency;
}
