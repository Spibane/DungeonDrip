using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

using GameText = KamiToolKit.Nodes.TextNode;

namespace DungeonDrip.Native;

/// <summary>The roulette advice as a table, built out of rows rather than out of a table.</summary>
/// <remarks>
/// <b>Deliberately not an imitation of the overlay's table widget.</b> That one sorts, resizes by
/// drag and paints alternating row backgrounds; none of those are things this table does or wants,
/// and reproducing the machinery to leave it switched off would be a great deal of node tree spent
/// on nothing. What a reader needs is four columns that line up and a row that answers on hover, and
/// that is a list of rows with fixed column widths.
///
/// The widths come from <see cref="RouletteTableModel"/> rather than from here, so the columns are
/// the same fractions in both looks - see there on why that is the point of the model existing.
///
/// <b>Ruled between rows rather than striped behind them.</b> The overlay shades alternate rows,
/// which is the same job done the way its toolkit offers - here it would mean a nine-grid behind
/// every other row, which is a great many nodes to spend on a stripe. A line between rows costs one
/// node each and does more, because it also says where a row ends when its cells are short.
/// </remarks>
internal static class NativeRouletteTable
{
    private const float RowHeight = 24f;
    private const float HeadingHeight = 22f;
    private const uint FontSize = 14;

    /// <summary>Space at the right of each cell, so a long value cannot touch the next column.</summary>
    private const float CellGap = 6f;

    /// <summary>How thick the game draws a dividing rule, and how much of one is wanted here.</summary>
    /// <remarks>
    /// Faint on purpose. A rule at full strength between every row turns a short table into a grid
    /// of boxes and competes with the window's own divisions; at a third it reads as guidance for
    /// the eye and nothing else. The one under the headings is left at full strength, because that
    /// division is real - it separates what the columns are from what they hold.
    /// </remarks>
    private const float RuleHeight = 4f;

    private const float RuleAlpha = 0.35f;

    public static NodeBase Build(RouletteTableModel model, float width)
    {
        var table = new VerticalListNode
        {
            Width = width,
            FitContents = true,
            IsVisible = true,
        };

        table.AddNode(Headings(model, width));
        table.AddNode(Rule(width, 1f));

        for (var index = 0; index < model.Rows.Count; index++)
        {
            table.AddNode(Row(model, model.Rows[index], width));

            // Between rows and not after the last, so the table ends where its last row does rather
            // than with a line under nothing.
            if (index < model.Rows.Count - 1)
                table.AddNode(Rule(width, RuleAlpha));
        }

        table.RecalculateLayout();
        return table;
    }

    private static NodeBase Rule(float width, float alpha) => new HorizontalLineNode
    {
        Size = new Vector2(width, RuleHeight),
        Alpha = alpha,
        IsVisible = true,
    };

    private static NodeBase Headings(RouletteTableModel model, float width)
    {
        var line = new HorizontalListNode
        {
            Size = new Vector2(width, HeadingHeight),
            IsVisible = true,
        };

        for (var column = 0; column < model.Headings.Count; column++)
            line.AddNode(Cell(model.Headings[column], ColourRole.Muted, Column(model, width, column)));

        return line;
    }

    /// <remarks>
    /// The tooltip is on the row rather than on any cell, which is what makes it reachable from
    /// anywhere along the row - the same thing the overlay gets from a selectable spanning every
    /// column, by a different route.
    ///
    /// A row with fewer cells than there are columns simply stops. Its second cell is given the rest
    /// of the width, since what it holds is a sentence rather than a value in a column.
    /// </remarks>
    private static NodeBase Row(RouletteTableModel model, RouletteRowModel row, float width)
    {
        var line = new HorizontalListNode
        {
            Size = new Vector2(width, RowHeight),
            IsVisible = true,
        };

        var short_ = row.Cells.Count < model.Headings.Count;

        for (var column = 0; column < row.Cells.Count; column++)
        {
            var cell = row.Cells[column];

            var cellWidth = short_ && column == row.Cells.Count - 1
                ? width - Column(model, width, 0)
                : Column(model, width, column);

            line.AddNode(Cell(cell.Text, cell.Colour, cellWidth));
        }

        if (row.Tooltip.Count > 0)
            line.TextTooltip = NativeRenderer.Tooltip(row.Tooltip);

        return line;
    }

    private static float Column(RouletteTableModel model, float width, int column) =>
        width * model.Weights[column];

    private static GameText Cell(string content, ColourRole role, float width)
    {
        var (fill, outline) = NativePalette.For(role);

        return new GameText
        {
            String = content,
            TextColor = fill,
            TextOutlineColor = outline,
            TextFlags = TextFlags.Edge | TextFlags.Ellipsis,
            FontSize = FontSize,
            AlignmentType = AlignmentType.Left,
            Size = new Vector2(System.Math.Max(0f, width - CellGap), RowHeight),
            IsVisible = true,
        };
    }
}
