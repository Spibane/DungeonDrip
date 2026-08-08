namespace DungeonDrip.Core;

/// <summary>One item up for a Need/Greed roll, and whether the collection already has it.</summary>
/// <remarks>
/// Deliberately not a <see cref="GearRow"/>, which is the wrong shape here in both directions. A
/// roll has no slot and no grouping, so most of that record would be filled in to be ignored; and
/// the marker vocabulary is too fine, because the only question anyone has time for mid-roll is
/// whether to press Need.
/// </remarks>
public sealed record LootRoll(uint ItemId, string Name, ushort IconId, bool Owned);
