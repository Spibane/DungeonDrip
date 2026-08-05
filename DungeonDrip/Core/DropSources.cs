using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Data;

namespace DungeonDrip.Core;

/// <summary>One duty a piece is known to drop in.</summary>
public sealed record DropSource(uint TerritoryId, string DutyName, byte Level, LootProvenance Provenance);

/// <summary>
/// The loot tables read backwards: which duties drop a given piece.
/// </summary>
/// <remarks>
/// Built beside the duty catalogue rather than inside the loot merge. The merge has these pairs for
/// free, but it can only emit bare territory ids, and everything asking this question wants a name
/// and a level to rank by - which the catalogue has already worked out one step later. The merge is
/// not the invalidation boundary either; the rebuild that produces the catalogue is.
///
/// <para><b>This index is as complete as the loot data, which is not very.</b> Coverage comes from
/// the downloaded dataset, plus whatever the wiki has been asked about - and the wiki is looked up
/// one duty at a time, only for duties that have actually been opened. For a dungeon new enough that
/// the dataset has a couple of entries and the wiki has the whole table, this will know almost nothing
/// until it has been viewed once. Anything drawing these results has to say "nothing in the loot
/// data lists this" and must never say "this does not drop anywhere".</para>
/// </remarks>
public sealed class DropSources
{
    private static readonly DropSource[] None = [];

    private readonly Dictionary<uint, DropSource[]> byItem;

    private DropSources(Dictionary<uint, DropSource[]> byItem) => this.byItem = byItem;

    /// <summary>Duties known to drop this piece, current content first. Empty when none are.</summary>
    public IReadOnlyList<DropSource> For(uint itemId) =>
        byItem.TryGetValue(itemId, out var sources) ? sources : None;

    public static DropSources Build(DungeonLootData loot, DutyCatalog duties)
    {
        var accumulated = new Dictionary<uint, List<DropSource>>();

        foreach (var territoryId in loot.Territories)
        {
            if (!loot.TryGetItems(territoryId, out var items))
                continue;

            // The catalogue is built from the same territories, so this all but always hits; NameOf
            // carries its own fallback for the case where it does not.
            var known = duties.TryGet(territoryId, out var duty);
            var name = known ? duty.Name : duties.NameOf(territoryId);
            var level = known ? duty.Level : (byte)0;

            foreach (var itemId in items)
            {
                if (!accumulated.TryGetValue(itemId, out var sources))
                    accumulated[itemId] = sources = [];

                sources.Add(new DropSource(territoryId, name, level, loot.ProvenanceOf(territoryId, itemId)));
            }
        }

        // Highest level first, matching the picker, so the answer leads with the duty someone is
        // most likely to actually be able to run for it.
        var byItem = accumulated.ToDictionary(
            entry => entry.Key,
            entry => entry.Value
                .OrderByDescending(source => source.Level)
                .ThenBy(source => source.DutyName, StringComparer.OrdinalIgnoreCase)
                .ToArray());

        Plugin.Log.Information(
            $"Indexed drop sources for {byItem.Count} pieces across {loot.DutyCount} duties");

        return new DropSources(byItem);
    }
}
