using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GlamourAssistant.Data;
using GlamourAssistant.Game;
using Lumina.Excel.Sheets;

namespace GlamourAssistant.Core;

public sealed record ReportItem(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ItemLevel,
    int SlotOrder,
    string SlotName,
    OwnershipSource Source)
{
    public bool IsOwned => Source != OwnershipSource.None;
}

public sealed record DutyReport(
    uint TerritoryId,
    string Name,
    IReadOnlyList<ReportItem> Items,
    int MissingCount,
    int TotalCount,
    int HiddenByJobFilter);

/// <summary>Turns a territory plus an ownership snapshot into the list the window draws.</summary>
public sealed class DutyReportBuilder(DungeonLootData loot, DutyCatalog catalog, OutfitCatalog outfits)
{
    private readonly Dictionary<(uint Category, uint Job), bool> jobFilterCache = [];

    public DutyReport? Build(uint territoryId, OwnershipView ownership, Configuration configuration)
    {
        if (!loot.TryGetItems(territoryId, out var itemIds))
            return null;

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var results = new List<ReportItem>(itemIds.Length);
        var hidden = 0;

        foreach (var itemId in itemIds)
        {
            if (!items.TryGetRow(itemId, out var item))
                continue;

            if (configuration.OnlyCurrentJobEquippable && !CurrentJobCanEquip(item))
            {
                hidden++;
                continue;
            }

            var source = MissingItems.Resolve(
                itemId, ownership, outfits.SetsContaining(itemId), configuration.OutfitOwnership);

            var (order, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);

            results.Add(new ReportItem(
                itemId,
                item.Name.ExtractText(),
                item.Icon,
                (ushort)item.LevelItem.RowId,
                order,
                slotName,
                source));
        }

        results.Sort((a, b) =>
        {
            var bySlot = a.SlotOrder.CompareTo(b.SlotOrder);
            if (bySlot != 0) return bySlot;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new DutyReport(
            territoryId,
            catalog.NameOf(territoryId),
            results,
            results.Count(r => !r.IsOwned),
            results.Count,
            hidden);
    }

    /// <summary>
    /// ClassJobCategory exposes one boolean column per job abbreviation, so the check is a reflected
    /// property read - cached, because it runs once per item per frame otherwise.
    /// </summary>
    private bool CurrentJobCanEquip(Item item)
    {
        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded || !playerState.ClassJob.IsValid)
            return true;

        var jobRow = playerState.ClassJob.RowId;
        var categoryRow = item.ClassJobCategory.RowId;

        if (jobFilterCache.TryGetValue((categoryRow, jobRow), out var cached))
            return cached;

        var allowed = true;
        if (item.ClassJobCategory.IsValid)
        {
            var abbreviation = playerState.ClassJob.Value.Abbreviation.ExtractText();
            var property = typeof(ClassJobCategory).GetProperty(abbreviation, BindingFlags.Public | BindingFlags.Instance);
            if (property != null && property.GetValue(item.ClassJobCategory.Value) is bool value)
                allowed = value;
        }

        jobFilterCache[(categoryRow, jobRow)] = allowed;
        return allowed;
    }
}
