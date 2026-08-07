using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Game;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>
/// The widgets more than one window draws, so they cannot come to look or behave differently.
/// </summary>
/// <remarks>
/// Small pieces on purpose - a checkbox, a toolbar button, an icon, a right-click menu. Anything
/// larger is a window's own business; what is here is the furniture that would otherwise be
/// copy-pasted, and the right-click menu especially, since a second hand-written copy is how an
/// action ends up on one surface and not the others.
/// </remarks>
internal static class UiParts
{
    /// <summary>
    /// A settings checkbox bound straight to its field, reporting whether it moved.
    /// </summary>
    /// <remarks>
    /// ImGui takes a ref, and a property cannot be passed by ref, so every one of these was a local,
    /// a Checkbox, a write-back and a flag - four lines each and a place to mistype which setting is
    /// being written. The caller now names the setting once, in the two places that cannot disagree.
    /// </remarks>
    public static bool Toggle(string label, ref bool value, string? tooltip = null)
    {
        var changed = ImGui.Checkbox(label, ref value);

        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return changed;
    }

    /// <summary>
    /// A square toolbar button, with the words that would have been on it on hover instead.
    /// </summary>
    /// <remarks>
    /// A toolbar of sentences forces a window wider than its list needs to be, so these are icons.
    /// Each shows the state it is in rather than the state it would move to, which makes it an
    /// indicator that can also be pressed - and puts the burden of saying so on the tooltip. That tooltip
    /// is read through <see cref="ImGuiHoveredFlags.AllowWhenDisabled"/> so a greyed-out button can
    /// still explain why it is greyed out, which an icon cannot do on its own.
    /// </remarks>
    public static bool ToolButton(string id, FontAwesomeIcon icon, string tooltip)
    {
        var pressed = ImGuiComponents.IconButton(id, icon);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(tooltip);

        return pressed;
    }

    /// <summary>
    /// The right-click menu every gear row carries, wherever it is drawn.
    /// </summary>
    /// <remarks>
    /// Must be called against the row's name text, since ImGui binds a context popup to the last item
    /// drawn - and so must <see cref="ImGui.IsItemHovered()"/>, which is why callers take their hover
    /// flag before this rather than after.
    ///
    /// What goes in the menu lives in <see cref="ItemActions"/>, shared with the game's own
    /// right-click menu. This is only the ImGui rendering of it.
    /// </remarks>
    public static void ItemContextMenu(Plugin plugin, uint itemId, string name)
    {
        using var context = ImRaii.ContextPopupItem($"##ctx{itemId}");
        if (!context.Success)
            return;

        foreach (var action in ItemActions.For(plugin, itemId, name, ItemActionSurface.PluginWindow))
            DrawAction(action);
    }

    private static void DrawAction(ItemAction action)
    {
        if (action.StartsGroup)
            ImGui.Separator();

        if (!action.IsSubmenu)
        {
            // A choice with nothing behind it is a label - the drop list uses those to explain
            // itself. Disabled rather than absent, because a missing row reads as a broken menu.
            if (action.Invoke == null)
            {
                ImGui.TextColored(Palette.For(ColourRole.Muted), action.Label);
                return;
            }

            if (ImGui.Selectable(action.Label))
                action.Invoke();

            return;
        }

        using var submenu = ImRaii.Menu(action.Label);
        if (!submenu.Success)
            return;

        foreach (var entry in action.Submenu!)
            DrawAction(entry);
    }

    /// <summary>
    /// A collapsing header whose folded state survives a restart.
    /// </summary>
    /// <remarks>
    /// The remembered state is applied on <see cref="ImGuiCond.Appearing"/> only, so a click wins
    /// for the rest of the session rather than being overruled on the next frame, and whatever it
    /// settles on is written back.
    ///
    /// <paramref name="id"/> is separate from <paramref name="key"/> because the duty list's
    /// headings are keyed on the bare group label but carry a prefixed ImGui id. Folding them
    /// together would be tidier and would silently unfold every heading anyone has folded, since the
    /// key is what is already written in their config.
    /// </remarks>
    /// <returns>Whether the section's contents should be drawn.</returns>
    public static bool CollapsingHeader(
        HeaderState state, string label, string key, string? id = null, ColourRole? colour = null,
        bool defaultOpen = true)
    {
        ImGui.SetNextItemOpen(state.IsOpen(key, defaultOpen), ImGuiCond.Appearing);

        bool open;
        using (ImRaii.PushColor(ImGuiCol.Text, Palette.For(colour ?? ColourRole.Default), colour != null))
            open = ImGui.CollapsingHeader($"{label}###{id ?? key}");

        state.Record(key, open, defaultOpen);
        return open;
    }

    /// <summary>
    /// Draws an item's icon and leaves the cursor on the same line, ready for its name. Falls back
    /// to blank space of the same size, so a texture still loading does not shunt the row leftwards.
    /// </summary>
    public static void ItemIcon(ushort iconId, float size)
    {
        var scaled = new Vector2(size, size) * ImGuiHelpers.GlobalScale;
        var icon = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();

        if (icon != null)
            ImGui.Image(icon.Handle, scaled);
        else
            ImGui.Dummy(scaled);

        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
    }
}

/// <summary>Reads an addon's placement off the live struct, for <see cref="AddonAnchor"/>.</summary>
/// <remarks>
/// The arithmetic lives in <see cref="AddonAnchor"/>, which both backends share. This is the half
/// that has to touch the game's own memory, kept here so the shared half does not have to.
/// </remarks>
internal static unsafe class AddonPlacement
{
    /// <summary>The addon's top-left and its width on screen, both in screen pixels.</summary>
    public static (Vector2 Position, float Width) Of(AtkUnitBase* unit) =>
        (new Vector2(unit->X, unit->Y), unit->GetScaledWidth(true));
}
