using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DungeonDrip.Windows;

/// <summary>
/// The settings window: every preference, plus the data resets and what the plugin knows.
/// </summary>
/// <remarks>
/// Tabbed rather than one column, because the settings divide by where they are noticed - the duty
/// window, the panels beside game windows, the entries added to game UI - and a flat list of thirty
/// checkboxes gives no clue which of them a given surface reads.
///
/// Every write saves immediately rather than on a button, so there is no state to be lost by closing
/// the window, and the tabs report back whether anything moved rather than saving themselves - one
/// save per frame however many toggles were hit.
/// </remarks>
public class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    /// <summary>
    /// Which reset button has been pressed once and is waiting to be confirmed.
    /// </summary>
    /// <remarks>
    /// One at a time, deliberately: arming a second forgets the first, so there is never more than one
    /// primed "yes" on screen to hit by accident. Per-session rather than saved - a half-pressed button
    /// is not a preference.
    /// </remarks>
    private string? armedReset;

    /// <summary>The marker legend the two store panels share, which is a location rather than a state.</summary>
    private const string StorePanelNote =
        "Markers give the location, not ownership:\n" +
        "    case: bags        box: armoury chest\n" +
        "    figure: worn        horse: saddlebag\n" +
        "Amber: something is required first.";

    /// <summary>Said on both copies of a setting that has one value.</summary>
    private const string StorePanelShared = "Applies to the Glamour Dresser and Armoire panels alike.";

    public ConfigWindow(Plugin plugin)
        : base("Dungeon Drip Settings###DungeonDripConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }


    /// <summary>
    /// Draws the tabs and saves once at the end if any of them reported a change.
    /// </summary>
    /// <remarks>
    /// The report is invalidated alongside the save, unconditionally: nearly every setting here
    /// changes what the duty list should contain, and working out which ones do not would be a
    /// second copy of that knowledge to keep in step for no gain - a rebuild is one frame's work.
    /// </remarks>
    public override void Draw()
    {
        var changed = false;

        using var tabs = ImRaii.TabBar("##settingsTabs");
        if (!tabs.Success)
            return;

        using (var general = ImRaii.TabItem("General"))
        {
            if (general.Success)
                changed |= DrawGeneralTab();
        }

        using (var duties = ImRaii.TabItem("Duties"))
        {
            if (duties.Success)
                changed |= DrawDutiesTab();
        }

        using (var panels = ImRaii.TabItem("Panels"))
        {
            if (panels.Success)
                changed |= DrawPanelsTab();
        }

        using (var inGame = ImRaii.TabItem("In-game UI"))
        {
            if (inGame.Success)
                changed |= DrawInGameTab();
        }

        using (var data = ImRaii.TabItem("Data"))
        {
            if (data.Success)
                changed |= DrawDataTab();
        }

        if (changed)
        {
            configuration.Save();
            plugin.InvalidateReport();
        }
    }

    /// <summary>
    /// A muted heading and the lines qualifying it, for the block that opens a tab.
    /// </summary>
    /// <remarks>
    /// No rule above it, unlike <see cref="SectionHeading"/>: the tab bar draws its own line, and a
    /// second one under it separates nothing from the tab strip.
    /// </remarks>
    private static void TabHeading(string label, params string[] notes)
    {
        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, label);

        foreach (var note in notes)
            ImGui.TextColored(Palette.Muted, note);

        ImGui.Spacing();
    }

    /// <summary>
    /// The same heading with a rule above it, for every block after the first.
    /// </summary>
    /// <remarks>
    /// The rule is what makes a tab read as a handful of subjects rather than one run of checkboxes.
    /// A blank line was doing this job and could not: the notes under a heading are muted text the
    /// same as the heading itself, so where one block ended and the next began was a matter of
    /// counting gaps.
    /// </remarks>
    private static void SectionHeading(string label, params string[] notes)
    {
        Rule();
        TabHeading(label, notes);
    }

    /// <summary>A rule with room around it, for the two places that divide without a heading.</summary>
    private static void Rule()
    {
        ImGui.Spacing();
        ImGui.Separator();
    }

    private bool DrawGeneralTab()
    {
        var changed = false;

        TabHeading(
            "What to list",
            "Applies everywhere: the duty window, the loot roll list and vendors.");

        var showOwned = configuration.ShowOwnedItems;
        if (UiParts.Toggle("List pieces I already have, greyed out", ref showOwned))
        {
            configuration.ShowOwnedItems = showOwned;
            changed = true;
        }

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (UiParts.Toggle("Only show gear my current job can wear", ref jobOnly))
        {
            configuration.OnlyCurrentJobEquippable = jobOnly;
            changed = true;
        }

        // The two locks are one idea with two switches, so they sit together under one line rather
        // than as two unrelated toggles in the run of filters above.
        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Leave out gear this character can never wear:");

        var genderOnly = configuration.OnlyCurrentGenderEquippable;
        if (UiParts.Toggle("Locked to the other gender", ref genderOnly,
                "A couple of hundred pieces are locked to one gender, and 97 outfit sets are\n" +
                "nothing but those - the summer halters and tops, the Moonfire attire.\n" +
                "A set with no wearable pieces left stops being one you are part way through,\n" +
                "and a partly locked set counts out of the pieces you can wear."))
        {
            configuration.OnlyCurrentGenderEquippable = genderOnly;
            changed = true;
        }

        var raceOnly = configuration.OnlyCurrentRaceEquippable;
        if (UiParts.Toggle("Locked to another race", ref raceOnly,
                "The 56 pieces of another race's starting gear - the Hyuran Tunic, the\n" +
                "Elezen Surcoat and their kin.\n" +
                "Every race-locked piece is gender-locked as well, so with both on this adds\n" +
                "the same-gender half of what the other setting has not already taken out."))
        {
            configuration.OnlyCurrentRaceEquippable = raceOnly;
            changed = true;
        }

        ImGui.Spacing();

        var hideWeapons = configuration.HideWeapons;
        if (UiParts.Toggle("Skip weapons", ref hideWeapons,
                "Main hands and off-hands."))
        {
            configuration.HideWeapons = hideWeapons;
            changed = true;
        }

        SectionHeading(
            "Trying on",
            "Right-click any piece in any of the three lists.");

        var clearFittingRoom = configuration.ClearFittingRoomForOutfits;
        if (UiParts.Toggle("Empty the fitting room before trying on an outfit", ref clearFittingRoom,
                "Shows the set on its own rather than over what was already in the room.\n" +
                "Discards the preview only; saved outfits are untouched.\n" +
                "Single pieces never clear anything."))
        {
            configuration.ClearFittingRoomForOutfits = clearFittingRoom;
            changed = true;
        }

        SectionHeading(
            "What counts as owned",
            "Also applies everywhere.");

        var scope = configuration.Scope;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Compare against", ScopeLabel(scope)))
        {
            foreach (var option in Enum.GetValues<CollectionScope>())
            {
                if (ImGui.Selectable(ScopeLabel(option), scope == option))
                {
                    configuration.Scope = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Lists only pieces the chosen store can hold. The two overlap: most Armoire items\n" +
                "can also go in the Dresser, so Armoire-only is a narrower view of the same gear.");
        }

        // The two holding places sit under one line rather than as unrelated toggles. Neither is a
        // store: nothing outside the Dresser and the Armoire can be worn as a glamour, so both of
        // these change the lists from "can I wear this" to "do I own one anywhere".
        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Also count gear you are only holding, which cannot be");
        ImGui.TextColored(Palette.Muted, "worn as a glamour until it goes in a box:");

        var countInventory = configuration.CountInventoryAndEquipped;
        if (UiParts.Toggle("Bags, armoury chest, equipped gear and saddlebags", ref countInventory))
        {
            configuration.CountInventoryAndEquipped = countInventory;
            changed = true;
        }

        var countRetainers = configuration.CountRetainers;
        if (UiParts.Toggle("Gear in your retainers' bags", ref countRetainers,
                "A retainer's bags can only be read while you have that retainer open, so this works\n" +
                "from the last time you did - one snapshot per retainer, and a retainer you have never\n" +
                "opened is simply not counted. Gear listed on the market board is left out, since it\n" +
                "can sell without you being there.\n" +
                "The Data tab lists each retainer, when it was last read, and can forget any of them."))
        {
            configuration.CountRetainers = countRetainers;
            changed = true;
        }

        // Indented under the setting it narrows, and only offered while that one is on: counting what
        // a retainer wears but not what they hold is a combination nobody means.
        if (countRetainers)
        {
            ImGui.Indent();

            var countWorn = configuration.CountRetainerEquipped;
            if (UiParts.Toggle("...including gear the retainer is wearing", ref countWorn,
                    "Off by default, because a retainer's own gear is the worst kind of owned.\n" +
                    "It is not in their bags, so \"you have one\" sends you through seven pages looking\n" +
                    "for something that was never going to be there - and it is doing a job where it is,\n" +
                    "since a retainer's item level decides what their ventures bring back.\n" +
                    "With this on, every list says \"worn by one of your retainers\" rather than naming\n" +
                    "the bags."))
            {
                configuration.CountRetainerEquipped = countWorn;
                changed = true;
            }

            ImGui.Unindent();
        }

        ImGui.Spacing();
        ImGui.Text("Pieces stored inside outfit sets:");

        var mode = configuration.OutfitOwnership;
        if (ImGui.RadioButton("Owned if any stored outfit contains the piece", ref mode, OutfitOwnershipMode.AnyOutfit))
        {
            configuration.OutfitOwnership = mode;
            changed = true;
        }

        if (ImGui.RadioButton("Owned only if every outfit with that piece is stored", ref mode, OutfitOwnershipMode.AllOutfits))
        {
            configuration.OutfitOwnership = mode;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "A piece can belong to several sets. Strict requires all of them stored.");
        }

        Rule();
        ImGui.Spacing();

        // Worth showing: a short alias is skipped when another plugin already owns it, and silently
        // missing commands would otherwise look like a bug here.
        ImGui.TextColored(Palette.Muted, $"Commands: {string.Join("  ", plugin.Commands.Registered)}");

        ImGui.Spacing();
        return changed;
    }

    private bool DrawDutiesTab()
    {
        var changed = false;

        TabHeading("Duty window");

        var autoOpen = configuration.AutoOpenOnDutyEnter;
        if (UiParts.Toggle("Open automatically when I enter a duty", ref autoOpen))
        {
            configuration.AutoOpenOnDutyEnter = autoOpen;
            changed = true;
        }

        var hideWhenComplete = configuration.HideWhenNothingMissing;
        if (UiParts.Toggle("...but not when I already have everything", ref hideWhenComplete))
        {
            configuration.HideWhenNothingMissing = hideWhenComplete;
            changed = true;
        }

        var closeOnLeave = configuration.CloseWhenLeavingDuty;
        if (UiParts.Toggle("Close again when I leave the duty", ref closeOnLeave,
                "A pinned duty stays open."))
        {
            configuration.CloseWhenLeavingDuty = closeOnLeave;
            changed = true;
        }

        var roleSummary = configuration.ShowRoleSummary;
        if (UiParts.Toggle("Call out the role with the most missing pieces", ref roleSummary,
                "A line above the list naming whichever role still needs the most, so you know what to\n" +
                "chase. Counts by role even when the list itself is grouped by slot; hover it for the\n" +
                "full breakdown."))
        {
            configuration.ShowRoleSummary = roleSummary;
            changed = true;
        }

        var grouping = configuration.Grouping;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo("Group the list by", GroupingLabel(grouping)))
        {
            foreach (var option in Enum.GetValues<MissingGrouping>())
            {
                if (ImGui.Selectable(GroupingLabel(option), grouping == option))
                {
                    configuration.Grouping = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Role grouping buckets pieces by who is allowed to roll Need, which is what you want\n" +
                "when claiming during a run.\n" +
                "Boss and coffer grouping is only as complete as the wiki lookup for that duty: a duty\n" +
                "covered by the downloaded dataset alone lands in one heading and says so.\n" +
                "Headings can be collapsed and stay that way.");
        }

        SectionHeading("Loot roll window");

        var companion = configuration.ShowLootCompanion;
        if (UiParts.Toggle("Show a companion list beside the Need/Greed window", ref companion,
                "A separate window pinned to the side of the roll window, marking what you do not own.\n" +
                "It never draws into the game's own loot window, so it cannot fight with plugins that do\n" +
                "(Allagan Tools, Simple Tweaks, VanillaPlus, Collections)."))
        {
            configuration.ShowLootCompanion = companion;
            changed = true;
        }

        if (companion)
        {
            var side = configuration.LootCompanionSide;
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Side", side.ToString()))
            {
                foreach (var option in Enum.GetValues<PanelSide>())
                {
                    if (ImGui.Selectable(option.ToString(), side == option))
                    {
                        configuration.LootCompanionSide = option;
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }
        }

        DrawSharedSettingsNote();
        return changed;
    }

    /// <summary>
    /// The panels that ride beside a game window. One drawing routine, three callers.
    /// </summary>
    /// <remarks>
    /// The marker legend is shown once at the top rather than per panel, since they all speak the
    /// same vocabulary, and a third copy of the enable/side/grouping block is where hand-written
    /// copies would start to drift.
    /// </remarks>
    private bool DrawPanelsTab()
    {
        var changed = false;

        TabHeading("Markers");
        ImGui.BulletText("x - not collected");
        ImGui.BulletText("amber x - not collected, dresser data is old");
        ImGui.BulletText("tick - Glamour Dresser");
        ImGui.BulletText("layers - stored outfit set with gaps");
        ImGui.BulletText("half circle - stored in some of its outfit sets, not all");
        ImGui.BulletText("star - completed outfit set");
        ImGui.BulletText("box - Armoire");
        ImGui.BulletText("case - carried or equipped");
        ImGui.BulletText("figure - in a retainer's bags");
        ImGui.BulletText("figure in a tie - worn by a retainer, not in their bags");
        ImGui.BulletText("? - no dresser data");

        // A rule and no heading: the three panel sections are collapsing headers of their own, so
        // naming the block above them would only repeat the tab.
        Rule();
        ImGui.Spacing();

        changed |= DrawPanelSection(
            "Vendor windows", "vendor", configuration.VendorPanel,
            "Glamour gear the vendor is currently showing, marked by where it is in the\n" +
            "collection. Non-glamour items are left out.\n" +
            "\"/dungeondrip shop\" identifies an unrecognised vendor window.",
            null);

        changed |= DrawPanelSection(
            "Market board", "market", configuration.MarketBoardPanel,
            "Gear in the board's browse list, marked by where it is in the collection.\n" +
            "The listings for a single item get no panel.",
            null);

        changed |= DrawPanelSection(
            "Glamour Dresser", "dresser", configuration.DresserPanel,
            "Carried gear that is not in the dresser or Armoire.",
            StorePanelNote);

        changed |= DrawPanelSection(
            "Armoire", "armoire", configuration.ArmoirePanel,
            "Held gear the Armoire has not got. The game's own \"store an item\" list shows\n" +
            "what the Armoire accepts rather than what it is missing, so a piece already in\n" +
            "there is listed the same as one that is not and is refused on the attempt.",
            StorePanelNote);

        DrawSharedSettingsNote();
        return changed;
    }

    private bool DrawPanelSection(
        string heading, string id, PanelSettings settings, string enableTooltip, string? note)
    {
        if (!ImGui.CollapsingHeader($"{heading}###panel{id}"))
            return false;

        var changed = false;

        if (note != null)
        {
            ImGui.TextColored(Palette.Muted, note);
            ImGui.Spacing();
        }

        var enabled = settings.Enabled;
        if (UiParts.Toggle($"Show this panel##{id}", ref enabled, enableTooltip))
        {
            settings.Enabled = enabled;
            changed = true;
        }

        if (!enabled)
        {
            ImGui.Spacing();
            return changed;
        }

        var side = settings.Side;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.BeginCombo($"Side##{id}", side.ToString()))
        {
            foreach (var option in Enum.GetValues<PanelSide>())
            {
                if (ImGui.Selectable($"{option}##{id}", side == option))
                {
                    settings.Side = option;
                    changed = true;
                }
            }

            ImGui.EndCombo();
        }

        var grouped = settings.GroupBySlot;
        if (UiParts.Toggle($"Group by equipment slot##{id}", ref grouped))
        {
            settings.GroupBySlot = grouped;
            changed = true;
        }

        // Both store panels, off the same two settings rather than a pair each: gear a gearset is
        // using is using it whichever box is being stood at. Shown under both sections and said so,
        // because a switch that moves in two places is only confusing when one of them is silent.
        if (id is "dresser" or "armoire")
        {
            var armoury = configuration.DresserPanelIncludesArmoury;
            if (UiParts.Toggle($"Include armoury chest gear##{id}", ref armoury,
                    "Armoury gear may belong to a gearset.\n" + StorePanelShared))
            {
                configuration.DresserPanelIncludesArmoury = armoury;
                changed = true;
            }

            var equipped = configuration.DresserPanelIncludesEquipped;
            if (UiParts.Toggle($"Include gear you are wearing##{id}", ref equipped,
                    "Has to come off before it can be stored.\n" + StorePanelShared))
            {
                configuration.DresserPanelIncludesEquipped = equipped;
                changed = true;
            }
        }

        ImGui.Spacing();
        return changed;
    }

    /// <summary>
    /// Everything the plugin puts inside the game's own windows, as opposed to beside them.
    /// </summary>
    /// <remarks>
    /// Its own tab because the distinction is worth drawing. Everything on the other tabs is a
    /// window of the plugin's that happens to sit next to a game one; this is the plugin appearing
    /// in the game's UI, which is a thing people reasonably want a say over.
    /// </remarks>
    private bool DrawInGameTab()
    {
        var changed = false;

        TabHeading("Right-click menus");

        var contextMenu = configuration.ShowGameContextMenu;
        if (UiParts.Toggle("Add gear options to the game's right-click menus", ref contextMenu,
                "Adds: try on outfit, where a piece drops."))
        {
            configuration.ShowGameContextMenu = contextMenu;
            changed = true;
        }

        SectionHeading("Item tooltips");

        if (!plugin.TooltipLineAvailable)
        {
            // The one thing here worth the space: a toggle that cannot work should not be offered
            // as though it can.
            ImGui.TextColored(Palette.Warning, "Item tooltip marking is unavailable on this game version.");
            ImGui.Spacing();
            return changed;
        }

        var tooltipLine = configuration.ShowTooltipLine;
        if (UiParts.Toggle("Mark the game's item tooltips", ref tooltipLine,
                "Adds an icon and a word to the tooltip's category row.\n\n" +
                "gold star: put away, in the Dresser, the Armoire or a finished outfit\n" +
                "silver star: in a stored outfit that still has gaps\n" +
                "blue star: stored in some of its outfit sets, not all\n" +
                "orange diamond: on you, in no box at all\n" +
                "no entry: not collected"))
        {
            configuration.ShowTooltipLine = tooltipLine;
            changed = true;
        }

        ImGui.Spacing();
        return changed;
    }

    /// <summary>
    /// The filters and the ownership rules are shared by every list, and the whole point of the tab
    /// split is that it stays visible which settings reach this surface. Saying where they live beats
    /// repeating them per tab and letting the two drift apart.
    /// </summary>
    private static void DrawSharedSettingsNote()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, "What to list and what counts as owned are on the General tab,");
        ImGui.TextColored(Palette.Muted, "and apply here too.");
        ImGui.Spacing();
    }

    private bool DrawDataTab()
    {
        var changed = false;

        TabHeading("Collection snapshot");

        var staleDays = configuration.StaleAfterDays;
        ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Warn when dresser data is older than (days)", ref staleDays, 1, 60))
        {
            configuration.StaleAfterDays = staleDays;
            changed = true;
        }

        ImGui.TextWrapped(
            "The game clears Glamour Dresser data every time you change zone and only loads the Armoire " +
            "when you open it, so Dungeon Drip remembers the last time it saw them.");

        var ownership = plugin.Ownership;
        var slots = ownership.DresserSlotCapacity > 0
            ? $"{ownership.DresserSlotsUsed} of {ownership.DresserSlotCapacity} slots"
            : $"{ownership.DresserSlotsUsed} slots";

        ImGui.TextColored(Palette.Muted, ownership.HasDresserData
            ? $"Dresser: {slots}, read {ownership.DresserUpdatedUtc:yyyy-MM-dd HH:mm} UTC."
            : "Dresser: never read.");

        ImGui.TextColored(Palette.Muted, ownership.ArmoireUpdatedUtc == null
            ? "Armoire: never read."
            : $"Armoire: read {ownership.ArmoireUpdatedUtc:yyyy-MM-dd HH:mm} UTC.");

        DrawRetainers();

        if (ImGui.Button("Refresh collection now"))
            ownership.RequestRefresh();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Re-reads the Glamour Dresser, the Armoire and any retainer, if the game currently has\n" +
                "them loaded.");
        }

        ImGui.SameLine();

        if (ResetButton(
                "ownership",
                "Forget collection",
                "Throws away this character's cached Dresser, Armoire and retainer snapshots, and the\n" +
                "file they live in. Whatever the game has loaded right now is read again immediately, so\n" +
                "you are left with only what can be seen to be true.\n" +
                "Nothing in the game is touched - this is the plugin's copy."))
        {
            ownership.Forget();
        }

        SectionHeading("Dungeon loot data");

        ImGui.TextWrapped(plugin.LootData.StatusMessage);

        var data = plugin.LootData.Data;
        if (data != null)
            ImGui.TextColored(Palette.Muted, $"{data.DutyCount} duties, downloaded {data.FetchedUtc:yyyy-MM-dd HH:mm} UTC.");

        ImGui.TextWrapped(
            "The list is downloaded from FFXIV Teamcraft's public data every time the plugin loads, " +
            "so new dungeons arrive without a plugin update. A copy is kept on disk for offline use.");

        if (ImGui.Button("Check for updates now"))
            plugin.LootData.CheckForUpdates(force: true);

        ImGui.SameLine();

        if (ResetButton(
                "loot",
                "Forget the download",
                "Deletes the cached copy, including the tags that let the check above be answered with\n" +
                "\"nothing has changed\", and fetches the whole thing again. This is the one to reach for\n" +
                "if the data on disk is wrong rather than merely old.\n" +
                "The duty lists keep working from what is already loaded until the new copy arrives."))
        {
            plugin.LootData.Forget();
        }

        SectionHeading("Console Games Wiki");

        var useWiki = configuration.UseWikiSource;
        if (UiParts.Toggle("Fill gaps from the wiki", ref useWiki,
                "Looks the duty you are viewing up on ffxiv.consolegameswiki.com and merges its loot\n" +
                "table in. One page per duty, cached for two weeks, checked one at a time. The primary\n" +
                "dataset lags months on new dungeons where the wiki is usually complete."))
        {
            configuration.UseWikiSource = useWiki;
            changed = true;
        }

        if (useWiki)
        {
            var wiki = plugin.Wiki;
            ImGui.TextColored(Palette.Muted, wiki.TotalItems == 0
                ? "No wiki lookups cached yet."
                : $"{wiki.TotalItems} items cached across {wiki.DutiesWithData} duties.");

            var entry = wiki.EntryFor(plugin.SelectedTerritory);
            if (entry != null)
            {
                var detail = entry.Error != null ? $"failed: {entry.Error}"
                    : entry.NotFound ? "no matching page"
                    : $"{entry.Items.Length} items from \"{entry.Title}\"" +
                      (entry.Attributions.Length > 0
                          ? $" across {entry.Attributions.Length} bosses and coffers"
                          : ", none attributed to a boss") +
                      (entry.UnmatchedNames > 0 ? $", {entry.UnmatchedNames} names unrecognised" : string.Empty);

                ImGui.TextColored(Palette.Muted, $"This duty: {detail}");
            }
            else if (wiki.IsBusy)
            {
                ImGui.TextColored(Palette.Muted, "Looking this duty up...");
            }

            if (ImGui.Button("Re-fetch this duty"))
                plugin.RefetchWikiForSelection();

            ImGui.SameLine();

            if (ResetButton(
                    "wiki",
                    "Forget every lookup",
                    "Throws away every cached wiki lookup, per-boss tables and all. Each duty is looked\n" +
                    "up again the next time you view it, one at a time."))
            {
                wiki.Clear();
            }
        }

        SectionHeading("Learning from what you see drop");

        var learn = configuration.LearnDropsFromLoot;
        if (UiParts.Toggle("Record gear that drops in duties", ref learn,
                "Watches loot messages and adds anything new to this duty's list, including rolls other\n" +
                "party members win. Upstream lags months behind on new dungeons; this fills the gap with\n" +
                "what you actually see. Nothing is uploaded anywhere."))
        {
            configuration.LearnDropsFromLoot = learn;
            changed = true;
        }

        var learned = plugin.LearnedLoot;
        ImGui.TextColored(Palette.Muted, learned.ItemCount == 0
            ? "Nothing learned yet."
            : $"{learned.ItemCount} pieces learned across {learned.TerritoryCount} duties.");

        if (learned.ItemCount > 0)
        {
            ImGui.SameLine();

            if (ResetButton(
                    "learned",
                    "Forget them",
                    "Throws away every drop recorded from watching loot messages. What you see drop from\n" +
                    "here is recorded again; what you saw before this is gone unless upstream has it.",
                    small: true))
            {
                learned.Clear();
            }
        }

        ImGui.TextWrapped(
            "Learned drops are written to learned-loot.json in the same format as loot-overrides.json, " +
            "so you can promote them by hand or contribute them upstream.");

        SectionHeading("Files");

        ImGui.TextWrapped(
            "Missing drops for a very recent dungeon? Add them to loot-overrides.json in the config " +
            "folder and reload the plugin.");

        // Shown as text as well as a button: under Wine the shell handler usually cannot open a
        // folder, and on Linux this path is the easiest way to inspect the caches by hand.
        var configPath = Plugin.PluginInterface.GetPluginConfigDirectory();
        ImGui.TextColored(Palette.Muted, configPath);

        if (ImGui.Button("Open config folder"))
            OpenConfigFolder(configPath);

        ImGui.SameLine();
        if (ImGui.Button("Copy path"))
            ImGui.SetClipboardText(configPath);

        SectionHeading("Start again");

        ImGui.TextWrapped(
            "Everything above in one go: the collection snapshot for this character, the downloaded " +
            "loot data, the wiki lookups and the drops learned in game. Each of them rebuilds itself " +
            "from the game or from upstream, so this costs time rather than anything permanent.");

        // Said plainly, because "reset everything" is exactly the phrase somebody reaches for when they
        // want their settings back to defaults, and this button does not do that.
        ImGui.TextColored(Palette.Muted, "Your settings are not touched.");

        if (ResetButton(
                "everything",
                "Forget everything cached",
                "The four resets above, together. Nothing in the game is touched and no setting changes."))
        {
            ForgetEverything();
        }

        ImGui.Spacing();
        return changed;
    }

    /// <summary>Every cache the plugin keeps, dropped in one press.</summary>
    /// <remarks>
    /// Deliberately the same calls the per-section buttons make rather than a routine of its own, so
    /// this cannot come to mean something different from the sum of them.
    /// </remarks>
    private void ForgetEverything()
    {
        plugin.Ownership.Forget();
        plugin.Wiki.Clear();
        plugin.LearnedLoot.Clear();
        plugin.LootData.Forget();

        Plugin.Log.Information("Forgot every cached file at the user's request");
    }

    /// <summary>
    /// Every retainer read, and when. Longest unread first.
    /// </summary>
    /// <remarks>
    /// Names them rather than counting them, because "3 retainers read" cannot answer the question
    /// somebody looking at this has - which of my ten have been seen, and how long ago. Absent
    /// entirely when retainers are switched off, since the list would be describing data nothing is
    /// using.
    /// </remarks>
    private void DrawRetainers()
    {
        if (!configuration.CountRetainers)
            return;

        var retainers = plugin.Ownership.Retainers;
        if (retainers.Count == 0)
        {
            ImGui.TextColored(Palette.Muted,
                "Retainers: none read - open one at a bell to include what it is holding.");
            return;
        }

        ImGui.TextColored(Palette.Muted, retainers.Count == 1
            ? "Retainers: 1 read."
            : $"Retainers: {retainers.Count} read.");

        foreach (var retainer in retainers)
        {
            // The two counts apart, because they are not the same claim and only one of them is a
            // place that can be gone to and searched.
            var worn = retainer.Equipped.Count > 0
                ? $", {retainer.Equipped.Count} worn"
                : string.Empty;

            ImGui.TextColored(Palette.Muted,
                $"    {retainer.Name}: {retainer.Items.Count} in bags{worn}, " +
                $"read {Core.Format.Age(retainer.UpdatedUtc)} ago.");

            ImGui.SameLine();

            // Per retainer as well as for the lot, because the case that needs it is one of them:
            // dismiss a retainer in game and nothing ever comes back to say so, leaving the name here
            // and its contents counted forever.
            if (ResetButton(
                    $"retainer{retainer.RetainerId}",
                    "Forget",
                    $"Throws away what was read from {retainer.Name}. Visiting them again records it\n" +
                    "afresh; a retainer you have dismissed stays gone.",
                    small: true))
            {
                plugin.Ownership.ForgetRetainer(retainer.RetainerId);

                // The remaining rows describe a list that has just changed underneath them.
                break;
            }
        }

        if (retainers.Count > 1 &&
            ResetButton(
                "retainers",
                "Forget every retainer",
                "Throws away all the retainer snapshots and keeps the Dresser and Armoire ones.",
                small: true))
        {
            plugin.Ownership.ForgetRetainers();
        }
    }

    /// <summary>
    /// A button that asks again before throwing something away.
    /// </summary>
    /// <remarks>
    /// Two presses rather than a modal. Every one of these deletes a cache that rebuilds itself, so the
    /// cost of a misfire is a re-download or a trip to a dresser rather than anything lost - but a
    /// dresser snapshot can be weeks of visits, and a row of one-click "forget" buttons beside the
    /// refresh button is a mistake waiting for a stray click.
    ///
    /// The confirmation replaces the button rather than appearing beside it, so nothing moves under the
    /// cursor between the press that arms it and the press that means it.
    /// </remarks>
    /// <returns>Whether the user has just confirmed this one.</returns>
    private bool ResetButton(string id, string label, string tooltip, bool small = false)
    {
        if (armedReset != id)
        {
            var pressed = small
                ? ImGui.SmallButton($"{label}##{id}")
                : ImGui.Button($"{label}##{id}");

            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(tooltip);

            if (pressed)
                armedReset = id;

            return false;
        }

        ImGui.TextColored(Palette.Warning, "Sure?");
        ImGui.SameLine();

        var confirmed = small
            ? ImGui.SmallButton($"Forget it##confirm{id}")
            : ImGui.Button($"Forget it##confirm{id}");

        ImGui.SameLine();

        var cancelled = small
            ? ImGui.SmallButton($"Keep it##cancel{id}")
            : ImGui.Button($"Keep it##cancel{id}");

        if (confirmed || cancelled)
            armedReset = null;

        return confirmed;
    }

    private static string GroupingLabel(MissingGrouping grouping) => grouping switch
    {
        MissingGrouping.Role => "Role that can roll Need",
        MissingGrouping.Source => "Boss or coffer",
        _ => "Equipment slot",
    };

    private static string ScopeLabel(CollectionScope scope) => scope switch
    {
        CollectionScope.DresserOnly => "Glamour Dresser only",
        CollectionScope.ArmoireOnly => "Armoire only",
        _ => "Glamour Dresser and Armoire",
    };

    private static void OpenConfigFolder(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // Expected under Wine, where there is no shell handler to hand the folder to.
            Plugin.Log.Warning(ex, "Could not open the plugin config folder");
            Plugin.ChatGui.Print($"Dungeon Drip config folder: {path}");
        }
    }
}
