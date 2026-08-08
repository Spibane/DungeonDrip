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
    /// <summary>
    /// A row of toolbar buttons, with the rule that has always followed them.
    /// </summary>
    /// <remarks>
    /// <paramref name="id"/> prefixes each button's ImGui id, which has to be unique across every
    /// window drawn in a frame - two panels open beside two game windows is the ordinary case, not
    /// an unusual one, and buttons sharing an id share their pressed state.
    ///
    /// A pressed button runs its own action and reports nothing back. What saving or rebuilding the
    /// press needs is the presenter's to know - see <see cref="ToolButtonModel"/>.
    /// </remarks>
    public static void Toolbar(string id, IReadOnlyList<ToolButtonModel> buttons)
    {
        for (var index = 0; index < buttons.Count; index++)
        {
            if (index > 0)
                ImGui.SameLine();

            var button = buttons[index];

            // Greyed rather than skipped, and the tooltip still reads: a disabled button here is
            // one showing a state worth seeing named, so hiding it would take the state with it.
            using var disabled = ImRaii.Disabled(!button.Enabled);

            if (UiParts.ToolButton($"##{id}{index}", ToolGlyphs.For(button.Icon), button.Tooltip))
                button.Press();
        }

        ImGui.Separator();
    }

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

            case ButtonNode button:
                if (ImGui.Button(button.Label))
                    button.Press();

                break;

            case TableNode table:
                DrawTable(plugin, table.Model);
                break;

            case SpacingNode:
                ImGui.Spacing();
                break;

            default:
                DrawPart(node);
                break;
        }
    }

    /// <summary>
    /// The roulette advice, through ImGui's own table.
    /// </summary>
    /// <remarks>
    /// Stretch-proportional, so the weights in the model are what ImGui divides the width by rather
    /// than something worked out here. The row's first cell is a selectable spanning every column,
    /// which is what makes the tooltip reachable from anywhere along the row instead of only from
    /// the name - and it is why the hover flag is taken there and used at the end.
    /// </remarks>
    private static void DrawTable(Plugin plugin, RouletteTableModel model)
    {
        using var table = ImRaii.Table(
            "roulettes", model.Headings.Count,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp);

        if (!table.Success)
            return;

        for (var column = 0; column < model.Headings.Count; column++)
        {
            ImGui.TableSetupColumn(
                model.Headings[column], ImGuiTableColumnFlags.WidthStretch, model.Weights[column]);
        }

        ImGui.TableHeadersRow();

        foreach (var row in model.Rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var first = row.Cells[0];
            ImGui.Selectable($"{first.Text}##roulette{first.Text}", false, ImGuiSelectableFlags.SpanAllColumns);
            var hovered = ImGui.IsItemHovered();

            for (var cell = 1; cell < row.Cells.Count; cell++)
            {
                ImGui.TableNextColumn();
                ImGui.TextColored(Palette.For(row.Cells[cell].Colour), row.Cells[cell].Text);
            }

            if (!hovered)
                continue;

            using var tooltip = ImRaii.Tooltip();
            Draw(plugin, row.Tooltip);
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
            case TextNode { Wrap: true } wrapped:
            {
                using var colour = ImRaii.PushColor(ImGuiCol.Text, Palette.For(wrapped.Colour));
                ImGui.TextWrapped(wrapped.Text);
                return false;
            }

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

            case SymbolNode symbol:
                UiParts.GameSymbol(symbol.Symbol);
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
