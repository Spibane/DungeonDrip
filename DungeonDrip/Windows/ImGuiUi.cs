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

    public ImGuiUi(Plugin plugin)
    {
        this.plugin = plugin;

        mainWindow = new MissingItemsWindow(plugin);
        configWindow = new ConfigWindow(plugin);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(configWindow);

        // Always open; each decides for itself whether the addon it rides on is up.
        windowSystem.AddWindow(new LootCompanionWindow(plugin) { IsOpen = true });
        windowSystem.AddWindow(new VendorPanelWindow(plugin) { IsOpen = true });
        windowSystem.AddWindow(new DresserPanelWindow(plugin) { IsOpen = true });
        windowSystem.AddWindow(new ArmoirePanelWindow(plugin) { IsOpen = true });
        windowSystem.AddWindow(new MarketBoardPanelWindow(plugin) { IsOpen = true });

        Plugin.PluginInterface.UiBuilder.Draw += windowSystem.Draw;
    }

    public bool MainOpen
    {
        get => mainWindow.IsOpen;
        set => mainWindow.IsOpen = value;
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
