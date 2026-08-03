using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DungeonDrip.Windows;

public class ConfigWindow : Window
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Dungeon Drip Settings###DungeonDripConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }


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

    private bool DrawGeneralTab()
    {
        var changed = false;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "What to list");
        ImGui.TextColored(Palette.Muted, "Applies everywhere: the duty window, the loot roll list and vendors.");
        ImGui.Spacing();

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

        var hideWeapons = configuration.HideWeapons;
        if (UiParts.Toggle("Skip weapons", ref hideWeapons,
                "Hides main hands and off-hands, which drop together."))
        {
            configuration.HideWeapons = hideWeapons;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Trying on");
        ImGui.TextColored(Palette.Muted, "Right-click any piece in any of the three lists.");
        ImGui.Spacing();

        var clearFittingRoom = configuration.ClearFittingRoomForOutfits;
        if (UiParts.Toggle("Empty the fitting room before trying on an outfit", ref clearFittingRoom,
                "Closes the fitting room first, so the set is shown on its own rather than over\n" +
                "whatever was already in there. Only the preview is discarded; outfits you have saved\n" +
                "out of the room are in your Glamour Dresser and are not touched.\n" +
                "Trying on a single piece never clears anything."))
        {
            configuration.ClearFittingRoomForOutfits = clearFittingRoom;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "What counts as owned");
        ImGui.TextColored(Palette.Muted, "Also applies everywhere.");
        ImGui.Spacing();

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
                "The list only ever shows pieces the chosen store can actually hold. Most Armoire\n" +
                "items can live in either place, so Armoire-only is a narrower view of the same gear -\n" +
                "recent dungeon sets qualify, older ones are Dresser-only.");
        }

        var countInventory = configuration.CountInventoryAndEquipped;
        if (UiParts.Toggle("Also count bags, armoury, equipped gear and saddlebags", ref countInventory,
                "Off by default: a drop sitting in your bag is not in your collection yet.\n" +
                "Retainer inventories cannot be read unless you are at a retainer, so they are never counted."))
        {
            configuration.CountInventoryAndEquipped = countInventory;
            changed = true;
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
                "A piece can appear in more than one outfit set. The strict option only calls it\n" +
                "collected once you have stored every set that includes it.");
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Worth showing: a short alias is skipped when another plugin already owns it, and silently
        // missing commands would otherwise look like a bug here.
        ImGui.TextColored(Palette.Muted, $"Commands: {string.Join("  ", plugin.Commands.Registered)}");

        ImGui.Spacing();
        return changed;
    }

    private bool DrawDutiesTab()
    {
        var changed = false;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Duty window");

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
                "A duty you pinned yourself stays open - only the automatic tracking closes."))
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
        if (ImGui.BeginCombo("Group the list by", grouping == MissingGrouping.Role ? "Role" : "Equipment slot"))
        {
            if (ImGui.Selectable("Equipment slot", grouping == MissingGrouping.Slot))
            {
                configuration.Grouping = MissingGrouping.Slot;
                changed = true;
            }

            if (ImGui.Selectable("Role that can roll Need", grouping == MissingGrouping.Role))
            {
                configuration.Grouping = MissingGrouping.Role;
                changed = true;
            }

            ImGui.EndCombo();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Role grouping buckets pieces by who is allowed to roll Need, which is what you want\n" +
                "when claiming during a run. Headings can be collapsed and stay that way.");
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Loot roll window");

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

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "What the markers mean");
        ImGui.BulletText("x - not collected");
        ImGui.BulletText("tick - in the Glamour Dresser");
        ImGui.BulletText("layers - in a stored outfit set that still has gaps");
        ImGui.BulletText("star - in an outfit set you have completed");
        ImGui.BulletText("box - in the Armoire");
        ImGui.BulletText("case - carried or equipped");
        ImGui.BulletText("? - no dresser data, so nothing can be said either way");
        ImGui.TextColored(Palette.Muted, "An old dresser snapshot turns the x amber, not the ticks:");
        ImGui.TextColored(Palette.Muted, "what it says you own stays true far longer than what it says you lack.");

        ImGui.Spacing();

        changed |= DrawPanelSection(
            "Vendor windows", "vendor", configuration.VendorPanel,
            "Lists the glamour gear the vendor is currently showing, marked by where you already\n" +
            "have each piece. Items that cannot be kept as a glamour are left out entirely.\n" +
            "Run \"/dungeondrip shop\" at a vendor it does not recognise to identify it.",
            null);

        changed |= DrawPanelSection(
            "Market board", "market", configuration.MarketBoardPanel,
            "Lists the gear the board's browse list is currently showing, marked by where you\n" +
            "already have each piece. The listings for one item get no panel - by then you have\n" +
            "chosen it, and every row would be the same piece.",
            null);

        changed |= DrawPanelSection(
            "Glamour Dresser", "dresser", configuration.DresserPanel,
            "Lists what you are carrying that is not in the dresser or Armoire yet.",
            "This is the one panel whose list means the opposite of the others: it is what you have\n" +
            "on you and have not stored, rather than what you are being shown and do not own.");

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

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Right-click menus");
        ImGui.Spacing();

        var contextMenu = configuration.ShowGameContextMenu;
        if (UiParts.Toggle("Add gear options to the game's right-click menus", ref contextMenu,
                "Adds \"Try on outfit\" and \"Where does this drop?\" when you right-click a piece of\n" +
                "gear in your inventory, the inspect window, a chat link, the Glamour Dresser and so on.\n" +
                "Only options the game does not already offer are added - it has its own Try On, Link\n" +
                "and Copy, and a second of each would just sit beside the real one."))
        {
            configuration.ShowGameContextMenu = contextMenu;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted,
            "Entries are added through the interface Dalamud provides for it and are marked with a D,\n" +
            "so they sit below the game's own options and alongside any other plugin's rather than\n" +
            "replacing anything.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Item tooltips");
        ImGui.Spacing();

        if (!plugin.TooltipLineAvailable)
        {
            // Said plainly rather than left as a toggle that quietly does nothing.
            ImGui.TextColored(Palette.Warning,
                "Unavailable on this game version - the tooltip line could not attach.");
            ImGui.TextColored(Palette.Muted,
                "A patch has moved what it hooks. Everything else is unaffected.");
            ImGui.Spacing();
            return changed;
        }

        var tooltipLine = configuration.ShowTooltipLine;
        if (UiParts.Toggle("Add a line to the game's item tooltips", ref tooltipLine,
                "Says where the piece is in your collection, on the tooltip itself."))
        {
            configuration.ShowTooltipLine = tooltipLine;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted,
            "Off by default, and the only thing here that changes what a game window contains rather\n" +
            "than adding beside it. It only ever adds to what is already there, and marks its own\n" +
            "line so it cannot double up or overwrite another plugin's.");
        ImGui.TextColored(Palette.Muted,
            "Simple Tweaks' Track Outfits does a similar job; with both on you get two lines.");

        ImGui.Spacing();
        return changed;
    }

    /// <summary>
    /// The filters and the ownership rules are shared by every list, and the whole point of the tab
    /// split is that you can see which settings reach this surface. Saying where they live beats
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

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, "Collection snapshot");

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

        if (ImGui.Button("Refresh collection now"))
            ownership.RequestRefresh();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Re-reads the Glamour Dresser and Armoire, if the game currently has them loaded.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, "Dungeon loot data");

        ImGui.TextWrapped(plugin.LootData.StatusMessage);

        var data = plugin.LootData.Data;
        if (data != null)
            ImGui.TextColored(Palette.Muted, $"{data.DutyCount} duties, downloaded {data.FetchedUtc:yyyy-MM-dd HH:mm} UTC.");

        ImGui.TextWrapped(
            "The list is downloaded from FFXIV Teamcraft's public data every time the plugin loads, " +
            "so new dungeons arrive without a plugin update. A copy is kept on disk for offline use.");

        if (ImGui.Button("Check for updates now"))
            plugin.LootData.CheckForUpdates(force: true);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, "Console Games Wiki");

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
            if (ImGui.Button("Clear wiki cache"))
                wiki.Clear();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, "Learning from what you see drop");

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
            if (ImGui.Button("Forget all"))
                learned.Clear();
        }

        ImGui.TextWrapped(
            "Learned drops are written to learned-loot.json in the same format as loot-overrides.json, " +
            "so you can promote them by hand or contribute them upstream.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Palette.Muted, "Files");

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

        ImGui.Spacing();
        return changed;
    }

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
