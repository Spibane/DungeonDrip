using System.Collections.Generic;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>Everything that changes the shape of a panel's list, compared by value in one go.</summary>
public readonly record struct ViewOptions(
    bool ShowOwned,
    bool JobOnly,
    bool GenderOnly,
    bool RaceOnly,
    bool HideWeapons,
    bool GroupBySlot);

/// <summary>One heading's worth of rows.</summary>
public sealed class SlotGroup(string label, int order)
{
    public string Label { get; } = label;

    public int Order { get; } = order;

    public List<GearRow> Rows { get; } = [];

    public int NotCollected { get; set; }

    /// <summary>Owned rows filtered out, so the heading can still account for them.</summary>
    public int Hidden { get; set; }
}

/// <summary>
/// Buckets a panel's rows into the headings it draws them under.
/// </summary>
/// <remarks>
/// Shared by every addon-anchored panel, so the filters cannot come to mean different things on
/// different surfaces. The memoisation stays with the caller, since what it remembers is that one
/// window's last frame.
/// </remarks>
internal static class PanelGrouping
{
    /// <summary>Refills <paramref name="groups"/> in place; returns how many rows the filters dropped.</summary>
    public static int Regroup(IReadOnlyList<GearRow> fresh, in ViewOptions options, List<SlotGroup> groups)
    {
        groups.Clear();
        var hiddenByFilter = 0;

        var byLabel = new Dictionary<string, SlotGroup>();

        foreach (var row in fresh)
        {
            // Dropped, not counted: these say nothing about your collection, only that you asked
            // not to see them.
            if (options.HideWeapons && EquipSlots.IsWeaponSlot(row.SlotOrder))
            {
                hiddenByFilter++;
                continue;
            }

            if (options.JobOnly && !row.JobEquippable)
            {
                hiddenByFilter++;
                continue;
            }

            if (EquipLockFilter.Excludes(row.Locks, options.GenderOnly, options.RaceOnly))
            {
                hiddenByFilter++;
                continue;
            }

            var hidden = !options.ShowOwned && row.IsOwned;

            // One bucket when flat, so the same drawing path serves both layouts.
            var label = options.GroupBySlot ? row.SlotName : string.Empty;
            var order = options.GroupBySlot ? row.SlotOrder : 0;

            if (!byLabel.TryGetValue(label, out var group))
            {
                group = new SlotGroup(label, order);
                byLabel[label] = group;
                groups.Add(group);
            }

            if (hidden)
            {
                group.Hidden++;
                continue;
            }

            group.Rows.Add(row);
            if (CollectionMarkers.IsMissing(row.Marker))
                group.NotCollected++;
        }

        groups.RemoveAll(group => group.Rows.Count == 0);
        groups.Sort((a, b) => a.Order.CompareTo(b.Order));

        return hiddenByFilter;
    }
}
