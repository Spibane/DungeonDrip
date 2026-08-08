using System;

namespace DungeonDrip;

/// <summary>Everything the plugin asks of its windows, without saying what draws them.</summary>
/// <remarks>
/// Deliberately four members. The plugin body opens the main window on entering a duty, closes it
/// on leaving, toggles both from commands and from Dalamud's own buttons, and opens the duty picker
/// pre-filtered from a chat lookup - and that is the whole of what it wants. Everything else a
/// window does, it decides for itself by reading the plugin.
///
/// Small on purpose, because a wide interface would be a second place for the two backends to
/// disagree. Anything a backend needs that is not here is something it can reach through
/// <see cref="Plugin"/> directly.
/// </remarks>
internal interface IPluginUi : IDisposable
{
    /// <summary>Whether the main window is up. Setting it false does not close the settings.</summary>
    bool MainOpen { get; set; }

    void ToggleMain();

    void ToggleConfig();

    /// <summary>
    /// Whether the settings window is up.
    /// </summary>
    /// <remarks>
    /// Here rather than left to <see cref="ToggleConfig"/> alone because the look can be switched
    /// while it is open - and it is switched from inside it, so a backend swap that dropped this
    /// would close the window the player was working in.
    /// </remarks>
    bool ConfigOpen { get; set; }

    /// <summary>Brings up the main window with the duty picker open and typed into.</summary>
    void OpenDutyPicker(string filter);
}

/// <summary>Which surfaces a backend has taken over.</summary>
/// <remarks>
/// Flags rather than a bool per surface, because the list only grows as the native port proceeds
/// and every addition would otherwise be another constructor parameter to thread through.
/// </remarks>
[System.Flags]
internal enum Surfaces
{
    None = 0,
    VendorPanel = 1 << 0,
    DresserPanel = 1 << 1,
    ArmoirePanel = 1 << 2,
    MarketBoardPanel = 1 << 3,
    LootCompanion = 1 << 4,

    /// <summary>The duty view and the collection view, which are one window with two modes.</summary>
    /// <remarks>
    /// One flag for both, because they are one window and its toolbar switches between them. Taking
    /// over half of it would leave a button on screen that switched to a view this look could not
    /// draw.
    /// </remarks>
    MainWindow = 1 << 5,

    /// <summary>Every panel that rides beside a game window.</summary>
    /// <remarks>
    /// Named rather than spelled out at the call site, because these four go native together and
    /// are the set a player judges as one - four game windows beside four game windows, rather than
    /// a mix that looks like a mistake.
    /// </remarks>
    AddonPanels = VendorPanel | DresserPanel | ArmoirePanel | MarketBoardPanel,
}
