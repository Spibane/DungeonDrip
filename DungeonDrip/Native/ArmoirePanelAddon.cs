using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The Armoire panel as a game window: held gear the Armoire has not got.</summary>
/// <remarks>
/// The game's store screen lists what the Armoire accepts, which is a fact about the item rather
/// than about this character: a piece already deposited sits in that list looking exactly like one
/// that is not, and the only way to find out is to try it and be told no. This is the missing
/// column.
///
/// The store screen only. The Armoire's own window is a list of what is in there already, which is a
/// question it answers on its own, so a panel beside it would be answering something nobody standing
/// at it asked.
/// </remarks>
internal sealed class ArmoirePanelAddon(Plugin plugin, GlyphTextures glyphs)
    : NativeStorePanelAddon(plugin, glyphs, "DungeonDripArmoire", "To store in the Armoire")
{
    protected override PanelSettings Settings => Plugin.Configuration.ArmoirePanel;

    protected override string AnchorAddonName => ArmoireAddWatcher.AddonName;

    protected override string CollapsePrefix => "armoire:";

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Armoire.Resolve();

    protected override string EmptyMessage => "Nothing on you that the Armoire has not already got.";

    /// <summary>
    /// The Armoire is read live in front of the player, so the dresser's age says nothing about this
    /// list - and a warning about data a list does not consult is how the warning gets ignored on
    /// the panels where it means something.
    /// </summary>
    protected override bool DependsOnDresserSnapshot => false;

    protected override IReadOnlyList<Node> Header() =>
        StorePanelPresenter.ArmoireHeader(Plugin.Armoire.Duplicates);
}
