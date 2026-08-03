using System;
using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Data;
using DungeonDrip.Game;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

public sealed record ReportItem(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ItemLevel,
    int SlotOrder,
    string SlotName,
    OwnershipSource Source,
    LootProvenance Provenance,
    IReadOnlyList<RoleGroup> RoleGroups)
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
    int HiddenWeapons,
    int HiddenUnwearable = 0);

/// <summary>Turns a territory plus an ownership snapshot into the list the window draws.</summary>
public sealed class DutyReportBuilder(
    DungeonLootData loot,
    DutyCatalog catalog,
    OutfitCatalog outfits,
    JobRoleIndex jobRoles,
    StorageEligibility storage,
    JobFilter jobFilter,
    EquipLockFilter equipLocks)
{
    public DutyReport? Build(uint territoryId, OwnershipView ownership, Configuration configuration)
    {
        if (!loot.TryGetItems(territoryId, out var itemIds))
            return null;

        var items = Plugin.DataManager.GetExcelSheet<Item>();
        var results = new List<ReportItem>(itemIds.Length);
        var hiddenByJob = 0;
        var hiddenUnwearable = 0;
        var hiddenWeapons = 0;

        foreach (var itemId in itemIds)
        {
            if (!items.TryGetRow(itemId, out var item))
                continue;

            // Only list what the store being compared against can actually hold.
            if (!storage.MatchesScope(storage.Of(item), configuration.Scope))
                continue;

            if (configuration.OnlyCurrentJobEquippable && !jobFilter.CanEquip(item))
            {
                hiddenByJob++;
                continue;
            }

            // Counted separately from the job filter. The two are not the same news: one is "wrong
            // job today", the other is "this character will never wear it".
            if (equipLocks.Hides(item, configuration))
            {
                hiddenUnwearable++;
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

            var roleGroups = jobRoles.GroupsFor(item.ClassJobCategory.RowId);

            results.Add(new ReportItem(
                itemId,
                item.Name.ExtractText(),
                item.Icon,
                (ushort)item.LevelItem.RowId,
                order,
                slotName,
                source,
                loot.ProvenanceOf(territoryId, itemId),
                roleGroups));
        }

        // One entry per piece, always slot-sorted. The window buckets into headings, because with
        // shared gear a piece can belong to more than one role heading and a flat sort cannot say so.
        results.Sort((a, b) =>
        {
            var bySlot = a.SlotOrder.CompareTo(b.SlotOrder);
            return bySlot != 0 ? bySlot : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });

        return new DutyReport(
            territoryId,
            catalog.NameOf(territoryId),
            results,
            results.Count(r => !r.IsOwned),
            results.Count,
            hiddenByJob,
            hiddenWeapons,
            hiddenUnwearable);
    }
}
