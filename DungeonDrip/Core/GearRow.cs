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
/// <param name="Locks">
/// Which of the piece's own locks this character falls foul of. Baked in beside the job answer rather
/// than asked at draw time, for the same reason: the row is cached and the sheet read is not free.
/// The user's filters are applied to it where it is drawn, so flipping one costs no rebuild.
/// </param>
/// <param name="OutfitsStored">
/// Only meaningful beside <see cref="CollectionMarker.OutfitPartial"/>: how many of the outfit sets
/// using this piece are stored holding it. Zero for every other marker, and defaulted so the
/// subclasses that pass the base's fields through do not have to know about it.
/// </param>
/// <param name="OutfitsTotal">How many sets use it at all, as the denominator for the above.</param>
public record GearRow(
    uint ItemId,
    string Name,
    ushort IconId,
    int SlotOrder,
    string SlotName,
    CollectionMarker Marker,
    bool JobEquippable,
    EquipLock Locks,
    int OutfitsStored = 0,
    int OutfitsTotal = 0)
{
    public bool IsOwned =>
        !CollectionMarkers.IsMissing(Marker) && Marker != CollectionMarker.Unknown;
}
