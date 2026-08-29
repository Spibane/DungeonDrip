using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Presentation;

/// <summary>What the loot companion shows, described for either backend to draw.</summary>
/// <remarks>
/// The one surface in the plugin that refuses to show its list rather than showing it with a
/// caveat. Everywhere else a stale snapshot gets a warning above the answer, because those are
/// decisions that can be come back to; a roll cannot be undone and cannot be checked in the moment,
/// so a confident guess is worse than an admission.
///
/// No grouping and no headings, unlike the panels, and that is a design commitment rather than an
/// omission - see <c>Windows.LootCompanionWindow</c>. The list is a handful of rows on screen for
/// the length of a roll, and anything that reorders itself while somebody is about to click is
/// hostile.
/// </remarks>
internal static class LootCompanionPresenter
{
    /// <summary>
    /// The whole window: what can be trusted, what is up for roll, and what is worth taking.
    /// </summary>
    /// <remarks>
    /// The order is load-bearing at the top. No dresser data at all returns before the list is
    /// described, because there is no answer to give; a stale one returns the warning and the list,
    /// since the answer is probably right and saying which parts might not be is not possible.
    /// </remarks>
    public static IReadOnlyList<Node> Build(IReadOnlyList<LootRoll> rolls, OwnershipTracker ownership)
    {
        if (!ownership.HasDresserData)
        {
            return
            [
                new TextNode("No dresser data.", ColourRole.Warning),
                new TextNode("Open your Glamour Dresser.", ColourRole.Warning),
            ];
        }

        var built = new List<Node>(rolls.Count + 3);

        if (ownership.IsDresserStale)
        {
            built.Add(new TextNode("Dresser data is stale -", ColourRole.Warning));
            built.Add(new TextNode("these may be wrong.", ColourRole.Warning));
            built.Add(new RuleNode());
        }

        var anyMissing = false;

        foreach (var roll in rolls)
        {
            if (!roll.Owned)
                anyMissing = true;

            built.Add(Row(roll));
        }

        // Said when nothing on the list is new, not when the list is empty: a roll of five pieces
        // already collected is the answer somebody wanted, and an unmarked list of five names does
        // not read as one.
        if (!anyMissing)
            built.Add(new TextNode("Nothing new here.", ColourRole.Muted));

        return built;
    }

    /// <summary>
    /// One rolled item: its icon and its name, marked when it is not collected.
    /// </summary>
    /// <remarks>
    /// The mark is doing work the colour cannot be left to do alone. This is the one list read under
    /// a countdown, often over a dungeon's own effects, and a row that differs from its neighbours
    /// only in shade is a row that gets missed.
    ///
    /// It leads the name rather than following it, and the rows it is absent from are not padded to
    /// match. A marked row starting further right than the ones around it is the fastest thing on
    /// the list to find, which is the whole job here.
    ///
    /// Trailing it went wrong once for a reason worth remembering: the row gave the name too little
    /// width, and since a name is drawn with an ellipsis, what got cut was the end of it - which was
    /// exactly where the mark sat. Leading it cannot be lost that way.
    ///
    /// It took two goes to work out that it cannot be a character. The marks that look like part of
    /// the game are icons the game substitutes into text, not codepoints in a font, so a typed arrow
    /// and then a private-use character both came out as nothing at all in both looks.
    ///
    /// No tooltip, deliberately: hovering costs seconds nobody has here. The right-click menu stays,
    /// because it is how a piece gets looked up after the roll rather than during it.
    /// </remarks>
    private static RowNode Row(LootRoll roll) => new(
        roll.Owned
            ?
            [
                new IconNode(roll.IconId, 20),
                new TextNode(roll.Name, ColourRole.Muted),
            ]
            :
            [
                new SymbolNode(GameSymbol.Wanted),
                new IconNode(roll.IconId, 20),
                new TextNode(roll.Name, ColourRole.Missing),
            ],
        Tooltip: null,
        new ItemContext(roll.ItemId, roll.Name));
}
