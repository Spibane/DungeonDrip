using System;
using System.Collections.Generic;
using System.Text;
using DungeonDrip.Presentation;
using KamiToolKit.BaseTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

// The two vocabularies collide by design - a description's TextNode and the game's are the same
// idea at either end of this file - so each is named for which side it is on.
using TextNode = DungeonDrip.Presentation.TextNode;
using IconNode = DungeonDrip.Presentation.IconNode;
using GameText = KamiToolKit.Nodes.TextNode;
using GameIcon = KamiToolKit.Nodes.IconImageNode;

namespace DungeonDrip.Native;

/// <summary>Builds a <see cref="Node"/> description into game interface nodes.</summary>
/// <remarks>
/// The counterpart to <c>Windows.ImGuiRenderer</c>, and like it, a walker holding no decision about
/// what any surface shows. If a question about the gear or about wording is being answered here,
/// it is in the wrong file and the other renderer will have to answer it again.
///
/// The two differ in when they run, and that difference is the whole reason the description exists.
/// ImGui rebuilds its whole window every frame, so a changed row costs nothing to express. These
/// nodes are allocated objects the game owns, so a rebuild is real work and has to be driven off a
/// change rather than off the clock - see <see cref="PanelBody"/>.
/// </remarks>
internal static class NativeRenderer
{
    /// <summary>
    /// One row's height, and the widths of the two fixed columns before the name.
    /// </summary>
    /// <remarks>
    /// The row has to be at least as tall as the icon or consecutive icons overlap, which is what
    /// the first cut got wrong: nothing clips a node to its row, so an icon taller than the step
    /// between rows simply draws over its neighbour.
    ///
    /// The two leading columns are fixed widths rather than measured, so the names line up down the
    /// list whichever marker each row carries - the same reason the ImGui panel pushes the
    /// fixed-width icon font for its glyph.
    /// </remarks>
    private const float RowHeight = 28f;

    private const float GlyphWidth = 20f;
    private const float IconSize = 24f;

    /// <summary>
    /// The size the game sets its own list rows in.
    /// </summary>
    /// <remarks>
    /// A text node defaults to 12, and the shop window beside this one is 14 - so the default made
    /// the panel read as a smaller, separate thing rather than as a column of the same list.
    /// </remarks>
    private const uint FontSize = 14;

    /// <summary>Indent before the marker, so it is not against the window's inside edge.</summary>
    private const float RowIndent = 6f;

    /// <summary>Space between the marker, the icon and the name.</summary>
    private const float ColumnGap = 4f;

    /// <summary>How big a game item icon actually is, as opposed to how big it is drawn.</summary>
    private const float GameIconSize = 40f;


    /// <summary>
    /// Builds a list of rows into a vertical list, replacing whatever was there.
    /// </summary>
    /// <remarks>
    /// The old nodes are disposed rather than reused. Reusing them by index is the obvious
    /// optimisation and is wrong for this list: rows are inserted and removed in the middle as the
    /// filters change, so index N is not the same piece between two builds, and a reused row that
    /// kept its tooltip would describe the piece that used to be there.
    /// </remarks>
    /// <param name="reservedRight">
    /// Width at the right edge a row must not reach into. The scroll bar sits over the list rather
    /// than beside it - the list is given the full width and the bar is drawn on top of its last
    /// few pixels - so nothing about the list's own size keeps a long name out from under it.
    /// </param>
    public static void Fill(
        NativeContextMenu menu, HeaderState headers, VerticalListNode list,
        IReadOnlyList<Node> nodes, float reservedRight, Action onFoldChanged)
    {
        var width = list.Width - reservedRight;

        list.Clear();

        foreach (var node in nodes)
        {
            var built = Build(menu, headers, node, width, onFoldChanged);
            if (built != null)
                list.AddNode(built);
        }

        list.RecalculateLayout();
    }

    private static NodeBase? Build(
        NativeContextMenu menu, HeaderState headers, Node node, float width,
        Action onFoldChanged) => node switch
    {
        RowNode row => Row(menu, row, width),
        CollapsibleNode group => Collapsible(menu, headers, group, width, onFoldChanged),
        _ => null,
    };

    /// <summary>
    /// A heading with its rows folded under it, opening as the user last left it.
    /// </summary>
    /// <remarks>
    /// The folded state is read and written through the same <see cref="HeaderState"/> the ImGui
    /// panels use, so a heading folded in one look is folded in the other. Which config list the
    /// deviation goes in is the subtle part and neither backend should be deciding it twice.
    ///
    /// A native header keeps its children and hides them, where ImGui simply stops drawing them -
    /// so this attaches every row up front and lets the header do the hiding.
    ///
    /// <b>The heading is hard to read on the two light themes, and that is a standing limit.</b>
    /// The header bar is a dark texture under every theme, so its text wants to be pale in all
    /// four - but the library colours the label from the themed body text colour, which goes dark
    /// brown on the light themes and leaves dark text on a dark bar.
    ///
    /// It is the one place in the panel where following the theme is the wrong answer, and it
    /// cannot be corrected from here: the label sits on a node the library keeps private. Exposing
    /// it is a one-line change upstream and the fix is waiting on that rather than on a different
    /// approach. Carrying the change as a local patch was tried and rejected - it pins the
    /// submodule to a commit no clone can fetch, which trades a colour for a build.
    /// </remarks>
    private static NodeBase Collapsible(
        NativeContextMenu menu, HeaderState headers, CollapsibleNode group, float width,
        Action onFoldChanged)
    {
        var header = new CollapsingHeaderNode
        {
            String = group.Label,
            Width = width,
            FitWidth = true,
            IsCollapsed = !headers.IsOpen(group.Key),
            IsVisible = true,
        };

        // Folding changes this header's height but nothing tells the list holding it, so without
        // the callback the rows below stay where they were: a collapsed group leaves a hole, and
        // reopening it draws over the group beneath. The scroll extent moves with it.
        header.OnCollapse = () =>
        {
            headers.Record(group.Key, false);
            onFoldChanged();
        };

        header.OnUncollapse = () =>
        {
            headers.Record(group.Key, true);
            onFoldChanged();
        };

        foreach (var child in group.Children)
        {
            var built = Build(menu, headers, child, width, onFoldChanged);
            if (built != null)
                header.AddNode(built);
        }

        return header;
    }

    private static NodeBase Row(NativeContextMenu menu, RowNode row, float width)
    {
        var line = new HorizontalListNode
        {
            Size = new System.Numerics.Vector2(width, RowHeight),
            ItemSpacing = ColumnGap,
            FirstItemSpacing = RowIndent,
            IsVisible = true,
        };

        // What the name has left after the fixed columns and the gaps between them. A text node
        // given no width does not shrink to fit - it reports zero, the row lays the next part out
        // on top of it, and the name ends up over the icon.
        var remaining = width - RowIndent - GlyphWidth - IconSize - (ColumnGap * 3f);

        foreach (var part in row.Parts)
        {
            var node = Part(part, remaining);
            if (node != null)
                line.AddNode(node);
        }

        // One tooltip on the row rather than on each part, so the whole line answers rather than
        // only the words. KamiToolKit registers the hover events and the collision flag itself.
        if (row.Tooltip is { Count: > 0 })
            line.TextTooltip = Tooltip(row.Tooltip);

        if (row.Context is { } context)
            menu.Attach(line, context.ItemId, context.Name);

        return line;
    }

    private static NodeBase? Part(Node part, float textWidth) => part switch
    {
        // A text mark rather than an icon, for now - see NativeGlyphs on why a guessed icon id is
        // worse than a visible placeholder. Fixed width so the names line up whichever mark a row
        // carries, which is the same reason the ImGui panels push the fixed-width icon font.
        // Centred in its column rather than left-aligned in it, so a one-character mark sits under
        // the marks above and below it instead of hugging the window's edge.
        GlyphNode glyph => Text(
            NativeGlyphs.For(glyph.Token), glyph.Colour, GlyphWidth, AlignmentType.Center),

        IconNode icon => Icon(icon.IconId),

        TextNode text => Text(text.Text, text.Colour, textWidth),

        _ => null,
    };

    /// <summary>An item icon, scaled down to the row rather than cropped to it.</summary>
    /// <remarks>
    /// Both halves of this are needed and neither is obvious. A node's Size is how much of the
    /// texture is shown, not how big it is drawn, so a 24px node over a 40px icon shows the icon's
    /// top-left corner - <see cref="GameIcon.FitTexture"/> is what makes it scale instead.
    ///
    /// And <see cref="SimpleImageNode.TextureSize"/> has to say how big the icon really is. It
    /// defaults to 32, game item icons are 40, and the eight pixels of difference are the sliver
    /// that was being cut off every icon.
    /// </remarks>
    private static GameIcon Icon(ushort iconId) => new()
    {
        IconId = iconId,
        TextureSize = new System.Numerics.Vector2(GameIconSize, GameIconSize),
        FitTexture = true,
        Size = new System.Numerics.Vector2(IconSize, IconSize),

        // Centred against the row rather than sat on its top edge, since the icon is shorter than
        // the line and the eye reads the two as one row.
        Y = (RowHeight - IconSize) / 2f,
        IsVisible = true,
    };

    /// <remarks>
    /// <see cref="AlignmentType.Left"/> is left-aligned and <em>vertically centred</em>, which is
    /// what makes the text sit on the same line as the icon beside it rather than on the row's top
    /// edge.
    /// </remarks>
    private static GameText Text(
        string content, ColourRole role, float width, AlignmentType alignment = AlignmentType.Left)
    {
        var (fill, outline) = NativePalette.For(role);

        return new GameText
        {
            String = content,
            TextColor = fill,
            TextOutlineColor = outline,
            // Ellipsis, because a text node otherwise draws past its own width rather than
            // stopping at it - which is how a long name ended up under the scroll bar. Nothing is
            // lost by cutting it: the row's tooltip opens with the name in full.
            TextFlags = TextFlags.Edge | TextFlags.Ellipsis,
            FontSize = FontSize,
            AlignmentType = alignment,
            Size = new System.Numerics.Vector2(width, RowHeight),
            IsVisible = true,
        };
    }

    /// <summary>
    /// Flattens a tooltip's lines into the one string the game's tooltip takes.
    /// </summary>
    /// <remarks>
    /// <b>The colours are dropped, and this is the honest limit of the arrangement.</b> The game's
    /// text tooltip is a single string, and colouring runs inside it means UIForeground payloads
    /// keyed on UIColor rows rather than the RGBA the rest of the plugin speaks - so the amber on
    /// an uncertain note reads as plain text here. Every tooltip in the plugin puts its caveat on
    /// its own line and words it as a caveat, so nothing is unsayable, but the eye no longer finds
    /// it first.
    ///
    /// Worth fixing with a role-to-UIColor mapping once those row ids are known, which is the same
    /// missing piece <see cref="NativePalette"/> is waiting on.
    /// </remarks>
    private static string Tooltip(IReadOnlyList<Node> lines)
    {
        var text = new StringBuilder();

        foreach (var line in lines)
        {
            switch (line)
            {
                case TextNode entry:
                    text.AppendLine(entry.Text);
                    break;

                case SpacingNode:
                    text.AppendLine();
                    break;
            }
        }

        return text.ToString().TrimEnd();
    }
}
