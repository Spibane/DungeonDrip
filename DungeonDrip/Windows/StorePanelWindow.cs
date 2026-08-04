using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside a box, listing held gear that box has not got.
/// </summary>
/// <remarks>
/// The mirror of the vendor and market panels: those ask "is this needed" of gear the game is
/// offering, while standing at a box the useful question is the other one - what is on the character
/// that should go in. Both boxes get one, and both are surfaces where the answer can be acted on
/// without going anywhere.
///
/// Here because the two are the same panel with a different box behind them: every row is one the box
/// does not have, so the marker column is dead on both and goes to where the piece is instead; the
/// filters for gear that is spoken for mean the same thing at either window; and an empty list is good
/// news at both. What differs is the box - which window to sit beside, what to say above the list, and
/// which tooltip lines are worth adding - and that is what the subclasses are.
/// </remarks>
public abstract class StorePanelWindow : AddonPanelWindow
{
    private readonly string toolbarId;

    protected StorePanelWindow(Plugin plugin, string title, string collapsePrefix, string toolbarId)
        : base(plugin, title, collapsePrefix, toolbarId)
    {
        this.toolbarId = toolbarId;
    }

    /// <summary>Every row here is one the box has not got, so the filter has nothing to act on.</summary>
    protected override bool HasOwnedRows => false;

    /// <summary>An empty list means nothing is left to store, which is worth the good colour.</summary>
    protected override bool EmptyIsGood => true;

    /// <summary>Anything this box wants to add to a row's tooltip.</summary>
    protected virtual void DrawRowNotes(HeldGearRow row) { }

    /// <summary>
    /// The two filters for gear that is already spoken for.
    /// </summary>
    /// <remarks>
    /// Written to the settings both store panels read, not to a pair of their own: armoury gear is a
    /// gearset's and worn gear has to come off whichever box is being stood at, so switching one at the
    /// dresser and finding it switched at the Armoire is the behaviour that needs no explaining.
    /// </remarks>
    protected sealed override bool DrawExtraToolbarButtons()
    {
        var configuration = Plugin.Configuration;
        var changed = false;

        ImGui.SameLine();

        var armoury = configuration.DresserPanelIncludesArmoury;
        if (UiParts.ToolButton(
                $"##{toolbarId}Armoury",
                armoury ? FontAwesomeIcon.Archive : FontAwesomeIcon.BoxOpen,
                armoury
                    ? "Listing armoury chest gear. Click to hide it."
                    : "Hiding armoury chest gear. Click to list it."))
        {
            configuration.DresserPanelIncludesArmoury = !armoury;
            changed = true;
        }

        ImGui.SameLine();

        var equipped = configuration.DresserPanelIncludesEquipped;
        if (UiParts.ToolButton(
                $"##{toolbarId}Equipped",
                equipped ? FontAwesomeIcon.Tshirt : FontAwesomeIcon.UserSlash,
                equipped
                    ? "Listing gear you are wearing. Click to hide it."
                    : "Hiding gear you are wearing. Click to list it."))
        {
            configuration.DresserPanelIncludesEquipped = !equipped;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Every row here is one the box has not got by construction, so the ownership glyph would say
    /// the same thing on all of them. The column goes to where the piece is instead, which is the part
    /// that changes what has to be done about it.
    /// </summary>
    protected sealed override void DrawRow(GearRow row, bool stale)
    {
        var held = (HeldGearRow)row;

        ImGui.AlignTextToFramePadding();

        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(
                held.Blocked == null ? Palette.Muted : Palette.Warning,
                LocationGlyph(held.Location).ToIconString());

        ImGui.SameLine();
        UiParts.ItemIcon(row.IconId, 20);

        var quantity = held.Quantity > 1 ? $"  x{held.Quantity}" : string.Empty;
        ImGui.Text($"{row.Name}{quantity}");

        var hovered = ImGui.IsItemHovered();
        UiParts.ItemContextMenu(Plugin, row.ItemId, row.Name);

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.TextColored(Palette.Muted, Describe(held.Location));

        DrawRowNotes(held);

        if (held.Blocked != null)
            ImGui.TextColored(Palette.Warning, held.Blocked);

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click for options.");
    }

    private static FontAwesomeIcon LocationGlyph(CarryLocation location) => location switch
    {
        CarryLocation.Bags => FontAwesomeIcon.Briefcase,
        CarryLocation.Armoury => FontAwesomeIcon.Archive,
        CarryLocation.Equipped => FontAwesomeIcon.User,
        _ => FontAwesomeIcon.Horse,
    };

    private static string Describe(CarryLocation location) => location switch
    {
        CarryLocation.Bags => "In your bags",
        CarryLocation.Armoury => "In your armoury chest",
        CarryLocation.Equipped => "Equipped",
        _ => "In your saddlebag",
    };
}
