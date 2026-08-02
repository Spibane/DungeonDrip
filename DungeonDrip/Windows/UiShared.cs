using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>The plugin's colours, in one place so they cannot drift apart again.</summary>
internal static class Palette
{
    /// <summary>Something the user should act on or distrust: stale data, hidden weapons.</summary>
    public static readonly Vector4 Warning = new(1.00f, 0.71f, 0.20f, 1f);

    /// <summary>Finished: a complete duty list, a completed outfit.</summary>
    public static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);

    /// <summary>Present but not the point - owned pieces, secondary lines.</summary>
    public static readonly Vector4 Muted = new(0.58f, 0.58f, 0.58f, 1f);

    /// <summary>Not collected, in the loot roll list.</summary>
    public static readonly Vector4 Missing = new(1.00f, 0.85f, 0.40f, 1f);

    /// <summary>Not collected, at a vendor, where gold read as a recommendation to buy.</summary>
    public static readonly Vector4 NotOwned = new(0.94f, 0.38f, 0.38f, 1f);

    /// <summary>The duty currently being looked at.</summary>
    public static readonly Vector4 Focus = new(0.55f, 0.80f, 1.00f, 1f);
}

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
    /// indicator you can also press - and puts the burden of saying so on the tooltip. That tooltip
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
    /// Trying on is the one thing here that reaches into the game, so it is kept off the rows the
    /// game will not preview: an entry that silently does nothing is worse than no entry.
    /// </remarks>
    public static void ItemContextMenu(Plugin plugin, uint itemId, string name)
    {
        using var context = ImRaii.ContextPopupItem($"##ctx{itemId}");
        if (!context.Success)
            return;

        if (TryOnService.CanTryOn(itemId))
        {
            if (ImGui.Selectable("Try on"))
                plugin.TryOn.QueuePiece(itemId);

            // Named rather than a bare "Try on outfit", because a piece is often in several sets and
            // the whole point of the entry is choosing which of them to look at.
            foreach (var (setId, setName) in plugin.Outfits.NamedSetsContaining(itemId))
            {
                if (ImGui.Selectable($"Try on outfit: {setName}"))
                    plugin.TryOn.QueueOutfit(setId);
            }

            ImGui.Separator();
        }

        if (ImGui.Selectable("Link in chat"))
            Plugin.ChatGui.Print(new SeStringBuilder().AddItemLink(itemId, false).Build());

        if (ImGui.Selectable("Copy name"))
            ImGui.SetClipboardText(name);
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

/// <summary>
/// Places a window alongside a game addon.
/// </summary>
/// <remarks>
/// Game screen pixels and ImGui pixels are the same thing here, so nothing on this path is
/// multiplied by <see cref="ImGuiHelpers.GlobalScale"/>. That scales glyphs, not coordinates;
/// applying it to a position is how these panels drift away from their addon.
/// </remarks>
internal static unsafe class AddonAnchor
{
    private const float Gap = 6f;

    /// <summary>Top-left corner for a panel of <paramref name="ownSize"/> beside this addon.</summary>
    public static Vector2 Beside(AtkUnitBase* unit, LootCompanionSide side, Vector2 ownSize)
    {
        var display = ImGui.GetIO().DisplaySize;
        var width = unit->GetScaledWidth(true);

        var toRight = side switch
        {
            LootCompanionSide.Left => false,
            LootCompanionSide.Right => true,
            _ => unit->X + width + Gap + ownSize.X <= display.X,
        };

        var x = toRight
            ? unit->X + width + Gap
            : unit->X - ownSize.X - Gap;

        // Clamped against the panel's own size: an addon jammed against an edge would otherwise
        // push it off screen.
        return new Vector2(
            Math.Clamp(x, 0f, Math.Max(0f, display.X - ownSize.X)),
            Math.Clamp(unit->Y, 0f, Math.Max(0f, display.Y - ownSize.Y)));
    }
}
