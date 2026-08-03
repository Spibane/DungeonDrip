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

/// <summary>Which side of the game window a pinned panel rides on.</summary>
/// <remarks>
/// Serialised as an integer, so the rename from the loot companion's own name costs existing
/// configs nothing. The properties keep their names for the same reason - those are the JSON keys.
/// </remarks>
public enum PanelSide
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

/// <summary>Which of the main window's two jobs it is currently doing.</summary>
public enum MainWindowMode
{
    /// <summary>Loot for one duty, or the roulette advice when you are not standing in one.</summary>
    Duty,

    /// <summary>Your collection as a whole, which no duty is involved in.</summary>
    Collection,
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

    /// <summary>Show owned pieces greyed out alongside the missing ones, in every gear list.</summary>
    public bool ShowOwnedItems { get; set; }

    /// <summary>Call out which role has the most still missing, above the list.</summary>
    public bool ShowRoleSummary { get; set; } = true;

    /// <summary>Restrict every gear list to what the current job can actually wear.</summary>
    public bool OnlyCurrentJobEquippable { get; set; }

    /// <summary>Leave weapons and off-hands out of every gear list.</summary>
    public bool HideWeapons { get; set; }

    /// <summary>
    /// Empty the fitting room before trying on a whole outfit, so the set is shown on its own rather
    /// than over whatever was already in there.
    /// </summary>
    public bool ClearFittingRoomForOutfits { get; set; } = true;

    /// <summary>Which storage is compared against.</summary>
    public CollectionScope Scope { get; set; } = CollectionScope.Both;

    /// <summary>Group the missing list by slot, or by who can roll Need on it.</summary>
    public MissingGrouping Grouping { get; set; } = MissingGrouping.Slot;

    /// <summary>
    /// Which view the main window was last showing. Window state rather than a preference, so it
    /// has no control in Settings - like the vendor panel's dragged size.
    /// </summary>
    public MainWindowMode MainWindowMode { get; set; } = MainWindowMode.Duty;

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
    public PanelSide LootCompanionSide { get; set; } = PanelSide.Auto;

    /// <summary>Show a panel beside vendor windows marking which of their stock you already have.</summary>
    public bool ShowVendorPanel { get; set; } = true;

    /// <summary>Which side of the vendor window the panel sits on. Auto flips if space is tight.</summary>
    public PanelSide VendorPanelSide { get; set; } = PanelSide.Auto;

    /// <summary>
    /// Size the user dragged the vendor panel to. Zero means follow the vendor window's height and
    /// fit the width to the longest name, which is the default and what the reset button restores.
    /// </summary>
    public float VendorPanelWidth { get; set; }

    /// <inheritdoc cref="VendorPanelWidth"/>
    public float VendorPanelHeight { get; set; }

    /// <summary>Group the vendor panel by equipment slot rather than listing it flat.</summary>
    public bool VendorGroupBySlot { get; set; } = true;

    /// <summary>
    /// Offer the plugin's gear actions on the game's own right-click menus.
    /// </summary>
    /// <remarks>
    /// On by default. This adds entries through the API Dalamud provides for exactly that and
    /// composes with other plugins doing the same, and Dalamud's own prefix makes the source
    /// obvious - so it is not the kind of thing that needs opting into.
    /// </remarks>
    public bool ShowGameContextMenu { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
