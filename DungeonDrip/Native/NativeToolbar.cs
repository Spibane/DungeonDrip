using System;
using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Presentation;
using DungeonDrip.Windows;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>A panel's filter buttons as game buttons, above the list rather than inside it.</summary>
/// <remarks>
/// Separate from <see cref="NativeRenderer"/> because it is not part of the same tree. The renderer
/// fills the scrolling list; these buttons must stay put while that list scrolls, so they hang off
/// the addon beside it. That is also why a toolbar is not a <see cref="Node"/> - see
/// <see cref="ToolButtonModel"/>.
///
/// It holds state where the renderer does not, and both pieces of it are about the buttons
/// outliving a single fill: which nodes are already on the row, so a press does not rebuild them,
/// and which one the pointer is over, so a tooltip whose wording just changed can be shown again.
/// </remarks>
internal sealed class NativeToolbar
{
    /// <summary>
    /// The buttons are square, and larger than they look like they need to be.
    /// </summary>
    /// <remarks>
    /// A game button insets its icon eight pixels on every side, so the glyph is sixteen pixels
    /// across on a button of this size - about what the same glyph gets in the plugin overlay. Any
    /// smaller and the frame has more of the button than the mark does.
    /// </remarks>
    public const float ButtonSize = 32f;

    private const float ButtonGap = 2f;

    private readonly GlyphTextures glyphs;
    private readonly HorizontalListNode row;

    /// <summary>The buttons on the row, in the order they are on it.</summary>
    /// <remarks>
    /// Kept here rather than read back off the row, because what is wanted is the buttons this put
    /// there in the order it put them - not whatever the node happens to be holding.
    /// </remarks>
    private readonly List<ImGuiIconButtonNode> buttons = [];

    /// <summary>The button the pointer is over, or null when it is over none of them.</summary>
    /// <remarks>
    /// Tracked rather than asked for because the game does not offer the question, and it is needed
    /// for one thing only - see the reshow in <see cref="Fill"/>.
    /// </remarks>
    private ImGuiIconButtonNode? hovered;

    public NativeToolbar(GlyphTextures glyphs, HorizontalListNode row)
    {
        this.glyphs = glyphs;
        this.row = row;

        row.ItemSpacing = ButtonGap;
    }

    /// <summary>
    /// Puts the described buttons on the row, and asks for their icons.
    /// </summary>
    /// <remarks>
    /// Buttons already on the row are reused by position rather than freed and rebuilt. That is safe
    /// here and is not safe for the gear list, which reuses nothing for the opposite reason: a
    /// panel's filters are a fixed set in a fixed order, so the button at position two is the same
    /// filter between two fills, where row two is a different piece of gear as soon as anything is
    /// filtered out.
    ///
    /// It matters because this is refilled on every press. Freeing the buttons and building them
    /// again made the whole toolbar blink on each click, which reads as the panel being restarted
    /// rather than as a filter being flipped.
    /// </remarks>
    public void Fill(IReadOnlyList<ToolButtonModel> models)
    {
        while (buttons.Count > models.Count)
        {
            row.RemoveNode(buttons[^1]);
            buttons.RemoveAt(buttons.Count - 1);
        }

        for (var index = 0; index < models.Count; index++)
        {
            var model = models[index];

            if (index == buttons.Count)
                buttons.Add(Add());

            var button = buttons[index];

            button.TextTooltip = model.Tooltip;
            button.OnClick = model.Press;

            // The game shows the text a tooltip had when the pointer arrived, so a button whose
            // wording changes under a stationary pointer goes on saying the old thing until the
            // pointer leaves and comes back. Since pressing a filter is done with the pointer on it,
            // that is exactly when it matters, and telling the tooltip to show again is what makes
            // the button read as the state it has just moved to.
            if (ReferenceEquals(hovered, button))
                button.ShowTooltip();

            // Hidden until its glyph has been drawn, which is only ever the first time one is
            // asked for. An image node with nothing loaded draws a black square, which on a row of
            // buttons reads as three broken ones rather than three not yet drawn.
            // White, so the game's own button timelines are what tint and dim it through hover,
            // press and disable. A glyph drawn in any other colour would keep that colour through
            // all of them.
            button.ImageNode.Alpha =
                glyphs.TryLoad(button.ImageNode, ToolGlyphs.For(model.Icon), Vector4.One) ? 1f : 0f;
        }

        row.RecalculateLayout();
    }

    private ImGuiIconButtonNode Add()
    {
        var button = new ImGuiIconButtonNode
        {
            Size = new Vector2(ButtonSize, ButtonSize),
            IsVisible = true,
        };

        // Alongside the handlers the tooltip itself registers, not instead of them. These only
        // record where the pointer is; the game goes on deciding when a tooltip appears.
        button.AddEvent(AtkEventType.MouseOver, () => hovered = button);
        button.AddEvent(AtkEventType.MouseOut, () =>
        {
            if (ReferenceEquals(hovered, button))
                hovered = null;
        });

        row.AddNode(button);
        return button;
    }

    /// <summary>Says the buttons are gone, so nothing here refers to a freed node.</summary>
    /// <remarks>
    /// Called when the addon closes, not only when the panel is disposed. Closing deallocates the
    /// whole node tree, and nothing will send a mouse-out for a node that has stopped existing.
    /// </remarks>
    public void Invalidate()
    {
        buttons.Clear();
        hovered = null;
    }
}
