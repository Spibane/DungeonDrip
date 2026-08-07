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

    /// <summary>Brings up the main window with the duty picker open and typed into.</summary>
    void OpenDutyPicker(string filter);
}
