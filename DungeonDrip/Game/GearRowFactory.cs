using System.Collections.Generic;
using Dalamud.Utility;
using DungeonDrip.Core;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>
/// Turns an item id into a drawable row, once, and remembers the answer.
/// </summary>
/// <remarks>
/// Every panel asks the same question of the same collection, so they share one cache: a piece
/// looked up at a vendor costs nothing when it turns up again at the market board.
///
/// <see cref="Revision"/> is what makes one cache safe for several readers.
/// <see cref="Revalidate"/> is idempotent and order-independent - it recomputes a handful of cheap
/// reads and compares - so every surface can call it once a frame in any order, and each keeps its
/// own note of the revision it last built against.
///
/// Draw thread only. There is no locking here and the panels are the only callers.
/// </remarks>
public sealed class GearRowFactory(Plugin plugin)
{
    /// <summary>Null values are remembered too - "not glamour gear" is worth caching.</summary>
    private readonly Dictionary<uint, GearRow?> cache = [];

    private MarkerContext context;

    /// <summary>Bumped whenever the cache is dropped, so each surface can rebuild its own list.</summary>
    public int Revision { get; private set; }

    /// <summary>Drops every cached row if anything a marker depends on has moved.</summary>
    public void Revalidate()
    {
        var fresh = Capture();
        if (fresh == context)
            return;

        context = fresh;
        cache.Clear();
        Revision++;
    }

    /// <summary>The row for a piece, or null when it is not gear that can be kept as a glamour.</summary>
    public GearRow? Row(uint itemId)
    {
        if (cache.TryGetValue(itemId, out var cached))
            return cached;

        var row = Build(itemId);
        cache[itemId] = row;
        return row;
    }

    private GearRow? Build(uint itemId)
    {
        if (!Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item))
            return null;

        var storage = plugin.Storage;
        var kind = storage.Of(item);

        // Absence of a row must mean exactly one thing: "not glamour gear". If a dye ever appears
        // here, absence starts reading as "you already have it" instead.
        if (kind == StorageKind.None || !storage.MatchesScope(kind, plugin.Configuration.Scope))
            return null;

        var view = plugin.Ownership.Current;
        var source = MissingItems.Resolve(
            itemId,
            view,
            plugin.Outfits.SetsContaining(itemId),
            plugin.Configuration.OutfitOwnership,
            plugin.Configuration.Scope);

        // Meaningless for anything not held in a set, and walking a set's pieces is not free.
        var completed = source == OwnershipSource.Outfit && plugin.Outfits.IsInCompletedSet(itemId, view);

        var (slotOrder, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);

        return new GearRow(
            itemId,
            item.Name.ExtractText(),
            (ushort)item.Icon,
            (ushort)item.LevelItem.RowId,
            slotOrder,
            slotName,
            CollectionMarkers.For(source, plugin.Ownership.HasDresserData, completed),
            plugin.JobFilter.CanEquip(item));
    }

    private MarkerContext Capture() => new(
        plugin.Ownership.Revision,
        plugin.Ownership.HasDresserData,
        plugin.Configuration.Scope,
        plugin.Configuration.OutfitOwnership,
        plugin.Configuration.CountInventoryAndEquipped,
        Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ClassJob.RowId : 0);

    /// <summary>
    /// Everything a cached row depends on. Compared by value once a frame, which keeps "what
    /// invalidates this" to a single line instead of several scattered checks.
    /// </summary>
    /// <remarks>
    /// Staleness is absent on purpose: it changes how a marker is drawn, not which marker it is, so
    /// an ageing snapshot never rebuilds the cache. The job is here because wearability is baked
    /// into the row.
    /// </remarks>
    private readonly record struct MarkerContext(
        int OwnershipRevision,
        bool HasDresserData,
        CollectionScope Scope,
        OutfitOwnershipMode OutfitMode,
        bool CountInventory,
        uint ClassJob);
}
