using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Presentation;

namespace DungeonDrip.Native;

/// <summary>A panel beside a storage box, listing held gear that box has not got.</summary>
/// <remarks>
/// The mirror of the vendor and market panels, and the same split their overlay counterparts make:
/// those ask "is this needed" of gear the game is offering, while standing at a box the useful
/// question is the other one - what is on the character that should go in.
///
/// Here because the two boxes are the same panel with a different container behind them. Every row
/// is one the box does not have, so the marker column is dead on both and goes to where the piece is
/// instead; the filters for gear that is spoken for mean the same thing at either window; and an
/// empty list is good news at both.
/// </remarks>
internal abstract class NativeStorePanelAddon(
    Plugin plugin, GlyphTextures glyphs, string internalName, string title)
    : NativePanelAddon(plugin, glyphs, internalName, title)
{
    /// <summary>Every row here is one the box has not got, so the filter has nothing to act on.</summary>
    protected sealed override bool HasOwnedRows => false;

    /// <summary>An empty list means nothing is left to store, which is worth the good colour.</summary>
    protected sealed override bool EmptyIsGood => true;

    /// <summary>Anything this box wants to add to a row's tooltip.</summary>
    protected virtual IReadOnlyList<Node> RowNotes(HeldGearRow row) => [];

    protected sealed override IReadOnlyList<ToolButtonModel> ExtraToolButtons() =>
        PanelPresenter.StoreFilters(Plugin.Configuration, FiltersChanged);

    protected sealed override Node Row(GearRow row, bool stale)
    {
        var held = (HeldGearRow)row;
        return StorePanelPresenter.Row(held, RowNotes(held));
    }
}
