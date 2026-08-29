namespace DungeonDrip.Presentation;

/// <summary>The wording each panel uses, in one place per panel rather than one per backend.</summary>
/// <remarks>
/// These were properties on the ImGui panels, which was right while those were the only panels.
/// Written out a second time for the native ones they immediately drifted - the vendor note said
/// "check before buying" in one look and "you may already own this" in the other, which are
/// different advice about the same snapshot.
///
/// A string only belongs here once both backends' version of a panel would say it. Anything one
/// panel says about itself stays with that panel.
/// </remarks>
internal static class PanelText
{
    /// <summary>
    /// What a stale snapshot means at a vendor.
    /// </summary>
    /// <remarks>
    /// A vendor is reached on foot, so the dresser snapshot is usually old by then, and the mistake
    /// this prevents costs actual gil.
    /// </remarks>
    public const string VendorUncertain = "Your dresser snapshot predates this - check before buying.";

    /// <summary>
    /// What a stale snapshot means at a market board.
    /// </summary>
    /// <remarks>
    /// The same sentence as the vendor's, and named separately because the two mean it for different
    /// reasons and either could come to want its own wording. Staleness bites hardest here of
    /// anywhere: a board is reached by travelling to one, and zoning is exactly what wipes the
    /// dresser data, so this is almost always the oldest snapshot the plugin ever draws from.
    /// </remarks>
    public const string MarketUncertain = VendorUncertain;
}
