namespace DungeonDrip.Presentation;

/// <summary>The mark on a toolbar button, named by the state the button is showing.</summary>
/// <remarks>
/// Separate from <see cref="GlyphToken"/> because the two answer different questions. A
/// <see cref="GlyphToken"/> says where a piece of gear is; these say what the list is currently
/// doing, which is why they come in pairs - a filter button shows the state it is in rather than
/// the state it would move to, so each filter needs a mark for both of its states.
///
/// Named for the state and never for the shape, on the same rule as <see cref="GlyphToken"/>: a
/// presenter that said "eye" would be describing one backend's rendering of a fact rather than the
/// fact.
/// </remarks>
internal enum ToolGlyph
{
    /// <summary>Pieces already collected are in the list.</summary>
    ShowingOwned,

    /// <summary>Only what is missing is in the list.</summary>
    HidingOwned,

    /// <summary>Only gear the current job can wear.</summary>
    CurrentJobOnly,

    /// <summary>Gear for every job.</summary>
    EveryJob,

    /// <summary>Weapons and off-hands are left out.</summary>
    WeaponsHidden,

    /// <summary>Weapons and off-hands are in the list.</summary>
    WeaponsListed,

    /// <summary>Armoury chest gear is in the list.</summary>
    ArmouryListed,

    /// <summary>Armoury chest gear is left out.</summary>
    ArmouryHidden,

    /// <summary>Worn gear is in the list.</summary>
    EquippedListed,

    /// <summary>Worn gear is left out.</summary>
    EquippedHidden,

    /// <summary>
    /// The panel is at a size the player dragged it to rather than the game window's.
    /// </summary>
    /// <remarks>
    /// Only ever reached in the plugin overlays. A game window has no resize handle, so natively
    /// there is no dragged size to be at and no button to undo one - see
    /// <c>Native.VendorPanelAddon</c>.
    /// </remarks>
    SizeOverridden,
}
