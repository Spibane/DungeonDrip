using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using Dalamud.Utility;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>One piece of a vendor's stock, resolved and ready to draw.</summary>
public sealed record VendorRow(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ItemLevel,
    int SlotOrder,
    string SlotName,
    VendorMarker Marker,
    bool JobEquippable)
{
    public bool IsOwned => Marker is not (VendorMarker.NotCollected or VendorMarker.Unknown);
}

/// <summary>
/// Tracks which vendor window is open and what it is selling, and answers the ownership question
/// for each piece of that stock.
/// </summary>
/// <remarks>
/// Discovery is belt and braces. Lifecycle events are the fast path, but a slow sweep of the
/// registry runs too, so the panel still appears if one never fires. The stock is compared in full
/// every frame rather than trusted to a refresh event, because a category dropdown may not raise one
/// and a panel listing the previous category would be quietly wrong.
///
/// Nothing runs on the framework tick: the panel is the only consumer, so this costs nothing when it
/// is switched off.
/// </remarks>
public sealed class ShopWatcher : IDisposable
{
    /// <summary>Roughly twice a second at 60fps. Only runs while no shop is known to be open.</summary>
    private const int SweepIntervalFrames = 30;

    private readonly Plugin plugin;
    private readonly HashSet<string> warnedAddons = [];

    /// <summary>Reused every frame so the read path allocates nothing.</summary>
    private readonly List<uint> scratch = new(256);

    /// <summary>Null values are remembered too - "not glamour gear" is worth caching.</summary>
    private readonly Dictionary<uint, VendorRow?> rowCache = [];

    private ShopAddonDescriptor? active;
    private string? loggedFor;
    private uint[] currentIds = [];
    private List<VendorRow>? stock;
    private MarkerContext context;
    private int framesUntilSweep;

    public ShopWatcher(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, ShopAddons.Names, OnShopOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, ShopAddons.Names, OnShopClosed);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, ShopAddons.Names, OnShopOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, ShopAddons.Names, OnShopClosed);
    }

    /// <summary>The vendor addon believed to be open, for the panel to position itself against.</summary>
    public string? ActiveAddonName => active?.AddonName;

    /// <summary>
    /// The stock in front of the player, or null when there is no vendor open or it could not be
    /// read. Call once per frame.
    /// </summary>
    public unsafe IReadOnlyList<VendorRow>? Resolve()
    {
        if (!plugin.Configuration.ShowVendorPanel || Plugin.GameGui.GameUiHidden)
            return null;

        var descriptor = FindActive();
        if (descriptor == null)
            return Forget();

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(descriptor.AddonName);
        if (unit == null)
        {
            // Genuinely gone. Drop it so the sweep can find whatever opened instead.
            active = null;
            loggedFor = null;
            return Forget();
        }

        // Invisible is not gone - shops flicker through it while opening - so keep it claimed.
        if (!unit->IsVisible)
            return Forget();

        if (!ShopItemReader.TryRead(descriptor, unit, scratch))
        {
            WarnOnce(descriptor, unit);
            return Forget();
        }

        // Ownership or the settings behind it moved, so every cached answer is suspect.
        var fresh = CaptureContext();
        if (fresh != context)
        {
            context = fresh;
            rowCache.Clear();
            stock = null;
        }

        if (stock != null && StockUnchanged())
            return stock;

        currentIds = [.. scratch];
        stock = Build(descriptor, currentIds);
        return stock;
    }

    /// <summary>Reports what the plugin can see of the open vendor, for <c>/dungeondrip shop</c>.</summary>
    public unsafe string Describe()
    {
        var name = FindVisibleAddonName();
        if (name == null)
            return "Dungeon Drip: no vendor window is open.";

        if (!ShopAddons.TryGet(name, out var descriptor))
        {
            Plugin.Log.Information($"Vendor diagnostics: {name} is open but is not in the registry.");
            return $"Dungeon Drip: \"{name}\" is open but is not a vendor Dungeon Drip knows. " +
                   "Please report that addon name.";
        }

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return $"Dungeon Drip: \"{name}\" vanished while being inspected.";

        // Its own buffer rather than the per-frame one, so a diagnostic can never disturb the panel.
        var found = new List<uint>();
        var read = ShopItemReader.TryRead(descriptor, unit, found);
        var preview = string.Join(", ", found.GetRange(0, Math.Min(3, found.Count)));

        Plugin.Log.Information(
            $"Vendor diagnostics: {name}, source {descriptor.Source}, {unit->AtkValuesCount} AtkValues " +
            $"(expected {descriptor.ExpectedValueCount}), read {(read ? "ok" : "FAILED")}, " +
            $"{found.Count} ids, first: {(preview.Length > 0 ? preview : "none")}");

        return read
            ? $"Dungeon Drip: \"{name}\" read {found.Count} items. Details in the log."
            : $"Dungeon Drip: could not read \"{name}\" - its layout has changed. Details in the log.";
    }

    private List<VendorRow>? Forget()
    {
        stock = null;
        return null;
    }

    private ShopAddonDescriptor? FindActive()
    {
        if (active != null)
            return active;

        if (--framesUntilSweep > 0)
            return null;

        framesUntilSweep = SweepIntervalFrames;

        var name = FindVisibleAddonName();
        if (name == null || !ShopAddons.TryGet(name, out var descriptor))
            return null;

        active = descriptor;
        return descriptor;
    }

    private unsafe string? FindVisibleAddonName()
    {
        foreach (var name in ShopAddons.Names)
        {
            var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
            if (unit != null && unit->IsVisible)
                return name;
        }

        return null;
    }

    /// <summary>
    /// A full comparison rather than a hash: at these sizes it costs the same and cannot collide,
    /// and swapping two categories of equal length is exactly the case this has to catch.
    /// </summary>
    private bool StockUnchanged()
    {
        if (scratch.Count != currentIds.Length)
            return false;

        for (var i = 0; i < currentIds.Length; i++)
        {
            if (currentIds[i] != scratch[i])
                return false;
        }

        return true;
    }

    private List<VendorRow> Build(ShopAddonDescriptor descriptor, uint[] ids)
    {
        var rows = new List<VendorRow>(ids.Length);
        var seen = new HashSet<uint>(ids.Length);
        var notCollected = 0;

        foreach (var id in ids)
        {
            // HQ and NQ stock collapse onto one base id, and some shops list a piece under more
            // than one heading.
            if (!seen.Add(id))
                continue;

            var row = Resolve(id);
            if (row == null)
                continue;

            rows.Add(row);
            if (row.Marker == VendorMarker.NotCollected)
                notCollected++;
        }

        var summary =
            $"Vendor {descriptor.AddonName}: {ids.Length} items, {rows.Count} glamour-eligible, " +
            $"{notCollected} not collected";

        // Once per vendor at Information - the line that makes a bug report useful. Rebuilds after
        // that drop to Debug, so a jittering value cannot flood the log.
        if (loggedFor != descriptor.AddonName)
        {
            loggedFor = descriptor.AddonName;
            Plugin.Log.Information(summary);
        }
        else
        {
            Plugin.Log.Debug(summary);
        }

        return rows;
    }

    private VendorRow? Resolve(uint itemId)
    {
        if (rowCache.TryGetValue(itemId, out var cached))
            return cached;

        var row = BuildRow(itemId);
        rowCache[itemId] = row;
        return row;
    }

    private VendorRow? BuildRow(uint itemId)
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

        return new VendorRow(
            itemId,
            item.Name.ExtractText(),
            (ushort)item.Icon,
            (ushort)item.LevelItem.RowId,
            slotOrder,
            slotName,
            VendorMarkers.For(source, plugin.Ownership.HasDresserData, completed),
            plugin.JobFilter.CanEquip(item));
    }

    private MarkerContext CaptureContext() => new(
        plugin.Ownership.Revision,
        plugin.Ownership.HasDresserData,
        plugin.Configuration.Scope,
        plugin.Configuration.OutfitOwnership,
        plugin.Configuration.CountInventoryAndEquipped,
        Plugin.PlayerState.IsLoaded ? Plugin.PlayerState.ClassJob.RowId : 0);

    private unsafe void WarnOnce(ShopAddonDescriptor descriptor, AtkUnitBase* unit)
    {
        if (!warnedAddons.Add(descriptor.AddonName))
            return;

        Plugin.Log.Warning(
            $"Could not read stock from {descriptor.AddonName} (source {descriptor.Source}, " +
            $"{unit->AtkValuesCount} AtkValues, expected {descriptor.ExpectedValueCount}). " +
            "No vendor panel will be shown for this shop.");
    }

    private void OnShopOpened(AddonEvent type, AddonArgs args)
    {
        if (!ShopAddons.TryGet(args.AddonName, out var descriptor))
            return;

        active = descriptor;

        // Cannot conjure a dresser up from a town, but does pick up an armoire opened at an inn.
        plugin.Ownership.RequestRefresh();
    }

    private void OnShopClosed(AddonEvent type, AddonArgs args)
    {
        if (args.AddonName != active?.AddonName)
            return;

        active = null;

        loggedFor = null;
    }

    /// <summary>
    /// Everything a cached row's marker depends on. Compared by value once a frame, which keeps
    /// "what invalidates this" to a single line instead of five scattered checks.
    /// </summary>
    /// <remarks>
    /// Staleness is absent on purpose: it changes how a marker is drawn, not which marker it is, so
    /// an aging snapshot never rebuilds the cache. The job is here because wearability is baked into
    /// the row.
    /// </remarks>
    private readonly record struct MarkerContext(
        int OwnershipRevision,
        bool HasDresserData,
        CollectionScope Scope,
        OutfitOwnershipMode OutfitMode,
        bool CountInventory,
        uint ClassJob);
}
