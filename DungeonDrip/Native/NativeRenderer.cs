using System;
using System.Collections.Generic;
using DungeonDrip.Presentation;
using DungeonDrip.Windows;
using Lumina.Text;
using Lumina.Text.ReadOnly;
using KamiToolKit.BaseTypes;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

// The two vocabularies collide by design - a description's TextNode and the game's are the same
// idea at either end of this file - so each is named for which side it is on.
using TextNode = DungeonDrip.Presentation.TextNode;
using IconNode = DungeonDrip.Presentation.IconNode;
using GameText = KamiToolKit.Nodes.TextNode;
using GameIcon = KamiToolKit.Nodes.IconImageNode;
using GameImage = KamiToolKit.Nodes.ImGuiImageNode;
using ProgressNode = DungeonDrip.Presentation.ProgressNode;

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

    /// <summary>The shortest a row is allowed to be, whatever it holds.</summary>
    /// <remarks>
    /// A row of nothing but text still has to be tall enough to read and to hit. Below this the
    /// lines of a long list run together.
    /// </remarks>
    private const float MinRowHeight = 22f;

    /// <summary>Room above and below the tallest thing in a row.</summary>
    private const float RowPadding = 4f;

    private const float GlyphWidth = 20f;
    private const float IconSize = 24f;

    /// <summary>The column one of the game's own icons gets, which is about a character wide.</summary>
    private const float SymbolWidth = 18f;

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

    /// <summary>
    /// How tall a line of a notice is, which is less than a row.
    /// </summary>
    /// <remarks>
    /// A row is as tall as the item icon in it. A notice has no icon, so a row's height there would
    /// space two sentences as though something were missing between them.
    /// </remarks>
    private const float NoticeLineHeight = 20f;

    /// <summary>How thick the game draws a dividing rule.</summary>
    private const float RuleHeight = 4f;

    /// <summary>How big a game item icon actually is, as opposed to how big it is drawn.</summary>
    private const float GameIconSize = 40f;

    /// <summary>
    /// A gap where a rule would overstate the division.
    /// </summary>
    /// <remarks>
    /// Smaller than it reads, because the list this sits in spaces its own items as well - eight
    /// pixels of gap plus the list's own made a blank line out of what should be a breath.
    /// </remarks>
    private const float SpacingHeight = 4f;

    private const float ButtonWidth = 120f;
    private const float ButtonHeight = 28f;
    private const float SmallButtonWidth = 180f;
    private const float SmallButtonHeight = 22f;

    /// <summary>How thick the occupancy bar is drawn.</summary>
    private const float BarHeight = 12f;

    /// <summary>
    /// How much narrower than its node a wrapped line is assumed to be.
    /// </summary>
    /// <remarks>
    /// A word that will not fit is moved down whole, so the last word of a line ends somewhere short
    /// of the edge rather than at it. Counting lines against the full width therefore counts too
    /// few, and one line too few is a line drawn over the next thing along.
    /// </remarks>
    private const float WrapSlack = 24f;

    /// <summary>
    /// How far apart the lines of a wrapped sentence sit.
    /// </summary>
    /// <remarks>
    /// A text node defaults to twelve, which is the size of the font it was written for and smaller
    /// than the one used here - so a wrapped sentence came out with its lines touching.
    ///
    /// Slightly tighter than the step between two separate lines, and deliberately: lines of one
    /// sentence belong to each other, and spacing them as far apart as two sentences makes a
    /// paragraph read as a list.
    /// </remarks>
    private const uint WrappedLineSpacing = 18;

    /// <summary>
    /// How much of a row's remaining width the name keeps when there is a note after it.
    /// </summary>
    /// <remarks>
    /// A fixed share rather than a measurement, because measuring text is something the overlay's
    /// toolkit offers and a node tree does not. Names are longer than the notes after them almost
    /// always, which is why the split is not even.
    /// </remarks>
    private const float NameShareWithTrailing = 0.62f;


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
        NativeContextMenu menu, GlyphTextures glyphs, HeaderState headers, VerticalListNode list,
        IReadOnlyList<Node> nodes, float reservedRight, Action onFoldChanged)
    {
        list.Clear();
        Append(menu, glyphs, headers, list, nodes, reservedRight, onFoldChanged);
        list.RecalculateLayout();
    }

    /// <summary>
    /// Adds a description to whatever is already in a list, without clearing or re-stacking it.
    /// </summary>
    /// <remarks>
    /// For the surfaces that put their own widgets among described ones - a search field between a
    /// banner and a list, a tab strip above one. Those cannot be described, because they hold state
    /// of their own, so the alternative would be a node kind per widget that only one surface ever
    /// uses.
    ///
    /// The caller re-stacks when it has finished adding, which is why this does not.
    /// </remarks>
    public static void Append(
        NativeContextMenu menu, GlyphTextures glyphs, HeaderState headers, VerticalListNode list,
        IReadOnlyList<Node> nodes, float reservedRight, Action onFoldChanged)
    {
        var width = list.Width - reservedRight;

        foreach (var node in nodes)
        {
            var built = Build(menu, glyphs, headers, node, width, onFoldChanged);
            if (built != null)
                list.AddNode(built);
        }
    }

    /// <summary>
    /// Fills a list with what a panel says above its rows: lines of text, and rules between them.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Fill"/> because the two go to different places rather than because
    /// they draw different things. This is the part that must not scroll - a caveat about how far
    /// the list can be trusted is worth nothing if it scrolls off the top of the list it is about -
    /// so it hangs above the scroll area and the rows hang inside it.
    ///
    /// The list is expected to fit itself to its contents, since what is in it is the difference
    /// between a full-strength warning, a milder one and nothing at all, and everything below has to
    /// move to suit.
    /// </remarks>
    public static void Notice(VerticalListNode list, IReadOnlyList<Node> nodes, float width)
    {
        list.Clear();

        foreach (var node in nodes)
        {
            var built = Line(node, width);
            if (built != null)
                list.AddNode(built);
        }

        list.RecalculateLayout();
    }

    private static NodeBase? Build(
        NativeContextMenu menu, GlyphTextures glyphs, HeaderState headers, Node node, float width,
        Action onFoldChanged) => node switch
    {
        RowNode row => Row(menu, glyphs, row, width),
        CollapsibleNode group => Collapsible(menu, glyphs, headers, group, width, onFoldChanged),
        TreeNode tree => Tree(menu, glyphs, headers, tree, width, onFoldChanged),
        TableNode table => NativeRouletteTable.Build(table.Model, width),

        // A list is not only rows. The loot companion says what it cannot answer and what it found
        // nothing new in, on lines between the rows rather than above them.
        _ => Line(node, width),
    };

    /// <summary>A line that is not a row: a sentence, a rule, a control, or a bar.</summary>
    private static NodeBase? Line(Node node, float width) => node switch
    {
        TextNode text => Wrapped(text, width),

        RuleNode => new HorizontalLineNode
        {
            Size = new System.Numerics.Vector2(width, RuleHeight),
            IsVisible = true,
        },

        SpacingNode => new ResNode
        {
            Size = new System.Numerics.Vector2(width, SpacingHeight),
            IsVisible = true,
        },

        ButtonNode button => new TextButtonNode
        {
            String = button.Label,
            OnClick = button.Press,

            // A small button is narrower as well as shorter. Full width would make the one that
            // reveals the rest of a list look like the list's own footer.
            Size = new System.Numerics.Vector2(
                button.Small ? SmallButtonWidth : ButtonWidth,
                button.Small ? SmallButtonHeight : ButtonHeight),

            IsVisible = true,
        },

        ToggleNode toggle => new CheckboxNode
        {
            String = toggle.Label,
            IsChecked = toggle.Value,
            OnClick = toggle.Set,
            TextTooltip = toggle.Tooltip ?? string.Empty,
            Size = new System.Numerics.Vector2(width, RowHeight),
            IsVisible = true,
        },

        ProgressNode progress => Bar(progress.Fraction, width),

        _ => null,
    };

    /// <summary>
    /// How full something is, as a bar.
    /// </summary>
    /// <remarks>
    /// <b>Sized before it is filled, and the order is the whole of it.</b> A bar's fill is stored as
    /// a width rather than as a fraction, so setting the fraction first multiplies it by a width of
    /// nothing - and then sizing the bar stretches the fill to the full width. It came out solid at
    /// every value, which reads as a box that is always full.
    /// </remarks>
    private static NodeBase Bar(float fraction, float width)
    {
        var bar = new ProgressBarNode
        {
            Size = new System.Numerics.Vector2(width, BarHeight),
            IsVisible = true,
        };

        bar.Progress = System.Math.Clamp(fraction, 0f, 1f);
        return bar;
    }

    /// <summary>
    /// A tree row, which is a heading in this look and is not in the other.
    /// </summary>
    /// <remarks>
    /// <b>A first cut, and the one place the two looks differ in shape rather than in colour.</b> The
    /// overlay draws these as bare arrows precisely so that thirty outfit sets do not read as thirty
    /// sections, and the nearest thing the game offers is the same framed bar its headings use. The
    /// library's own tree is data-bound to a row type rather than to arbitrary contents, so using it
    /// would mean describing these lists twice.
    ///
    /// What follows the label and what sits under it are folded into the header and the children,
    /// since a framed bar has nowhere else to put them, and the icon is dropped.
    /// </remarks>
    private static NodeBase Tree(
        NativeContextMenu menu, GlyphTextures glyphs, HeaderState headers, TreeNode tree,
        float width, Action onFoldChanged)
    {
        var children = new List<Node>();

        if (tree.Note != null)
            children.Add(tree.Note);

        children.AddRange(tree.Children);

        var label = tree.Trailing != null ? $"{tree.Label}   {tree.Trailing.Text}" : tree.Label;

        return Collapsible(
            menu, glyphs, headers,
            new CollapsibleNode(tree.Key, label, children, tree.DefaultOpen),
            width, onFoldChanged);
    }

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
        NativeContextMenu menu, GlyphTextures glyphs, HeaderState headers, CollapsibleNode group,
        float width, Action onFoldChanged)
    {
        var header = new CollapsingHeaderNode
        {
            String = group.Label,
            Width = width,
            FitWidth = true,
            IsVisible = true,
        };

        foreach (var child in group.Children)
        {
            var built = Build(menu, glyphs, headers, child, width, onFoldChanged);
            if (built != null)
                header.AddNode(built);
        }

        // <b>Set twice, to the wrong state and then the right one, and it has to be.</b> The header
        // only rearranges itself when the value actually changes, and a header built in the state it
        // already believes it is in therefore never rearranges at all - which left its bar drawn in
        // one place and the part of it that answered a click in another. Doing it once the children
        // are attached is what makes the library lay the whole thing out.
        //
        // Before the callbacks are hung, so the pass that fixes the layout does not read as the
        // player folding something.
        var open = headers.IsOpen(group.Key, group.DefaultOpen);

        header.IsCollapsed = open;
        header.IsCollapsed = !open;

        // Folding changes this header's height but nothing tells the list holding it, so without
        // the callback the rows below stay where they were: a collapsed group leaves a hole, and
        // reopening it draws over the group beneath. The scroll extent moves with it.
        header.OnCollapse = () =>
        {
            headers.Record(group.Key, false, group.DefaultOpen);
            onFoldChanged();
        };

        header.OnUncollapse = () =>
        {
            headers.Record(group.Key, true, group.DefaultOpen);
            onFoldChanged();
        };

        return header;
    }

    private static NodeBase Row(
        NativeContextMenu menu, GlyphTextures glyphs, RowNode row, float width)
    {
        // Sized to what the row holds rather than to a constant. The panels put a 24-pixel item icon
        // on every row and wanted the height; the collection's lists use icons two thirds that size,
        // and a fixed row spread thirty of them over half again as much window as they need.
        var height = RowHeightFor(row);

        // The indent is there to keep a marker off the window's inside edge. A row that opens with
        // words has no marker, and indenting it sets its text a few pixels right of every plain line
        // around it - which is exactly what it looked like.
        var indented = row.Parts.Count > 0 && row.Parts[0] is not TextNode;

        var line = new HorizontalListNode
        {
            Size = new System.Numerics.Vector2(width, height),
            ItemSpacing = ColumnGap,
            FirstItemSpacing = indented ? RowIndent : 0f,
            IsVisible = true,
        };

        // What the name has left after the parts beside it and the gaps between them. A text node
        // given no width does not shrink to fit - it reports zero, the row lays the next part out
        // on top of it, and the name ends up over the icon.
        //
        // Measured from the parts this row actually has, rather than from the panels' three columns.
        // Assuming a marker that a row does not carry takes twenty pixels off the name for nothing,
        // and since a name is drawn with an ellipsis what goes missing is the end of it - which on
        // the loot companion is exactly where the mark saying "you do not have this" sits.
        var taken = RowIndent + (ColumnGap * row.Parts.Count);

        foreach (var part in row.Parts)
        {
            taken += part switch
            {
                GlyphNode => GlyphWidth,
                SymbolNode => SymbolWidth,
                IconNode icon => icon.Size,
                _ => 0f,
            };
        }

        var remaining = System.Math.Max(0f, width - taken);

        // The name gives up part of its width when there is a note after it, since both share what
        // is left once the fixed columns are taken off.
        var nameWidth = row.Trailing != null ? remaining * NameShareWithTrailing : remaining;

        foreach (var part in row.Parts)
        {
            var node = Part(glyphs, part, nameWidth, height);
            if (node != null)
                line.AddNode(node);
        }

        if (row.Trailing != null)
        {
            line.AddNode(Text(
                row.Trailing.Text, row.Trailing.Colour, remaining - nameWidth, height: height));
        }

        // One tooltip on the row rather than on each part, so the whole line answers rather than
        // only the words. KamiToolKit registers the hover events and the collision flag itself.
        if (row.Tooltip is { Count: > 0 })
            line.TextTooltip = Tooltip(row.Tooltip);

        if (row.Context is { } context)
            menu.Attach(line, context.ItemId, context.Name);

        return line;
    }

    /// <summary>How tall a row has to be for the tallest thing in it.</summary>
    private static float RowHeightFor(RowNode row)
    {
        var tallest = 0f;

        foreach (var part in row.Parts)
        {
            if (part is IconNode icon)
                tallest = System.Math.Max(tallest, icon.Size);
        }

        return System.Math.Max(MinRowHeight, tallest + RowPadding);
    }

    private static NodeBase? Part(
        GlyphTextures glyphs, Node part, float textWidth, float rowHeight) => part switch
    {
        GlyphNode glyph => Marker(glyphs, glyph, rowHeight),

        SymbolNode symbol => Symbol(symbol.Symbol, rowHeight),

        IconNode icon => Icon(icon.IconId, icon.Size, rowHeight),

        TextNode text => Text(text.Text, text.Colour, textWidth, height: rowHeight),

        _ => null,
    };

    /// <summary>
    /// The mark at the head of a row: the same FontAwesome glyph the plugin overlays draw.
    /// </summary>
    /// <remarks>
    /// A picture of a font glyph rather than a game icon, and that is a decision rather than an
    /// expedient - see <see cref="GlyphTextures"/> on why a table of curated icon ids would have
    /// been guesses. It also keeps the readme's marker table true of both looks, since there is only
    /// one set of shapes to describe.
    ///
    /// Square and fixed width, so the names line up down the list whichever mark each row carries.
    /// That is the same reason the ImGui panels push the fixed-width icon font, and it is why the
    /// texture behind this is square too: a node stretches a texture to fill it, so an oblong one
    /// would come out leaning.
    ///
    /// <b>The colour is drawn into the texture rather than tinted onto the node.</b> Tinting was the
    /// first cut and came out wrong in a way worth recording: the green star rendered purple and the
    /// red cross blue, so a node's colour multiply is neither the scale nor the channel order it
    /// looks like. Drawing each mark in the colour it needs costs a texture per colour a mark is
    /// ever shown in - a handful, cached for the session - and takes the guessing out.
    ///
    /// The colour is <see cref="NativePalette.Solid"/> rather than the pair a text node is given,
    /// for the same reason a tooltip's is: a texture cannot carry a muted alpha to be composited
    /// later, so what goes in has to be faded already.
    /// </remarks>
    private static NodeBase Marker(GlyphTextures glyphs, GlyphNode glyph, float rowHeight)
    {
        var node = new GameImage
        {
            FitTexture = true,
            Size = new System.Numerics.Vector2(GlyphWidth, GlyphWidth),

            // Centred against the row rather than sat on its top edge, as the item icon is.
            Y = (rowHeight - GlyphWidth) / 2f,
            IsVisible = true,
        };

        // Nothing is remembered about this node. A mark not yet drawn stays invisible for as long
        // as it takes to draw, and the panel rebuilds when it lands - which is why there is no
        // callback here holding a row that may not survive the frame.
        node.Alpha = glyphs.TryLoad(node, Glyphs.For(glyph.Token), NativePalette.Solid(glyph.Colour))
            ? 1f
            : 0f;

        return node;
    }

    /// <summary>
    /// One of the game's own icons, which is a macro in a string rather than anything drawn.
    /// </summary>
    /// <remarks>
    /// A text node holding nothing but the icon. That is how the game puts one in its own text, and
    /// it is the only way to get one: these are substituted by the renderer when it meets the
    /// macro, so an icon has no id a node could be pointed at and no character a font could carry.
    /// </remarks>
    private static GameText Symbol(GameSymbol symbol, float rowHeight)
    {
        var builder = new SeStringBuilder();
        builder.AppendIcon((uint)GameSymbols.For(symbol));

        return new GameText
        {
            String = builder.ToReadOnlySeString(),
            FontSize = FontSize,
            AlignmentType = AlignmentType.Left,
            Size = new System.Numerics.Vector2(SymbolWidth, rowHeight),
            IsVisible = true,
        };
    }

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
    private static GameIcon Icon(ushort iconId, float size, float rowHeight) => new()
    {
        IconId = iconId,
        TextureSize = new System.Numerics.Vector2(GameIconSize, GameIconSize),
        FitTexture = true,
        Size = new System.Numerics.Vector2(size, size),

        // Centred against the row rather than sat on its top edge, since the icon is shorter than
        // the line and the eye reads the two as one row.
        Y = (rowHeight - size) / 2f,
        IsVisible = true,
    };

    private static TextFlags Edge(bool wrap)
    {
        var flags = wrap
            ? TextFlags.WordWrap | TextFlags.MultiLine
            : TextFlags.Ellipsis;

        return NativePalette.OnLightTheme ? flags : flags | TextFlags.Edge;
    }

    /// <remarks>
    /// <see cref="AlignmentType.Left"/> is left-aligned and <em>vertically centred</em>, which is
    /// what makes the text sit on the same line as the icon beside it rather than on the row's top
    /// edge.
    /// </remarks>
    /// <summary>
    /// A line of a surface's own prose, broken to fit rather than cut to fit.
    /// </summary>
    /// <remarks>
    /// <b>Every standalone line wraps; a line inside a row never does.</b> The two are different
    /// kinds of writing. A row's text is a name in a column and has to keep its baseline with the
    /// icon beside it, so a name too long for its column is cut and given in full on hover. A line
    /// like "every row is two copies of one piece" is a sentence, and half a sentence with an
    /// ellipsis on it cannot be recovered from anywhere - which is what these were.
    ///
    /// How tall it ends up is worked out rather than measured, and the difference matters: asking a
    /// text node how big its text is answers for the text laid out in one line, whatever the node's
    /// own width and flags say. Believing that answer left the second line drawn outside the node's
    /// box and over whatever came next.
    ///
    /// So the line count is the full width over the width available, rounded up, with a little
    /// taken off the divisor because words break where words end rather than where the edge is. The
    /// estimate leans towards too tall on purpose: a line too many is a gap, and a line too few is
    /// text written over other text.
    /// </remarks>
    private static NodeBase Wrapped(TextNode text, float width)
    {
        var node = Text(text.Text, text.Colour, width, height: NoticeLineHeight, wrap: true);
        node.LineSpacing = WrappedLineSpacing;

        var measured = node.GetTextDrawSize(text.Text);
        var usable = System.Math.Max(1f, width - WrapSlack);
        var lines = System.Math.Max(1, (int)System.Math.Ceiling(measured.X / usable));

        // The box is what the lines take, not what the measurement claims. Sizing it to the reported
        // height of a line left a paragraph with an empty line's worth of window under it, which
        // read as a gap between it and whatever came next.
        node.Height = System.Math.Max(NoticeLineHeight, lines * WrappedLineSpacing);
        return node;
    }

    private static GameText Text(
        string content, ColourRole role, float width, AlignmentType alignment = AlignmentType.Left,
        float? height = null, bool wrap = false)
    {
        var (fill, outline) = NativePalette.For(role);

        return new GameText
        {
            String = content,
            TextColor = fill,
            TextOutlineColor = outline,
            // Ellipsis, because a text node otherwise draws past its own width rather than
            // stopping at it - which is how a long name ended up under the scroll bar. Nothing is
            // lost by cutting a name: the row's tooltip opens with it in full. A sentence has no
            // such second copy, so it is broken across lines instead.
            // The edge is what makes pale text carry over a dark window, and what makes dark text
            // look thin over a light one - so it is dropped on the light themes rather than being
            // given a colour that works there, because there is no such colour.
            TextFlags = Edge(wrap),
            FontSize = FontSize,
            AlignmentType = alignment,
            Size = new System.Numerics.Vector2(width, height ?? RowHeight),
            IsVisible = true,
        };
    }

    /// <summary>
    /// Flattens a tooltip's lines into the one string the game's tooltip takes, colours and all.
    /// </summary>
    /// <remarks>
    /// The colours are macros inside the string rather than anything set on a node, which is why
    /// they come from <see cref="NativePalette.TooltipFor"/> - the same values a row is drawn in,
    /// resolved for a place that cannot be handed a colour. A role that resolves to nothing is
    /// written plainly and keeps whatever the game writes tooltips in.
    ///
    /// The newline goes before each line rather than after it, so there is no trailing one to trim
    /// off a tooltip the game would otherwise leave a blank row at the bottom of. A
    /// <see cref="SpacingNode"/> contributes its newline and nothing else, which is what makes it a
    /// blank line.
    ///
    /// Anything that is not text or a gap is skipped rather than described. A rule or an icon has
    /// no meaning in a string, and the presenters do not put one in a tooltip.
    /// </remarks>
    internal static ReadOnlySeString Tooltip(IReadOnlyList<Node> lines)
    {
        var builder = new SeStringBuilder();
        var written = 0;

        foreach (var line in lines)
        {
            if (line is not (TextNode or SpacingNode))
                continue;

            if (written++ > 0)
                builder.AppendNewLine();

            if (line is not TextNode entry)
                continue;

            if (NativePalette.TooltipFor(entry.Colour) is not { } colour)
            {
                builder.Append(entry.Text);
                continue;
            }

            builder.PushColorRgba(colour);
            builder.Append(entry.Text);
            builder.PopColor();
        }

        return builder.ToReadOnlySeString();
    }
}
