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
    private readonly VendorPanelAddon? vendorPanel;

    /// <summary>Whether anything is actually being drawn natively.</summary>
    /// <remarks>
    /// False after a failed start, when this is an <see cref="ImGuiUi"/> in all but name. Worth
    /// asking before telling anyone the game look is on.
    /// </remarks>
    public bool Active => vendorPanel != null;

    private NativeUi(Plugin plugin, VendorPanelAddon? vendorPanel)
    {
        this.vendorPanel = vendorPanel;

        fallback = new ImGuiUi(
            plugin, vendorPanel != null ? Surfaces.VendorPanel : Surfaces.None);
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
        try
        {
            KamiToolKitLibrary.Initialize(Plugin.PluginInterface, "Dungeon Drip");
            return new NativeUi(plugin, new VendorPanelAddon(plugin));
        }
        catch (System.Exception exception)
        {
            Plugin.Log.Error(exception, "Native windows could not start; falling back to the plugin overlays.");

            // Cleanup, not Dispose-on-a-half-built-thing: Initialize may have got far enough to
            // allocate before whatever threw, and the library's own teardown is what knows.
            TryCleanup();
            return new ImGuiUi(plugin);
        }
    }

    public bool MainOpen
    {
        get => fallback.MainOpen;
        set => fallback.MainOpen = value;
    }

    public void ToggleMain() => fallback.ToggleMain();

    public void ToggleConfig() => fallback.ToggleConfig();

    public void OpenDutyPicker(string filter) => fallback.OpenDutyPicker(filter);

    /// <remarks>
    /// Native surfaces first, then the ImGui ones, then the library. Any other order leaves
    /// allocated nodes behind for the library's leak check to complain about at best, and hands the
    /// game a freed node at worst.
    /// </remarks>
    public void Dispose()
    {
        vendorPanel?.Dispose();
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
