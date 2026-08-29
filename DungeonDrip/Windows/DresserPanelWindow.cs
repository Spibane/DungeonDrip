using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Game;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside the Glamour Dresser listing carried gear that is not in it.
/// </summary>
/// <remarks>
/// The one thing this has that <see cref="ArmoirePanelWindow"/> does not is pressure: the dresser has
/// a size, so which of these to put in is a decision and the header exists to inform it. The Armoire
/// has no capacity at all, and the question there is only whether a piece is in yet.
/// </remarks>
public sealed class DresserPanelWindow(Plugin plugin)
    : StorePanelWindow(plugin, "To store###DungeonDripDresserPanel", "dresser:", "dresser")
{
    protected override PanelSettings Settings => Plugin.Configuration.DresserPanel;

    protected override string AnchorAddonName => DresserAddWatcher.AddonName;

    protected override IReadOnlyList<GearRow>? ResolveRows() => Plugin.Dresser.Resolve();

    protected override string EmptyMessage => "Nothing on you that is not already stored.";

    /// <summary>
    /// How full the box is, and whether what is on this list would even fit.
    /// </summary>
    /// <remarks>
    /// The decision this panel exists to support is "which of these do I put in", and that only
    /// becomes a decision when there is not room for all of them.
    /// </remarks>
    private protected override IReadOnlyList<Node> Header()
    {
        var (used, capacity) = Plugin.Dresser.Space;
        return StorePanelPresenter.DresserHeader(used, capacity);
    }

    /// <summary>What the dresser knows about a piece that the Armoire's panel does not.</summary>
    private protected override IReadOnlyList<Node> RowNotes(HeldGearRow row) =>
        StorePanelPresenter.DresserNotes((DresserAddRow)row);
}
