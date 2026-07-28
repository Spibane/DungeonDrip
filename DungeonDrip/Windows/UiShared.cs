using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Windows;

/// <summary>
/// The plugin's colours, in one place because four windows were each declaring their own and had
/// already drifted into two greys a shade apart doing the same job.
/// </summary>
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
    /// Draws an item's icon and leaves the cursor on the same line, ready for its name.
    /// </summary>
    /// <remarks>
    /// Falls back to blank space of the same size rather than skipping, so a texture that has not
    /// loaded yet does not shunt the whole row leftwards for a frame.
    /// </remarks>
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
/// Shared by the loot roll companion and the vendor panel, which pin themselves to the addon they
/// describe. Game screen pixels and ImGui pixels are the same thing here, so nothing on this path is
/// multiplied by <see cref="ImGuiHelpers.GlobalScale"/> - that scales glyphs, not coordinates, and
/// applying it to a position is the classic way these panels end up drifting away from their addon.
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

        // Clamped against the panel's own size, not the addon's: the panel is the one that can
        // overrun, and an addon jammed against an edge would otherwise push it off screen.
        return new Vector2(
            Math.Clamp(x, 0f, Math.Max(0f, display.X - ownSize.X)),
            Math.Clamp(unit->Y, 0f, Math.Max(0f, display.Y - ownSize.Y)));
    }
}
