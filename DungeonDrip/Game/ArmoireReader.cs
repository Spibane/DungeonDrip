using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using CabinetSheet = Lumina.Excel.Sheets.Cabinet;

namespace DungeonDrip.Game;

public static unsafe class ArmoireReader
{
    /// <summary>
    /// Reads which Armoire-eligible items are stored.
    /// </summary>
    /// <returns><c>null</c> when the armoire has not been loaded from the server yet.</returns>
    /// <remarks>
    /// The armoire only loads on demand - opening it at an inn, or certain glamour operations - so
    /// this returns null most of the time and the caller falls back to its cache.
    /// </remarks>
    public static HashSet<uint>? TryRead()
    {
        var uiState = UIState.Instance();
        if (uiState == null || !uiState->Cabinet.IsCabinetLoaded())
            return null;

        var stored = new HashSet<uint>();
        foreach (var row in Plugin.DataManager.GetExcelSheet<CabinetSheet>())
        {
            var itemId = row.Item.RowId;
            if (itemId == 0)
                continue;

            if (uiState->Cabinet.IsItemInCabinet(row.RowId))
                stored.Add(itemId);
        }

        return stored;
    }
}
