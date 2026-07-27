using System;
using System.IO;
using System.Text.Json;

namespace DungeonDrip.Data;

/// <summary>
/// Reads and writes the plugin's JSON files.
/// </summary>
/// <remarks>
/// Every one of these files is disposable cache that rebuilds itself, so a corrupt or unreadable one
/// is logged and treated as absent rather than allowed to break loading. Centralised because four
/// stores were each carrying their own copy of the same try/catch.
/// </remarks>
public static class JsonStore
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>Returns null when the file is missing, empty or unreadable.</summary>
    public static T? Read<T>(string path)
        where T : class
    {
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not read {path}; treating it as absent");
            return null;
        }
    }

    /// <summary>Writes via a temporary file, so an interrupted write cannot truncate the original.</summary>
    public static void Write<T>(string path, T value, bool indented = false)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, indented ? Indented : null));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, $"Could not write {path}");
        }
    }
}
