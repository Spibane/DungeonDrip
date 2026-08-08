using System.Collections.Generic;
using DungeonDrip.Windows;
using KamiToolKit;

namespace DungeonDrip.Native;

/// <summary>The plugin's windows as the game's own windows, for the surfaces that have one.</summary>
/// <remarks>
/// Wraps <see cref="ImGuiUi"/> rather than replacing it, and will keep doing so: the settings
/// window is staying in ImGui permanently, so a mixed backend is the destination and not a stage of
/// getting there. Each surface that goes native is named in <see cref="Surfaces"/> and withheld
/// from the wrapped backend, because two panels beside one shop is worse than either alone.
///
/// <b>Falls back rather than failing.</b> KamiToolKit is pinned to a commit and a game patch can
/// break it independently of anything here, so a native surface that will not start leaves the
/// plugin as its old self for the session rather than leaving it with no windows. That turns a
/// patch-day break into a downgrade, which matters when the ImGui half is perfectly fine and the
/// plugin releases most days.
/// </remarks>
internal sealed class NativeUi : IPluginUi
{
    private readonly ImGuiUi fallback;

    /// <summary>The panels drawn as game windows, or empty after a failed start.</summary>
    private readonly IReadOnlyList<NativePanelAddon> panels;

    /// <summary>The loot companion, which is not a panel and does not share their base.</summary>
    private readonly LootCompanionAddon? loot;

    /// <summary>The main window, which is the only surface here the player opens.</summary>
    private readonly NativeMainAddon? main;

    /// <summary>One per plugin, because it holds a draw callback and every panel's icons go through it.</summary>
    private readonly GlyphTextures? glyphs;

    /// <summary>Whether anything is actually being drawn natively.</summary>
    /// <remarks>
    /// False after a failed start, when this is an <see cref="ImGuiUi"/> in all but name. Worth
    /// asking before telling anyone the game look is on.
    /// </remarks>
    public bool Active => panels.Count > 0;

    private NativeUi(
        Plugin plugin, GlyphTextures? glyphs, IReadOnlyList<NativePanelAddon> panels,
        LootCompanionAddon? loot, NativeMainAddon? main)
    {
        this.glyphs = glyphs;
        this.panels = panels;
        this.loot = loot;
        this.main = main;

        var taken = panels.Count > 0 ? Surfaces.AddonPanels : Surfaces.None;

        if (loot != null)
            taken |= Surfaces.LootCompanion;

        if (main != null)
            taken |= Surfaces.MainWindow;

        fallback = new ImGuiUi(plugin, taken);
    }

    /// <summary>
    /// Starts the native surfaces, or reports why not and hands back a plain ImGui backend.
    /// </summary>
    /// <remarks>
    /// A factory rather than a constructor because the honest failure result is a different object.
    /// A constructor that swallowed the exception would leave a NativeUi claiming to be native, and
    /// the first thing anyone would do with it is wonder why nothing looks different.
    /// </remarks>
    public static IPluginUi Start(Plugin plugin)
    {
        GlyphTextures? glyphs = null;

        try
        {
            // No default subtitle: the panels set their own titles, and a default would print twice in
            // the header beside a title that already says the same thing.
            KamiToolKitLibrary.Initialize(Plugin.PluginInterface);

            glyphs = new GlyphTextures();

            // All four together. They are the set a player judges as one, and a shop drawn as a
            // game window beside a dresser drawn as an overlay reads as something being broken
            // rather than as a port in progress.
            return new NativeUi(plugin, glyphs, [
                new VendorPanelAddon(plugin, glyphs),
                new DresserPanelAddon(plugin, glyphs),
                new ArmoirePanelAddon(plugin, glyphs),
                new MarketPanelAddon(plugin, glyphs),
            ], new LootCompanionAddon(plugin, glyphs), new NativeMainAddon(plugin, glyphs));
        }
        catch (System.Exception exception)
        {
            // Before the library's own teardown, and unconditionally: it holds a draw callback, and
            // one left subscribed by a failed start would go on firing against a dead backend.
            glyphs?.Dispose();

            Plugin.Log.Error(exception, "Native windows could not start; falling back to the plugin overlays.");

            // Cleanup, not Dispose-on-a-half-built-thing: Initialize may have got far enough to
            // allocate before whatever threw, and the library's own teardown is what knows.
            TryCleanup();
            return new ImGuiUi(plugin);
        }
    }

    public bool MainOpen
    {
        get => main?.IsOpen ?? fallback.MainOpen;
        set
        {
            if (main != null)
                main.IsOpen = value;
            else
                fallback.MainOpen = value;
        }
    }

    public bool ConfigOpen
    {
        get => fallback.ConfigOpen;
        set => fallback.ConfigOpen = value;
    }

    public void ToggleMain()
    {
        if (main != null)
            main.Toggle();
        else
            fallback.ToggleMain();
    }

    public void ToggleConfig() => fallback.ToggleConfig();

    public void OpenDutyPicker(string filter)
    {
        if (main != null)
            main.OpenPicker(filter);
        else
            fallback.OpenDutyPicker(filter);
    }

    /// <remarks>
    /// Native surfaces first, then the ImGui ones, then the library. Any other order leaves
    /// allocated nodes behind for the library's leak check to complain about at best, and hands the
    /// game a freed node at worst.
    ///
    /// The glyph textures go after the panels that asked for them, so nothing is still waiting on a
    /// texture that will now never be rendered.
    /// </remarks>
    public void Dispose()
    {
        main?.Dispose();
        loot?.Dispose();

        foreach (var panel in panels)
            panel.Dispose();

        glyphs?.Dispose();
        fallback.Dispose();
        TryCleanup();
    }

    /// <remarks>
    /// Guarded because this runs on the teardown path, where an exception replaces a tidy shutdown
    /// with a plugin that will not unload. The library's own leak warnings will still have gone to
    /// the log, which is the part worth having.
    /// </remarks>
    private static void TryCleanup()
    {
        try
        {
            KamiToolKitLibrary.Cleanup();
        }
        catch (System.Exception exception)
        {
            Plugin.Log.Error(exception, "KamiToolKit cleanup failed.");
        }
    }
}
