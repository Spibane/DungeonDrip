namespace DungeonDrip.Core;

/// <summary>
/// One piece of gear a panel is showing, resolved against the collection and ready to draw.
/// </summary>
/// <remarks>
/// Not sealed: a surface with something extra to say about a row - how many listings there are, or
/// which bag a piece is in - subclasses rather than growing this with fields the other surfaces
/// would carry as nulls.
///
/// Deliberately <em>not</em> merged with <see cref="ReportItem"/>, which it resembles. That one
/// carries an <see cref="OwnershipSource"/> rather than a marker, because the duty list refuses to
/// draw at all without a dresser snapshot and so never needs the "cannot say" case; it also carries
/// loot provenance and role groups, which no panel wants, and it is built on the framework thread
/// with a different lifetime. Folding them together would drag loot data into the panels to save
/// six fields.
/// </remarks>
public record GearRow(
    uint ItemId,
    string Name,
    ushort IconId,
    ushort ItemLevel,
    int SlotOrder,
    string SlotName,
    CollectionMarker Marker,
    bool JobEquippable)
{
    public bool IsOwned => Marker is not (CollectionMarker.NotCollected or CollectionMarker.Unknown);
}
