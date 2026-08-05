namespace DungeonDrip.Core.Sources;

/// <summary>
/// One way a piece can be obtained, other than by being handed it in a duty.
/// </summary>
/// <remarks>
/// Ordered by how directly a player can act on it, which is the order every surface draws these in.
/// A currency shop beats a quest because a quest may be one already completed and therefore no longer
/// a route to anything, whereas a shop is a route whenever the currency is.
/// </remarks>
public enum AcquisitionKind
{
    /// <summary>Made at a crafting job's bench.</summary>
    Crafted,

    /// <summary>Bought for gil from an ordinary vendor.</summary>
    GilShop,

    /// <summary>Traded for tomestones, scrips, marks or gemstones.</summary>
    CurrencyShop,

    /// <summary>Traded for Grand Company seals.</summary>
    GrandCompany,

    /// <summary>Handed over on finishing a quest.</summary>
    Quest,

    /// <summary>Claimed from an achievement reward.</summary>
    Achievement,
}

/// <summary>
/// One route to a piece, ready to draw: what the route is, and the one detail worth naming.
/// </summary>
/// <remarks>
/// <paramref name="Detail"/> is a rendered string rather than a typed amount, because nothing
/// downstream does arithmetic on it - every surface prints it. Keeping it a string is also what lets
/// one field carry a price, a crafting job and a quest name without a shared type having to model
/// gil, seals, tomestones, class levels and quest titles as one shape.
///
/// Every line therefore reads <c>Kind - Detail</c>, uniformly. It used to vary - a quest read
/// "Quest reward: Sap for Smiles" with a colon while a vendor read "Sold by a vendor - 45 gil" with a
/// dash - and two punctuation styles down one list looked like two kinds of statement.
///
/// Not a record of *which shop*. Several shop rows commonly sell the same piece at the same price, and
/// the plugin cannot say who or where any of them are, so a shop identity would be a field that
/// multiplied the rows without answering anything. See <see cref="ItemSources"/> on the collapse to one
/// line per kind that follows from that.
/// </remarks>
/// <param name="Label">The route, named as briefly as it can be: "Vendor", "Special Shop", "Quest".</param>
/// <param name="Detail">Null when there is nothing to add, which no current builder produces.</param>
public sealed record AcquisitionSource(AcquisitionKind Kind, string Label, string? Detail = null)
{
    /// <summary>The whole route as one line, which is all any surface draws.</summary>
    public string Describe() => Detail == null ? Label : $"{Label} - {Detail}";
}
