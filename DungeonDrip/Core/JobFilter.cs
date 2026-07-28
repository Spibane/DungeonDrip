using System.Collections.Generic;
using System.Reflection;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// Whether the job the player is on right now can wear a given piece.
/// </summary>
/// <remarks>
/// ClassJobCategory exposes one boolean column per job abbreviation rather than a list, so the check
/// is a reflected property read, cached by (category, job). Shared by every gear list so they cannot
/// disagree.
/// </remarks>
public sealed class JobFilter
{
    private readonly Dictionary<(uint Category, uint Job), bool> cache = [];

    public bool CanEquip(Item item)
    {
        var playerState = Plugin.PlayerState;

        // Between characters this would otherwise hide everything; showing too much is safer.
        if (!playerState.IsLoaded || !playerState.ClassJob.IsValid)
            return true;

        var jobRow = playerState.ClassJob.RowId;
        var categoryRow = item.ClassJobCategory.RowId;

        if (cache.TryGetValue((categoryRow, jobRow), out var cached))
            return cached;

        var allowed = true;
        if (item.ClassJobCategory.IsValid)
        {
            var abbreviation = playerState.ClassJob.Value.Abbreviation.ExtractText();
            var property = typeof(ClassJobCategory).GetProperty(
                abbreviation, BindingFlags.Public | BindingFlags.Instance);

            if (property != null && property.GetValue(item.ClassJobCategory.Value) is bool value)
                allowed = value;
        }

        cache[(categoryRow, jobRow)] = allowed;
        return allowed;
    }
}
