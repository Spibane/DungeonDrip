using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;

namespace DungeonDrip.Windows;

/// <summary>
/// The main window's other half: your collection as a whole, with no duty involved.
/// </summary>
/// <remarks>
/// Composed into <see cref="MissingItemsWindow"/> rather than being a window of its own. That one is
/// already long and its spine is entirely duty-shaped - pinning, auto-open, a title taken from the
/// report - so this keeps the two apart without adding a second thing to open and find.
///
/// Every section here sweeps the whole collection, which is far too much to do per frame, so all of
/// it is computed behind <see cref="Stale"/> and cached until something it depends on moves.
/// </remarks>
public sealed class CollectionView(Plugin plugin)
{
    /// <summary>
    /// Prefix on the remembered heading state, so a section can never collide with a slot or role
    /// heading from the duty list, which share the same store.
    /// </summary>
    private const string CollapsePrefix = "collection:";

    /// <summary>Above this share of the box, the count is worth colouring.</summary>
    private const float CrowdedAt = 0.9f;

    private int seenOwnershipRevision = -1;
    private Inputs seenInputs;

    private DresserPressureReport? pressure;

    public void Draw()
    {
        Recompute();

        using var child = ImRaii.Child("collectionList", Vector2.Zero, false);
        if (!child.Success)
            return;

        Section("Sets in progress", DrawSetsInProgress);
        Section("Glamour Dresser", DrawDresserPressure);
        Section("Already in your collection", DrawAlreadyStored);
    }

    /// <summary>
    /// Rebuilds the sections when, and only when, something they are derived from has changed.
    /// </summary>
    /// <remarks>
    /// Staleness of the snapshot is deliberately not an input, matching the ownership tracker's own
    /// rule: an ageing snapshot changes how the answer should be worded, not what it is.
    /// </remarks>
    private void Recompute()
    {
        var inputs = Capture();
        if (plugin.Ownership.Revision == seenOwnershipRevision && inputs == seenInputs)
            return;

        seenOwnershipRevision = plugin.Ownership.Revision;
        seenInputs = inputs;

        var ownership = plugin.Ownership.Current;
        pressure = DresserPressure.Build(
            ownership, plugin.Outfits, plugin.Storage, plugin.Ownership.ArmoireUpdatedUtc != null);
    }

    private Inputs Capture()
    {
        var configuration = plugin.Configuration;
        return new Inputs(
            configuration.Scope,
            configuration.OutfitOwnership,
            configuration.CountInventoryAndEquipped,
            configuration.HideWeapons,
            configuration.OnlyCurrentJobEquippable);
    }

    private void DrawSetsInProgress() =>
        ImGui.TextColored(Palette.Muted, "Nothing yet.");

    private void DrawDresserPressure()
    {
        if (pressure is not { HasData: true })
        {
            ImGui.TextColored(Palette.Muted, "No dresser snapshot to measure.");
            return;
        }

        DrawOccupancy(pressure);

        if (pressure.Reclaimable > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Focus, $"Up to {pressure.Reclaimable} slots could be freed:");
        }

        DrawCollapsible(pressure);
        DrawArmoireLevers(pressure);
    }

    /// <summary>
    /// How full the box is, with a denominator only when there is an honest one to give.
    /// </summary>
    /// <remarks>
    /// The capacity read off the client is the box's structural size, which is not the same as how
    /// many slots a given character has actually unlocked, and the client offers no way to tell. So
    /// a bare count is the claim that can always be stood behind; the "of N" is worded as the most
    /// the box can hold rather than as the room you have.
    ///
    /// A stale snapshot only ever undercounts, because you add to a dresser far more often than you
    /// take from it, so the number is prefixed rather than suppressed.
    /// </remarks>
    private void DrawOccupancy(DresserPressureReport report)
    {
        var stale = plugin.Ownership.IsDresserStale;
        var prefix = stale ? "At least " : string.Empty;
        var crowded = report.Capacity > 0 && report.Used >= report.Capacity * CrowdedAt;

        if (report.Capacity > 0)
        {
            ImGui.TextColored(
                crowded ? Palette.Warning : Palette.Muted,
                $"{prefix}{report.Used} slots used, of the {report.Capacity} the box holds.");

            ImGui.ProgressBar(
                (float)report.Used / report.Capacity,
                new Vector2(-1, 6 * ImGuiHelpers.GlobalScale),
                string.Empty);
        }
        else
        {
            ImGui.TextColored(Palette.Muted, $"{prefix}{report.Used} slots used.");
        }
    }

    private void DrawCollapsible(DresserPressureReport report)
    {
        if (report.Collapsible.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted, report.Collapsible.Count == 1
            ? "1 outfit is stored a piece at a time:"
            : $"{report.Collapsible.Count} outfits are stored a piece at a time:");

        foreach (var set in report.Collapsible)
        {
            // Conditional on purpose. Storing a set needs the outfit item - a tradeable attire box -
            // and owning all eleven pieces does not give you one; plenty of sets have no such item
            // at all. Telling someone to do something they may have no way to do is worse than
            // telling them what it would be worth if they could.
            var line = set.HoldingOutfitItem
                ? $"   {set.Name} - you have the outfit item; storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots"
                : $"   {set.Name} - if you have the outfit item, storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots";

            ImGui.TextColored(set.HoldingOutfitItem ? Palette.Good : Palette.Muted, line);
        }

        ImGui.TextColored(Palette.Muted,
            "   The loose copies come back to your bags, so the space appears once you clear them.");
    }

    private void DrawArmoireLevers(DresserPressureReport report)
    {
        if (!report.ArmoireKnown)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Muted,
                "Open your Armoire once and this can also say what it would take off your hands.");
            return;
        }

        if (report.DuplicateInArmoire.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.Focus,
                $"{report.DuplicateInArmoire.Count} pieces are in your Armoire as well as the dresser.");
            ImGui.TextColored(Palette.Muted,
                "   The dresser copy is doing nothing the Armoire is not already doing.");
        }

        if (report.ArmoireWouldTake.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Palette.Muted,
            $"{report.ArmoireWouldTake.Count} more would be accepted by the Armoire, which costs no " +
            "dresser slot - though each has to be taken out and deposited.");
    }

    private void DrawAlreadyStored() =>
        ImGui.TextColored(Palette.Muted, "Nothing yet.");

    /// <summary>A collapsible heading whose state survives a restart, as the duty list's do.</summary>
    private void Section(string label, System.Action body)
    {
        var key = CollapsePrefix + label;
        var configuration = plugin.Configuration;
        var collapsed = configuration.CollapsedGroups.Contains(key);

        ImGui.SetNextItemOpen(!collapsed, ImGuiCond.Appearing);
        var open = ImGui.CollapsingHeader($"{label}###{key}");

        if (open == collapsed)
        {
            if (open)
                configuration.CollapsedGroups.Remove(key);
            else
                configuration.CollapsedGroups.Add(key);

            configuration.Save();
        }

        if (!open)
            return;

        body();
        ImGui.Spacing();
    }

    /// <summary>The settings any section reads, folded into one value so a change is one compare.</summary>
    private readonly record struct Inputs(
        CollectionScope Scope,
        OutfitOwnershipMode Outfits,
        bool CountInventory,
        bool HideWeapons,
        bool JobOnly);
}
