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
    /// <summary>Loot for one duty, or the roulette advice outside one.</summary>
    Duty,

    /// <summary>The collection as a whole, which no duty is involved in.</summary>
    Collection,
}

/// <summary>
/// Which reference site the plugin's item links point at.
/// </summary>
/// <remarks>
/// Serialised by ordinal, so new members must be appended and never inserted - reordering silently
/// repoints every existing install at a different site.
///
/// The order is also the order of the Settings combo, and it puts the three sites addressed by item id
/// first. Those cannot land on the wrong page; the three below them are addressed by article title,
/// which is a guess that can miss. See <see cref="Core.Sources.ItemLink"/>.
/// </remarks>
public enum LookupSite
{
    Teamcraft,
    GarlandTools,
    Universalis,
    ConsoleGamesWiki,
    GamerEscape,
    Lodestone,
}

/// <summary>
/// Which of the Collection view's three tabs is showing.
/// </summary>
/// <remarks>
/// Serialised by ordinal, so new members are appended and never inserted.
///
/// Window state rather than a preference, like <see cref="MainWindowMode"/>, so it has no control in
/// Settings. It is stored for the same reason that one is: the window already reopens on whichever of
/// its two halves was last used, and landing on the first tab every time would half-remember where the
/// reader had got to.
/// </remarks>
public enum CollectionTab
{
    /// <summary>Outfit sets part way through.</summary>
    Sets,

    /// <summary>What a held currency will buy that is not collected.</summary>
    Buy,

    /// <summary>How full the Glamour Dresser is, and the spare copies that would empty it.</summary>
    Dresser,
}

/// <summary>How the missing list is grouped.</summary>
public enum MissingGrouping
{
    /// <summary>By equipment slot - head, body, hands and so on.</summary>
    Slot,

    /// <summary>By the role allowed to roll Need, for claiming during a run.</summary>
    Role,

    /// <summary>
    /// By the boss or coffer inside the duty that drops it.
    /// </summary>
    /// <remarks>
    /// Only as complete as the wiki lookup for that duty, which is why this is not the default: a duty
    /// covered by the downloaded dataset alone has nothing to group by and lands in one heading.
    /// </remarks>
    Source,
}

/// <summary>
/// Everything the user has chosen, as one object serialised to the plugin's config file.
/// </summary>
/// <remarks>
/// Read by nearly every surface, which is why it is passed around rather than consulted through a
/// static: the ownership decision and the report builder take it as an argument so they stay
/// testable without a live plugin behind them.
///
/// Property names are the JSON keys, so renaming one silently resets that setting for every existing
/// user. <see cref="Version"/> plus <see cref="MigrateIfNeeded"/> is the way to reshape anything.
/// </remarks>
public class Configuration : IPluginConfiguration
{
    /// <summary>Config-file shape, bumped when a migration reshapes it. See <see cref="MigrateIfNeeded"/>.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Pop the window open on zoning into a duty that has loot data.</summary>
    public bool AutoOpenOnDutyEnter { get; set; } = true;

    /// <summary>Suppress the automatic pop-up when nothing is missing.</summary>
    public bool HideWhenNothingMissing { get; set; } = true;

    /// <summary>Close the window again on leaving the duty.</summary>
    public bool CloseWhenLeavingDuty { get; set; } = true;

    /// <summary>Also treat bags, armoury, equipped gear and saddlebags as owning a piece.</summary>
    public bool CountInventoryAndEquipped { get; set; }

    /// <summary>
    /// Also treat gear left with a retainer as owning a piece.
    /// </summary>
    /// <remarks>
    /// Off, like the bags it sits beside, and for the same reason: a retainer is somewhere a piece is
    /// held, not somewhere it is put away. Nothing outside the Glamour Dresser and the Armoire can be
    /// worn as a glamour, so counting a retainer copy answers "do I own one" rather than "can I wear
    /// it", and which of those the lists should mean is the user's call.
    /// </remarks>
    public bool CountRetainers { get; set; }

    /// <summary>
    /// Whether <see cref="CountRetainers"/> also covers gear the retainer is wearing.
    /// </summary>
    /// <remarks>
    /// Off, and a narrowing of the setting above rather than a place of its own - counting what a
    /// retainer wears while not counting what a retainer holds is a combination nobody means.
    ///
    /// Excluded by default because a retainer's own gear is the worst kind of owned. It is not in their
    /// bags, so "with a retainer" sends the player through seven pages after a piece that was never
    /// going to be there; and it is doing a job where it is, since a retainer's item level decides what
    /// their ventures bring back. Both halves of that argue for leaving it out unless it is asked for.
    /// </remarks>
    public bool CountRetainerEquipped { get; set; }

    /// <summary>How a piece present only inside stored outfit sets is judged.</summary>
    public OutfitOwnershipMode OutfitOwnership { get; set; } = OutfitOwnershipMode.AnyOutfit;

    /// <summary>Show owned pieces greyed out alongside the missing ones, in every gear list.</summary>
    public bool ShowOwnedItems { get; set; }

    /// <summary>Call out which role has the most still missing, above the list.</summary>
    public bool ShowRoleSummary { get; set; } = true;

    /// <summary>Restrict every gear list to what the current job can actually wear.</summary>
    public bool OnlyCurrentJobEquippable { get; set; }

    /// <summary>
    /// Leave out gear locked to the other gender, everywhere, including whole outfit sets that turn
    /// out to be nothing but such pieces.
    /// </summary>
    /// <remarks>
    /// Separate from the job filter although it reads like a second helping of it. That one is about
    /// what is equipped this minute and flips several times an evening; this is a standing fact
    /// about the character, and the two are wanted independently - "everything I could ever wear" is
    /// the common case and needs this on and that off.
    ///
    /// Off by default, like every other filter here. Neither lock is quite permanent - a Fantasia
    /// changes both - so a collector keeping the other half in mind is not doing anything odd, and the
    /// plugin should not decide for them.
    /// </remarks>
    public bool OnlyCurrentGenderEquippable { get; set; }

    /// <summary>
    /// The same for gear locked to races this character is not.
    /// </summary>
    /// <remarks>
    /// Its own setting rather than folded in with the gender one, because they are not the same size
    /// of thing: the gender lock covers a couple of hundred pieces and 97 whole outfit sets, the race
    /// lock the 56 pieces of another race's starting gear. Somebody who wants the sets out of the way
    /// does not necessarily want the racial pieces out of the way as well.
    ///
    /// Every race-locked row in the game's sheet locks a gender too, so with both on the second one
    /// only adds the same-gender half of another race's wardrobe.
    /// </remarks>
    public bool OnlyCurrentRaceEquippable { get; set; }

    /// <summary>Leave weapons and off-hands out of every gear list.</summary>
    public bool HideWeapons { get; set; }

    /// <summary>
    /// Empty the fitting room before trying on a whole outfit, so the set is shown on its own rather
    /// than over whatever was already in there.
    /// </summary>
    public bool ClearFittingRoomForOutfits { get; set; } = true;

    /// <summary>Which storage is compared against.</summary>
    public CollectionScope Scope { get; set; } = CollectionScope.Both;

    /// <summary>Group the missing list by slot, by who can roll Need, or by the boss that drops it.</summary>
    public MissingGrouping Grouping { get; set; } = MissingGrouping.Slot;

    /// <summary>
    /// Which view the main window was last showing. Window state rather than a preference, so it
    /// has no control in Settings - like the vendor panel's dragged size.
    /// </summary>
    public MainWindowMode MainWindowMode { get; set; } = MainWindowMode.Duty;

    /// <summary>Which Collection tab was last showing. Window state, as above.</summary>
    public CollectionTab CollectionTab { get; set; } = CollectionTab.Sets;

    /// <summary>Headings the user has collapsed, remembered between sessions.</summary>
    public List<string> CollapsedGroups { get; set; } = [];

    /// <summary>
    /// Headings the user has opened, for the few that start shut.
    /// </summary>
    /// <remarks>
    /// A second list rather than a flag inside the first, because both record a <em>deviation from the
    /// default</em> and the defaults point opposite ways. A heading that starts open remembers being
    /// shut; one that starts shut - the gil group, which buys thousands of pieces - remembers being
    /// opened. Storing "open" and "closed" in one list would make an absent key ambiguous.
    /// </remarks>
    public List<string> OpenedGroups { get; set; } = [];

    /// <summary>Age at which the cached Glamour Dresser snapshot starts warning.</summary>
    public int StaleAfterDays { get; set; } = 7;

    /// <summary>Record gear seen dropping in a duty, to fill gaps the upstream dataset has.</summary>
    public bool LearnDropsFromLoot { get; set; } = true;

    /// <summary>Look duties up on the FFXIV Console Games Wiki, which covers new content far better.</summary>
    public bool UseWikiSource { get; set; } = true;

    /// <summary>Show a companion window beside the loot roll window listing what is missing.</summary>
    public bool ShowLootCompanion { get; set; } = true;

    /// <summary>Which side of the loot window the companion sits on. Auto flips if space is tight.</summary>
    public PanelSide LootCompanionSide { get; set; } = PanelSide.Auto;

    /// <summary>The panel beside vendor windows, marking which of their stock is collected.</summary>
    public PanelSettings VendorPanel { get; set; } = new();

    /// <summary>The panel beside the Glamour Dresser, listing carried gear that is not stored.</summary>
    public PanelSettings DresserPanel { get; set; } = new();

    /// <summary>The panel beside the market board's browse list.</summary>
    public PanelSettings MarketBoardPanel { get; set; } = new();

    /// <summary>The panel beside the Armoire, listing held gear the Armoire has not got.</summary>
    public PanelSettings ArmoirePanel { get; set; } = new();

    /// <summary>
    /// List armoury-chest gear on both store panels.
    /// </summary>
    /// <remarks>
    /// Shared by the Glamour Dresser panel and the Armoire one, and named for the dresser only because
    /// that panel existed first and renaming the property would silently reset it. Gear a gearset is
    /// depending on is depending on it whichever box is being stood at, so a second copy of this per
    /// panel would be two switches for one idea.
    /// </remarks>
    public bool DresserPanelIncludesArmoury { get; set; } = true;

    /// <summary>List equipped gear on both store panels.</summary>
    /// <inheritdoc cref="DresserPanelIncludesArmoury" path="/remarks"/>
    public bool DresserPanelIncludesEquipped { get; set; } = true;

    // The vendor panel's settings used to be five flat properties. They are nullable so that
    // "absent" stays distinguishable from "the user turned this off", and they are cleared once
    // folded in, so a second run is a no-op even if the version number is wrong.
    public bool? ShowVendorPanel { get; set; }
    public PanelSide? VendorPanelSide { get; set; }
    public float? VendorPanelWidth { get; set; }
    public float? VendorPanelHeight { get; set; }
    public bool? VendorGroupBySlot { get; set; }

    /// <summary>
    /// Folds any pre-v2 flat vendor settings into their panel object.
    /// </summary>
    /// <returns>Whether anything moved, and the caller therefore has to save.</returns>
    public bool MigrateIfNeeded()
    {
        if (ShowVendorPanel == null && VendorPanelSide == null && VendorPanelWidth == null &&
            VendorPanelHeight == null && VendorGroupBySlot == null)
        {
            // Bumping the version counts as something to save, or the check runs again every
            // launch for the life of the config.
            var stamp = Version < 2;
            Version = 2;
            return stamp;
        }

        VendorPanel.Enabled = ShowVendorPanel ?? VendorPanel.Enabled;
        VendorPanel.Side = VendorPanelSide ?? VendorPanel.Side;
        VendorPanel.Width = VendorPanelWidth ?? VendorPanel.Width;
        VendorPanel.Height = VendorPanelHeight ?? VendorPanel.Height;
        VendorPanel.GroupBySlot = VendorGroupBySlot ?? VendorPanel.GroupBySlot;

        ShowVendorPanel = null;
        VendorPanelSide = null;
        VendorPanelWidth = null;
        VendorPanelHeight = null;
        VendorGroupBySlot = null;
        Version = 2;

        Plugin.Log.Information("Migrated the vendor panel's settings into their own object.");
        return true;
    }

    /// <summary>
    /// Offer the plugin's gear actions on the game's own right-click menus.
    /// </summary>
    /// <remarks>
    /// On by default. This adds entries through the API Dalamud provides for exactly that and
    /// composes with other plugins doing the same, and Dalamud's own prefix makes the source
    /// obvious - so it is not the kind of thing that needs opting into.
    /// </remarks>
    public bool ShowGameContextMenu { get; set; } = true;

    /// <summary>
    /// Append a line to the game's own item tooltip saying where the piece is in the collection.
    /// </summary>
    /// <remarks>
    /// Off by default, unlike the right-click menu. This is the one thing that modifies a game
    /// window's contents rather than adding beside it, and the one thing that hooks a game
    /// function, so it is opted into rather than out of.
    /// </remarks>
    public bool ShowTooltipLine { get; set; }

    /// <summary>
    /// Say where a piece comes from besides a duty - crafted, bought, or given for something.
    /// </summary>
    /// <remarks>
    /// On by default, but worth a switch rather than being unconditional: answering it means sweeping
    /// every recipe, shop, quest and achievement row in the game, and that build is skipped entirely
    /// while this is off. Somebody who only uses the plugin inside duties pays nothing for a question
    /// they never ask.
    /// </remarks>
    public bool ShowAcquisitionSources { get; set; } = true;

    /// <summary>Which reference site the item links open.</summary>
    public LookupSite LookupSite { get; set; } = LookupSite.Teamcraft;

    /// <summary>
    /// Hold the "Ready to buy" list to pieces that belong to an outfit set.
    /// </summary>
    /// <remarks>
    /// Off by default, like every other filter here, since a filter that hides things before being asked
    /// to is the wrong way round. It is worth having because the unfiltered list is mostly single pieces:
    /// only 14% of priced gear belongs to an outfit set, and for gil it is 8% - 483 pieces of 5,690. On
    /// somebody collecting whole outfits the rest is noise, and someone hunting one specific piece is
    /// better served by looking it up by name than by scrolling this.
    ///
    /// Written from the section's own toggle rather than only from Settings, which is why it lives here
    /// rather than in per-session state: a view filter that resets every launch would be re-set every
    /// launch.
    /// </remarks>
    public bool ReadyToBuyOutfitsOnly { get; set; }

    /// <summary>
    /// Leave out gear that a counter will only sell back to whoever already earned it.
    /// </summary>
    /// <remarks>
    /// <b>On by default, unlike the other filters here, because this one corrects the list rather than
    /// narrowing it.</b> The sheets record what a counter stocks and not what a character is entitled to,
    /// so 1,053 pieces are offered at a price most characters cannot pay at any balance - 1,028 of them
    /// in gil, which is the balance everybody has. Shipping that as the default would ship a list that is
    /// wrong for nearly everyone.
    ///
    /// Two counters are involved and they need different tests. 337 pieces come from the Calamity
    /// Salvager and the Recompense Officer, who restock seasonal and event gear for whoever was there at
    /// the time. The other 716 come from any special shop whose only cost is gil, which is a buy-back
    /// price wherever it appears - Rowena's representatives re-selling for small change what tomestones
    /// bought.
    ///
    /// Off is still worth offering: a long-standing character really can buy these back, and for them the
    /// exclusion hides real answers. See <see cref="Core.Sources.RestrictedVendors"/> and
    /// <see cref="Core.Sources.ShopSources"/>.
    ///
    /// <b>Reaches the descriptions as well as the list, but not evenly.</b> With this on, the 4 pieces whose
    /// only route is a nameless gil buy-back are described as unknown everywhere, since a price with no way
    /// to earn the thing tells nobody anything. The 337 event pieces keep their line: hiding "Event
    /// re-purchase - 47 gil" would leave a lookup answering "source unknown" about a Pumpkin Head, which is
    /// the one thing worth saying about it. Applied in <see cref="Plugin.SourcesFor"/>, which every surface
    /// that describes a piece goes through.
    /// </remarks>
    public bool ExcludeSellBackVendors { get; set; } = true;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
