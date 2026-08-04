using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using CabinetSheet = Lumina.Excel.Sheets.Cabinet;

namespace DungeonDrip.Game;

/// <summary>
/// Reads the Armoire out of the client, when the client happens to have it.
/// </summary>
/// <remarks>
/// The Armoire is not a container the client can enumerate. It is a set of flags on UIState keyed by
/// Cabinet row, so the whole sheet is walked and each row asked about - a few thousand cheap
/// questions rather than one read, which is why this runs on the poll rather than per frame.
///
/// Rarely loaded: opening it at an inn, and some glamour operations. Everything else answers from
/// the snapshot <see cref="OwnershipTracker"/> keeps, which is why null here has to mean "nothing to
/// say" rather than "empty".
/// </remarks>
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
