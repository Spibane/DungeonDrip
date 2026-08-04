using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;
using DungeonDrip.Game;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside the Glamour Dresser listing carried gear that is not in it.
/// </summary>
/// <remarks>
/// The mirror of every other panel. Those ask "is this needed" about gear the game is offering;
/// standing at the dresser, the useful question is the other one - what have I got on me that
/// should go in. It is also the only surface where the answer can be acted on without
/// going anywhere.
/// </remarks>
public sealed class DresserPanelWindow(Plugin plugin)
    : AddonPanelWindow(plugin, "To store###DungeonDripDresserPanel", "dresser:", "dresser")
{
    protected override PanelSettings Settings => Plugin.Configuration.DresserPanel;

    protected override string AnchorAddonName => DresserAddWatcher.AddonName;

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Dresser.Resolve();

    /// <summary>Every row here is unstored by construction, so the filter has nothing to act on.</summary>
    protected override bool HasOwnedRows => false;

    /// <summary>An empty list here means nothing is left to store, worth saying in the good colour.</summary>
    protected override string EmptyMessage => "Nothing on you that is not already stored.";

    protected override bool EmptyIsGood => true;

    protected override bool DrawExtraToolbarButtons()
    {
        var configuration = Plugin.Configuration;
        var changed = false;

        ImGui.SameLine();

        var armoury = configuration.DresserPanelIncludesArmoury;
        if (UiParts.ToolButton(
                "##dresserArmoury",
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
                "##dresserEquipped",
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
    /// How full the box is, and whether what is on this list would even fit.
    /// </summary>
    /// <remarks>
    /// The decision this panel exists to support is "which of these do I put in", and that only
    /// becomes a decision when there is not room for all of them.
    /// </remarks>
    protected override void DrawHeader()
    {
        var (used, capacity) = Plugin.Dresser.Space;
        if (capacity <= 0)
            return;

        var free = capacity - used;
        ImGui.TextColored(free <= 0 ? Palette.Warning : Palette.Muted,
            $"{used} of {capacity} slots used.");
    }

    /// <summary>
    /// Every row here is uncollected by construction, so the ownership glyph would say the same
    /// thing on all of them. The column goes to where the piece is instead, which is the part that
    /// changes what has to be done about it.
    /// </summary>
    protected override void DrawRow(GearRow row, bool stale)
    {
        var add = (DresserAddRow)row;

        ImGui.AlignTextToFramePadding();

        using (Plugin.PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push())
            ImGui.TextColored(
                add.Blocked == null ? Palette.Muted : Palette.Warning,
                LocationGlyph(add.Location).ToIconString());

        ImGui.SameLine();
        UiParts.ItemIcon(row.IconId, 20);

        var quantity = add.Quantity > 1 ? $"  x{add.Quantity}" : string.Empty;
        ImGui.Text($"{row.Name}{quantity}");

        var hovered = ImGui.IsItemHovered();
        UiParts.ItemContextMenu(Plugin, row.ItemId, row.Name);

        if (!hovered)
            return;

        using var tooltip = ImRaii.Tooltip();
        ImGui.Text(row.Name);
        ImGui.TextColored(Palette.Muted, Describe(add.Location));

        if (add.ArmoireWouldTake)
            ImGui.TextColored(Palette.Good, "The Armoire would take this, at no dresser slot.");

        if (add.Blocked != null)
            ImGui.TextColored(Palette.Warning, add.Blocked);

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
