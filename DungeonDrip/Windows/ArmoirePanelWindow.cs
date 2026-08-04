using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside the Armoire listing held gear the Armoire has not got.
/// </summary>
/// <remarks>
/// The game's store screen lists what the Armoire accepts, which is a fact about the item rather than
/// about this character: a piece already deposited sits in that list looking exactly like one that is
/// not, and the only way to find out is to try it and be told no. This is the missing column - held
/// gear the Armoire genuinely has not got.
///
/// No staleness warning, and that is not an oversight: see
/// <see cref="AddonPanelWindow.DependsOnDresserSnapshot"/>. Nor a slot header, because the Armoire has
/// no capacity - what it holds is decided by the game's Cabinet sheet, so nothing here is a choice
/// between pieces the way the dresser's is.
/// </remarks>
public sealed class ArmoirePanelWindow(Plugin plugin)
    : StorePanelWindow(plugin, "To store in the Armoire###DungeonDripArmoirePanel", "armoire:", "armoire")
{
    protected override PanelSettings Settings => Plugin.Configuration.ArmoirePanel;

    protected override string? AnchorAddonName => Plugin.Armoire.AnchorAddon;

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Armoire.Resolve();

    protected override string EmptyMessage => "Nothing on you that the Armoire has not already got.";

    /// <summary>
    /// The Armoire is read live in front of the player, so the dresser's age says nothing about this
    /// list - and a warning about data a list does not consult is how the warning gets ignored on the
    /// panels where it means something.
    /// </summary>
    protected override bool DependsOnDresserSnapshot => false;

    /// <summary>
    /// How many held pieces the Armoire already has.
    /// </summary>
    /// <remarks>
    /// The count the game's own screen cannot give, which is the whole reason this panel exists: those
    /// pieces are on its list, they will be offered, and each will be refused on the attempt. Said as a
    /// number rather than listed, because they are the rows this panel deliberately does not have.
    /// </remarks>
    protected override void DrawHeader()
    {
        var duplicates = Plugin.Armoire.Duplicates;
        if (duplicates == 0)
            return;

        ImGui.TextColored(Palette.Muted, duplicates == 1
            ? "1 piece on you is in the Armoire already."
            : $"{duplicates} pieces on you are in the Armoire already.");
    }
}
