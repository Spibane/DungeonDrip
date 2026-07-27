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
    OwnershipSource Source,
    LootProvenance Provenance,
    int RoleOrder,
    string RoleGroup)
{
    public bool IsOwned => Source != OwnershipSource.None;
}

public sealed record DutyReport(
    uint TerritoryId,
    string Name,
    IReadOnlyList<ReportItem> Items,
    int MissingCount,
    int TotalCount,
    int HiddenByJobFilter,
    int HiddenWeapons);

/// <summary>Turns a territory plus an ownership snapshot into the list the window draws.</summary>
public sealed class DutyReportBuilder(
    DungeonLootData loot,
    DutyCatalog catalog,
    OutfitCatalog outfits,
    JobRoleIndex jobRoles,
    StorageEligibility storage)
{
    private readonly Dictionary<(uint Category, uint Job), bool> jobFilterCache = [];

    public DutyReport? Build(uint territoryId, OwnershipView ownership, Configuration configuration)
    {
        if (!loot.TryGetItems(territoryId, out var itemIds))
            return null;

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var results = new List<ReportItem>(itemIds.Length);
        var hiddenByJob = 0;
        var hiddenWeapons = 0;

        foreach (var itemId in itemIds)
        {
            if (!items.TryGetRow(itemId, out var item))
                continue;

            // Only list what the store being compared against can actually hold.
            if (!storage.MatchesScope(storage.Of(item), configuration.Scope))
                continue;

            if (configuration.OnlyCurrentJobEquippable && !CurrentJobCanEquip(item))
            {
                hiddenByJob++;
                continue;
            }

            var (order, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);

            // Weapons are the bulk of a dungeon's list and are often not what people are hunting.
            if (configuration.HideWeapons && EquipSlots.IsWeaponSlot(order))
            {
                hiddenWeapons++;
                continue;
            }

            var source = MissingItems.Resolve(
                itemId, ownership, outfits.SetsContaining(itemId), configuration.OutfitOwnership,
                configuration.Scope);

            var (roleOrder, roleGroup) = jobRoles.GroupFor(item.ClassJobCategory.RowId);

            results.Add(new ReportItem(
                itemId,
                item.Name.ExtractText(),
                item.Icon,
                (ushort)item.LevelItem.RowId,
                order,
                slotName,
                source,
                loot.ProvenanceOf(territoryId, itemId),
                roleOrder,
                roleGroup));
        }

        // Grouping drives the primary sort so the window can just walk the list in order.
        var byRole = configuration.Grouping == MissingGrouping.Role;
        results.Sort((a, b) =>
        {
            if (byRole)
            {
                var byRoleOrder = a.RoleOrder.CompareTo(b.RoleOrder);
                if (byRoleOrder != 0) return byRoleOrder;
            }

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
            hiddenByJob,
            hiddenWeapons);
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
