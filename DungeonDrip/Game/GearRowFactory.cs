using System.Collections.Generic;
using Dalamud.Game.Player;
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
        // here, absence starts reading as "already collected" instead.
        if (kind == StorageKind.None || !storage.MatchesScope(kind, plugin.Configuration.Scope))
            return null;

        var view = plugin.Ownership.Current;
        var sets = plugin.Outfits.SetsContaining(itemId);
        var source = MissingItems.Resolve(
            itemId,
            view,
            sets,
            plugin.Configuration.OutfitOwnership,
            plugin.Configuration.Scope);

        // Meaningless for anything not held in a set, and walking a set's pieces is not free.
        var completed = source == OwnershipSource.Outfit && plugin.Outfits.IsInCompletedSet(itemId, view);

        // The other half of the same question: a piece the rule rejected while a stored outfit is
        // holding it, which is a shortfall to report rather than a piece to go and find.
        var (stored, total) = MissingItems.Shortfall(
            itemId, view, sets, source, plugin.Configuration.OutfitOwnership, plugin.Configuration.Scope);

        var (slotOrder, slotName) = EquipSlots.Describe(item.EquipSlotCategory.Value);

        return new GearRow(
            itemId,
            item.Name.ExtractText(),
            (ushort)item.Icon,
            slotOrder,
            slotName,
            CollectionMarkers.For(source, plugin.Ownership.HasDresserData, completed, stored > 0),
            plugin.JobFilter.CanEquip(item),
            plugin.EquipLocks.LocksOn(item),
            stored,
            total);
    }

    private MarkerContext Capture() => new(
        plugin.Ownership.Revision,
        plugin.Ownership.HasDresserData,
        plugin.Configuration.Scope,
        plugin.Configuration.OutfitOwnership,
        plugin.Configuration.CountInventoryAndEquipped,
        plugin.Configuration.CountRetainers,
        plugin.Configuration.CountRetainerEquipped,
        Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ClassJob.RowId : 0,
        Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.Sex : null,
        Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.Race.RowId : 0);

    /// <summary>
    /// Everything a cached row depends on. Compared by value once a frame, which keeps "what
    /// invalidates this" to a single line instead of several scattered checks.
    /// </summary>
    /// <remarks>
    /// Staleness is absent on purpose: it changes how a marker is drawn, not which marker it is, so
    /// an ageing snapshot never rebuilds the cache. The job, the gender and the race are here because
    /// wearability is baked into the row - the last two because a Fantasia mid-session would
    /// otherwise leave every cached row answering for the previous appearance.
    ///
    /// Neither of the filter toggles belongs here: they decide whether a row is drawn, not what it
    /// says, so flipping one must not cost a rebuild.
    /// </remarks>
    private readonly record struct MarkerContext(
        int OwnershipRevision,
        bool HasDresserData,
        CollectionScope Scope,
        OutfitOwnershipMode OutfitMode,
        bool CountInventory,
        bool CountRetainers,
        bool CountRetainerEquipped,
        uint ClassJob,
        Sex? Sex,
        uint Race);
}
