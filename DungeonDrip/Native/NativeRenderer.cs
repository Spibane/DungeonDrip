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

    private const float GlyphWidth = 16f;
    private const float IconSize = 24f;

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
    public static void Fill(VerticalListNode list, IReadOnlyList<RowNode> rows)
    {
        // The list's own width, not the window's. The two differ by the window's padding and by
        // whatever the scrollbar takes, and sizing a row to the window is how a name ends up
        // running under the bar.
        var width = list.Width;

        list.Clear();

        foreach (var row in rows)
            list.AddNode(Row(row, width));

        list.RecalculateLayout();
    }

    private static NodeBase Row(RowNode row, float width)
    {
        var line = new HorizontalListNode
        {
            Size = new System.Numerics.Vector2(width, RowHeight),
            ItemSpacing = ColumnGap,
            IsVisible = true,
        };

        // What the name has left after the fixed columns and the gaps between them. A text node
        // given no width does not shrink to fit - it reports zero, the row lays the next part out
        // on top of it, and the name ends up over the icon.
        var remaining = width - GlyphWidth - IconSize - (ColumnGap * 3f);

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

        return line;
    }

    private static NodeBase? Part(Node part, float textWidth) => part switch
    {
        // A text mark rather than an icon, for now - see NativeGlyphs on why a guessed icon id is
        // worse than a visible placeholder. Fixed width so the names line up whichever mark a row
        // carries, which is the same reason the ImGui panels push the fixed-width icon font.
        GlyphNode glyph => Text(NativeGlyphs.For(glyph.Token), glyph.Colour, GlyphWidth),

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
    private static GameText Text(string content, ColourRole role, float width)
    {
        var (fill, outline) = NativePalette.For(role);

        return new GameText
        {
            String = content,
            TextColor = fill,
            TextOutlineColor = outline,
            TextFlags = TextFlags.Edge,
            AlignmentType = AlignmentType.Left,
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
