using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>Draws a <see cref="Node"/> description with ImGui.</summary>
/// <remarks>
/// A walker and nothing else. It holds no decision about what any surface shows - if a question
/// about the gear or about wording is being answered here rather than in a presenter, it is in the
/// wrong file, and the native renderer will need the same answer written out a second time.
/// </remarks>
internal static class ImGuiRenderer
{
    public static void Draw(Plugin plugin, IReadOnlyList<Node> nodes, HeaderState? headers = null)
    {
        foreach (var node in nodes)
            Draw(plugin, node, headers);
    }

    public static void Draw(Plugin plugin, Node node, HeaderState? headers = null)
    {
        switch (node)
        {
            // Without somewhere to remember the folded state there is nothing to draw a heading
            // against, so the rows are drawn plainly rather than silently dropped.
            case CollapsibleNode group when headers == null:
                Draw(plugin, group.Children);
                break;

            case CollapsibleNode group:
                if (UiParts.CollapsingHeader(headers, group.Label, group.Key))
                {
                    Draw(plugin, group.Children, headers);
                    ImGui.Spacing();
                }

                break;

            case RowNode row:
                DrawRow(plugin, row);
                break;

            case RuleNode:
                ImGui.Separator();
                break;

            case SpacingNode:
                ImGui.Spacing();
                break;

            default:
                DrawPart(node);
                break;
        }
    }

    /// <remarks>
    /// The order is load-bearing and is ImGui's, not the description's. A context popup and
    /// <see cref="ImGui.IsItemHovered()"/> both bind to the last item drawn, so the row's text has
    /// to be the last part, the hover flag has to be taken before the menu goes on, and the tooltip
    /// has to come after both.
    /// </remarks>
    private static void DrawRow(Plugin plugin, RowNode row)
    {
        ImGui.AlignTextToFramePadding();

        var positioned = true;
        foreach (var part in row.Parts)
        {
            if (!positioned)
                ImGui.SameLine();

            positioned = DrawPart(part);
        }

        // Taken against the last part, before the menu is attached to the same item.
        var hovered = ImGui.IsItemHovered();

        if (row.Context is { } context)
            UiParts.ItemContextMenu(plugin, context.ItemId, context.Name);

        if (!hovered || row.Tooltip == null)
            return;

        using var tooltip = ImRaii.Tooltip();
        foreach (var line in row.Tooltip)
            Draw(plugin, line);
    }

    /// <returns>
    /// Whether the part has already left the cursor on the same line. An item icon has - that is
    /// <see cref="UiParts.ItemIcon"/>'s whole contract - and a second <c>SameLine</c> after one
    /// would push the name a space to the right of where it has always sat.
    /// </returns>
    private static bool DrawPart(Node node)
    {
        switch (node)
        {
            case TextNode text:
                ImGui.TextColored(Palette.For(text.Colour), text.Text);
                return false;

            case GlyphNode glyph:
                // Fixed width so the names line up regardless of which glyph each row carries.
                using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
                    ImGui.TextColored(Palette.For(glyph.Colour), Glyphs.For(glyph.Token).ToIconString());

                return false;

            case IconNode icon:
                UiParts.ItemIcon(icon.IconId, icon.Size);
                return true;

            case SpacingNode:
                ImGui.Spacing();
                return false;

            case RuleNode:
                ImGui.Separator();
                return false;

            default:
                return false;
        }
    }
}
