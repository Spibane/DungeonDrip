using Dalamud.Interface.Windowing;

namespace DungeonDrip.Windows;

/// <summary>The plugin's windows as ImGui overlays, and the window system holding them.</summary>
/// <remarks>
/// The seven windows used to be constructed and wired up in <see cref="Plugin"/>'s constructor,
/// which was fine while there was one way to draw them. Gathering them here is what lets the plugin
/// body hold an <see cref="IPluginUi"/> and stop knowing that ImGui exists.
///
/// The draw callback is subscribed and unsubscribed here too, so that swapping backends is a
/// dispose and a construct rather than a list of handlers to remember to move.
/// </remarks>
internal sealed class ImGuiUi : IPluginUi
{
    private readonly Plugin plugin;
    private readonly WindowSystem windowSystem = new("DungeonDrip");
    private readonly MissingItemsWindow mainWindow;
    private readonly ConfigWindow configWindow;

    /// <param name="without">
    /// Surfaces another backend has taken over, which must not also be drawn here. Two panels
    /// beside one shop is the failure mode this exists to prevent, and it is not hypothetical -
    /// native mode is arriving one surface at a time, so for most of that time some of these are
    /// native and the rest are not.
    /// </param>
    public ImGuiUi(Plugin plugin, Surfaces without = Surfaces.None)
    {
        this.plugin = plugin;

        mainWindow = new MissingItemsWindow(plugin);
        configWindow = new ConfigWindow(plugin);
        windowSystem.AddWindow(configWindow);

        // The settings window is always here; the main one only when nothing else has taken it. It
        // is still built either way, since the seam below answers through it.
        if (!without.HasFlag(Surfaces.MainWindow))
            windowSystem.AddWindow(mainWindow);

        // Always open; each decides for itself whether the addon it rides on is up.
        if (!without.HasFlag(Surfaces.LootCompanion))
            windowSystem.AddWindow(new LootCompanionWindow(plugin) { IsOpen = true });

        if (!without.HasFlag(Surfaces.VendorPanel))
            windowSystem.AddWindow(new VendorPanelWindow(plugin) { IsOpen = true });

        if (!without.HasFlag(Surfaces.DresserPanel))
            windowSystem.AddWindow(new DresserPanelWindow(plugin) { IsOpen = true });

        if (!without.HasFlag(Surfaces.ArmoirePanel))
            windowSystem.AddWindow(new ArmoirePanelWindow(plugin) { IsOpen = true });

        if (!without.HasFlag(Surfaces.MarketBoardPanel))
            windowSystem.AddWindow(new MarketBoardPanelWindow(plugin) { IsOpen = true });

        Plugin.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
    }

    public bool MainOpen
    {
        get => mainWindow.IsOpen;
        set => mainWindow.IsOpen = value;
    }

    public bool ConfigOpen
    {
        get => configWindow.IsOpen;
        set => configWindow.IsOpen = value;
    }

    public void ToggleMain() => mainWindow.Toggle();

    public void ToggleConfig() => configWindow.Toggle();

    public void OpenDutyPicker(string filter) => mainWindow.OpenPicker(filter);

    public void Dispose()
    {
        Plugin.PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        windowSystem.RemoveAllWindows();
    }
}
