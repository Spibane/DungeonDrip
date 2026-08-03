using System;
using System.Collections.Generic;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using DungeonDrip.Core;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DungeonDrip.Game;

/// <summary>
/// Tracks the market board's browse list and answers the ownership question for each piece of it.
/// </summary>
/// <remarks>
/// Much smaller than the shop watcher, because there is one addon rather than a registry of them:
/// a lifecycle registration plus a null-and-visible check each frame, no sweep.
///
/// The board fills its results in pages of twenty as they arrive from the server, so the list
/// rebuilds a few times while a category loads. That is the same situation as switching category at
/// a vendor and needs no special handling beyond noticing the ids changed.
///
/// Nothing runs on the framework tick; the panel is the only consumer.
/// </remarks>
public sealed unsafe class MarketBoardWatcher : IDisposable
{
    public const string AddonName = "ItemSearch";

    private readonly Plugin plugin;

    /// <summary>Reused every frame so the read path allocates nothing.</summary>
    private readonly List<uint> scratch = new(128);

    private uint[] currentIds = [];
    private List<GearRow>? listed;
    private int seenRowRevision = -1;
    private bool open;
    private bool warned;

    public MarketBoardWatcher(Plugin plugin)
    {
        this.plugin = plugin;

        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, AddonName, OnOpened);
        Plugin.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, AddonName, OnClosed);
    }

    public void Dispose()
    {
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PostSetup, AddonName, OnOpened);
        Plugin.AddonLifecycle.UnregisterListener(AddonEvent.PreFinalize, AddonName, OnClosed);
    }

    /// <summary>
    /// The gear the board is currently browsing, or null when it is shut or unreadable.
    /// Call once per frame.
    /// </summary>
    public IReadOnlyList<GearRow>? Resolve()
    {
        if (!plugin.Configuration.MarketBoardPanel.Enabled || Plugin.GameGui.GameUiHidden)
            return null;

        if (!open)
            return Forget();

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(AddonName);
        if (unit == null || !unit->IsVisible)
            return Forget();

        if (!MarketBoardReader.TryRead(unit, scratch))
        {
            WarnOnce();
            return Forget();
        }

        plugin.Rows.Revalidate();
        if (plugin.Rows.Revision != seenRowRevision)
        {
            seenRowRevision = plugin.Rows.Revision;
            listed = null;
        }

        if (listed != null && Unchanged())
            return listed;

        currentIds = [.. scratch];
        listed = Build(currentIds);
        return listed;
    }

    private List<GearRow>? Forget()
    {
        listed = null;
        return null;
    }

    /// <summary>
    /// A full comparison rather than a hash: at these sizes it costs the same and cannot collide,
    /// and two categories of equal length is exactly the case this has to catch.
    /// </summary>
    private bool Unchanged()
    {
        if (scratch.Count != currentIds.Length)
            return false;

        for (var i = 0; i < currentIds.Length; i++)
        {
            if (currentIds[i] != scratch[i])
                return false;
        }

        return true;
    }

    private List<GearRow> Build(uint[] ids)
    {
        var rows = new List<GearRow>(ids.Length);
        var seen = new HashSet<uint>(ids.Length);

        foreach (var id in ids)
        {
            // HQ and NQ collapse onto one base id, and a piece can be listed under more than one
            // heading.
            if (!seen.Add(id))
                continue;

            var row = plugin.Rows.Row(id);
            if (row != null)
                rows.Add(row);
        }

        Plugin.Log.Debug($"Market board: {ids.Length} listed, {rows.Count} glamour-eligible.");
        return rows;
    }

    private void WarnOnce()
    {
        if (warned)
            return;

        warned = true;
        Plugin.Log.Warning(
            "Could not read the market board's browse list. No panel will be shown for it.");
    }

    private void OnOpened(AddonEvent type, AddonArgs args)
    {
        open = true;

        // Cannot conjure a dresser up at a board, but does pick up anything read since last time.
        plugin.Ownership.RequestRefresh();
    }

    private void OnClosed(AddonEvent type, AddonArgs args) => open = false;
}
