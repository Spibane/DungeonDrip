using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>The Glamour Dresser panel as a game window: carried gear that is not in it.</summary>
/// <remarks>
/// The one thing this has that <see cref="ArmoirePanelAddon"/> does not is pressure: the dresser has
/// a size, so which of these to put in is a decision and the header exists to inform it. The Armoire
/// has no capacity at all, and the question there is only whether a piece is in yet.
/// </remarks>
internal sealed class DresserPanelAddon(Plugin plugin, GlyphTextures glyphs)
    : NativeStorePanelAddon(plugin, glyphs, "DungeonDripDresser", "To store")
{
    protected override PanelSettings Settings => Plugin.Configuration.DresserPanel;

    protected override string AnchorAddonName => DresserAddWatcher.AddonName;

    protected override string CollapsePrefix => "dresser:";

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Dresser.Resolve();

    protected override string EmptyMessage => "Nothing on you that is not already stored.";

    protected override IReadOnlyList<Node> Header()
    {
        var (used, capacity) = Plugin.Dresser.Space;
        return StorePanelPresenter.DresserHeader(used, capacity);
    }

    /// <summary>What the dresser knows about a piece that the Armoire's panel does not.</summary>
    protected override IReadOnlyList<Node> RowNotes(HeldGearRow row) =>
        StorePanelPresenter.DresserNotes((DresserAddRow)row);
}
