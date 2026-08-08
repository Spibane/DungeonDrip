using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// The main window's other half: the collection as a whole, with no duty involved.
/// </summary>
/// <remarks>
/// Composed into <see cref="MissingItemsWindow"/> rather than being a window of its own. That one is
/// already long and its spine is entirely duty-shaped - pinning, auto-open, a title taken from the
/// report - so this keeps the two apart without adding a second thing to open and find.
///
/// What each tab shows is described by <see cref="CollectionPresenter"/> and its two companions;
/// what is left here is the tab strip and the handful of things that are about this window rather
/// than about the collection - which tab is open, and which lists have been expanded past their cap.
/// </remarks>
public sealed class CollectionView(Plugin plugin)
{
    private readonly CollectionSnapshot snapshot = new(plugin);
    private readonly HeaderState headers = new(plugin.Configuration);

    /// <summary>
    /// Per-session, not a setting. The config file is documentation of what the user chose, and
    /// "I expanded a list once" is not a choice worth writing down.
    /// </summary>
    private bool showAllSets;

    /// <summary>Which currency groups have been expanded past the cap, per session and for the same reason.</summary>
    private readonly HashSet<uint> showAllOffers = [];

    /// <summary>
    /// Whether the stored tab has been forced open yet.
    /// </summary>
    /// <remarks>
    /// Per instance rather than saved, because it is about this window's lifetime and not the user's
    /// choice. False again after a plugin reload, which is exactly when the stored tab needs
    /// restoring; within a session ImGui keeps the selection itself.
    /// </remarks>
    private bool tabRestored;

    /// <summary>
    /// Rebuilds anything stale, then draws the three tabs.
    /// </summary>
    /// <remarks>
    /// Tabs rather than the stack of collapsing headers this used to be. Four headers one under
    /// another, each with its own collapsibles inside it, made every list look like a row in the list
    /// above it - there was no reading of the window that told a section apart from a group within
    /// one. A tab strip says outright that these are three separate questions.
    /// </remarks>
    public void Draw()
    {
        snapshot.Refresh();

        using var tabs = ImRaii.TabBar("##collectionTabs");
        if (!tabs.Success)
            return;

        // Restoring is a one-shot, and has to be: the flag that selects a tab wins over a click, so
        // asking for it every frame would pin the stored tab open and make the strip unusable.
        var restoring = !tabRestored;

        // Read before any tab is drawn, and not from the configuration inside the loop. On the
        // restoring frame ImGui reports the first tab active until the stored one is submitted with
        // its flag, so a tab that recorded itself as it went would overwrite the target before
        // reaching it.
        var target = plugin.Configuration.CollectionTab;

        Tab("Sets in progress", CollectionTab.Sets, restoring, target,
            () => CollectionPresenter.Sets(plugin, snapshot, showAllSets, () => showAllSets = !showAllSets));

        Tab("Ready to buy", CollectionTab.Buy, restoring, target,
            () => ShoppingPresenter.Tab(plugin, snapshot, showAllOffers, ToggleOffers));

        Tab("Glamour Dresser", CollectionTab.Dresser, restoring, target,
            () => DresserTabPresenter.Tab(plugin, snapshot));

        tabRestored = true;
    }

    private void ToggleOffers(uint currencyItemId)
    {
        if (!showAllOffers.Remove(currencyItemId))
            showAllOffers.Add(currencyItemId);
    }

    /// <summary>
    /// One tab, its body in a region that scrolls by itself.
    /// </summary>
    /// <remarks>
    /// The child is what keeps the tab strip in place. Letting the window scroll instead would carry
    /// the strip off the top edge, so reaching another tab would mean scrolling up first - the same
    /// reason the settings window wraps its tabs this way.
    ///
    /// The selection is written to the configuration on the frame it changes rather than polled, and
    /// only when it actually changed, so an unchanged tab costs no save.
    ///
    /// The body is a function rather than a list because describing a tab walks the whole collection,
    /// and only one tab is on screen.
    /// </remarks>
    /// <param name="restoring">
    /// True only on the first frame the strip is drawn, when the stored tab is forced open.
    /// <see cref="ImGuiTabItemFlags.SetSelected"/> overrides a click, so passing it on later frames
    /// would undo every attempt to switch away.
    /// </param>
    /// <param name="target">
    /// The stored tab as it was before any tab was drawn. Nothing is recorded while
    /// <paramref name="restoring"/> is true, since the selection ImGui reports that frame is not yet
    /// the one being restored to.
    /// </param>
    private void Tab(
        string label, CollectionTab tab, bool restoring, CollectionTab target,
        System.Func<IReadOnlyList<Node>> body)
    {
        var flags = restoring && target == tab
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;

        using var item = ImRaii.TabItem(label, flags);
        if (!item.Success)
            return;

        if (!restoring && plugin.Configuration.CollectionTab != tab)
        {
            plugin.Configuration.CollectionTab = tab;
            plugin.Configuration.Save();
        }

        using var child = ImRaii.Child($"collection{tab}", Vector2.Zero, false);
        if (!child.Success)
            return;

        ImGuiRenderer.Draw(plugin, body(), headers);
    }
}
