using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>The ImGui backend's answer to each <see cref="ColourRole"/>.</summary>
/// <remarks>
/// These are the values the plugin has always drawn, unchanged - the split into roles was so a
/// second backend could answer differently, not so this one could change its mind. The two greys
/// that had drifted a shade apart doing the same job were merged long before that, and this stays
/// the one place they live.
///
/// <see cref="ColourRole.Default"/> resolves to ImGui's own text colour rather than to a constant,
/// so a window drawing ordinary text through the same call as coloured text still follows the
/// user's chosen Dalamud style.
/// </remarks>
internal static class Palette
{
    private static readonly Vector4 WarningColour = new(1.00f, 0.71f, 0.20f, 1f);
    private static readonly Vector4 GoodColour = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 MutedColour = new(0.58f, 0.58f, 0.58f, 1f);
    private static readonly Vector4 MissingColour = new(1.00f, 0.85f, 0.40f, 1f);
    private static readonly Vector4 NotOwnedColour = new(0.94f, 0.38f, 0.38f, 1f);
    private static readonly Vector4 FocusColour = new(0.55f, 0.80f, 1.00f, 1f);

    /// <summary>The colour to draw a role in.</summary>
    public static Vector4 For(ColourRole role) => role switch
    {
        ColourRole.Default => ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
        ColourRole.Warning => WarningColour,
        ColourRole.Good => GoodColour,
        ColourRole.Muted => MutedColour,
        ColourRole.Missing => MissingColour,
        ColourRole.NotOwned => NotOwnedColour,
        ColourRole.Focus => FocusColour,

        // Not a default case that quietly picks something: a role added to the enum and not to this
        // switch would otherwise ship as whatever the fallback was, and nobody would see it.
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "No ImGui colour for this role."),
    };
}
