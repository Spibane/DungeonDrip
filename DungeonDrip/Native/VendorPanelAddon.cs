using System;
using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Core;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>The vendor panel as a game window: gear the shop stocks, and where each piece is held.</summary>
/// <remarks>
/// The first surface drawn natively, and picked because it carries every hard part of a gear list
/// in the fewest lines - a marker whose shape and colour are separate decisions, an item icon, a
/// name, a multi-line tooltip - while riding on an addon that appears and disappears as the player
/// walks up to a counter.
///
/// <b>A standalone addon, never nodes attached inside the shop window.</b> That is a promise the
/// plugin already makes about its panels and keeps here: several popular plugins write to the same
/// game nodes, last writer per frame wins, and a refresh reverts either. It also removes the whole
/// class of crash where the game frees an addon holding the plugin's nodes, because nothing of the
/// plugin's is ever inside anything the game frees.
///
/// It cannot be resized by dragging, unlike its ImGui counterpart, because a game window has no
/// resize handle. It sizes itself to the shop and the player scales it from the header's own
/// right-click menu, which scales the text with it - see the readme's Not implemented section.
/// </remarks>
internal sealed unsafe class VendorPanelAddon : IDisposable
{
    /// <summary>
    /// The panel's width, and its height until a shop is found to match.
    /// </summary>
    /// <remarks>
    /// Fixed, where the ImGui panel measures the longest name and sizes to it. Measuring text is
    /// something ImGui offers and a node tree does not, so the honest first cut is a width that
    /// suits most shop stock; a name too long for it will wrap or clip until this is measured
    /// properly off the font.
    /// </remarks>
    private static readonly Vector2 UnanchoredSize = new(260f, 400f);

    private readonly Plugin plugin;
    private readonly Panel addon;

    /// <summary>The row list this panel was last built from, so an unchanged list is not rebuilt.</summary>
    /// <remarks>
    /// Reference equality against the watcher's cached list, which is the same instance frame to
    /// frame until something actually moves. A native rebuild allocates and frees game nodes, so
    /// unlike an ImGui redraw it is not free and must not happen on a tick.
    /// </remarks>
    private IReadOnlyList<GearRow>? builtFrom;

    private bool opened;

    public VendorPanelAddon(Plugin plugin)
    {
        this.plugin = plugin;

        addon = new Panel
        {
            InternalName = "DungeonDripVendor",
            Title = "Dungeon Drip",
            Size = UnanchoredSize,

            // The panel belongs beside its shop, so it is placed every frame and there is no
            // remembered position to restore. Leaving this on would fight the anchoring.
            RememberClosePosition = false,
        };

        Plugin.Framework.Update += OnUpdate;
    }

    /// <summary>
    /// Opens, closes and repositions the panel against whichever shop is up.
    /// </summary>
    /// <remarks>
    /// On the framework tick rather than in a draw callback, which is the real difference from the
    /// ImGui panels: there is no draw callback to be in. A consequence worth watching for is that
    /// the panel can lag its shop by a tick while the shop is being dragged.
    /// </remarks>
    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var rows = Resolve();

        if (rows == null)
        {
            if (opened)
            {
                addon.Close();
                opened = false;
                builtFrom = null;
            }

            return;
        }

        if (!opened)
        {
            addon.Open();
            opened = true;
        }

        // The list only exists once the game has run the addon's setup, which is not guaranteed to
        // have happened by the time Open returns. Leaving builtFrom alone rather than recording a
        // build that did not happen is what makes this a retry next tick instead of a panel that
        // comes up empty and stays that way.
        if (addon.List is not { } list)
            return;

        if (!ReferenceEquals(builtFrom, rows))
        {
            builtFrom = rows;
            Rebuild(list, rows);
        }

        Anchor();
    }

    /// <summary>The list to draw, or null when the panel should not be up at all.</summary>
    private IReadOnlyList<GearRow>? Resolve()
    {
        if (!plugin.Configuration.VendorPanel.Enabled)
            return null;

        var rows = plugin.Shop.Resolve();

        // A shop with nothing wearable gets no panel. An empty panel at the potion merchant is
        // noise, and the same rule the ImGui panel applies.
        return rows is { Count: > 0 } ? rows : null;
    }

    private void Rebuild(VerticalListNode list, IReadOnlyList<GearRow> rows)
    {
        var stale = plugin.Ownership.IsDresserStale;

        var built = new List<RowNode>(rows.Count);
        foreach (var row in rows)
            built.Add(PanelPresenter.Row(row, stale, UncertainNote));

        NativeRenderer.Fill(list, built, addon.Size.X);
    }

    /// <summary>Why a stale snapshot makes a "not collected" claim here worth checking.</summary>
    private const string UncertainNote = "Your dresser data is old - you may already own this.";

    /// <remarks>
    /// The same arithmetic the ImGui panels use, through the shared
    /// <see cref="AddonAnchor"/> - the placement is a fact about the screen and does not change
    /// with the backend. What does change is where the numbers come from: there is no
    /// <c>ImGui.GetIO</c> here, so the screen size is read off the game's own device.
    /// </remarks>
    private void Anchor()
    {
        var name = plugin.Shop.ActiveAddonName;
        if (name == null)
            return;

        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(name);
        if (unit == null)
            return;

        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        var display = new Vector2(device->Width, device->Height);

        // Matching the shop's height keeps a long list on screen and lines the two windows up,
        // which is the whole of the sizing story now that dragging one is not on offer.
        var size = new Vector2(addon.Size.X, unit->GetScaledHeight(true));
        addon.SetWindowSize(size.X, size.Y);

        addon.SetWindowPosition(AddonAnchor.Beside(
            new Vector2(unit->X, unit->Y), unit->GetScaledWidth(true),
            plugin.Configuration.VendorPanel.Side, size, display));
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;

        if (opened)
            addon.Close();

        addon.Dispose();
    }

    /// <summary>The addon itself, and the list of rows inside it.</summary>
    /// <remarks>
    /// The list is built in <see cref="OnSetup"/> and not in a constructor, because there is
    /// nothing to attach it to until then: the root node is allocated by Open, and a node attached
    /// before that attaches to nothing and the panel comes up empty.
    ///
    /// Closing deallocates, so this runs again on every reopen and the list is a different node
    /// each time. That is why the panel forgets what it built from when it closes - the rows it
    /// remembers building no longer exist.
    /// </remarks>
    private sealed class Panel : NativeAddon
    {
        public VerticalListNode? List { get; private set; }

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
        {
            List = new VerticalListNode
            {
                Position = ContentStartPosition,
                Size = ContentSize,
                IsVisible = true,
            };

            AddNode(List);
        }
    }
}
