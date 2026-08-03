using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Game;

/// <summary>How far along one outfit set is, as stored in the Glamour Dresser.</summary>
/// <param name="Filled">Slots of the set that are stored with a piece in them.</param>
/// <param name="Total">Slots the set has at all, per the sheet.</param>
/// <param name="Missing">The pieces behind the empty slots.</param>
/// <param name="StoredAsSet">
/// Whether the set is in the box at all. A set can be held with every slot empty, which is
/// different news from not having it.
/// </param>
public sealed record SetProgress(
    uint SetId, int Filled, int Total, IReadOnlyList<uint> Missing, bool StoredAsSet);

/// <summary>
/// Static view of the game's outfit sets: which pieces each set is made of, and which sets a given
/// piece belongs to.
/// </summary>
/// <remarks>
/// A Glamour Dresser slot can hold an outfit set instead of a single item. The set's components live
/// in the MirageStoreSetItem sheet, whose row id is the outfit's own item id. Slot order below is the
/// sheet's column order, which is also the bit order used by
/// <c>MirageManager.IsSetSlotUnlocked</c> and <c>RestorePrismBoxSetItem</c>.
/// </remarks>
public sealed class OutfitCatalog
{
    public const int SlotCount = 11;

    private readonly Dictionary<uint, HashSet<uint>> setsByPiece;
    private readonly Dictionary<uint, HashSet<uint>> piecesBySet;

    private OutfitCatalog(
        Dictionary<uint, HashSet<uint>> setsByPiece,
        Dictionary<uint, HashSet<uint>> piecesBySet)
    {
        this.setsByPiece = setsByPiece;
        this.piecesBySet = piecesBySet;
    }

    /// <summary>Every outfit set that lists <paramref name="itemId"/> as one of its pieces.</summary>
    public IReadOnlySet<uint> SetsContaining(uint itemId) =>
        setsByPiece.TryGetValue(itemId, out var sets) ? sets : EmptySet;

    /// <summary>Every outfit set in the game, for anything that has to sweep them.</summary>
    public IReadOnlyCollection<uint> SetIds => piecesBySet.Keys;

    /// <summary>Every piece the given outfit set is made of.</summary>
    public IReadOnlySet<uint> PiecesOf(uint setId) =>
        piecesBySet.TryGetValue(setId, out var pieces) ? pieces : EmptySet;

    /// <summary>
    /// The pieces of an outfit set, in the sheet's slot order, for anything that has to hand them to
    /// the game one at a time.
    /// </summary>
    /// <remarks>
    /// Read off the sheet rather than out of <see cref="piecesBySet"/>: that one is a set, and a
    /// fitting room filled weapon-first then top-down reads as an outfit being put on, whereas hash
    /// order reads as noise.
    ///
    /// That sheet read is per call, so this is for the handful of sets someone picked out of a
    /// menu. Anything sweeping every set wants <see cref="PiecesOf"/> and its own ordering.
    /// </remarks>
    public IReadOnlyList<uint> PiecesInSlotOrder(uint setId)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        if (!sheet.TryGetRow(setId, out var row))
            return [];

        var pieces = new List<uint>(SlotCount);

        for (var slot = 0; slot < SlotCount; slot++)
        {
            var piece = GetSlotItemId(row, slot);
            if (piece != 0)
                pieces.Add(piece);
        }

        return pieces;
    }

    /// <summary>
    /// The outfit sets a piece belongs to, named and sorted, ready to be listed to the user.
    /// </summary>
    public IReadOnlyList<(uint Id, string Name)> NamedSetsContaining(uint itemId)
    {
        var sets = SetsContaining(itemId);
        if (sets.Count == 0)
            return [];

        var items = Plugin.DataManager.GetExcelSheet<Item>();

        return sets
            .Select(setId => (
                Id: setId,
                Name: items.TryGetRow(setId, out var row) ? row.Name.ExtractText() : $"Outfit {setId}"))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Whether any stored outfit set holding this piece has every one of its slots filled.
    /// </summary>
    /// <remarks>
    /// A set can sit in the dresser with gaps in it, so "finished" is different news from
    /// "accounted for".
    /// </remarks>
    public bool IsInCompletedSet(uint itemId, OwnershipView view)
    {
        if (!view.DresserOutfits.TryGetValue(itemId, out var holders))
            return false;

        foreach (var setId in holders)
        {
            if (IsComplete(setId, view))
                return true;
        }

        return false;
    }

    /// <summary>
    /// How much of an outfit set is sitting filled in the dresser, and what is missing from it.
    /// </summary>
    /// <remarks>
    /// This is "complete as stored", which is a narrower question than "do you own these pieces" -
    /// a piece can be in your Armoire, or in a different set, and still leave this slot empty. The
    /// broader reading belongs with the ownership decision rather than here, because it has to obey
    /// the user's storage scope and outfit mode and this class knows about neither.
    /// </remarks>
    public SetProgress ProgressInDresser(uint setId, OwnershipView view)
    {
        var pieces = PiecesOf(setId);
        var missing = new List<uint>();

        foreach (var piece in pieces)
        {
            // The reader only records a piece against a set when the slot is unlocked, so
            // membership here answers "is this slot filled".
            if (!view.DresserOutfits.TryGetValue(piece, out var owners) || !owners.Contains(setId))
                missing.Add(piece);
        }

        return new SetProgress(
            setId, pieces.Count - missing.Count, pieces.Count, missing, view.StoredOutfits.Contains(setId));
    }

    // One definition of "complete as stored", so the per-piece green star and anything counting
    // sets cannot disagree about what finished means.
    private bool IsComplete(uint setId, OwnershipView view)
    {
        var progress = ProgressInDresser(setId, view);
        return progress.Total > 0 && progress.Filled == progress.Total;
    }

    private static readonly HashSet<uint> EmptySet = [];

    public static OutfitCatalog Build()
    {
        var sheet = Plugin.DataManager.GetExcelSheet<MirageStoreSetItem>();
        var byPiece = new Dictionary<uint, HashSet<uint>>();
        var bySet = new Dictionary<uint, HashSet<uint>>();

        foreach (var row in sheet)
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var piece = GetSlotItemId(row, slot);
                if (piece == 0)
                    continue;

                if (!byPiece.TryGetValue(piece, out var sets))
                    byPiece[piece] = sets = [];

                sets.Add(row.RowId);

                if (!bySet.TryGetValue(row.RowId, out var pieces))
                    bySet[row.RowId] = pieces = [];

                pieces.Add(piece);
            }
        }

        return new OutfitCatalog(byPiece, bySet);
    }

    public static uint GetSlotItemId(MirageStoreSetItem row, int slot) => slot switch
    {
        0 => row.MainHand.RowId,
        1 => row.OffHand.RowId,
        2 => row.Head.RowId,
        3 => row.Body.RowId,
        4 => row.Hands.RowId,
        5 => row.Legs.RowId,
        6 => row.Feet.RowId,
        7 => row.Earrings.RowId,
        8 => row.Necklace.RowId,
        9 => row.Bracelets.RowId,
        10 => row.Ring.RowId,
        _ => 0,
    };
}
