using System.Collections.Generic;

namespace DungeonDrip.Presentation;

/// <summary>A table of roulette advice: what to queue, and what it stands to give.</summary>
/// <remarks>
/// <b>The column weights live here rather than in either renderer, and that is the point of the
/// type.</b> One look has a table widget that divides a width by weights and the other has to do
/// the arithmetic itself, so the numbers would otherwise be written twice and corrected once - and
/// a column that is a third of the width in one look and a quarter in the other is exactly the kind
/// of drift nobody diffs for.
///
/// A row may carry fewer cells than there are headings. A roulette with nothing worth saying about
/// it answers in the second column and stops, rather than filling two more with dashes.
/// </remarks>
internal sealed record RouletteTableModel(
    IReadOnlyList<string> Headings,
    IReadOnlyList<float> Weights,
    IReadOnlyList<RouletteRowModel> Rows);

/// <summary>One roulette's row, and what the whole row says on hover.</summary>
/// <remarks>
/// The tooltip is on the row rather than on a cell because it is about the roulette rather than
/// about any one number in it - which is also why both looks make the whole row hoverable rather
/// than just its name.
/// </remarks>
internal sealed record RouletteRowModel(
    IReadOnlyList<TextNode> Cells,
    IReadOnlyList<Node> Tooltip);
