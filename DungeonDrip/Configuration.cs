using System.Collections.Generic;
using Dalamud.Configuration;

namespace DungeonDrip;

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

public enum LootCompanionSide
{
    Auto,
    Left,
    Right,
}

/// <summary>Which storage counts when deciding whether a piece is collected.</summary>
public enum CollectionScope
{
    Both,
    DresserOnly,
    ArmoireOnly,
}

/// <summary>How the missing list is grouped.</summary>
public enum MissingGrouping
{
    /// <summary>By equipment slot - head, body, hands and so on.</summary>
    Slot,

    /// <summary>By the role allowed to roll Need, for claiming during a run.</summary>
    Role,
}

public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    /// <summary>Pop the window open when you zone into a duty we have loot data for.</summary>
    public bool AutoOpenOnDutyEnter { get; set; } = true;

    /// <summary>Suppress the automatic pop-up when you already have everything.</summary>
    public bool HideWhenNothingMissing { get; set; } = true;

    /// <summary>Close the window again when you leave the duty.</summary>
    public bool CloseWhenLeavingDuty { get; set; } = true;

    /// <summary>Also treat bags, armoury, equipped gear and saddlebags as owning a piece.</summary>
    public bool CountInventoryAndEquipped { get; set; }

    public OutfitOwnershipMode OutfitOwnership { get; set; } = OutfitOwnershipMode.AnyOutfit;

    /// <summary>Show owned pieces greyed out alongside the missing ones.</summary>
    public bool ShowOwnedItems { get; set; }

    /// <summary>Call out which role has the most still missing, above the list.</summary>
    public bool ShowRoleSummary { get; set; } = true;

    /// <summary>Restrict the list to gear the current job can actually wear.</summary>
    public bool OnlyCurrentJobEquippable { get; set; }

    /// <summary>Leave main-hand weapons out of the list.</summary>
    public bool HideWeapons { get; set; }

    /// <summary>Which storage is compared against.</summary>
    public CollectionScope Scope { get; set; } = CollectionScope.Both;

    /// <summary>Group the missing list by slot, or by who can roll Need on it.</summary>
    public MissingGrouping Grouping { get; set; } = MissingGrouping.Slot;

    /// <summary>Headings the user has collapsed, remembered between sessions.</summary>
    public List<string> CollapsedGroups { get; set; } = [];

    /// <summary>Age at which the cached Glamour Dresser snapshot starts warning you.</summary>
    public int StaleAfterDays { get; set; } = 7;

    /// <summary>Record gear seen dropping in a duty, to fill gaps the upstream dataset has.</summary>
    public bool LearnDropsFromLoot { get; set; } = true;

    /// <summary>Look duties up on the FFXIV Console Games Wiki, which covers new content far better.</summary>
    public bool UseWikiSource { get; set; } = true;

    /// <summary>Show a companion window beside the loot roll window listing what you still need.</summary>
    public bool ShowLootCompanion { get; set; } = true;

    /// <summary>Which side of the loot window the companion sits on. Auto flips if space is tight.</summary>
    public LootCompanionSide LootCompanionSide { get; set; } = LootCompanionSide.Auto;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
