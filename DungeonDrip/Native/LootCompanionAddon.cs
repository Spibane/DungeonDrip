using System;
using System.Collections.Generic;
using System.Numerics;
using DungeonDrip.Core;
using DungeonDrip.Game;
using DungeonDrip.Presentation;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Nodes;

namespace DungeonDrip.Native;

/// <summary>The loot companion as a game window, beside the Need/Greed roll.</summary>
/// <remarks>
/// <b>The one surface where being a game window is a straightforward improvement rather than a
/// trade.</b> The overlay auto-resizes, and its own remarks say why that is uncomfortable: a roll is
/// a thirty-second decision and a window that changes size under a pointer about to click is
/// hostile. A game window is sized once when the roll's contents are read and does not move again.
///
/// Not a <see cref="NativePanelAddon"/>, for the same reasons its overlay is not an
/// <c>AddonPanelWindow</c>: no toolbar, no grouping, no folded headings, and it refuses to draw its
/// list at all without a dresser snapshot where a panel would show the list with a warning. Sharing
/// a base would mean switching most of that off.
///
/// No scroll area either. A roll is at most a handful of rows by the game's own limit, so a scroll
/// bar would be furniture that never moves - and one more thing to reach for under a countdown.
/// </remarks>
internal sealed unsafe class LootCompanionAddon : IDisposable
{
    /// <summary>
    /// The window's width, which is fixed rather than measured.
    /// </summary>
    /// <remarks>
    /// Wide enough for the longest item names, because the alternative is worse here than anywhere
    /// else: a name cut short is a name that has to be read off the roll window instead, which is
    /// the work this exists to save.
    /// </remarks>
    private const float Width = 280f;

    private readonly Plugin plugin;
    private readonly Companion addon;
    private readonly NativeContextMenu menu;
    private readonly GlyphTextures glyphs;

    /// <summary>
    /// Needed by the renderer and never used.
    /// </summary>
    /// <remarks>
    /// There are no collapsible headings in a roll, so nothing ever reads or writes this. It is here
    /// because the renderer takes one, and giving it a real one costs nothing next to carving out a
    /// second entry point that differs only in what it cannot be asked.
    /// </remarks>
    private readonly HeaderState headers;

    private IReadOnlyList<LootRoll>? builtFrom;
    private VerticalListNode? builtInto;

    public LootCompanionAddon(Plugin plugin, GlyphTextures glyphs)
    {
        this.plugin = plugin;
        this.glyphs = glyphs;
        menu = new NativeContextMenu(plugin);
        headers = new HeaderState(plugin.Configuration);

        addon = new Companion
        {
            InternalName = "DungeonDripLoot",
            Title = "Still needed",
            Size = new Vector2(Width, 200f),

            // Placed against the roll window every tick, so there is no remembered position to
            // restore and leaving this on would fight the anchoring.
            RememberClosePosition = false,

            // Not the player's to close: it belongs to the roll and goes when the roll does. A close
            // button would shut it for the length of one roll and reopen on the next, which reads
            // as a broken button rather than as a preference.
            CreateWindowNode = () => new WindowNode { NodeId = 2, ShowCloseButton = false },
            DisableClose = true,
            RespectCloseAll = false,
        };

        Plugin.Framework.Update += OnUpdate;
        glyphs.Ready += OnGlyphsReady;
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        var rolls = plugin.Loot.Resolve();

        if (rolls == null)
        {
            if (addon.IsOpen)
                addon.Close();

            return;
        }

        if (!addon.IsOpen)
            addon.Open();

        if (addon.List is not { } list)
            return;

        // Watching the node as well as the rows, because closing an addon frees the whole tree: a
        // roll that ends and starts again on the same items has somewhere new to draw and nothing
        // else would say so.
        if (!ReferenceEquals(builtInto, list) || !ReferenceEquals(builtFrom, rolls))
        {
            builtInto = list;
            builtFrom = rolls;
            Rebuild(list, rolls);
        }

        Anchor();
    }

    /// <remarks>
    /// The window is sized to what it holds, once, as part of the same build. That is what makes
    /// fixed sizing an improvement rather than a compromise: the contents of a roll do not change
    /// while it runs, so sizing to them cannot move anything under a pointer.
    /// </remarks>
    private void Rebuild(VerticalListNode list, IReadOnlyList<LootRoll> rolls)
    {
        var built = LootCompanionPresenter.Build(rolls, plugin.Ownership);

        NativeRenderer.Fill(menu, glyphs, headers, list, built, reservedRight: 0f, () => { });

        // Measured rather than counted. A warning line and a rolled row are not the same height, so
        // a count of nodes times a constant is right only for a list with nothing to warn about -
        // which is the common case, and so the one where the error would go unnoticed.
        addon.SetWindowSize(Width, addon.ChromeHeight + list.Height);
        addon.Relayout();
    }

    /// <remarks>
    /// Beside the roll window on whichever side the player asked for, through the same arithmetic
    /// every other surface uses. The roll window has no ornamental border worth allowing for, so
    /// there is no frame inset here.
    /// </remarks>
    private void Anchor()
    {
        var unit = Plugin.GameGui.GetAddonByName<AtkUnitBase>(LootRollWatcher.AddonName);
        if (unit == null)
            return;

        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        var display = new Vector2(device->Width, device->Height);

        addon.SetWindowPosition(AddonAnchor.Beside(
            new Vector2(unit->X, unit->Y), unit->GetScaledWidth(true),
            plugin.Configuration.LootCompanionSide, addon.Size, display));
    }

    private void OnGlyphsReady() => builtFrom = null;

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        glyphs.Ready -= OnGlyphsReady;

        if (addon.IsOpen)
            addon.Close();

        addon.Dispose();
        menu.Dispose();
    }

    /// <summary>The addon itself, and the one list inside it.</summary>
    private sealed class Companion : NativeAddon
    {
        private VerticalListNode? list;

        /// <summary>The list to put the roll in, or null while the addon is not allocated.</summary>
        public VerticalListNode? List => list;

        /// <summary>
        /// How much of the window is header and padding rather than list.
        /// </summary>
        /// <remarks>
        /// Derived rather than written down, because it is the library's arithmetic and not this
        /// plugin's: the difference between what the window is and what it has room for is exactly
        /// what has to be added back to size the window to its contents.
        /// </remarks>
        public float ChromeHeight => Size.Y - ContentSize.Y;

        protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
        {
            list = new VerticalListNode { FitContents = true, IsVisible = true };
            AddNode(list);

            Relayout();
        }

        public void Relayout()
        {
            if (list == null)
                return;

            list.Position = ContentStartPosition;
            list.Width = ContentSize.X;
        }

        protected override unsafe void OnFinalize(AtkUnitBase* addon) => list = null;
    }
}
