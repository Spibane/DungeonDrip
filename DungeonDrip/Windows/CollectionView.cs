using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

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

    private int seenOwnershipRevision = -1;
    private Inputs seenInputs;

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

    private void DrawDresserPressure() =>
        ImGui.TextColored(Palette.Muted, "Nothing yet.");

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
