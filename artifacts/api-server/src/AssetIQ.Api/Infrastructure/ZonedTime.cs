using System.Globalization;
using AssetIQ.Api.Configuration;

namespace AssetIQ.Api.Infrastructure;

/// <summary>Wall-clock components of an instant in a time zone. Weekday: 0 = Sunday … 6 = Saturday.</summary>
public readonly record struct ZonedParts(int Year, int Month, int Day, int Hour, int Minute, int Second, int Weekday);

/// <summary>
/// Business rules that care about "working hours", "today" or "this week" are evaluated in the
/// organisation's time zone (ORG_TIMEZONE, default Africa/Harare), never the server's: the API may
/// run in UTC on Replit or Azure. Instants are UTC <see cref="DateTime"/>s.
/// </summary>
public static class ZonedTime
{
    public static readonly TimeSpan Day = TimeSpan.FromDays(1);

    private static readonly string[] WeekdayNames = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];
    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static ZonedParts Parts(DateTime instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(AsUtc(instant), zone);
        return new(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, (int)local.DayOfWeek);
    }

    /// <summary>UTC instant of a wall-clock time in <paramref name="zone"/> (two passes, so DST edges resolve).</summary>
    public static DateTime ZonedTimeToUtc(int year, int month, int day, int hour, int minute, TimeZoneInfo zone)
    {
        var guess = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
        var offset = zone.GetUtcOffset(guess);
        var result = guess - offset;
        var corrected = zone.GetUtcOffset(result);
        if (corrected != offset) result = guess - corrected;
        return result;
    }

    /// <summary>Calendar day (YYYY-MM-DD) of <paramref name="instant"/> in <paramref name="zone"/>.</summary>
    public static string DateKey(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        return string.Create(CultureInfo.InvariantCulture, $"{p.Year}-{p.Month:D2}-{p.Day:D2}");
    }

    /// <summary>Midnight (start of the local day) containing <paramref name="instant"/>.</summary>
    public static DateTime StartOfZonedDay(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        return ZonedTimeToUtc(p.Year, p.Month, p.Day, 0, 0, zone);
    }

    /// <summary>Monday 00:00 of the local week containing <paramref name="instant"/> (ISO weeks start on Monday).</summary>
    public static DateTime StartOfZonedWeek(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        var daysSinceMonday = (p.Weekday + 6) % 7;
        var noon = ZonedTimeToUtc(p.Year, p.Month, p.Day, 12, 0, zone);
        return StartOfZonedDay(noon - daysSinceMonday * Day, zone);
    }

    /// <summary>First day of the local month containing <paramref name="instant"/>, 00:00.</summary>
    public static DateTime StartOfZonedMonth(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        return ZonedTimeToUtc(p.Year, p.Month, 1, 0, 0, zone);
    }

    /// <summary>The last <paramref name="count"/> calendar days (YYYY-MM-DD), oldest first, ending with today.</summary>
    public static IReadOnlyList<string> LastNDayKeys(DateTime now, int count, TimeZoneInfo zone)
    {
        var p = Parts(now, zone);
        var todayNoon = ZonedTimeToUtc(p.Year, p.Month, p.Day, 12, 0, zone);
        var keys = new List<string>(Math.Max(count, 0));
        for (var i = count - 1; i >= 0; i--) keys.Add(DateKey(todayNoon - i * Day, zone));
        return keys;
    }

    /// <summary>Start (00:00 local) of a YYYY-MM-DD calendar day.</summary>
    public static DateTime StartOfDayKey(string key, TimeZoneInfo zone)
    {
        var parts = key.Split('-');
        return ZonedTimeToUtc(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            0, 0, zone);
    }

    public static bool IsWithinWorkingHours(DateTime instant, TimeZoneInfo zone, WorkingHours hours)
    {
        var p = Parts(instant, zone);
        return hours.Days.Contains(p.Weekday) && p.Hour >= hours.StartHour && p.Hour < hours.EndHour;
    }

    /// <summary>e.g. "Mon–Fri 07:00–18:00"</summary>
    public static string DescribeWorkingHours(WorkingHours hours)
    {
        var days = hours.Days.Order().ToList();
        var contiguous = days.Select((d, i) => i == 0 || d == days[i - 1] + 1).All(x => x);
        var dayText = days.Count == 0
            ? "no days"
            : contiguous && days.Count > 1
                ? $"{WeekdayNames[days[0]]}–{WeekdayNames[days[^1]]}"
                : string.Join(", ", days.Select(d => WeekdayNames[d]));
        return string.Create(CultureInfo.InvariantCulture, $"{dayText} {hours.StartHour:D2}:00–{hours.EndHour:D2}:00");
    }

    /// <summary>e.g. "Sat 18 Oct 02:14" in the organisation time zone.</summary>
    public static string FormatZoned(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        return string.Create(CultureInfo.InvariantCulture,
            $"{WeekdayNames[p.Weekday]} {p.Day} {MonthNames[p.Month - 1]} {p.Hour:D2}:{p.Minute:D2}");
    }

    /// <summary>e.g. "Sat 18 Oct" in the organisation time zone.</summary>
    public static string FormatZonedDay(DateTime instant, TimeZoneInfo zone)
    {
        var p = Parts(instant, zone);
        return string.Create(CultureInfo.InvariantCulture, $"{WeekdayNames[p.Weekday]} {p.Day} {MonthNames[p.Month - 1]}");
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
