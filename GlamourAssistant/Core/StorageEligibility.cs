using System.Collections.Generic;
using Lumina.Excel.Sheets;
using CabinetSheet = Lumina.Excel.Sheets.Cabinet;

namespace GlamourAssistant.Core;

/// <summary>Where a piece can be kept. A piece is commonly accepted by both stores.</summary>
[System.Flags]
public enum StorageKind
{
    /// <summary>Cannot be kept as a glamour at all, so not worth listing.</summary>
    None = 0,

    /// <summary>Accepted by the Glamour Dresser.</summary>
    Dresser = 1,

    /// <summary>Accepted by the Armoire.</summary>
    Armoire = 2,
}

/// <summary>
/// Decides whether a piece can actually be kept, and where.
/// </summary>
/// <remarks>
/// The two stores overlap - they are not alternatives. Which items the Armoire accepts is decided
/// solely by the game's Cabinet sheet, read in full at load. Square Enix keeps adding to that sheet,
/// so nothing here encodes a level, an expansion or a set name: whatever the sheet says on the day
/// is what the plugin believes, and a patch that adds sets takes effect on the next game start.
///
/// For orientation only, and true when this was written rather than a rule to rely on: every
/// Dawntrail dungeon set was present and no Endwalker or earlier one was, which put the boundary
/// between equip Lv90 and Lv91. Do not turn that into a check - it will drift.
///
/// One known gap: relic weapons cannot be stored in the Dresser and no sheet column marks them.
/// They are not dungeon drops, so it does not affect what this plugin lists.
/// </remarks>
public sealed class StorageEligibility
{
    private readonly HashSet<uint> armoireItems;

    private StorageEligibility(HashSet<uint> armoireItems) => this.armoireItems = armoireItems;

    public static StorageEligibility Build()
    {
        var armoire = new HashSet<uint>();
        foreach (var row in Plugin.DataManager.GetExcelSheet<CabinetSheet>())
        {
            var itemId = row.Item.RowId;
            if (itemId != 0)
                armoire.Add(itemId);
        }

        Plugin.Log.Information($"Armoire accepts {armoire.Count} items");
        return new StorageEligibility(armoire);
    }

    public StorageKind Of(uint itemId) =>
        Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item)
            ? Of(item)
            : StorageKind.None;

    public StorageKind Of(Item item)
    {
        if (item.EquipSlotCategory.RowId == 0 || !item.EquipSlotCategory.IsValid)
            return StorageKind.None;

        if (item.EquipSlotCategory.Value.SoulCrystal != 0)
            return StorageKind.None;

        // Anything wearable goes in the Dresser; the Armoire is an additional home for the subset
        // the Cabinet sheet lists, not an alternative to it.
        var kind = StorageKind.Dresser;
        if (armoireItems.Contains(item.RowId))
            kind |= StorageKind.Armoire;

        return kind;
    }

    public bool CanBeStored(uint itemId) => Of(itemId) != StorageKind.None;

    /// <summary>Whether a piece belongs in the list given what the user is comparing against.</summary>
    public bool MatchesScope(StorageKind kind, CollectionScope scope) => scope switch
    {
        CollectionScope.DresserOnly => kind.HasFlag(StorageKind.Dresser),
        CollectionScope.ArmoireOnly => kind.HasFlag(StorageKind.Armoire),
        _ => kind != StorageKind.None,
    };
}
