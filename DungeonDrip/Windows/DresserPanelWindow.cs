using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using DungeonDrip.Core;
using DungeonDrip.Game;

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
    /// Says when a piece has a second home that costs no dresser slot.
    /// </summary>
    /// <remarks>
    /// Only on this panel. Beside the dresser it is a way out of spending a slot; beside the Armoire it
    /// would be saying that the box being stood at is the box to use.
    /// </remarks>
    protected override void DrawRowNotes(HeldGearRow row)
    {
        if (((DresserAddRow)row).ArmoireWouldTake)
            ImGui.TextColored(Palette.Good, "The Armoire would take this, at no dresser slot.");
    }
}
