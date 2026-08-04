using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Data;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>One duty the plugin has loot for, as the picker lists it.</summary>
/// <param name="Level">
/// The level the duty finder asks for, or zero for a duty with no duty-finder row at all - which is
/// what sinks those to the bottom of the list rather than letting them lead it.
/// </param>
/// <param name="ItemCount">How many pieces are known to drop here, before any filter is applied.</param>
public sealed record DutyEntry(
    uint TerritoryId,
    string Name,
    byte Level,
    int ExpansionOrder,
    int ItemCount);

/// <summary>
/// Every duty with loot data, named and sorted for the picker so a dungeon can be looked up
/// without setting foot in it.
/// </summary>
public sealed class DutyCatalog
{
    private readonly Dictionary<uint, DutyEntry> byTerritory;

    public IReadOnlyList<DutyEntry> Entries { get; }

    private DutyCatalog(IReadOnlyList<DutyEntry> entries)
    {
        Entries = entries;
        byTerritory = entries.ToDictionary(e => e.TerritoryId);
    }

    public bool TryGet(uint territoryId, out DutyEntry entry) => byTerritory.TryGetValue(territoryId, out entry!);

    public string NameOf(uint territoryId) =>
        byTerritory.TryGetValue(territoryId, out var entry) ? entry.Name : $"Territory {territoryId}";

    /// <summary>
    /// Duties whose name contains the query, or all of them for an empty one.
    /// </summary>
    /// <remarks>
    /// A substring match rather than a prefix, because half these names begin with "the". The cost
    /// is that a piece of gear whose name appears inside a duty's is unreachable through this, which
    /// is what <c>/dungeondrip item</c> exists to get past.
    /// </remarks>
    public IEnumerable<DutyEntry> Search(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return Entries;

        return Entries.Where(e => e.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static DutyCatalog Build(DungeonLootData loot, ContentFinderIndex conditions)
    {
        var entries = new List<DutyEntry>();
        foreach (var territoryId in loot.Territories)
        {
            if (!loot.TryGetItems(territoryId, out var items))
                continue;

            if (conditions.TryGet(territoryId, out var cfc))
            {
                entries.Add(new DutyEntry(
                    territoryId,
                    Capitalise(cfc.Name.ExtractText()),
                    cfc.ClassJobLevelRequired,
                    (int)cfc.RequiredExVersion.RowId,
                    items.Length));
            }
            else
            {
                // No duty-finder entry (deep dungeons, some story instances) - fall back to the
                // dataset's own English name so the duty is still selectable.
                entries.Add(new DutyEntry(
                    territoryId,
                    loot.GetFallbackName(territoryId) ?? $"Territory {territoryId}",
                    0,
                    int.MaxValue,
                    items.Length));
            }
        }

        // Highest level first, so current content is at the top where it is wanted. Duties with no
        // duty-finder row have no level, so they sink to the bottom rather than leading the list.
        entries.Sort((a, b) =>
        {
            var aLevel = a.Level == 0 ? int.MinValue : a.Level;
            var bLevel = b.Level == 0 ? int.MinValue : b.Level;

            var byLevel = bLevel.CompareTo(aLevel);
            if (byLevel != 0) return byLevel;

            var byExpansion = a.ExpansionOrder.CompareTo(b.ExpansionOrder);
            return byExpansion != 0 ? byExpansion : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new DutyCatalog(entries);
    }

    /// <summary>Duty names are stored lowercase ("the Aery"); the UI reads better capitalised.</summary>
    private static string Capitalise(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
