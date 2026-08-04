using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>A carried piece the Glamour Dresser has not got, and how to get at it.</summary>
/// <param name="ArmoireWouldTake">
/// The one thing this panel knows that the Armoire's does not: the piece has a home that costs no
/// dresser slot, which is worth saying while somebody is deciding what to spend a slot on.
/// </param>
public sealed record DresserAddRow(
    uint ItemId,
    string Name,
    ushort IconId,
    int SlotOrder,
    string SlotName,
    CollectionMarker Marker,
    bool JobEquippable,
    EquipLock Locks,
    CarryLocation Location,
    int Quantity,
    string? Blocked,
    bool ArmoireWouldTake)
    : HeldGearRow(
        ItemId, Name, IconId, SlotOrder, SlotName, Marker, JobEquippable, Locks,
        Location, Quantity, Blocked);

/// <summary>
/// Watches the Glamour Dresser and works out which carried pieces are not in it.
/// </summary>
/// <remarks>
/// The dresser is the one surface where the plugin's answer can be acted on where it is read, and it was
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
    private (bool Armoury, bool Equipped) seenFilters = (true, true);

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
    /// <remarks>
    /// Off the tracker rather than through the ownership view. The view is built fresh on every
    /// read and the header wants this once a frame, which is a record's worth of garbage per frame
    /// to fetch two integers that are sitting right there.
    /// </remarks>
    public (int Used, int Capacity) Space =>
        (plugin.Ownership.DresserSlotsUsed, plugin.Ownership.DresserSlotCapacity);

    /// <summary>
    /// Carried gear that is not stored, or null when there is nothing to draw against.
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
        // a list built against an unloaded box would confidently claim every piece owned is
        // unstored - which is exactly the moment someone would act on it.
        var mirage = MirageManager.Instance();
        if (mirage == null || !mirage->PrismBoxLoaded)
            return Forget();

        plugin.Rows.Revalidate();

        var inventory = InventoryReader.Fingerprint();
        var filters = Filters();

        if (rows != null && plugin.Rows.Revision == seenRowRevision &&
            inventory == seenInventory && filters == seenFilters)
        {
            return rows;
        }

        seenFilters = filters;
        seenRowRevision = plugin.Rows.Revision;
        seenInventory = inventory;
        rows = Build();
        return rows;
    }

    /// <summary>
    /// Which locations the panel is currently listing.
    /// </summary>
    /// <remarks>
    /// Both of these are gear that is spoken for. Armoury pieces are usually a gearset's, and one
    /// equipped gear has to come off before it can go anywhere - so a list of things to store is
    /// often better without them, and both are switchable rather than assumed either way.
    /// </remarks>
    private (bool Armoury, bool Equipped) Filters() => (
        plugin.Configuration.DresserPanelIncludesArmoury,
        plugin.Configuration.DresserPanelIncludesEquipped);

    private bool Included(CarryLocation location) => location switch
    {
        CarryLocation.Armoury => plugin.Configuration.DresserPanelIncludesArmoury,
        CarryLocation.Equipped => plugin.Configuration.DresserPanelIncludesEquipped,
        _ => true,
    };

    private List<DresserAddRow>? Forget()
    {
        rows = null;
        return null;
    }

    private List<DresserAddRow> Build()
    {
        // No retainers. This panel's subject is what can go into the box being stood at, and a piece
        // two zones away with a retainer is not on that list however definitely it is owned.
        var report = CarriedGear.Build(
            InventoryReader.ReadDetailed(), [], plugin.Ownership.Current, plugin.Outfits, plugin.Storage);

        var built = new List<DresserAddRow>(report.NotStored.Count);

        foreach (var piece in report.NotStored)
        {
            if (!Included(piece.Location))
                continue;

            // Through the shared factory, so job filtering and the marker vocabulary match every
            // other panel. Null here means the factory's scope filter excluded it.
            var row = plugin.Rows.Row(piece.ItemId);
            if (row == null)
                continue;

            built.Add(new DresserAddRow(
                row.ItemId,
                row.Name,
                row.IconId,
                row.SlotOrder,
                row.SlotName,

                // Not the factory's verdict. It answers "is this collected" using the user's
                // settings, and with "also count bags" on it calls a carried piece owned - which
                // hid this entire panel behind the show-owned toggle. Every row here is unstored
                // by construction; that is what the list is.
                CollectionMarker.NotCollected,
                row.JobEquippable,
                row.Locks,
                piece.Location,
                piece.Quantity,
                BlockedReason(piece),
                piece.ArmoireWouldTake));
        }

        Plugin.Log.Debug($"Dresser panel: {built.Count} carried pieces are not stored.");
        return built;
    }

    /// <summary>
    /// Why the box would refuse this piece today, if it would.
    /// </summary>
    /// <remarks>
    /// Only the two reasons that are both legible from here and not already explained by the game.
    /// Damaged gear is refused too, but the dresser says so itself on the attempt, and spiritbond is
    /// not a reason at all - storing a piece resets it rather than requiring it.
    ///
    /// A row without a note therefore reads "not stored" rather than "this can be added", and a few
    /// pieces the game turns down will still be listed.
    /// </remarks>
    private static string? BlockedReason(CarriedPiece piece) => piece.Location switch
    {
        CarryLocation.Saddlebag => "Retrieve it from the saddlebag first.",
        CarryLocation.Equipped => "Take it off first.",
        _ => null,
    };

    private void OnOpened(AddonEvent type, AddonArgs args)
    {
        open = true;

        // Standing at a dresser is the one moment the snapshot can actually be refreshed.
        plugin.Ownership.RequestRefresh();
    }

    private void OnClosed(AddonEvent type, AddonArgs args) => open = false;
}
