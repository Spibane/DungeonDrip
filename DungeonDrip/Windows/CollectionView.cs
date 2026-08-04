using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// The main window's other half: the collection as a whole, with no duty involved.
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

    /// <summary>
    /// Rebuilds anything stale, then draws the three sections inside one scrolling region.
    /// </summary>
    /// <remarks>
    /// The order is what the sections cost to act on: an outfit part way done is a duty to run, the
    /// dresser section is housekeeping, and a spare copy of something already collected is the one
    /// that ends in throwing something away.
    /// </remarks>
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
        // leaving the bags when the tracker has no reason to look.
        var inventory = Game.InventoryReader.Fingerprint();
        var inventoryMoved = inventory != seenInventory;

        if (!ownershipMoved && !inventoryMoved)
            return;

        seenOwnershipRevision = plugin.Ownership.Revision;
        seenInputs = inputs;
        seenInventory = inventory;

        var ownership = plugin.Ownership.Current;

        // Held gear is the collection compared against everywhere something is merely held, so
        // either side moving rebuilds it. The retainers go in regardless of whether they are being
        // counted as owning a piece, for the same reason the bags do: that setting decides what makes
        // a piece collected, and this section is about the holdings themselves either way.
        carried = CarriedGear.Build(
            Game.InventoryReader.ReadDetailed(), plugin.Ownership.Retainers,
            ownership, plugin.Outfits, plugin.Storage);

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
            configuration.CountRetainers,
            configuration.CountRetainerEquipped,
            configuration.HideWeapons,
            configuration.OnlyCurrentJobEquippable,
            configuration.OnlyCurrentGenderEquippable,
            configuration.OnlyCurrentRaceEquippable);
    }

    /// <summary>
    /// Outfit sets part way through, closest to done first.
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
    /// the box can hold rather than as the room left.
    ///
    /// A stale snapshot only ever undercounts, because a dresser is added to far more often than it is
    /// taken from, so the number is prefixed rather than suppressed.
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
            // and owning all eleven pieces does not produce one; plenty of sets have no such item
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
    /// Merely held gear that the collection already holds.
    /// </summary>
    /// <remarks>
    /// The heading is "already in your collection" rather than "safe to discard", and that is the
    /// whole design of the section. It reports a fact the plugin can stand behind - this piece is
    /// also in a box - and leaves the conclusion to the reader, because the conclusion is
    /// irreversible and is being drawn from a snapshot that may be days old.
    ///
    /// Retainers are one of the places looked at, not one of the boxes compared against. A piece with
    /// a retainer is no more wearable as a glamour than one in a bag, so it belongs on the left of
    /// this question rather than the right.
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

        // The premise, then where it reads from. Both are worth the two lines: the premise is what a
        // row is reporting and was being left for the reader to infer from a location heading and a
        // trailing clause, and where it reads from is not guessable from the contents and changes what
        // an empty list means.
        ImGui.TextColored(Palette.Muted, "Every row is two copies of one piece:");

        ImGui.TextColored(Palette.Muted, plugin.Ownership.Retainers.Count > 0
            ? "one in your bags, armoury chest, saddlebag or with a retainer, and one the collection has."
            : "one in your bags, armoury chest or saddlebag, and one the collection has.");

        if (carried.AlreadyStored.Count == 0 && carried.OnlyInsideAnOutfit.Count == 0)
        {
            ImGui.TextColored(Palette.Good, "Nothing held has a second copy in the collection.");
            DrawCaveats(stale);
            return;
        }

        DrawCarriedGroup("In your bags", HeldIn(CarryLocation.Bags), null, stale);

        DrawCarriedGroup(
            "In your armoury chest", HeldIn(CarryLocation.Armoury),
            "A gearset may be using these.", stale);

        DrawCarriedGroup("In your saddlebag", HeldIn(CarryLocation.Saddlebag), null, stale);

        DrawRetainerGroups(stale);

        DrawCaveats(stale);
    }

    /// <summary>
    /// Everything held in one place that the collection accounts for, however it accounts for it.
    /// </summary>
    /// <remarks>
    /// The two halves of the report are merged here rather than drawn as two tiers, which is how this
    /// section used to work and was the one confusing thing in it. Every heading answers "where is
    /// this" - bags, armoury chest, saddlebag, one per retainer - except that the outfit tier answered
    /// "how is this accounted for" instead, so a piece in the armoury chest held only inside a stored
    /// outfit appeared under a heading that never said armoury, below the other headings, reading like
    /// one more place to look.
    ///
    /// One axis, then: the heading is the place, and how the collection accounts for a piece is said on
    /// the row. Nothing is lost, because the row already carried that sentence - it is now the only
    /// thing saying it, and the outfit case is coloured because its second copy is a conditional one.
    /// </remarks>
    private IEnumerable<CarriedPiece> HeldIn(CarryLocation location) =>
        carried!.AlreadyStored
            .Concat(carried.OnlyInsideAnOutfit)
            .Where(piece => piece.Location == location)
            .OrderBy(piece => piece.SlotOrder)
            .ThenBy(piece => piece.Name, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A heading per retainer holding something the collection accounts for.
    /// </summary>
    /// <remarks>
    /// Named rather than lumped into one retainer heading, because the whole difficulty with a spare
    /// copy at a retainer is which retainer, and a list that will not say leaves all of them to check.
    /// Ordered by name here rather than by how stale the snapshot is, which is what the Data tab sorts
    /// by: this is a list to find a name in.
    /// </remarks>
    private void DrawRetainerGroups(bool stale)
    {
        foreach (var group in ByRetainer(CarryLocation.Retainer))
        {
            DrawCarriedGroup(
                $"In {Possessive(group.Key)} bags", group,
                "Only reachable at a bell, and only as fresh as your last visit.", stale);
        }

        // Separate headings, and this is the whole point of the split. "With Ysayle" for a coat Ysayle
        // is wearing is a true sentence that leads to seven pages of her bags being searched for it.
        foreach (var group in ByRetainer(CarryLocation.RetainerEquipped))
        {
            DrawCarriedGroup(
                $"Worn by {Name(group.Key)}", group,
                "On the retainer, not in their bags - and their gear decides what ventures bring back.",
                stale);
        }
    }

    private IEnumerable<IGrouping<string, CarriedPiece>> ByRetainer(CarryLocation location) =>
        HeldIn(location)
            .GroupBy(piece => piece.Holder)
            .OrderBy(group => group.Key, System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A retainer's name, with somewhere to fall back to.
    /// </summary>
    /// <remarks>
    /// The name comes from the client and is never blank in practice, but a heading reading "Worn by"
    /// would be a puzzle rather than a row.
    /// </remarks>
    private static string Name(string holder) => holder.Length > 0 ? holder : "a retainer";

    /// <summary>The same as a possessive, since "In Ysayle's bags" is the heading that reads.</summary>
    private static string Possessive(string holder) =>
        holder.Length == 0 ? "a retainer's" : holder.EndsWith('s') ? $"{holder}'" : $"{holder}'s";

    /// <summary>
    /// Where the collection's copy is, as the other half of a sentence about two of them.
    /// </summary>
    /// <remarks>
    /// Not <see cref="MissingItems.Describe"/>, which is written for a surface asking "do I have this"
    /// and answers in one place. Here both copies are being named at once, so each needs to say the
    /// Glamour Dresser out loud - "part of a stored outfit set" on its own never mentions the box the
    /// set is sitting in, which is the thing a reader is trying to place.
    /// </remarks>
    private static string SecondCopy(OwnershipSource source) => source switch
    {
        OwnershipSource.Armoire => "There is a second in your Armoire.",
        OwnershipSource.Outfit => "There is a second in your Glamour Dresser, inside a stored outfit set.",
        _ => "There is a second in your Glamour Dresser.",
    };

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

            // "also", because that word is the whole row. Every piece here exists twice - the one being
            // held, under the heading that says where, and a second the collection has - and without it
            // the row reads as a claim about where this copy is, which the heading has already answered.
            var insideOutfit = piece.StoredIn == OwnershipSource.Outfit;

            ImGui.SameLine();
            ImGui.TextColored(
                insideOutfit ? Palette.Warning : Palette.Muted,
                $"- also {MissingItems.Describe(piece.StoredIn).ToLowerInvariant()}");

            if (!hovered)
                continue;

            using var tooltip = ImRaii.Tooltip();
            ImGui.Text(piece.Name);

            // Both places, in the hover, even though the heading has one of them. A tooltip that
            // repeated only the collection half never said what the row is actually reporting - two
            // copies, and here is each one.
            ImGui.TextColored(Palette.Muted, $"{label}.");
            ImGui.TextColored(Palette.Muted, SecondCopy(piece.StoredIn));

            if (insideOutfit)
            {
                // Why this one is amber rather than grey. The dresser's copy is not a piece sitting in
                // the box, it is a slot of a whole set sitting in the box, so it is only there for as
                // long as the set is - which the two flat statements above cannot say on their own.
                ImGui.TextColored(Palette.Warning,
                    "That second copy belongs to the set, so taking the set out takes it too.");
            }

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

        // What the amber rows mean, said only when there are some. The row itself says which pieces
        // they are and the hover says what to do about it; this is here so the colour is not a thing
        // to work out.
        if (carried!.OnlyInsideAnOutfit.Count > 0)
        {
            ImGui.TextColored(Palette.Muted,
                "Amber: the second copy is a slot of a stored outfit set rather than a piece of its own.");
        }

        if (!carried.SaddlebagReadable)
            ImGui.TextColored(Palette.Muted, "Saddlebag unreadable away from a bell.");

        // Which retainers this could not look at, since an unvisited one is indistinguishable from an
        // empty one on the list itself.
        if (plugin.Ownership.Retainers.Count == 0)
            ImGui.TextColored(Palette.Muted, "No retainer has been read - open one at a bell to include it.");
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
        bool CountRetainers,
        bool CountRetainerEquipped,
        bool HideWeapons,
        bool JobOnly,
        bool GenderOnly,
        bool RaceOnly);
}
