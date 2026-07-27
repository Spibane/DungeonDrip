namespace DungeonDrip.Game;

public static class ItemId
{
    private const uint HqOffset = 1_000_000;
    private const uint CollectableOffset = 500_000;

    /// <summary>Strips the HQ / collectable offsets the client adds to raw item ids.</summary>
    public static uint Normalize(uint rawItemId) => rawItemId switch
    {
        >= HqOffset => rawItemId - HqOffset,
        >= CollectableOffset => rawItemId - CollectableOffset,
        _ => rawItemId,
    };
}
