namespace AssetIQ.Api.Infrastructure;

/// <summary>
/// The API's clock. Every timestamp the API writes comes from here (never the database's now()),
/// at millisecond precision so stored values round-trip exactly through the JSON timestamps.
/// </summary>
public sealed class AppClock(TimeProvider timeProvider)
{
    public TimeProvider TimeProvider { get; } = timeProvider;

    public DateTime UtcNow => TruncateToMilliseconds(TimeProvider.GetUtcNow().UtcDateTime);

    public static DateTime TruncateToMilliseconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerMillisecond, value.Kind);
}
