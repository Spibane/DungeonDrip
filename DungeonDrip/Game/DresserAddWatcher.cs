using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>A piece you are carrying that the collection has not got, and how to get at it.</summary>
public sealed record DresserAddRow(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ItemLevel,
    int SlotOrder,
    string SlotName,
    CollectionMarker Marker,
    bool JobEquippable,
    CarryLocation Location,
    int Quantity,
    bool ArmoireWouldTake,
    string? Blocked)
    : GearRow(ItemId, Name, IconId, ItemLevel, SlotOrder, SlotName, Marker, JobEquippable);

/// <summary>
/// Watches the Glamour Dresser and works out what you are carrying that is not in it.
/// </summary>
/// <remarks>
/// The dresser is the one surface where you can actually act on what the plugin knows, and it was
/// only ever a data source. This is the other half: not "what am I missing" but "what have I got on
/// me that should go in".
///
/// Everything runs from the panel's draw call, like the shop watcher, so it costs nothing when the
/// dresser is closed or the panel is off.
/// </remarks>
public sealed unsafe class DresserAddWatcher : IDisposable
{
    public const string AddonName = "MiragePrismPrismBox";

    private readonly Plugin plugin;

    private List<DresserAddRow>? rows;
    private int seenRowRevision = -1;
    private ulong seenInventory;
    private bool open;

    public DresserAddWatcher(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnClosed);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnClosed);
    }

    /// <summary>Slots in use and slots the box has, for the header. Zero when unread.</summary>
    public (int Used, int Capacity) Space =>
        plugin.Ownership.Current.Space is { } space ? (space.Used, space.Capacity) : (0, 0);

    /// <summary>
    /// What you are carrying that is not stored, or null when there is nothing to draw against.
    /// Call once per frame.
    /// </summary>
    public IReadOnlyList<DresserAddRow>? Resolve()
    {
        if (!plugin.Configuration.DresserPanel.Enabled || Plugin.GameGui.GameUiHidden)
            return null;

        if (!open)
            return Forget();

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (unit == null || !unit->IsVisible)
            return Forget();

        // The single most important guard here. The addon comes up before its contents arrive, and
        // a list built against an unloaded box would confidently claim every piece you own is
        // unstored - which is exactly the moment someone would act on it.
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return Forget();

        plugin.Rows.Revalidate();

        var inventory = InventoryReader.Fingerprint();
        if (rows != null && plugin.Rows.Revision == seenRowRevision && inventory == seenInventory)
            return rows;

        seenRowRevision = plugin.Rows.Revision;
        seenInventory = inventory;
        rows = Build();
        return rows;
    }

    private List<DresserAddRow>? Forget()
    {
        rows = null;
        return null;
    }

    private List<DresserAddRow> Build()
    {
        var carried = InventoryReader.ReadDetailed();
        var report = CarriedGear.Build(
            carried, plugin.Ownership.Current, plugin.Outfits, plugin.Storage);

        // Whether a piece is fully spiritbonded is per stack, and the report has collapsed them.
        var bonded = carried
            .GroupBy(stack => stack.ItemId)
            .ToDictionary(group => group.Key, group => group.Any(stack => stack.FullySpiritbonded));

        var built = new List<DresserAddRow>(report.NotStored.Count);

        foreach (var piece in report.NotStored)
        {
            // Through the shared factory, so job filtering and the marker vocabulary match every
            // other panel. Null here means the factory's scope filter excluded it.
            var row = plugin.Rows.Row(piece.ItemId);
            if (row == null)
                continue;

            built.Add(new DresserAddRow(
                row.ItemId,
                row.Name,
                row.IconId,
                row.ItemLevel,
                row.SlotOrder,
                row.SlotName,
                row.Marker,
                row.JobEquippable,
                piece.Location,
                piece.Quantity,
                piece.ArmoireWouldTake,
                BlockedReason(piece, bonded)));
        }

        Plugin.Log.Debug($"Dresser panel: {built.Count} carried pieces are not stored.");
        return built;
    }

    /// <summary>
    /// Why the box would refuse this piece today, if it would.
    /// </summary>
    /// <remarks>
    /// Only reasons that are legible from here. The dresser also refuses for reasons that are not
    /// readable, so a row without a note is "not stored" rather than "you can add this" - the panel
    /// words itself accordingly, and a few pieces the game turns down will still be listed.
    /// </remarks>
    private static string? BlockedReason(CarriedPiece piece, Dictionary<uint, bool> bonded)
    {
        if (piece.Location == CarryLocation.Saddlebag)
            return "Retrieve it from the saddlebag first.";

        if (piece.Location == CarryLocation.Equipped)
            return "Take it off first.";

        // Untradeable gear is exempt, and tradeability is not on the stack, so this can only be
        // raised as a possibility.
        if (bonded.TryGetValue(piece.ItemId, out var full) && !full)
            return "If it is tradeable, it has to be fully spiritbonded first.";

        return null;
    }

    private void OnOpened(AddonEvent type, AddonArgs args)
    {
        open = true;

        // Standing at a dresser is the one moment the snapshot can actually be refreshed.
        plugin.Ownership.RequestRefresh();
    }

    private void OnClosed(AddonEvent type, AddonArgs args) => open = false;
}
