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
///
/// <para><see cref="CurrencyItemId"/> and <see cref="Amount"/> are the same price as
/// <paramref name="Detail"/>, in the form arithmetic can be done on. They exist because grouping
/// pieces by what buys them, or comparing a cost to a balance, cannot be done from
/// <paramref name="Detail"/>: it is rendered, and it is translated, so it would group on the client's
/// language. Nothing draws them - every surface still prints <paramref name="Detail"/>, and the two
/// must stay descriptions of one fact rather than drifting apart.</para>
/// </remarks>
/// <param name="Label">The route, named as briefly as it can be: "Vendor", "Special Shop", "Quest".</param>
/// <param name="Detail">Null when there is nothing to add, which no current builder produces.</param>
/// <param name="CurrencyItemId">
/// The item row the price is paid in, or 0 for a route with no price - crafted, a quest, an
/// achievement. Gil is row 1 and is a currency here like any other.
/// </param>
/// <param name="Amount">How many of it, or 0 wherever <paramref name="CurrencyItemId"/> is 0.</param>
/// <param name="Repurchase">
/// Whether this particular route is a buy-back price - a special shop charging gil and nothing else.
/// Recorded rather than dropped because it is still true of the shop, and because a piece whose
/// <em>only</em> route is a buy-back has to be described somehow.
///
/// Per route, unlike <paramref name="EventOnly"/>, which is a fact about the piece. The two were one flag
/// and had to be separated: a buy-back route means "prefer the real route", where event stock means "there
/// is no real route".
/// </param>
/// <param name="EventOnly">
/// Whether the piece is stocked by an event sell-back counter, and so out of reach of anyone who was not
/// there. Set when the index is finished rather than by a builder, because it is decided from all of a
/// piece's routes at once.
///
/// Kept apart from <paramref name="Repurchase"/> so a reader can hide one without the other: a plain
/// buy-back tells nobody anything, where "this is event gear" is often the most useful thing that can be
/// said about a piece.
/// </param>
public sealed record AcquisitionSource(
    AcquisitionKind Kind,
    string Label,
    string? Detail = null,
    uint CurrencyItemId = 0,
    uint Amount = 0,
    bool Repurchase = false,
    bool EventOnly = false)
{
    /// <summary>The whole route as one line, which is all any surface draws.</summary>
    public string Describe() => Detail == null ? Label : $"{Label} - {Detail}";

    /// <summary>Whether this route can be priced against a balance rather than only printed.</summary>
    public bool HasPrice => CurrencyItemId != 0 && Amount > 0;
}
