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
/// <param name="Wrap">
/// Whether the line may be broken to fit the width it is given. Off by default, because almost
/// every line the plugin writes is short and a row of a list must never reflow - only the few
/// paragraphs that explain something want it, and those are explaining rather than listing.
/// </param>
internal sealed record TextNode(
    string Text, ColourRole Colour = ColourRole.Default, bool Wrap = false) : Node;

/// <summary>The small mark at the head of a row. See <see cref="GlyphToken"/>.</summary>
internal sealed record GlyphNode(GlyphToken Token, ColourRole Colour) : Node;

/// <summary>One of the game's own icons, drawn from a payload rather than a font.</summary>
/// <remarks>
/// A part of its own rather than something folded into the text beside it, and the reason is what
/// each backend has to do to draw one. The game draws it from a macro inside a string; the overlay
/// draws it through a renderer that understands those macros and is not the call that draws
/// ordinary text. Keeping it separate lets each use the right one, and leaves a row's name the
/// plain text item that a right-click menu can still be attached to.
/// </remarks>
internal sealed record SymbolNode(GameSymbol Symbol) : Node;

/// <summary>A game item icon, at a size in unscaled pixels.</summary>
internal sealed record IconNode(ushort IconId, float Size) : Node;

/// <summary>A dividing rule.</summary>
internal sealed record RuleNode : Node;

/// <summary>A pressable button with words on it, for the few places an icon cannot say enough.</summary>
/// <remarks>
/// Distinct from <see cref="ToolButtonModel"/>, which is a toolbar's business and never appears in
/// a list of nodes. This is the other kind: one button in the middle of a surface, saying the one
/// thing that surface can offer to do about what it is reporting.
/// </remarks>
internal sealed record ButtonNode(string Label, System.Action Press) : Node;

/// <summary>A table of columns sized by weight. See <see cref="RouletteTableModel"/>.</summary>
/// <remarks>
/// A node kind rather than something a surface hangs beside its list, unlike a toolbar, because it
/// genuinely belongs in the stream: the roulette advice is what the duty view shows in place of a
/// list, not alongside one.
/// </remarks>
internal sealed record TableNode(RouletteTableModel Model) : Node;

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

/// <summary>
/// A heading with rows folded under it, remembering whether it is open.
/// </summary>
/// <remarks>
/// <paramref name="Key"/> is separate from <paramref name="Label"/> for the same reason it is in
/// <see cref="HeaderState"/>: a label carrying a count changes as gear moves, and the folded state
/// has to outlive that.
///
/// The two backends draw this very differently - an ImGui <c>CollapsingHeader</c> re-decides
/// whether to draw its contents every frame, where a native header holds its children and hides
/// them - so what is described is the nesting and the key, not either of those mechanisms.
/// </remarks>
internal sealed record CollapsibleNode(
    string Key,
    string Label,
    IReadOnlyList<Node> Children) : Node;
