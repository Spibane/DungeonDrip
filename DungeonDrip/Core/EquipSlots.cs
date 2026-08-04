using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>
/// Names a piece's equipment slot and gives it a sort order.
/// </summary>
/// <remarks>
/// One place, because every gear list in the plugin sorts and groups by slot and they have to agree.
/// The order is the character sheet's - weapon down to ring - rather than the sheet's own column
/// order, which the outfit catalogue uses and which has no waist column at all.
///
/// The category row carries one column per slot rather than a slot id, so the answer is a chain of
/// tests rather than a lookup. Two-slot rings collapse into one heading: which hand a ring is on is
/// not something a collection cares about.
/// </remarks>
public static class EquipSlots
{
    private const int MainHandOrder = 0;
    private const int OffHandOrder = 1;

    /// <summary>
    /// Main hand and off hand together - off-hands drop alongside the weapon they pair with, so the
    /// "skip weapons" filter covers both.
    /// </summary>
    public static bool IsWeaponSlot(int order) => order is MainHandOrder or OffHandOrder;

    /// <summary>Groups an item by the slot it occupies, in the order the character sheet uses.</summary>
    public static (int Order, string Name) Describe(EquipSlotCategory category)
    {
        if (category.MainHand > 0) return (0, "Weapon");
        if (category.OffHand > 0) return (1, "Off Hand");
        if (category.Head > 0) return (2, "Head");
        if (category.Body > 0) return (3, "Body");
        if (category.Gloves > 0) return (4, "Hands");
        if (category.Legs > 0) return (5, "Legs");
        if (category.Feet > 0) return (6, "Feet");
        if (category.Waist > 0) return (7, "Waist");
        if (category.Ears > 0) return (8, "Earrings");
        if (category.Neck > 0) return (9, "Necklace");
        if (category.Wrists > 0) return (10, "Bracelets");
        if (category.FingerL > 0 || category.FingerR > 0) return (11, "Ring");
        return (12, "Other");
    }
}
