namespace Stockify.Utils;

public static class DateRangeHelper
{
    /// Normalizes a date range coming from the client (local time with offset)
    /// into a UTC range suitable for timestamptz queries.
    /// 
    /// Returned range is:
    ///   [startUtc, endUtcExclusive)
    public static (DateTime StartUtc, DateTime EndUtcExclusive) NormalizeToUtcRange(
        DateTimeOffset? startDate,
        DateTimeOffset? endDate)
    {
        var now = DateTimeOffset.Now;

        // Defaults in local time (with offset preserved)
        var startLocal = startDate ?? now.Date.AddDays(-(int)now.DayOfWeek);
        var endLocalInclusive = endDate
            ?? startLocal.Date.AddDays(6)
                .AddHours(23)
                .AddMinutes(59)
                .AddSeconds(59);

        // Convert to UTC for DB comparison
        var startUtc = startLocal.UtcDateTime;

        // Use exclusive upper bound to avoid 23:59:59 bugs
        var endUtcExclusive = endLocalInclusive.Date
            .AddDays(1)
            .ToUniversalTime();

        return (startUtc, endUtcExclusive);
    }
}
