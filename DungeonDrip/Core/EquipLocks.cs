using System;
using System.Collections.Generic;
using Dalamud.Game.Player;
using Lumina.Excel.Sheets;

namespace DungeonDrip.Core;

/// <summary>Which of a piece's own locks this character falls foul of.</summary>
/// <remarks>
/// Flags rather than one boolean because the two are settings the user holds separately, and because
/// the game's rows lock both at once: every race-locked row in the sheet is a gender-locked row as
/// well. A piece is only reported as race-locked when the race column is what shut this character
/// out, so turning one filter on never quietly does the other's work.
/// </remarks>
[Flags]
public enum EquipLock
{
    None = 0,

    /// <summary>Locked to the other gender.</summary>
    Gender = 1,

    /// <summary>Locked to races this character is not.</summary>
    Race = 2,
}

/// <summary>
/// Whether the character you are on can wear a given piece at all - the part of wearability that has
/// nothing to do with what you are doing today.
/// </summary>
/// <remarks>
/// A sibling of <see cref="JobFilter"/> and deliberately not folded into it. That one answers a
/// question whose answer changes every time you switch job, and it is a view filter in the ordinary
/// sense: the piece is yours to collect, you just cannot wear it right now. These are facts about
/// your character - a locked piece is one your Glamour Dresser will accept and your character will
/// never wear - which is why they are worth dropping without also dropping everything the current job
/// cannot use.
///
/// Both locks come off one row, so they are read together and cached together: the item's
/// <c>EquipRestriction</c> is a row in <c>EquipRaceCategory</c>, which carries a column per race and
/// one per gender. Sixteen of its rows lock a race, and each of those locks a gender too - the racial
/// starting gear, some 56 pieces a given race cannot wear.
/// </remarks>
public sealed class EquipLockFilter
{
    private readonly Dictionary<(uint Restriction, Sex Sex, uint Race), EquipLock> cache = [];

    /// <summary>Whether the user's filters say this piece should be left out.</summary>
    public bool Hides(Item item, Configuration configuration) =>
        Excludes(LocksOn(item), configuration);

    /// <summary>
    /// The same question against a lock already read - for the panels, whose rows carry the answer
    /// and whose filters flip without rebuilding them.
    /// </summary>
    public static bool Excludes(EquipLock locks, Configuration configuration) => Excludes(
        locks, configuration.OnlyCurrentGenderEquippable, configuration.OnlyCurrentRaceEquippable);

    public static bool Excludes(EquipLock locks, bool gender, bool race) =>
        (gender && locks.HasFlag(EquipLock.Gender)) ||
        (race && locks.HasFlag(EquipLock.Race));

    public EquipLock LocksOn(Item item)
    {
        var playerState = Plugin.PlayerState;

        // Between characters or on the title screen: nothing is locked, because nothing is known.
        // Showing too much is safer, exactly as the job filter has it.
        if (!playerState.IsLoaded)
            return EquipLock.None;

        var sex = playerState.Sex;
        var race = playerState.Race.RowId;

        var restriction = item.EquipRestriction.RowId;
        if (cache.TryGetValue((restriction, sex, race), out var cached))
            return cached;

        var locks = Read(item, sex, race);
        cache[(restriction, sex, race)] = locks;
        return locks;
    }

    private static EquipLock Read(Item item, Sex sex, uint race)
    {
        // A row this client cannot read is not evidence of anything, and neither is one that permits
        // nobody - row 0 has every column false and is what a piece with nothing to gate on points
        // at. A row has to let somebody through before its refusals mean anything.
        if (!item.EquipRestriction.IsValid)
            return EquipLock.None;

        var category = item.EquipRestriction.Value;
        if (!category.Male && !category.Female)
            return EquipLock.None;

        var locks = EquipLock.None;

        if (sex == Sex.Male && !category.Male)
            locks |= EquipLock.Gender;
        else if (sex == Sex.Female && !category.Female)
            locks |= EquipLock.Gender;

        if (!Permits(category, race))
            locks |= EquipLock.Race;

        return locks;
    }

    /// <summary>
    /// Whether the row's race columns include this one.
    /// </summary>
    /// <remarks>
    /// Switched on the Race sheet's own row ids, which run Hyur, Elezen, Lalafell, Miqo'te, Roegadyn,
    /// Au Ra, Hrothgar, Viera and line up one for one with this sheet's columns. A race this build of
    /// the client does not know is not restricted by anything - a future eighth-and-a-half race must
    /// not silently lose its wardrobe.
    /// </remarks>
    private static bool Permits(EquipRaceCategory category, uint race) => race switch
    {
        1 => category.Hyur,
        2 => category.Elezen,
        3 => category.Lalafell,
        4 => category.Miqote,
        5 => category.Roegadyn,
        6 => category.AuRa,
        7 => category.Hrothgar,
        8 => category.Viera,
        _ => true,
    };
}
