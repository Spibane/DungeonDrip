using System;
using System.Collections.Generic;
using System.Linq;
using GlamourAssistant.Data;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Core;

public sealed record DutyEntry(
    uint TerritoryId,
    string Name,
    byte Level,
    string Expansion,
    int ExpansionOrder,
    string ContentType,
    int ItemCount);

/// <summary>
/// Every duty we have loot for, named and sorted for the picker so a dungeon can be looked up
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

    public IEnumerable<DutyEntry> Search(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return Entries;

        return Entries.Where(e => e.Name.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    public static DutyCatalog Build(DungeonLootData loot)
    {
        // ContentFinderCondition is the only sheet that names a duty the way players do. Several rows
        // can point at one territory (unrestricted party variants and the like); the duty-finder row
        // carries the canonical name.
        var conditions = new Dictionary<uint, ContentFinderCondition>();
        foreach (var row in Plugin.DataManager.GetExcelSheet<ContentFinderCondition>())
        {
            var territory = row.TerritoryType.RowId;
            if (territory == 0 || row.Name.IsEmpty)
                continue;

            if (!conditions.TryGetValue(territory, out var existing) || (row.IsInDutyFinder && !existing.IsInDutyFinder))
                conditions[territory] = row;
        }

        var entries = new List<DutyEntry>();
        foreach (var territoryId in loot.Territories)
        {
            if (!loot.TryGetItems(territoryId, out var items))
                continue;

            if (conditions.TryGetValue(territoryId, out var cfc))
            {
                entries.Add(new DutyEntry(
                    territoryId,
                    Capitalise(cfc.Name.ExtractText()),
                    cfc.ClassJobLevelRequired,
                    cfc.RequiredExVersion.IsValid ? cfc.RequiredExVersion.Value.Name.ExtractText() : "Unknown",
                    (int)cfc.RequiredExVersion.RowId,
                    cfc.ContentType.IsValid ? cfc.ContentType.Value.Name.ExtractText() : "Duty",
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
                    "Unknown",
                    int.MaxValue,
                    "Duty",
                    items.Length));
            }
        }

        entries.Sort((a, b) =>
        {
            var byExpansion = a.ExpansionOrder.CompareTo(b.ExpansionOrder);
            if (byExpansion != 0) return byExpansion;

            var byLevel = a.Level.CompareTo(b.Level);
            return byLevel != 0 ? byLevel : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new DutyCatalog(entries);
    }

    /// <summary>Duty names are stored lowercase ("the Aery"); the UI reads better capitalised.</summary>
    private static string Capitalise(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..];
}
