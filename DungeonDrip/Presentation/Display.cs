using System.Collections.Generic;

namespace DungeonDrip.Presentation;

/// <summary>What a surface is showing, described rather than drawn.</summary>
/// <remarks>
/// Intent, not layout. A node says "this is a row of a marker, an icon and a name, and here is what
/// it says on hover" - it does not say how wide the marker column is or which call draws the text,
/// because the two backends answer those differently and neither answer is the canonical one.
///
/// The vocabulary is deliberately small, and grows only when both backends can honour a new kind.
/// A node one backend has to approximate would put the plugin's content back inside a renderer,
/// which is the arrangement this exists to end.
/// </remarks>
internal abstract record Node;

/// <summary>A run of text, in whatever colour the role resolves to.</summary>
internal sealed record TextNode(string Text, ColourRole Colour = ColourRole.Default) : Node;

/// <summary>The small mark at the head of a row. See <see cref="GlyphToken"/>.</summary>
internal sealed record GlyphNode(GlyphToken Token, ColourRole Colour) : Node;

/// <summary>A game item icon, at a size in unscaled pixels.</summary>
internal sealed record IconNode(ushort IconId, float Size) : Node;

/// <summary>A dividing rule.</summary>
internal sealed record RuleNode : Node;

/// <summary>A gap, where one is doing work that a rule would overstate.</summary>
internal sealed record SpacingNode : Node;

/// <summary>Which item a row's right-click menu is about.</summary>
internal readonly record struct ItemContext(uint ItemId, string Name);

/// <summary>
/// One line, its parts laid side by side, with what it says on hover and what it offers on
/// right-click.
/// </summary>
/// <remarks>
/// The tooltip and the menu are on the row rather than on a part because both backends bind them to
/// something, and they do not bind them to the same thing: ImGui attaches a popup to the last item
/// drawn, which is why the name has to be drawn last and the hover flag taken against it, whereas a
/// native row hangs a collision node over the whole line. Naming the row as the target lets each do
/// it its own way.
/// </remarks>
internal sealed record RowNode(
    IReadOnlyList<Node> Parts,
    IReadOnlyList<Node>? Tooltip = null,
    ItemContext? Context = null) : Node;
