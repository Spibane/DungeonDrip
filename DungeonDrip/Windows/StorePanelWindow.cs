using System.Collections.Generic;
using DungeonDrip.Core;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// A panel beside a box, listing held gear that box has not got.
/// </summary>
/// <remarks>
/// The mirror of the vendor and market panels: those ask "is this needed" of gear the game is
/// offering, while standing at a box the useful question is the other one - what is on the character
/// that should go in. Both boxes get one, and both are surfaces where the answer can be acted on
/// without going anywhere.
///
/// Here because the two are the same panel with a different box behind them: every row is one the box
/// does not have, so the marker column is dead on both and goes to where the piece is instead; the
/// filters for gear that is spoken for mean the same thing at either window; and an empty list is good
/// news at both. What differs is the box - which window to sit beside, what to say above the list, and
/// which tooltip lines are worth adding - and that is what the subclasses are.
/// </remarks>
public abstract class StorePanelWindow : AddonPanelWindow
{
    protected StorePanelWindow(Plugin plugin, string title, string collapsePrefix, string toolbarId)
        : base(plugin, title, collapsePrefix, toolbarId)
    {
    }

    /// <summary>Every row here is one the box has not got, so the filter has nothing to act on.</summary>
    protected override bool HasOwnedRows => false;

    /// <summary>An empty list means nothing is left to store, which is worth the good colour.</summary>
    protected override bool EmptyIsGood => true;

    /// <summary>Anything this box wants to add to a row's tooltip.</summary>
    private protected virtual IReadOnlyList<Node> RowNotes(HeldGearRow row) => [];

    /// <summary>The two filters for gear that is already spoken for.</summary>
    private protected sealed override IReadOnlyList<ToolButtonModel> ExtraToolButtons() =>
        PanelPresenter.StoreFilters(Plugin.Configuration, FiltersChanged);

    /// <summary>
    /// Every row here is one the box has not got by construction, so the ownership glyph would say
    /// the same thing on all of them. The column goes to where the piece is instead, which is the part
    /// that changes what has to be done about it.
    /// </summary>
    private protected sealed override Node Row(GearRow row, bool stale)
    {
        var held = (HeldGearRow)row;
        return StorePanelPresenter.Row(held, RowNotes(held));
    }
}
