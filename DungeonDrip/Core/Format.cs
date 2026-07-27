using System;

namespace DungeonDrip.Core;

public static class Format
{
    /// <summary>
    /// A bare span of time, so callers can say "{x} old" or "read {x} ago" from one wording.
    /// </summary>
    public static string Age(TimeSpan age) => age switch
    {
        { TotalMinutes: < 1 } => "less than a minute",
        { TotalHours: < 1 } => $"{(int)age.TotalMinutes} minutes",
        { TotalDays: < 1 } => $"{(int)age.TotalHours} hours",
        { TotalDays: < 2 } => "a day",
        _ => $"{(int)age.TotalDays} days",
    };

    public static string Age(DateTime utc) => Age(DateTime.UtcNow - utc);
}
