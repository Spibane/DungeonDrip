using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>
/// Watches the Armoire and works out which held pieces it has not got.
/// </summary>
/// <remarks>
/// The gap this fills is in the game's own screen rather than in the plugin's knowledge. The store
/// list shows what the Armoire <em>can</em> take, which is a fact about the item and not about the
/// character: a piece already deposited is listed exactly as one that is not, and the refusal comes
/// only on the attempt, one piece at a time. So the question "what is left to put in" cannot be
/// answered by reading the screen, however carefully.
///
/// Its own class rather than a mode of <see cref="DresserAddWatcher"/>, because almost nothing is
/// shared but the shape of the answer: a different addon, a different load guard, a different test for
/// what the box holds, and no slot pressure at all - the Armoire has no capacity, so which pieces to
/// put in is never a choice between them. What the two do share is
/// <see cref="HeldGearRow"/> and the panel that draws it.
///
/// Everything runs from the panel's draw call, like the dresser watcher, so it costs nothing while the
/// Armoire is closed or the panel is off.
/// </remarks>
public sealed unsafe class ArmoireAddWatcher : IDisposable
{
    /// <summary>
    /// The "store an item" screen - a category dropdown over a grid of what the Armoire accepts.
    /// </summary>
    /// <remarks>
    /// This is the screen the panel exists for, and the one whose list cannot be trusted to mean
    /// anything about this character.
    /// </remarks>
    public const string StoreAddonName = "Cabinet";

    /// <summary>The Armoire's own window, with a radio button per category and the search box.</summary>
    /// <remarks>
    /// Anchored to as well, because the two screens are one place and the answer is the same at both:
    /// somebody reading what is in the Armoire is the same person deciding what still has to go in.
    /// The store screen wins when both are up, since that is where the list is being acted on.
    /// </remarks>
    public const string WithdrawAddonName = "CabinetWithdraw";

    private readonly Plugin plugin;

    private List<HeldGearRow>? rows;
    private int seenRowRevision = -1;
    private ulong seenInventory;
    private bool storeOpen;
    private bool withdrawOpen;
    private (bool Armoury, bool Equipped) seenFilters = (true, true);

    /// <summary>
    /// When the Armoire was opened, as the earliest a read of it can be trusted to describe now.
    /// </summary>
    /// <remarks>
    /// Set on the first of the two windows to come up and kept across the other opening and closing,
    /// so stepping between the store screen and the list does not start the wait again. Null while
    /// neither is open.
    /// </remarks>
    private DateTime? openedUtc;

    public ArmoireAddWatcher(Plugin plugin)
    {
        this.plugin = plugin;

        foreach (var addon in Addons)
        {
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, addon, OnOpened);
            Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, addon, OnClosed);
        }
    }

    public void Dispose()
    {
        foreach (var addon in Addons)
        {
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, addon, OnOpened);
            Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, addon, OnClosed);
        }
    }

    private static string[] Addons => [StoreAddonName, WithdrawAddonName];

    /// <summary>Which Armoire window the panel should sit beside, or null while there is none.</summary>
    /// <remarks>
    /// Written by <see cref="Resolve"/> rather than worked out on demand, so the panel is anchored to
    /// the same window the rows were checked against in the same frame.
    /// </remarks>
    public string? AnchorAddon { get; private set; }

    /// <summary>
    /// How many held pieces the Armoire already has - the ones its own list will offer and refuse.
    /// </summary>
    /// <remarks>
    /// Zero also when nothing has been read, which is safe here in a way it would not be elsewhere:
    /// the number is only ever drawn while the panel is up, and the panel is only up once the cabinet
    /// has loaded.
    /// </remarks>
    public int Duplicates { get; private set; }

    /// <summary>
    /// Held gear the Armoire has not got, or null when there is nothing to draw against.
    /// Call once per frame.
    /// </summary>
    public IReadOnlyList<HeldGearRow>? Resolve()
    {
        if (!plugin.Configuration.ArmoirePanel.Enabled || Plugin.GameGui.GameUiHidden)
            return null;

        var addon = VisibleAddon();
        if (addon == null)
            return Forget();

        // The counterpart of the dresser watcher's PrismBoxLoaded guard, and just as load-bearing. The
        // Armoire is unlock flags that arrive on request, and every one of them reads as "not stored"
        // until they do - so a list built too early claims the whole wardrobe is still to put in, at
        // the exact moment somebody is standing there ready to act on it.
        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
            return Forget();

        // Loaded in the client is not the same as read by the plugin: the read is on the tracker's
        // poll, and until it lands the snapshot is the last visit's - which is nearly right, and
        // "nearly" is not good enough for a list somebody is about to deposit gear from. Opening the
        // window asks for a refresh, so this waits a frame rather than the poll's second.
        //
        // Spelt out rather than compared as nullables: "never read" would come out false on either
        // side of a < and let an unread armoire through as though it were an empty one.
        if (openedUtc == null ||
            plugin.Ownership.ArmoireUpdatedUtc is not { } read ||
            read < openedUtc)
        {
            return Forget();
        }

        AnchorAddon = addon;

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

    /// <summary>Whichever Armoire window is actually on screen, the store screen first.</summary>
    private string? VisibleAddon()
    {
        if (storeOpen && IsVisible(StoreAddonName))
            return StoreAddonName;

        return withdrawOpen && IsVisible(WithdrawAddonName) ? WithdrawAddonName : null;
    }

    private static bool IsVisible(string addon)
    {
        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(addon);
        return unit != null && unit->IsVisible;
    }

    /// <summary>
    /// The same two filters the dresser panel has, read from the same settings.
    /// </summary>
    /// <remarks>
    /// Shared rather than a second pair, so "listing gear you are wearing" means one thing wherever it
    /// is switched. Both panels ask what should go into a box, and gear that is spoken for is spoken
    /// for whichever box is being stood at.
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

    private List<HeldGearRow>? Forget()
    {
        rows = null;
        AnchorAddon = null;
        Duplicates = 0;
        return null;
    }

    private List<HeldGearRow> Build()
    {
        // No retainers, for the reason the dresser panel has none: the subject is what can go into the
        // box being stood at, and a piece at a bell two zones away is not on that list.
        var report = CarriedGear.Build(
            InventoryReader.ReadDetailed(), [], plugin.Ownership.Current, plugin.Outfits, plugin.Storage);

        Duplicates = report.ArmoireDuplicates;

        var built = new List<HeldGearRow>(report.ArmoireCandidates.Count);

        foreach (var piece in report.ArmoireCandidates)
        {
            if (!Included(piece.Location))
                continue;

            // Through the shared factory, so job filtering and the row's vocabulary match every other
            // panel. Null here means its scope filter excluded the piece.
            var row = plugin.Rows.Row(piece.ItemId);
            if (row == null)
                continue;

            built.Add(new HeldGearRow(
                row.ItemId,
                row.Name,
                row.IconId,
                row.SlotOrder,
                row.SlotName,

                // Not the factory's verdict, for the reason the dresser panel does not use it either:
                // that answers "is this collected" by the user's settings, and a piece sitting in the
                // Glamour Dresser is collected while still being one the Armoire has never had. Every
                // row here is one the Armoire has not got, which is what the list is.
                CollectionMarker.NotCollected,
                row.JobEquippable,
                row.Locks,
                piece.Location,
                piece.Quantity,
                BlockedReason(piece)));
        }

        Plugin.Log.Debug(
            $"Armoire panel: {built.Count} held pieces are not in it, {Duplicates} already are.");

        return built;
    }

    /// <summary>
    /// Why the Armoire would turn this down today, if anything here can say so.
    /// </summary>
    /// <remarks>
    /// The two reasons that hold of any container the game hands items to, and no others. Whether the
    /// Armoire reaches into the armoury chest is deliberately not claimed either way: nothing readable
    /// from outside it says, and a note that guessed wrong would be worse than the silence - so a row
    /// without one means "the Armoire has not got this" and never "the Armoire will take it from here".
    /// </remarks>
    private static string? BlockedReason(CarriedPiece piece) => piece.Location switch
    {
        CarryLocation.Saddlebag => "Retrieve it from the saddlebag first.",
        CarryLocation.Equipped => "Take it off first.",
        _ => null,
    };

    private void OnOpened(AddonEvent type, AddonArgs args)
    {
        Track(args.AddonName, true);

        // Standing at the Armoire is the one moment its flags are loaded and can be snapshotted.
        plugin.Ownership.RequestRefresh();
    }

    private void OnClosed(AddonEvent type, AddonArgs args) => Track(args.AddonName, false);

    private void Track(string addon, bool open)
    {
        if (addon == StoreAddonName)
            storeOpen = open;
        else if (addon == WithdrawAddonName)
            withdrawOpen = open;

        openedUtc = storeOpen || withdrawOpen
            ? openedUtc ?? DateTime.UtcNow
            : null;
    }
}
