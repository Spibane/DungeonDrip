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
    /// Says when a piece has a second home that costs no dresser slot, and when the dresser can be
    /// seen to be holding the piece already.
    /// </summary>
    /// <remarks>
    /// The first note is only on this panel: beside the dresser a slot-free home is a way out of
    /// spending a slot, while beside the Armoire it would be saying that the box being stood at is the
    /// box to use.
    ///
    /// The second is the caveat that would otherwise bite hardest here. Under "owned only if every
    /// outfit with that piece is stored", a row can be a piece the dresser is visibly holding inside
    /// one of its sets - correct, and indistinguishable from a bug unless the row says which rule put
    /// it on the list.
    /// </remarks>
    protected override void DrawRowNotes(HeldGearRow row)
    {
        var add = (DresserAddRow)row;

        if (add.ArmoireWouldTake)
            ImGui.TextColored(Palette.Good, "The Armoire would take this, at no dresser slot.");

        if (add.OutfitsStored > 0 && add.OutfitsTotal > add.OutfitsStored)
        {
            // Split across lines by hand: a tooltip does not wrap, and one sentence of this length
            // would drag it across the screen.
            ImGui.TextColored(Palette.Warning,
                $"Stored inside {add.OutfitsStored} of the {add.OutfitsTotal} outfit sets using it.");

            ImGui.TextColored(Palette.Warning,
                "Your setting only counts that as owned once every one of them is\n" +
                "stored, so storing this copy on its own settles it.");
        }
    }
}
