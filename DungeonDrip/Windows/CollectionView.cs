using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// The main window's other half: your collection as a whole, with no duty involved.
/// </summary>
/// <remarks>
/// Composed into <see cref="MissingItemsWindow"/> rather than being a window of its own. That one is
/// already long and its spine is entirely duty-shaped - pinning, auto-open, a title taken from the
/// report - so this keeps the two apart without adding a second thing to open and find.
///
/// Every section here sweeps the whole collection, which is far too much to do per frame, so all of
/// it is computed behind <see cref="Stale"/> and cached until something it depends on moves.
/// </remarks>
public sealed class CollectionView(Plugin plugin)
{
    /// <summary>
    /// Prefix on the remembered heading state, so a section can never collide with a slot or role
    /// heading from the duty list, which share the same store.
    /// </summary>
    private const string CollapsePrefix = "collection:";

    /// <summary>Above this share of the box, the count is worth colouring.</summary>
    private const float CrowdedAt = 0.9f;

    private int seenOwnershipRevision = -1;
    private Inputs seenInputs;
    private ulong seenInventory;

    /// <summary>Sets listed before the "show all" toggle earns its place.</summary>
    private const int InitialSetsShown = 25;

    private DresserPressureReport? pressure;
    private CarriedGearReport? carried;
    private IReadOnlyList<SetStanding> sets = [];

    /// <summary>
    /// Per-session, not a setting. The config file is documentation of what the user chose, and
    /// "I expanded a list once" is not a choice worth writing down.
    /// </summary>
    private bool showAllSets;

    public void Draw()
    {
        Recompute();

        using var child = ImRaii.Child("collectionList", Vector2.Zero, false);
        if (!child.Success)
            return;

        Section("Sets in progress", DrawSetsInProgress);
        Section("Glamour Dresser", DrawDresserPressure);
        Section("Already in your collection", DrawAlreadyStored);
    }

    /// <summary>
    /// Rebuilds the sections when, and only when, something they are derived from has changed.
    /// </summary>
    /// <remarks>
    /// Two keys, because the sections do not read the same things.
    ///
    /// The carried list needs its own. The ownership tracker only bumps its revision for inventory
    /// changes while "also count bags" is on, since that setting is the only reason it reads the
    /// bags at all - but this section reads them regardless, on purpose, because it asks the
    /// opposite question. With the setting off, which is the default, throwing a piece away left it
    /// on the list with nothing that could ever clear it.
    ///
    /// Staleness of the snapshot is deliberately in neither key, matching the tracker's own rule:
    /// an ageing snapshot changes how the answer should be worded, not what it is.
    /// </remarks>
    private void Recompute()
    {
        var inputs = Capture();
        var ownershipMoved = plugin.Ownership.Revision != seenOwnershipRevision || inputs != seenInputs;

        // One pass over the containers, no allocation, and the only thing that notices a piece
        // leaving your bags when the tracker has no reason to look.
        var inventory = Game.InventoryReader.Fingerprint();
        var inventoryMoved = inventory != seenInventory;

        if (!ownershipMoved && !inventoryMoved)
            return;

        seenOwnershipRevision = plugin.Ownership.Revision;
        seenInputs = inputs;
        seenInventory = inventory;

        var ownership = plugin.Ownership.Current;

        // Carried gear is the collection compared against the bags, so either side moving rebuilds it.
        carried = CarriedGear.Build(
            Game.InventoryReader.ReadDetailed(), ownership, plugin.Outfits, plugin.Storage);

        if (!ownershipMoved)
            return;

        pressure = DresserPressure.Build(
            ownership, plugin.Outfits, plugin.Storage, plugin.Ownership.ArmoireUpdatedUtc != null);

        sets = plugin.Ownership.HasDresserData
            ? SetCompletion.InProgress(plugin.Outfits, ownership, plugin.Configuration, plugin.EquipLocks)
            : [];
    }

    private Inputs Capture()
    {
        var configuration = plugin.Configuration;
        return new Inputs(
            configuration.Scope,
            configuration.OutfitOwnership,
            configuration.CountInventoryAndEquipped,
            configuration.HideWeapons,
            configuration.OnlyCurrentJobEquippable,
            configuration.OnlyCurrentGenderEquippable,
            configuration.OnlyCurrentRaceEquippable);
    }

    /// <summary>
    /// Outfit sets you are part way through, closest to done first.
    /// </summary>
    /// <remarks>
    /// One count per row. Reporting the dresser copy's filled slots beside it was tried and read as
    /// an error rather than as extra information - the two are out of different totals and only one
    /// of them obeys the storage scope, so they disagreed for correct reasons that no row has space
    /// to explain.
    /// </remarks>
    private void DrawSetsInProgress()
    {
        if (!plugin.Ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.Muted,
                "No Glamour Dresser data yet - open a dresser once so this can be answered.");
            return;
        }

        if (sets.Count == 0)
        {
            ImGui.TextColored(Palette.Muted,
                "No outfit set is part way done - every set is either finished or untouched.");
            return;
        }

        var shown = showAllSets ? sets.Count : System.Math.Min(sets.Count, InitialSetsShown);

        for (var i = 0; i < shown; i++)
            DrawSetRow(sets[i]);

        if (sets.Count <= InitialSetsShown)
            return;

        ImGui.Spacing();
        if (ImGui.SmallButton(showAllSets
                ? $"Show the closest {InitialSetsShown}###setsAll"
                : $"Show all {sets.Count}###setsAll"))
        {
            showAllSets = !showAllSets;
        }
    }

    private void DrawSetRow(SetStanding standing)
    {
        UiParts.ItemIcon(standing.IconId, 20);

        var open = ImGui.TreeNodeEx(
            $"{standing.Name}###set{standing.SetId}", ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGui.SameLine();
        ImGui.TextColored(Palette.Muted, $"{standing.Owned} of {standing.Total}");

        if (!open)
            return;

        foreach (var piece in standing.Missing)
            DrawMissingPiece(piece);

        ImGui.TreePop();
    }

    private void DrawMissingPiece(SetPieceState piece)
    {
        UiParts.ItemIcon(piece.IconId, 18);
        ImGui.Text(piece.Name);

        UiParts.ItemContextMenu(plugin, piece.ItemId, piece.Name);

        var sources = plugin.Drops?.For(piece.ItemId);
        if (sources is not { Count: > 0 })
            return;

        var best = sources[0];
        ImGui.SameLine();
        ImGui.TextColored(Palette.Muted, best.Level > 0
            ? $"- {best.DutyName} (Lv. {best.Level})"
            : $"- {best.DutyName}");
    }

    private void DrawDresserPressure()
    {
        if (pressure is not { HasData: true })
        {
            ImGui.TextColored(Palette.Muted, "No dresser snapshot to measure.");
            return;
        }

        DrawOccupancy(pressure);

        if (pressure.Reclaimable > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Focus, $"Up to {pressure.Reclaimable} slots could be freed:");
        }

        DrawCollapsible(pressure);
        DrawArmoireLevers(pressure);
    }

    /// <summary>
    /// How full the box is, with a denominator only when there is an honest one to give.
    /// </summary>
    /// <remarks>
    /// The capacity read off the client is the box's structural size, which is not the same as how
    /// many slots a given character has actually unlocked, and the client offers no way to tell. So
    /// a bare count is the claim that can always be stood behind; the "of N" is worded as the most
    /// the box can hold rather than as the room you have.
    ///
    /// A stale snapshot only ever undercounts, because you add to a dresser far more often than you
    /// take from it, so the number is prefixed rather than suppressed.
    /// </remarks>
    private void DrawOccupancy(DresserPressureReport report)
    {
        var stale = plugin.Ownership.IsDresserStale;
        var prefix = stale ? "At least " : string.Empty;
        var crowded = report.Capacity > 0 && report.Used >= report.Capacity * CrowdedAt;

        if (report.Capacity > 0)
        {
            ImGui.TextColored(
                crowded ? Palette.Warning : Palette.Muted,
                $"{prefix}{report.Used} slots used, of the {report.Capacity} the box holds.");

            ImGui.ProgressBar(
                (float)report.Used / report.Capacity,
                new Vector2(-1, 6 * ImGuiHelpers.GlobalScale),
                string.Empty);
        }
        else
        {
            ImGui.TextColored(Palette.Muted, $"{prefix}{report.Used} slots used.");
        }
    }

    private void DrawCollapsible(DresserPressureReport report)
    {
        if (report.Collapsible.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, report.Collapsible.Count == 1
            ? "1 outfit is stored a piece at a time:"
            : $"{report.Collapsible.Count} outfits are stored a piece at a time:");

        foreach (var set in report.Collapsible)
        {
            // Conditional on purpose. Storing a set needs the outfit item - a tradeable attire box -
            // and owning all eleven pieces does not give you one; plenty of sets have no such item
            // at all. Telling someone to do something they may have no way to do is worse than
            // telling them what it would be worth if they could.
            var line = set.HoldingOutfitItem
                ? $"   {set.Name} - you have the outfit item; storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots"
                : $"   {set.Name} - if you have the outfit item, storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots";

            ImGui.TextColored(set.HoldingOutfitItem ? Palette.Good : Palette.Muted, line);
        }

        ImGui.TextColored(Palette.Muted,
            "   The loose copies come back to your bags, so the space appears once you clear them.");
    }

    private void DrawArmoireLevers(DresserPressureReport report)
    {
        if (!report.ArmoireKnown)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Muted,
                "Open your Armoire once and this can also say what it would take off your hands.");
            return;
        }

        DrawResidents(
            report.DuplicateInArmoire,
            "In your Armoire as well as the dresser",
            "The dresser copy is doing nothing the Armoire is not already doing.",
            Palette.Focus);

        DrawResidents(
            report.ArmoireWouldTake,
            "The Armoire would take these, at no dresser slot",
            "Each has to be taken out of the dresser and deposited, so this one is work.",
            Palette.Muted);
    }

    /// <summary>
    /// Names the pieces rather than only counting them.
    /// </summary>
    /// <remarks>
    /// "3 pieces are duplicated" is not something anybody can act on without being told which
    /// three. Collapsed by default because these lists get long on a full dresser, and the count
    /// on the heading is the part worth seeing at a glance.
    /// </remarks>
    private void DrawResidents(
        IReadOnlyList<DresserResident> residents, string heading, string note, Vector4 colour)
    {
        if (residents.Count == 0)
            return;

        ImGui.Spacing();

        if (!ImGui.TreeNodeEx($"{heading} ({residents.Count})###{heading}"))
        {
            ImGui.TextColored(Palette.Muted, $"   {note}");
            return;
        }

        ImGui.TextColored(Palette.Muted, note);

        foreach (var resident in residents)
        {
            UiParts.ItemIcon(resident.IconId, 18);
            ImGui.TextColored(colour, resident.Name);
            UiParts.ItemContextMenu(plugin, resident.ItemId, resident.Name);
        }

        ImGui.TreePop();
    }

    /// <summary>
    /// Gear you are carrying that the collection already holds.
    /// </summary>
    /// <remarks>
    /// The heading is "already in your collection" rather than "safe to discard", and that is the
    /// whole design of the section. It reports a fact the plugin can stand behind - this piece is
    /// also in a box - and leaves the conclusion to the reader, because the conclusion is
    /// irreversible and is being drawn from a snapshot that may be days old.
    /// </remarks>
    private void DrawAlreadyStored()
    {
        if (!plugin.Ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.Muted,
                "No Glamour Dresser data yet - open a dresser once so this can be answered.");
            return;
        }

        if (carried == null)
            return;

        var stale = plugin.Ownership.IsDresserStale;

        // Where the list comes from, which is not guessable from its contents and changes what an
        // empty one means. Trimmed away with the rest of this section's text and put straight back:
        // it was the one line that was saying something rather than explaining something.
        ImGui.TextColored(Palette.Muted, "From your bags, armoury chest and saddlebag.");

        if (carried.AlreadyStored.Count == 0 && carried.OnlyInsideAnOutfit.Count == 0)
        {
            ImGui.TextColored(Palette.Good, "Nothing there is already stored.");
            DrawCaveats(stale);
            return;
        }

        DrawCarriedGroup(
            "In your bags", carried.AlreadyStored.Where(piece => piece.Location == CarryLocation.Bags),
            null, stale);

        DrawCarriedGroup(
            "In your armoury chest",
            carried.AlreadyStored.Where(piece => piece.Location == CarryLocation.Armoury),
            "A gearset may be using these.", stale);

        DrawCarriedGroup(
            "In your saddlebag",
            carried.AlreadyStored.Where(piece => piece.Location == CarryLocation.Saddlebag),
            null, stale);

        DrawCarriedGroup(
            "Only inside a stored outfit", carried.OnlyInsideAnOutfit,
            "Removing the set empties the slot.", stale);

        DrawCaveats(stale);
    }

    private void DrawCarriedGroup(
        string label, IEnumerable<CarriedPiece> pieces, string? note, bool stale)
    {
        var listed = pieces.ToList();
        if (listed.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(stale ? Palette.Warning : Palette.Focus, $"{label} ({listed.Count})");

        if (note != null)
            ImGui.TextColored(Palette.Muted, $"   {note}");

        foreach (var piece in listed)
        {
            UiParts.ItemIcon(piece.IconId, 20);

            var quantity = piece.Quantity > 1 ? $"  x{piece.Quantity}" : string.Empty;
            ImGui.Text($"{piece.Name}{quantity}");

            var hovered = ImGui.IsItemHovered();
            UiParts.ItemContextMenu(plugin, piece.ItemId, piece.Name);

            ImGui.SameLine();
            ImGui.TextColored(Palette.Muted, $"- {MissingItems.Describe(piece.StoredIn).ToLowerInvariant()}");

            if (!hovered)
                continue;

            using var tooltip = ImRaii.Tooltip();
            ImGui.Text(piece.Name);
            ImGui.TextColored(Palette.Muted, MissingItems.Describe(piece.StoredIn));

            if (piece.ArmoireWouldTake)
                ImGui.TextColored(Palette.Muted, "The Armoire would take this too.");
        }
    }

    /// <summary>
    /// What the list cannot see, in as few words as it can be put.
    /// </summary>
    /// <remarks>
    /// Each of these changes what the list means, which is why they are on screen at all - but
    /// none of them needs a sentence. The note about reading the bags regardless of the
    /// count-inventory setting is gone: it explained the plugin's reasoning to someone who only
    /// wanted to know what was on the list.
    /// </remarks>
    private void DrawCaveats(bool stale)
    {
        ImGui.Spacing();

        if (stale)
        {
            ImGui.TextColored(Palette.Warning,
                $"Dresser snapshot {Format.Age(plugin.Ownership.DresserUpdatedUtc!.Value)} old.");
        }

        ImGui.TextColored(Palette.Muted, carried!.SaddlebagReadable
            ? "Retainers not counted."
            : "Retainers not counted. Saddlebag unreadable away from a bell.");
    }

    /// <summary>A collapsible heading whose state survives a restart, as the duty list's do.</summary>
    private void Section(string label, System.Action body)
    {
        var key = CollapsePrefix + label;
        var configuration = plugin.Configuration;
        var collapsed = configuration.CollapsedGroups.Contains(key);

        ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);
        var open = ImGui.CollapsingHeader($"{label}###{key}");

        if (open == collapsed)
        {
            if (open)
                configuration.CollapsedGroups.Remove(key);
            else
                configuration.CollapsedGroups.Add(key);

            configuration.Save();
        }

        if (!open)
            return;

        body();
        ImGui.Spacing();
    }

    /// <summary>The settings any section reads, folded into one value so a change is one compare.</summary>
    private readonly record struct Inputs(
        CollectionScope Scope,
        OutfitOwnershipMode Outfits,
        bool CountInventory,
        bool HideWeapons,
        bool JobOnly,
        bool GenderOnly,
        bool RaceOnly);
}
