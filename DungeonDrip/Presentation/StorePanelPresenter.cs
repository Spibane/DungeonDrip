using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>What a panel beside a storage box shows, described for either backend to draw.</summary>
/// <remarks>
/// The mirror of <see cref="PanelPresenter"/>, and a separate file because the two answer opposite
/// questions about the same gear. That one asks "do I own this" of what the game is offering; this
/// one asks "should this go in" of what the character is carrying, which is why the marker column
/// says nothing here and goes to where the piece is instead.
///
/// Both boxes read from this. The Glamour Dresser and the Armoire ask the same question of different
/// containers, so what differs between their panels is the heading and one tooltip note - small
/// enough to name individually rather than let each build its own row and drift a field at a time.
/// </remarks>
internal static class StorePanelPresenter
{
    /// <summary>
    /// One row: where the piece is, its icon, its name and how many, and what all that means.
    /// </summary>
    /// <remarks>
    /// The glyph is the location rather than the ownership marker. Every row here is one the box has
    /// not got by construction, so the ownership marker would say the same thing on all of them, and
    /// the column goes to the part that changes what has to be done about it.
    ///
    /// Amber when the box would turn the piece down and muted otherwise - the colour carries whether
    /// this row is actionable, and the tooltip carries why not.
    /// </remarks>
    /// <param name="notes">
    /// What this particular box wants to add between the location and the refusal. Empty at the
    /// Armoire, which has nothing to say that the location has not already said.
    /// </param>
    public static RowNode Row(HeldGearRow row, IReadOnlyList<Node>? notes = null)
    {
        var tooltip = new List<Node>
        {
            new TextNode(row.Name),
            new TextNode(Describe(row.Location), ColourRole.Muted),
        };

        if (notes != null)
            tooltip.AddRange(notes);

        if (row.Blocked != null)
            tooltip.Add(new TextNode(row.Blocked, ColourRole.Warning));

        tooltip.Add(new SpacingNode());
        tooltip.Add(new TextNode("Right-click for options.", ColourRole.Muted));

        // Stacks are one row saying how many rather than one row each, since the decision is about
        // the piece and not about a copy of it.
        var quantity = row.Quantity > 1 ? $"  x{row.Quantity}" : string.Empty;

        return new RowNode(
            [
                new GlyphNode(
                    MarkerGlyph.For(row.Location),
                    row.Blocked == null ? ColourRole.Muted : ColourRole.Warning),
                new IconNode(row.IconId, 20),
                new TextNode($"{row.Name}{quantity}"),
            ],
            tooltip,
            new ItemContext(row.ItemId, row.Name));
    }

    /// <summary>
    /// What the dresser adds to a row's tooltip, which is the two things only it knows.
    /// </summary>
    /// <remarks>
    /// The first note is only on this panel: beside the dresser a slot-free home is a way out of
    /// spending a slot, while beside the Armoire it would be saying that the box being stood at is
    /// the box to use.
    ///
    /// The second is the caveat that bites hardest here. Under "owned only if every outfit with that
    /// piece is stored", a row can be a piece the dresser is visibly holding inside one of its sets -
    /// correct, and indistinguishable from a bug unless the row says which rule put it on the list.
    /// </remarks>
    public static IReadOnlyList<Node> DresserNotes(DresserAddRow row)
    {
        var notes = new List<Node>(3);

        if (row.ArmoireWouldTake)
            notes.Add(new TextNode("The Armoire would take this, at no dresser slot.", ColourRole.Good));

        if (row.OutfitsStored <= 0 || row.OutfitsTotal <= row.OutfitsStored)
            return notes;

        notes.Add(new TextNode(
            $"Stored inside {row.OutfitsStored} of the {row.OutfitsTotal} outfit sets using it.",
            ColourRole.Warning));

        // Split across lines by hand: a tooltip does not wrap, and one sentence of this length would
        // drag it across the screen.
        notes.Add(new TextNode(
            "Your setting only counts that as owned once every one of them is", ColourRole.Warning));

        notes.Add(new TextNode(
            "stored, so storing this copy on its own settles it.", ColourRole.Warning));

        return notes;
    }

    /// <summary>
    /// How full the dresser is, and whether what is on the list would even fit.
    /// </summary>
    /// <remarks>
    /// The decision this panel exists to support is "which of these do I put in", and that only
    /// becomes a decision when there is not room for all of them - hence the amber at zero free.
    ///
    /// Empty when the box has not been read, because "0 of 0 slots used" is a claim about the
    /// dresser rather than an admission that it has not been looked in.
    /// </remarks>
    public static IReadOnlyList<Node> DresserHeader(int used, int capacity) => capacity <= 0
        ? []
        : [new TextNode(
            $"{used} of {capacity} slots used.",
            capacity - used <= 0 ? ColourRole.Warning : ColourRole.Muted)];

    /// <summary>
    /// How many held pieces the Armoire already has.
    /// </summary>
    /// <remarks>
    /// The count the game's own store screen cannot give, which is most of why that panel exists:
    /// those pieces are on the game's list, they will be offered, and each will be refused on the
    /// attempt. Said as a number rather than listed, because they are the rows the panel
    /// deliberately does not have.
    /// </remarks>
    public static IReadOnlyList<Node> ArmoireHeader(int duplicates) => duplicates == 0
        ? []
        : [new TextNode(
            duplicates == 1
                ? "1 piece on you is in the Armoire already."
                : $"{duplicates} pieces on you are in the Armoire already.",
            ColourRole.Muted)];

    private static string Describe(CarryLocation location) => location switch
    {
        CarryLocation.Bags => "In your bags",
        CarryLocation.Armoury => "In your armoury chest",
        CarryLocation.Equipped => "Equipped",
        _ => "In your saddlebag",
    };
}
