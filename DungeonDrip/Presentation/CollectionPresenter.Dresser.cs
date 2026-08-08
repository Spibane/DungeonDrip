using System.Collections.Generic;
using System.Linq;
using DungeonDrip.Core;

namespace DungeonDrip.Presentation;

/// <summary>The Glamour Dresser tab: how full the box is, and what could come out of it.</summary>
/// <remarks>
/// <b>Two lists on purpose.</b> How full the box is and which spare copies could go are the same
/// errand from both ends - the first says the box is filling and what would free space, the second
/// is the gear that could actually be got rid of. Splitting them across tabs would put a problem on
/// one and its lever on another.
///
/// The second keeps its own heading and its rule, because the two answer different questions and ran
/// under separate headings before this became a tab. Losing the heading would leave no way to see
/// where the occupancy figures stop and the disposal list starts.
/// </remarks>
internal static class DresserTabPresenter
{
    public static IReadOnlyList<Node> Tab(Plugin plugin, CollectionSnapshot snapshot)
    {
        var built = new List<Node>();

        built.AddRange(Pressure(plugin, snapshot));

        built.Add(new SpacingNode());
        built.Add(new RuleNode());
        built.Add(new SpacingNode());
        built.Add(new TextNode("Already in your collection", ColourRole.Muted));
        built.Add(new SpacingNode());

        built.AddRange(AlreadyStored(plugin, snapshot));
        return built;
    }

    private static IReadOnlyList<Node> Pressure(Plugin plugin, CollectionSnapshot snapshot)
    {
        if (snapshot.Pressure is not { HasData: true } report)
            return [new TextNode("No dresser snapshot to measure.", ColourRole.Muted)];

        var built = new List<Node>();
        built.AddRange(CollectionPresenter.Occupancy(report, plugin.Ownership.IsDresserStale));

        if (report.Reclaimable > 0)
        {
            built.Add(new SpacingNode());
            built.Add(new TextNode(
                $"Up to {report.Reclaimable} slots could be freed:", ColourRole.Focus));
        }

        built.AddRange(Collapsible(report));
        built.AddRange(ArmoireLevers(plugin, report));
        return built;
    }

    private static IReadOnlyList<Node> Collapsible(DresserPressureReport report)
    {
        if (report.Collapsible.Count == 0)
            return [];

        var built = new List<Node>
        {
            new SpacingNode(),
            new TextNode(
                report.Collapsible.Count == 1
                    ? "1 outfit is stored a piece at a time:"
                    : $"{report.Collapsible.Count} outfits are stored a piece at a time:",
                ColourRole.Muted),
        };

        foreach (var set in report.Collapsible)
        {
            // Conditional on purpose. Storing a set needs the outfit item - a tradeable attire box -
            // and owning all eleven pieces does not produce one; plenty of sets have no such item at
            // all. Telling someone to do something they may have no way to do is worse than telling
            // them what it would be worth if they could.
            built.Add(new TextNode(
                set.HoldingOutfitItem
                    ? $"   {set.Name} - you have the outfit item; storing it instead of these " +
                      $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots"
                    : $"   {set.Name} - if you have the outfit item, storing it instead of these " +
                      $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots",
                set.HoldingOutfitItem ? ColourRole.Good : ColourRole.Muted));
        }

        built.Add(new TextNode(
            "   The loose copies come back to your bags, so the space appears once you clear them.",
            ColourRole.Muted));

        return built;
    }

    private static IReadOnlyList<Node> ArmoireLevers(Plugin plugin, DresserPressureReport report)
    {
        if (!report.ArmoireKnown)
        {
            return
            [
                new SpacingNode(),
                new TextNode(
                    "Open your Armoire once and this can also say what it would take off your hands.",
                    ColourRole.Muted),
            ];
        }

        var built = new List<Node>();

        built.AddRange(Residents(
            plugin, report.DuplicateInArmoire,
            "In your Armoire as well as the dresser",
            "The dresser copy is doing nothing the Armoire is not already doing.",
            ColourRole.Focus));

        built.AddRange(Residents(
            plugin, report.ArmoireWouldTake,
            "The Armoire would take these, at no dresser slot",
            "Each has to be taken out of the dresser and deposited, so this one is work.",
            ColourRole.Muted));

        return built;
    }

    /// <summary>
    /// Names the pieces rather than only counting them.
    /// </summary>
    /// <remarks>
    /// "3 pieces are duplicated" is not something anybody can act on without being told which three.
    /// Collapsed by default because these lists get long on a full dresser, and the count on the
    /// heading is the part worth seeing at a glance - which is also why the note under it shows
    /// whether or not it is open.
    /// </remarks>
    private static IReadOnlyList<Node> Residents(
        Plugin plugin, IReadOnlyList<DresserResident> residents, string heading, string note,
        ColourRole colour)
    {
        if (residents.Count == 0)
            return [];

        var rows = new List<Node>(residents.Count);

        foreach (var resident in residents)
        {
            rows.Add(new RowNode(
                [new IconNode(resident.IconId, 18), new TextNode(resident.Name, colour)],
                Tooltip: null,
                new ItemContext(resident.ItemId, resident.Name)));
        }

        return
        [
            new SpacingNode(),
            new TreeNode(
                heading,
                $"{heading} ({residents.Count})",
                rows,
                Note: new TextNode(note, ColourRole.Muted)),
        ];
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
    private static IReadOnlyList<Node> AlreadyStored(Plugin plugin, CollectionSnapshot snapshot)
    {
        if (!plugin.Ownership.HasDresserData)
        {
            return
            [
                new TextNode(
                    "No Glamour Dresser data yet - open a dresser once so this can be answered.",
                    ColourRole.Muted),
            ];
        }

        if (snapshot.Carried == null)
            return [];

        var stale = plugin.Ownership.IsDresserStale;

        // The premise, then where it reads from. Both are worth the two lines: the premise is what a
        // row is reporting and was being left for the reader to infer from a location heading and a
        // trailing clause, and where it reads from is not guessable from the contents and changes
        // what an empty list means.
        // One node rather than the two this was written as. The break was put in by hand because
        // the overlay does not wrap, and a sentence broken by hand cannot be re-broken to fit - so
        // the halves sat at the spacing of two separate lines with the second half wrapping again
        // underneath, which read as three lines with the gaps in the wrong places.
        var built = new List<Node>
        {
            new TextNode(
                plugin.Ownership.Retainers.Count > 0
                    ? "Every row is two copies of one piece: one in your bags, armoury chest, " +
                      "saddlebag or with a retainer, and one the collection has."
                    : "Every row is two copies of one piece: one in your bags, armoury chest or " +
                      "saddlebag, and one the collection has.",
                ColourRole.Muted,
                Wrap: true),
        };

        if (snapshot.Carried.AlreadyStored.Count == 0 && snapshot.Carried.OnlyInsideAnOutfit.Count == 0)
        {
            built.Add(new TextNode(
                "Nothing held has a second copy in the collection.", ColourRole.Good));

            built.AddRange(Caveats(plugin, snapshot, stale));
            return built;
        }

        built.AddRange(Group(plugin, "In your bags", snapshot.HeldIn(CarryLocation.Bags), null, stale));

        built.AddRange(Group(
            plugin, "In your armoury chest", snapshot.HeldIn(CarryLocation.Armoury),
            "A gearset may be using these.", stale));

        built.AddRange(Group(
            plugin, "In your saddlebag", snapshot.HeldIn(CarryLocation.Saddlebag), null, stale));

        built.AddRange(RetainerGroups(plugin, snapshot, stale));
        built.AddRange(Caveats(plugin, snapshot, stale));

        return built;
    }

    /// <summary>
    /// A heading per retainer holding something the collection accounts for.
    /// </summary>
    /// <remarks>
    /// Named rather than lumped into one retainer heading, because the whole difficulty with a spare
    /// copy at a retainer is which retainer, and a list that will not say leaves all of them to
    /// check. Ordered by name here rather than by how stale the snapshot is, which is what the Data
    /// tab sorts by: this is a list to find a name in.
    /// </remarks>
    private static IReadOnlyList<Node> RetainerGroups(
        Plugin plugin, CollectionSnapshot snapshot, bool stale)
    {
        var built = new List<Node>();

        foreach (var group in snapshot.ByRetainer(CarryLocation.Retainer))
        {
            built.AddRange(Group(
                plugin, $"In {CollectionSnapshot.Possessive(group.Key)} bags", group,
                "Only reachable at a bell, and only as fresh as your last visit.", stale));
        }

        // Separate headings, and this is the whole point of the split. "With Ysayle" for a coat
        // Ysayle is wearing is a true sentence that leads to seven pages of her bags being searched.
        foreach (var group in snapshot.ByRetainer(CarryLocation.RetainerEquipped))
        {
            built.AddRange(Group(
                plugin, $"Worn by {CollectionSnapshot.Name(group.Key)}", group,
                "On the retainer, not in their bags - and their gear decides what ventures bring back.",
                stale));
        }

        return built;
    }

    /// <remarks>
    /// Collapsible per place, because the places are separate errands and a full armoury chest is
    /// dozens of rows - enough to push the retainer headings below it off the bottom of the window,
    /// where somebody looking for which retainer has a spare copy will not find them. Remembered,
    /// since the chest is the one nobody wants to see again tomorrow either.
    ///
    /// The count is in the label and not in the key, so collecting something does not lose the state
    /// of the heading it is under.
    ///
    /// Amber only, and only when the snapshot behind it is old. The blue this used to carry otherwise
    /// was there to tell one section from the next while they were all stacked in one column; the
    /// tabs do that now, so a coloured heading would only compete with the warning that means
    /// something.
    /// </remarks>
    private static IReadOnlyList<Node> Group(
        Plugin plugin, string label, IEnumerable<CarriedPiece> pieces, string? note, bool stale)
    {
        var listed = pieces.ToList();
        if (listed.Count == 0)
            return [];

        var children = new List<Node>();

        if (note != null)
            children.Add(new TextNode($"   {note}", ColourRole.Muted));

        foreach (var piece in listed)
            children.Add(Piece(label, piece));

        return
        [
            new SpacingNode(),
            new CollapsibleNode(
                CollectionPresenter.CollapsePrefix + label,
                $"{label} ({listed.Count})",
                children,
                Colour: stale ? ColourRole.Warning : null),
        ];
    }

    /// <remarks>
    /// "also", because that word is the whole row. Every piece here exists twice - the one being
    /// held, under the heading that says where, and a second the collection has - and without it the
    /// row reads as a claim about where this copy is, which the heading has already answered.
    /// </remarks>
    private static RowNode Piece(string label, CarriedPiece piece)
    {
        var insideOutfit = piece.StoredIn == OwnershipSource.Outfit;
        var quantity = piece.Quantity > 1 ? $"  x{piece.Quantity}" : string.Empty;

        var tooltip = new List<Node>
        {
            new TextNode(piece.Name),

            // Both places, in the hover, even though the heading has one of them. A tooltip that
            // repeated only the collection half never said what the row is actually reporting - two
            // copies, and here is each one.
            new TextNode($"{label}.", ColourRole.Muted),
            new TextNode(CollectionSnapshot.SecondCopy(piece.StoredIn), ColourRole.Muted),
        };

        if (piece.ArmoireWouldTake)
            tooltip.Add(new TextNode("The Armoire would take this too.", ColourRole.Muted));

        return new RowNode(
            [new IconNode(piece.IconId, 20), new TextNode($"{piece.Name}{quantity}")],
            tooltip,
            new ItemContext(piece.ItemId, piece.Name),
            // Shorter than the plugin's usual wording for this state, which is "part of a stored
            // outfit set" and is both too long for the space and, after all that, a sentence about
            // set mechanics the game explains itself when a set is taken out. What the row has to
            // say is where the other copy is; the amber says it is a set's slot rather than a piece.
            new TextNode(
                insideOutfit
                    ? "- also in a stored outfit"
                    : $"- also {MissingItems.Describe(piece.StoredIn).ToLowerInvariant()}",
                insideOutfit ? ColourRole.Warning : ColourRole.Muted));
    }

    /// <summary>
    /// What the list cannot see, in as few words as it can be put.
    /// </summary>
    /// <remarks>
    /// Each of these changes what the list means, which is why they are on screen at all - but none
    /// of them needs a sentence.
    /// </remarks>
    private static IReadOnlyList<Node> Caveats(
        Plugin plugin, CollectionSnapshot snapshot, bool stale)
    {
        var built = new List<Node> { new SpacingNode() };

        if (stale)
        {
            built.Add(new TextNode(
                $"Dresser snapshot {Format.Age(plugin.Ownership.DresserUpdatedUtc!.Value)} old.",
                ColourRole.Warning));
        }

        // What the amber rows mean, said only when there are some. The row itself says which pieces
        // they are and the hover says what to do about it; this is here so the colour is not a thing
        // to work out.
        // What the colour means, and nothing about what follows from it. That a set comes out whole
        // is the game's to explain and it does, on the screen where it would happen.
        if (snapshot.Carried!.OnlyInsideAnOutfit.Count > 0)
        {
            built.Add(new TextNode(
                "Amber: the second copy is inside a stored outfit set.",
                ColourRole.Muted,
                Wrap: true));
        }

        if (!snapshot.Carried.SaddlebagReadable)
            built.Add(new TextNode("Saddlebag unreadable away from a bell.", ColourRole.Muted));

        // Which retainers this could not look at, since an unvisited one is indistinguishable from
        // an empty one on the list itself.
        if (plugin.Ownership.Retainers.Count == 0)
        {
            built.Add(new TextNode(
                "No retainer has been read - open one at a bell to include it.", ColourRole.Muted));
        }

        return built;
    }
}
