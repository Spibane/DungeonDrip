using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Reads a file keyed by territory id.
    /// </summary>
    /// <remarks>
    /// JSON object keys are strings, so every one of these files - the wiki cache, learned drops and
    /// the hand-written loot-overrides.json - stores the id as text and has to parse it back. Stated
    /// once here, including that an unparseable key is skipped rather than fatal, because
    /// loot-overrides.json is typed by hand.
    /// </remarks>
    public static Dictionary<uint, T> ReadByTerritory<T>(string path)
    {
        var parsed = new Dictionary<uint, T>();

        foreach (var (key, value) in Read<Dictionary<string, T>>(path) ?? [])
        {
            if (uint.TryParse(key, out var territoryId))
                parsed[territoryId] = value;
        }

        return parsed;
    }

    /// <summary>The matching write, so the two halves of the convention sit together.</summary>
    public static void WriteByTerritory<T>(string path, IEnumerable<KeyValuePair<uint, T>> entries) =>
        Write(path, entries.ToDictionary(entry => entry.Key.ToString(), entry => entry.Value), indented: true);

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
