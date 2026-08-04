using System;

namespace DungeonDrip.Core;

/// <summary>
/// Wording shared between surfaces, so the same quantity is not phrased two ways.
/// </summary>
/// <remarks>
/// Currently only the age of a snapshot, which four surfaces report - the panels, the duty window,
/// the collection view and the loot service's status line. Coarse on purpose: these ages are read to
/// decide whether to trust something, and a figure to the minute invites more confidence than a
/// cached snapshot deserves.
/// </remarks>
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

    /// <summary>The same, for a timestamp rather than a span - every caller has one of those.</summary>
    public static string Age(DateTime utc) => Age(DateTime.UtcNow - utc);
}
