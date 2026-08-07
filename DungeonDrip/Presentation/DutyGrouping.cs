using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>One heading's rows in the duty list, counting its own missing pieces.</summary>
/// <remarks>
/// The seen set is what makes the covering pass in <see cref="DutyGrouping.Build"/> safe: a piece
/// can reach a heading both as its own role and by way of a broader heading that covers it, and a
/// heading claiming the same piece twice would double its count as well as list it twice.
/// </remarks>
internal sealed class DutyBucket(RoleGroup group)
{
    private readonly HashSet<uint> seen = [];

    public RoleGroup Group { get; } = group;

    public List<ReportItem> Items { get; } = [];

    public int MissingCount { get; private set; }

    public void Add(ReportItem item)
    {
        if (!seen.Add(item.ItemId))
            return;

        Items.Add(item);
        if (!item.IsOwned)
            MissingCount++;
    }
}

/// <summary>Buckets a duty's pieces into the headings they are drawn under.</summary>
/// <remarks>
/// Which heading a piece belongs under is a claim about the gear, not about a window, and the role
/// summary and the item list both ask for it - so it cannot live in either. It is also the one
/// piece of grouping in the plugin that deliberately duplicates, which is worth only having to be
/// right about once.
/// </remarks>
internal static class DutyGrouping
{
    /// <summary>Heading for pieces no source is known for, and the order that puts it last.</summary>
    public const string UnattributedLabel = "Not attributed to a boss or coffer";

    public const int UnattributedOrder = int.MaxValue;

    /// <remarks>
    /// Role view deliberately duplicates. It answers "what can I claim if I queue as this", so a
    /// piece belongs under every heading whose jobs can roll on it: "of Slaying" accessories show
    /// under their own MNK DRG SAM RPR heading and again under Maiming and Striking, and "of Aiming"
    /// under both Physical Ranged and Scouting. Slot view never duplicates and stays the true list,
    /// which is where the counts in the summary come from.
    ///
    /// Broader headings are only folded into narrower ones that some item already created in this
    /// duty. A blind subset rule would conjure a "Melee DPS (DRG)" heading out of a legacy sheet row
    /// and fill it with nothing but shared accessories.
    ///
    /// Source view duplicates for its own reason: a piece in two coffers is genuinely in both, and the
    /// question being asked - what comes out of this - wants it under each. Pieces nothing attributes
    /// share one heading at the end, which on a duty with no wiki lookup is the whole list.
    /// </remarks>
    public static List<DutyBucket> Build(IReadOnlyList<ReportItem> items, MissingGrouping grouping)
    {
        var byRole = grouping == MissingGrouping.Role;
        var buckets = new Dictionary<string, DutyBucket>();

        DutyBucket Ensure(RoleGroup group)
        {
            if (!buckets.TryGetValue(group.Label, out var bucket))
                buckets[group.Label] = bucket = new DutyBucket(group);

            return bucket;
        }

        foreach (var item in items)
        {
            switch (grouping)
            {
                case MissingGrouping.Role:
                    foreach (var group in item.RoleGroups)
                        Ensure(group).Add(item);

                    break;

                case MissingGrouping.Source when item.Origins.Count > 0:
                    foreach (var origin in item.Origins)
                        Ensure(new RoleGroup(origin.Order, origin.Label, string.Empty)).Add(item);

                    break;

                case MissingGrouping.Source:
                    Ensure(new RoleGroup(UnattributedOrder, UnattributedLabel, string.Empty)).Add(item);
                    break;

                default:
                    Ensure(new RoleGroup(item.SlotOrder, item.SlotName, string.Empty)).Add(item);
                    break;
            }
        }

        if (byRole)
        {
            foreach (var bucket in buckets.Values.ToList())
            {
                foreach (var item in items)
                {
                    if (item.RoleGroups.Any(group => bucket.Group.IsCoveredBy(group)))
                        bucket.Add(item);
                }
            }
        }

        return [.. buckets.Values
            .OrderBy(b => b.Group.Order)
            .ThenBy(b => b.Group.Label, StringComparer.OrdinalIgnoreCase)];
    }
}
