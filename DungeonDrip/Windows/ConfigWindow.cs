using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace DungeonDrip.Windows;

public class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Muted = new(0.60f, 0.60f, 0.60f, 1f);

    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Dungeon Drip Settings###DungeonDripConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

        using var tabs = Dalamud.Interface.Utility.Raii.ImRaii.TabBar("##settingsTabs");
        if (!tabs.Success)
            return;

        using (var general = Dalamud.Interface.Utility.Raii.ImRaii.TabItem("General"))
        {
            if (general.Success)
                changed |= DrawGeneralTab();
        }

        using (var data = Dalamud.Interface.Utility.Raii.ImRaii.TabItem("Data"))
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
        ImGui.TextColored(Muted, "Window");

        var autoOpen = configuration.AutoOpenOnDutyEnter;
        if (ImGui.Checkbox("Open automatically when I enter a duty", ref autoOpen))
        {
            configuration.AutoOpenOnDutyEnter = autoOpen;
            changed = true;
        }

        var hideWhenComplete = configuration.HideWhenNothingMissing;
        if (ImGui.Checkbox("...but not when I already have everything", ref hideWhenComplete))
        {
            configuration.HideWhenNothingMissing = hideWhenComplete;
            changed = true;
        }

        var closeOnLeave = configuration.CloseWhenLeavingDuty;
        if (ImGui.Checkbox("Close again when I leave the duty", ref closeOnLeave))
        {
            configuration.CloseWhenLeavingDuty = closeOnLeave;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("A duty you pinned yourself stays open - only the automatic tracking closes.");

        var showOwned = configuration.ShowOwnedItems;
        if (ImGui.Checkbox("List pieces I already have, greyed out", ref showOwned))
        {
            configuration.ShowOwnedItems = showOwned;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.TextColored(Muted, "What to list");

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (ImGui.Checkbox("Only show gear my current job can wear", ref jobOnly))
        {
            configuration.OnlyCurrentJobEquippable = jobOnly;
            changed = true;
        }

        var hideWeapons = configuration.HideWeapons;
        if (ImGui.Checkbox("Skip weapons", ref hideWeapons))
        {
            configuration.HideWeapons = hideWeapons;
            changed = true;
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Hides main hands and off-hands, which drop together.");

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
        ImGui.TextColored(Muted, "What counts as owned");

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
        if (ImGui.Checkbox("Also count bags, armoury, equipped gear and saddlebags", ref countInventory))
        {
            configuration.CountInventoryAndEquipped = countInventory;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Off by default: a drop sitting in your bag is not in your collection yet.\n" +
                "Retainer inventories cannot be read unless you are at a retainer, so they are never counted.");
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
        ImGui.TextColored(Muted, "Loot roll window");

        var companion = configuration.ShowLootCompanion;
        if (ImGui.Checkbox("Show a companion list beside the Need/Greed window", ref companion))
        {
            configuration.ShowLootCompanion = companion;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "A separate window pinned to the side of the roll window, marking what you do not own.\n" +
                "It never draws into the game's own loot window, so it cannot fight with plugins that do\n" +
                "(Allagan Tools, Simple Tweaks, VanillaPlus, Collections).");
        }

        if (companion)
        {
            var side = configuration.LootCompanionSide;
            ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
            if (ImGui.BeginCombo("Side", side.ToString()))
            {
                foreach (var option in Enum.GetValues<LootCompanionSide>())
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

        ImGui.Spacing();
        ImGui.Separator();

        // Worth showing: a short alias is skipped when another plugin already owns it, and silently
        // missing commands would otherwise look like a bug here.
        ImGui.TextColored(Muted, $"Commands: {string.Join("  ", plugin.Commands.Registered)}");

        ImGui.Spacing();
        return changed;
    }

    private bool DrawDataTab()
    {
        var changed = false;

        ImGui.Spacing();
        ImGui.TextColored(Muted, "Collection snapshot");

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
        ImGui.TextColored(Muted, ownership.HasDresserData
            ? $"Dresser: {ownership.DresserSlotsUsed} slots, read {ownership.DresserUpdatedUtc:yyyy-MM-dd HH:mm} UTC."
            : "Dresser: never read.");

        ImGui.TextColored(Muted, ownership.ArmoireUpdatedUtc == null
            ? "Armoire: never read."
            : $"Armoire: read {ownership.ArmoireUpdatedUtc:yyyy-MM-dd HH:mm} UTC.");

        if (ImGui.Button("Refresh collection now"))
            ownership.RequestRefresh();

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Re-reads the Glamour Dresser and Armoire, if the game currently has them loaded.");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Muted, "Dungeon loot data");

        ImGui.TextWrapped(plugin.LootData.StatusMessage);

        var data = plugin.LootData.Data;
        if (data != null)
            ImGui.TextColored(Muted, $"{data.DutyCount} duties, downloaded {data.FetchedUtc:yyyy-MM-dd HH:mm} UTC.");

        ImGui.TextWrapped(
            "The list is downloaded from FFXIV Teamcraft's public data every time the plugin loads, " +
            "so new dungeons arrive without a plugin update. A copy is kept on disk for offline use.");

        if (ImGui.Button("Check for updates now"))
            plugin.LootData.CheckForUpdates(force: true);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Muted, "Console Games Wiki");

        var useWiki = configuration.UseWikiSource;
        if (ImGui.Checkbox("Fill gaps from the wiki", ref useWiki))
        {
            configuration.UseWikiSource = useWiki;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Looks the duty you are viewing up on ffxiv.consolegameswiki.com and merges its loot\n" +
                "table in. One page per duty, cached for two weeks, checked one at a time. The primary\n" +
                "dataset lags months on new dungeons where the wiki is usually complete.");
        }

        if (useWiki)
        {
            var wiki = plugin.Wiki;
            ImGui.TextColored(Muted, wiki.TotalItems == 0
                ? "No wiki lookups cached yet."
                : $"{wiki.TotalItems} items cached across {wiki.DutiesWithData} duties.");

            var entry = wiki.EntryFor(plugin.SelectedTerritory);
            if (entry != null)
            {
                var detail = entry.Error != null ? $"failed: {entry.Error}"
                    : entry.NotFound ? "no matching page"
                    : $"{entry.Items.Length} items from \"{entry.Title}\"" +
                      (entry.UnmatchedNames > 0 ? $", {entry.UnmatchedNames} names unrecognised" : string.Empty);

                ImGui.TextColored(Muted, $"This duty: {detail}");
            }
            else if (wiki.IsBusy)
            {
                ImGui.TextColored(Muted, "Looking this duty up...");
            }

            if (ImGui.Button("Re-fetch this duty"))
                plugin.RefetchWikiForSelection();

            ImGui.SameLine();
            if (ImGui.Button("Clear wiki cache"))
                wiki.Clear();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(Muted, "Learning from what you see drop");

        var learn = configuration.LearnDropsFromLoot;
        if (ImGui.Checkbox("Record gear that drops in duties", ref learn))
        {
            configuration.LearnDropsFromLoot = learn;
            changed = true;
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(
                "Watches loot messages and adds anything new to this duty's list, including rolls other\n" +
                "party members win. Upstream lags months behind on new dungeons; this fills the gap with\n" +
                "what you actually see. Nothing is uploaded anywhere.");
        }

        var learned = plugin.LearnedLoot;
        ImGui.TextColored(Muted, learned.ItemCount == 0
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
        ImGui.TextColored(Muted, "Files");

        ImGui.TextWrapped(
            "Missing drops for a very recent dungeon? Add them to loot-overrides.json in the config " +
            "folder and reload the plugin.");

        // Shown as text as well as a button: under Wine the shell handler usually cannot open a
        // folder, and on Linux this path is the easiest way to inspect the caches by hand.
        var configPath = Plugin.PluginInterface.GetPluginConfigDirectory();
        ImGui.TextColored(Muted, configPath);

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
