using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// The plugin's main window: what is missing from the duty in view, or the collection as a whole.
/// </summary>
/// <remarks>
/// Two views behind one window rather than two windows, because they are read one after the other -
/// a duty answers "what should I run for", the collection answers "what have I got" - and a second
/// thing to open and position is a cost paid every session for a switch that is one button.
///
/// The spine here is duty-shaped all the way through: the title comes from the report, the window
/// pins a territory, and it opens itself on zoning into one. The collection view is composed in
/// rather than sharing any of that, which is what keeps it from having to pretend to be about a duty.
///
/// Nothing is computed here. The report is built on the framework tick and the collection view keeps
/// its own cache, so a draw is a draw.
/// </remarks>
public class MissingItemsWindow : Window
{
    private readonly Plugin plugin;
    private readonly HeaderState headers;
    private readonly CollectionView collection;

    private string dutyFilter = string.Empty;
    private bool pickerOpen;

    public MissingItemsWindow(Plugin plugin)
        : base("Dungeon Drip###DungeonDripMain")
    {
        this.plugin = plugin;
        headers = new HeaderState(plugin.Configuration);
        collection = new CollectionView(plugin);
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(420, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }


    /// <summary>Opens the window with the picker already filtered, for <c>/dungeondrip &lt;name&gt;</c>.</summary>
    public void OpenPicker(string filter)
    {
        dutyFilter = filter;
        pickerOpen = true;
        IsOpen = true;
    }

    /// <summary>
    /// Retitles the window to whatever it is about, keeping the id suffix so ImGui still recognises
    /// it as the same window and remembers where it was put.
    /// </summary>
    public override void PreDraw()
    {
        var report = plugin.Report;
        var title = plugin.Mode == MainWindowMode.Collection
            ? "Collection"
            : report == null ? "Dungeon Drip" : report.Name;

        WindowName = $"{title}###DungeonDripMain";
    }

    /// <summary>
    /// Picks which of the window's states to draw: the collection, a plea for loot data, the
    /// roulette advice, or a duty's list.
    /// </summary>
    /// <remarks>
    /// Four outcomes rather than two, because "no report" splits: outside a duty there is nothing to
    /// report on and the roulette advice is the useful answer, whereas with no dataset at all
    /// nothing can be said and the window has to explain why rather than look broken.
    /// </remarks>
    public override void Draw()
    {
        ImGuiRenderer.Toolbar("main", DutyPresenter.Toolbar(plugin, pickerOpen, TogglePicker));

        // Above the loot-data check on purpose: none of the collection view needs a loot table, and
        // someone on a first run with nothing downloaded yet should still be able to see how full
        // their dresser is.
        if (plugin.Mode == MainWindowMode.Collection)
        {
            ImGuiRenderer.Draw(plugin, DutyPresenter.FreshnessBanner(plugin.Ownership));
            collection.Draw();
            return;
        }

        if (plugin.Duties == null)
        {
            ImGuiRenderer.Draw(plugin, DutyPresenter.NoLootData(plugin));
            return;
        }

        ImGuiRenderer.Draw(plugin, DutyPresenter.FreshnessBanner(plugin.Ownership));

        if (pickerOpen)
        {
            DrawDutyPicker();
            ImGui.Separator();
        }

        var report = plugin.Report;
        if (report == null)
        {
            ImGuiRenderer.Draw(plugin, DutyPresenterRoulettes.Advice(plugin));
            return;
        }

        ImGuiRenderer.Draw(plugin, DutyPresenter.Summary(report));
        ImGuiRenderer.Draw(plugin, DutyPresenter.RoleFocus(plugin, report), headers);
        ImGui.Separator();

        using var child = ImRaii.Child("itemList", Vector2.Zero, false);
        if (child.Success)
            ImGuiRenderer.Draw(plugin, DutyPresenterLists.Items(plugin, report), headers);
    }

    private void TogglePicker() => pickerOpen = !pickerOpen;

    /// <summary>
    /// The duty picker: a search field and the duties matching it.
    /// </summary>
    /// <remarks>
    /// Stays a hand-drawn widget rather than a described one, like the toolbar and for the same
    /// reason: it has state of its own - a field being typed into, a list being scrolled - which is
    /// not something a list of nodes can carry. What each row says comes from
    /// <see cref="DutyPickerPresenter"/> so the two looks cannot word a duty differently.
    /// </remarks>
    private void DrawDutyPicker()
    {
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##dutyFilter", "Search duties by name...", ref dutyFilter, 128);

        using var child = ImRaii.Child("dutyList", new Vector2(-1, 180 * ImGuiHelpers.GlobalScale), true);
        if (!child.Success)
            return;

        foreach (var entry in DutyPickerPresenter.Entries(plugin, dutyFilter))
        {
            if (!ImGui.Selectable($"{entry.Label}##{entry.TerritoryId}", entry.Selected))
                continue;

            plugin.PinTerritory(entry.TerritoryId);
            pickerOpen = false;
        }
    }
}
