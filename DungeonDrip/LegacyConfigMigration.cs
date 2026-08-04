using System;
using System.IO;
using Dalamud.Plugin;

namespace DungeonDrip;

/// <summary>
/// Carries settings and caches over from the plugin's former name.
/// </summary>
/// <remarks>
/// Dalamud derives both the config file and the config folder from the plugin's internal name, so
/// renaming orphans everything: the Glamour Dresser snapshot, learned drops and wiki lookups would
/// all silently start from nothing. Runs before any of that is read, moves rather than copies so it
/// cannot happen twice, and never overwrites anything already present under the new name.
/// </remarks>
public static class LegacyConfigMigration
{
    private const string LegacyName = "GlamourAssistant";

    public static void Run(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            var configDirectory = new DirectoryInfo(pluginInterface.GetPluginConfigDirectory());
            var root = configDirectory.Parent;
            if (root == null)
                return;

            MigrateConfigFile(root, pluginInterface.ConfigFile);
            MigrateConfigDirectory(root, configDirectory);
        }
        catch (Exception ex)
        {
            // Never fatal: worst case the user starts fresh under the new name.
            Plugin.Log.Warning(ex, "Could not migrate data from the previous plugin name");
        }
    }

    private static void MigrateConfigFile(DirectoryInfo root, FileInfo current)
    {
        var legacy = new FileInfo(Path.Combine(root.FullName, $"{LegacyName}.json"));
        if (!legacy.Exists || current.Exists)
            return;

        legacy.MoveTo(current.FullName);
        Plugin.Log.Information($"Migrated settings from {legacy.Name}");
    }

    private static void MigrateConfigDirectory(DirectoryInfo root, DirectoryInfo current)
    {
        var legacy = new DirectoryInfo(Path.Combine(root.FullName, LegacyName));
        if (!legacy.Exists)
            return;

        // The new directory may already exist, so move file by file rather than
        // renaming the folder wholesale.
        Directory.CreateDirectory(current.FullName);

        var moved = 0;
        foreach (var file in legacy.GetFiles())
        {
            var destination = Path.Combine(current.FullName, file.Name);
            if (File.Exists(destination))
                continue;

            file.MoveTo(destination);
            moved++;
        }

        if (moved > 0)
            Plugin.Log.Information($"Migrated {moved} cache file(s) from the {LegacyName} folder");

        if (legacy.GetFiles().Length == 0 && legacy.GetDirectories().Length == 0)
            legacy.Delete();
    }
}
