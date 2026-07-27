using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace GlamourAssistant.Windows;

public class ConfigWindow : Window, IDisposable
{
    private static readonly Vector4 Muted = new(0.60f, 0.60f, 0.60f, 1f);

    private readonly Plugin plugin;
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin)
        : base("Glamour Assistant Settings###GlamourAssistantConfig", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var changed = false;

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

        var showOwned = configuration.ShowOwnedItems;
        if (ImGui.Checkbox("List pieces I already have, greyed out", ref showOwned))
        {
            configuration.ShowOwnedItems = showOwned;
            changed = true;
        }

        var jobOnly = configuration.OnlyCurrentJobEquippable;
        if (ImGui.Checkbox("Only show gear my current job can wear", ref jobOnly))
        {
            configuration.OnlyCurrentJobEquippable = jobOnly;
            changed = true;
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
            ImGui.SetNextItemWidth(160 * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
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
        ImGui.TextColored(Muted, "What counts as owned");

        var countInventory = configuration.CountInventoryAndEquipped;
        if (ImGui.Checkbox("Bags, armoury chest, equipped gear and saddlebags", ref countInventory))
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
        ImGui.TextColored(Muted, "Collection snapshot");

        var staleDays = configuration.StaleAfterDays;
        ImGui.SetNextItemWidth(160 * Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale);
        if (ImGui.SliderInt("Warn when dresser data is older than (days)", ref staleDays, 1, 60))
        {
            configuration.StaleAfterDays = staleDays;
            changed = true;
        }

        ImGui.TextWrapped(
            "The game clears Glamour Dresser data every time you change zone and only loads the Armoire " +
            "when you open it, so Glamour Assistant remembers the last time it saw them.");

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

        ImGui.Spacing();
        ImGui.TextWrapped(
            "Learned drops are written to learned-loot.json in the same format as loot-overrides.json, " +
            "so you can promote them by hand or contribute them upstream.");

        // Shown as text as well as a button: under Wine the shell handler usually cannot open a
        // folder, and on Linux this path is the easiest way to inspect the caches by hand.
        var configPath = Plugin.PluginInterface.GetPluginConfigDirectory();
        ImGui.TextColored(Muted, configPath);

        if (ImGui.Button("Open config folder"))
            OpenConfigFolder(configPath);

        ImGui.SameLine();
        if (ImGui.Button("Copy path"))
            ImGui.SetClipboardText(configPath);

        if (changed)
        {
            configuration.Save();
            plugin.InvalidateReport();
        }
    }

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
            Plugin.ChatGui.Print($"Glamour Assistant config folder: {path}");
        }
    }
}
