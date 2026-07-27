using Dalamud.Configuration;

namespace GlamourAssistant;

/// <summary>
/// How a piece that is only present inside stored outfit sets should be judged.
/// </summary>
public enum OutfitOwnershipMode
{
    /// <summary>Owned as soon as one stored outfit contains the piece.</summary>
    AnyOutfit,

    /// <summary>Owned only when every outfit set that lists the piece is stored with that slot filled.</summary>
    AllOutfits,
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Pop the window open when you zone into a duty we have loot data for.</summary>
    public bool AutoOpenOnDutyEnter { get; set; } = true;

    /// <summary>Suppress the automatic pop-up when you already have everything.</summary>
    public bool HideWhenNothingMissing { get; set; } = true;

    /// <summary>Also treat bags, armoury, equipped gear and saddlebags as owning a piece.</summary>
    public bool CountInventoryAndEquipped { get; set; }

    public OutfitOwnershipMode OutfitOwnership { get; set; } = OutfitOwnershipMode.AnyOutfit;

    /// <summary>Show owned pieces greyed out alongside the missing ones.</summary>
    public bool ShowOwnedItems { get; set; }

    /// <summary>Restrict the list to gear the current job can actually wear.</summary>
    public bool OnlyCurrentJobEquippable { get; set; }

    /// <summary>Age at which the cached Glamour Dresser snapshot starts warning you.</summary>
    public int StaleAfterDays { get; set; } = 7;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
