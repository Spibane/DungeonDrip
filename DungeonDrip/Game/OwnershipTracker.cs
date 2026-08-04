using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DungeonDrip.Game;

/// <summary>How full the Glamour Dresser is.</summary>
/// <remarks>
/// Occupancy used to live only on the tracker, which meant pure query code - the part that is
/// deliberately kept free of Dalamud so it can be reasoned about on its own - could not see it.
/// <see cref="Capacity"/> carries the caveat from <see cref="DresserSnapshot.SlotCapacity"/>: it is
/// the box's structural size, not a per-account unlocked count.
/// </remarks>
public sealed record DresserSpace(int Used, int Capacity);

/// <summary>
/// One retainer's holdings: who, when, and what.
/// </summary>
/// <param name="Items">
/// What was in that retainer's bags, ids only. No quantities: the snapshot is taken to answer "do I
/// have one of these", and a count read weeks ago is a number that ages badly for no gain.
/// </param>
/// <param name="Equipped">
/// What the retainer was wearing, which is not in the bags and is not found by looking there.
/// </param>
public sealed record RetainerHolding(
    ulong RetainerId,
    string Name,
    DateTime UpdatedUtc,
    IReadOnlySet<uint> Items,
    IReadOnlySet<uint> Equipped);

/// <summary>An immutable view of what one character owns, safe to hand to pure query code.</summary>
/// <param name="Retainers">
/// Every piece in the bags of every retainer that has been read, or null when retainers are not being
/// counted - the same "not counted" convention <paramref name="Inventory"/> uses. Collapsed into one
/// set on purpose: which retainer a piece is with changes nothing about whether it is owned, and the
/// tracker can still name them for anything that wants to say where.
/// </param>
/// <param name="RetainersWearing">
/// The same for gear the retainers have on, kept separate all the way through rather than merged into
/// the bags. It is a different answer to "where is it", it has its own setting, and it is the one that
/// sends people hunting through bags it was never in.
/// </param>
public sealed record OwnershipView(
    IReadOnlySet<uint> DresserDirect,
    IReadOnlyDictionary<uint, HashSet<uint>> DresserOutfits,
    IReadOnlySet<uint> Armoire,
    IReadOnlySet<uint>? Inventory,
    IReadOnlySet<uint> StoredOutfits,
    // Defaulted so the existing construction sites keep compiling; null means no dresser data,
    // matching how Inventory says "not counted".
    DresserSpace? Space = null,
    IReadOnlySet<uint>? Retainers = null,
    IReadOnlySet<uint>? RetainersWearing = null)
{
    public static readonly OwnershipView Empty = new(
        new HashSet<uint>(), new Dictionary<uint, HashSet<uint>>(), new HashSet<uint>(), null, new HashSet<uint>());
}

/// <summary>
/// Keeps a per-character record of the Glamour Dresser and Armoire on disk.
/// </summary>
/// <remarks>
/// This cache is not an optimisation, it is the only way the feature can work: the client wipes
/// dresser data on every zone change, never loads the armoire unless it is opened, and only loads a
/// retainer's bags while standing at that retainer - so by the time the character is in a dungeon there
/// is nothing live to read. A snapshot is taken whenever the data happens to be loaded, and how old it
/// is gets reported.
/// </remarks>
public sealed class OwnershipTracker
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly string cacheDirectory;
    private readonly Configuration configuration;

    private DateTime nextPoll = DateTime.MinValue;
    private ulong loadedContentId;

    private DresserSnapshot? dresser;
    private HashSet<uint>? armoire;
    private HashSet<uint> inventory = [];

    /// <summary>One entry per retainer whose bags have ever been read, keyed by the client's id.</summary>
    private readonly Dictionary<ulong, StoredRetainer> retainers = [];

    /// <summary>
    /// The union of every retainer's bags, built on demand and dropped whenever one changes.
    /// </summary>
    /// <remarks>
    /// Cached because <see cref="Current"/> is asked for it far more often than the retainers move -
    /// every ownership question in the plugin goes through that property - and a retainer with seven
    /// full pages is a few hundred ids to merge.
    /// </remarks>
    private HashSet<uint>? retainerItems;

    /// <summary>The same for what they are wearing, which is its own answer and its own setting.</summary>
    private HashSet<uint>? retainerWornItems;

    public DateTime? DresserUpdatedUtc { get; private set; }
    public DateTime? ArmoireUpdatedUtc { get; private set; }
    public int DresserSlotsUsed { get; private set; }
    public int DresserSlotCapacity { get; private set; }
    public string CharacterName { get; private set; } = string.Empty;

    /// <summary>Bumped whenever the underlying sets change, so callers can invalidate derived state.</summary>
    public int Revision { get; private set; }

    public OwnershipTracker(string cacheDirectory, Configuration configuration)
    {
        this.cacheDirectory = cacheDirectory;
        this.configuration = configuration;
        Directory.CreateDirectory(cacheDirectory);
    }

    public bool HasDresserData => DresserUpdatedUtc.HasValue;

    public bool IsDresserStale =>
        !DresserUpdatedUtc.HasValue ||
        DateTime.UtcNow - DresserUpdatedUtc.Value > TimeSpan.FromDays(configuration.StaleAfterDays);

    /// <summary>
    /// Every retainer read, longest unread first.
    /// </summary>
    /// <remarks>
    /// Per retainer rather than one age for the lot, because there is no honest way to collapse them:
    /// visiting one retainer says nothing about the other nine, so a single figure would either be the
    /// oldest - alarming about data that is fine - or the newest, which lets one trip to a bell make a
    /// year-old snapshot look current.
    /// </remarks>
    public IReadOnlyList<RetainerHolding> Retainers =>
    [
        .. retainers
            .OrderBy(entry => entry.Value.UpdatedUtc)
            .Select(entry => new RetainerHolding(
                entry.Key,
                entry.Value.Name,
                entry.Value.UpdatedUtc,
                entry.Value.Items,
                entry.Value.Equipped)),
    ];

    public OwnershipView Current => new(
        dresser?.DirectItems ?? OwnershipView.Empty.DresserDirect,
        dresser?.ItemsInStoredOutfits ?? OwnershipView.Empty.DresserOutfits,
        armoire ?? OwnershipView.Empty.Armoire,
        configuration.CountInventoryAndEquipped ? inventory : null,
        dresser?.StoredOutfits ?? OwnershipView.Empty.StoredOutfits,
        dresser == null ? null : new DresserSpace(DresserSlotsUsed, DresserSlotCapacity),
        RetainerItems,
        RetainerWornItems);

    /// <summary>Null rather than empty when retainers are switched off, so "off" and "empty" differ.</summary>
    private IReadOnlySet<uint>? RetainerItems
    {
        get
        {
            if (!configuration.CountRetainers || retainers.Count == 0)
                return null;

            return retainerItems ??= [.. retainers.Values.SelectMany(entry => entry.Items)];
        }
    }

    /// <summary>
    /// What the retainers are wearing, and only if that has been asked for.
    /// </summary>
    /// <remarks>
    /// Behind the retainer setting as well as its own, because it is a narrowing of that one rather than
    /// a separate place: counting what a retainer wears while not counting what a retainer holds would
    /// be a combination nobody means.
    /// </remarks>
    private IReadOnlySet<uint>? RetainerWornItems
    {
        get
        {
            if (!configuration.CountRetainers || !configuration.CountRetainerEquipped || retainers.Count == 0)
                return null;

            return retainerWornItems ??= [.. retainers.Values.SelectMany(entry => entry.Equipped)];
        }
    }

    /// <summary>Makes the next <see cref="Update"/> re-read everything immediately.</summary>
    public void RequestRefresh() => nextPoll = DateTime.MinValue;

    /// <summary>
    /// Forgets both merged retainer sets, so the next reader rebuilds them.
    /// </summary>
    /// <remarks>
    /// Always both. They are built from the same entries, so anything that moves one has moved the
    /// other, and dropping one of the two is the bug this exists to make impossible.
    /// </remarks>
    private void DropRetainerUnions()
    {
        retainerItems = null;
        retainerWornItems = null;
    }

    /// <summary>
    /// Throws away everything cached about this character's collection, file and all.
    /// </summary>
    /// <remarks>
    /// A refresh is requested on the way out, so whatever the client happens to have loaded right now
    /// comes straight back. That is the point rather than a wrinkle: this exists for a snapshot that
    /// has gone wrong, and the useful outcome is being left with only what can be seen to be true.
    /// </remarks>
    public void Forget()
    {
        var path = CachePath;

        ClearInMemory();
        Data.JsonStore.Delete(path);
        Plugin.Log.Information("Forgot the cached collection for this character");

        RequestRefresh();
    }

    /// <summary>Throws away the retainer snapshots and keeps the dresser and armoire.</summary>
    public void ForgetRetainers()
    {
        if (retainers.Count == 0)
            return;

        retainers.Clear();
        DropRetainerUnions();
        Revision++;
        SaveToDisk();
    }

    /// <summary>
    /// Throws away one retainer's snapshot.
    /// </summary>
    /// <remarks>
    /// Worth having as well as the section-wide reset, because the case that needs it is one retainer:
    /// dismiss one in game and nothing ever comes back to say so, leaving a name in the list and its
    /// contents counted as owned forever.
    /// </remarks>
    public void ForgetRetainer(ulong retainerId)
    {
        if (!retainers.Remove(retainerId))
            return;

        DropRetainerUnions();
        Revision++;
        SaveToDisk();
    }

    public void Update()
    {
        if (DateTime.UtcNow < nextPoll)
            return;

        nextPoll = DateTime.UtcNow + PollInterval;

        var playerState = Plugin.PlayerState;
        if (!playerState.IsLoaded || playerState.ContentId == 0)
            return;

        if (playerState.ContentId != loadedContentId)
        {
            loadedContentId = playerState.ContentId;
            CharacterName = playerState.CharacterName;
            LoadFromDisk();
        }

        // Both stores stay loaded until the next zone change, so from the moment one is opened it is
        // re-read on every poll. Reading is cheap; treating each read as news is not - it rewrote the
        // cache file and invalidated every derived list once a second. The timestamps move regardless,
        // because those record when the store was last looked at, but only a change earns a save.
        var dirty = false;

        var freshDresser = DresserReader.TryRead();
        if (freshDresser != null)
        {
            dirty |= freshDresser.Fingerprint != dresser?.Fingerprint;
            // Not in the fingerprint, which covers the box's contents rather than its size. Compared
            // separately so a cache written before capacity was recorded picks it up on the first
            // live read instead of waiting for the contents to happen to change.
            dirty |= freshDresser.SlotCapacity != DresserSlotCapacity;
            dresser = freshDresser;
            DresserSlotsUsed = freshDresser.SlotsUsed;
            DresserSlotCapacity = freshDresser.SlotCapacity;
            DresserUpdatedUtc = DateTime.UtcNow;
        }

        var freshArmoire = ArmoireReader.TryRead();
        if (freshArmoire != null)
        {
            dirty |= armoire == null || !freshArmoire.SetEquals(armoire);
            armoire = freshArmoire;
            ArmoireUpdatedUtc = DateTime.UtcNow;
        }

        // One retainer at a time - whichever is open - so this adds an entry rather than replacing the
        // set. An unvisited retainer keeps whatever was last read from them.
        var freshRetainer = RetainerReader.TryRead();
        if (freshRetainer != null)
        {
            var known = retainers.GetValueOrDefault(freshRetainer.RetainerId);
            if (known == null || known.Fingerprint != freshRetainer.Fingerprint)
            {
                retainers[freshRetainer.RetainerId] = new StoredRetainer
                {
                    Name = freshRetainer.Name,
                    Items = freshRetainer.Items,
                    Equipped = freshRetainer.Equipped,
                    Fingerprint = freshRetainer.Fingerprint,
                    UpdatedUtc = DateTime.UtcNow,
                };

                DropRetainerUnions();
                dirty = true;
            }
            else
            {
                // Unchanged contents still move the timestamp: it records when the bags were last
                // looked at, not when they last moved. A rename lands here too, and is not worth a save
                // of its own.
                known.Name = freshRetainer.Name;
                known.UpdatedUtc = DateTime.UtcNow;
            }
        }

        // Always available for the current character, so never cached.
        var freshInventory = configuration.CountInventoryAndEquipped ? InventoryReader.Read() : [];
        if (!freshInventory.SetEquals(inventory))
        {
            inventory = freshInventory;
            Revision++;
        }

        if (!dirty)
            return;

        Revision++;
        SaveToDisk();
    }

    private string CachePath => Path.Combine(cacheDirectory, $"ownership-{loadedContentId}.json");

    /// <summary>
    /// Drops every cached store, leaving the tracker as it was before this character was seen.
    /// </summary>
    /// <remarks>
    /// Shared by loading a different character and by the reset in Settings. The inventory is not
    /// touched: it is read live every poll and belongs to whoever is logged in, so clearing it would
    /// only put a hole in the next second's answer.
    /// </remarks>
    private void ClearInMemory()
    {
        dresser = null;
        armoire = null;
        retainers.Clear();
        DropRetainerUnions();
        DresserUpdatedUtc = null;
        ArmoireUpdatedUtc = null;
        DresserSlotsUsed = 0;
        DresserSlotCapacity = 0;
        Revision++;
    }

    private void LoadFromDisk()
    {
        ClearInMemory();

        var dto = Data.JsonStore.Read<CacheFile>(CachePath);
        if (dto == null)
            return;

        // Zero means the cache predates the field rather than meaning an empty box.
        var capacity = dto.DresserSlotCapacity > 0 ? dto.DresserSlotCapacity : DresserReader.AssumedCapacity;

        dresser = new DresserSnapshot
            {
                DirectItems = [.. dto.DresserDirect],
                ItemsInStoredOutfits = dto.DresserOutfits.ToDictionary(kv => kv.Key, kv => new HashSet<uint>(kv.Value)),
                // Older caches predate this field; the union of the per-piece sets is a faithful
                // reconstruction for everything except a set stored with every slot empty.
                StoredOutfits = dto.StoredOutfits.Count > 0
                    ? [.. dto.StoredOutfits]
                    : [.. dto.DresserOutfits.Values.SelectMany(v => v)],
                SlotsUsed = dto.DresserSlotsUsed,
                SlotCapacity = capacity,
            };
        armoire = dto.ArmoireUpdatedUtc.HasValue ? [.. dto.Armoire] : null;

        // No fingerprint is carried over, so the first live read of each retainer saves - the same
        // rule the dresser snapshot follows.
        var predatingTheSplit = 0;
        foreach (var entry in dto.Retainers)
        {
            // An entry written before the bags and the worn gear were told apart has them in one list
            // and no way to separate them again. Dropped rather than loaded, because loading it means
            // going on claiming a piece is in bags it is not in - which is the whole reason for the
            // split. The cost is one visit to that retainer.
            if (!entry.EquippedSplitOut)
            {
                predatingTheSplit++;
                continue;
            }

            retainers[entry.RetainerId] = new StoredRetainer
            {
                Name = entry.Name,
                Items = [.. entry.Items],
                Equipped = [.. entry.Equipped],
                UpdatedUtc = entry.UpdatedUtc,
            };
        }

        if (predatingTheSplit > 0)
        {
            Plugin.Log.Information(
                $"Discarded {predatingTheSplit} retainer snapshots that predate telling bags and worn " +
                "gear apart; visit those retainers to record them again");
        }

        DresserSlotsUsed = dto.DresserSlotsUsed;
        DresserSlotCapacity = capacity;
        DresserUpdatedUtc = dto.DresserUpdatedUtc;
        ArmoireUpdatedUtc = dto.ArmoireUpdatedUtc;

        if (!DresserUpdatedUtc.HasValue)
            dresser = null;

        Plugin.Log.Information(
            $"Loaded collection cache for {dto.CharacterName}: {dto.DresserDirect.Count} dresser items, " +
            $"{dto.DresserOutfits.Count} outfit pieces, {dto.Armoire.Count} armoire items, " +
            $"{dto.Retainers.Count} retainers");
    }

    private void SaveToDisk()
    {
        var dto = new CacheFile
        {
            ContentId = loadedContentId,
            CharacterName = CharacterName,
            DresserDirect = dresser?.DirectItems.ToList() ?? [],
            DresserOutfits = dresser?.ItemsInStoredOutfits.ToDictionary(kv => kv.Key, kv => kv.Value.ToList()) ?? [],
            StoredOutfits = dresser?.StoredOutfits.ToList() ?? [],
            DresserSlotsUsed = DresserSlotsUsed,
            DresserSlotCapacity = DresserSlotCapacity,
            Armoire = armoire?.ToList() ?? [],
            DresserUpdatedUtc = DresserUpdatedUtc,
            ArmoireUpdatedUtc = ArmoireUpdatedUtc,
            Retainers =
            [
                .. retainers.Select(entry => new RetainerCacheEntry
                {
                    RetainerId = entry.Key,
                    Name = entry.Value.Name,
                    Items = entry.Value.Items.Order().ToList(),
                    Equipped = entry.Value.Equipped.Order().ToList(),
                    EquippedSplitOut = true,
                    UpdatedUtc = entry.Value.UpdatedUtc,
                }),
            ],
        };

        Data.JsonStore.Write(CachePath, dto);
    }

    /// <summary>One retainer as held in memory. Mutable, so a re-read need not replace the entry.</summary>
    private sealed class StoredRetainer
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Never added to in place: a changed retainer gets a whole new entry.
        /// </summary>
        /// <remarks>
        /// This set is handed out through <see cref="OwnershipTracker.Retainers"/> rather than copied,
        /// so mutating it would be changing something a caller is already looking at.
        /// </remarks>
        public HashSet<uint> Items { get; init; } = [];

        /// <summary>What the retainer has on, never merged into <see cref="Items"/>.</summary>
        public HashSet<uint> Equipped { get; init; } = [];

        public ulong Fingerprint { get; init; }

        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class RetainerCacheEntry
    {
        public ulong RetainerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<uint> Items { get; set; } = [];
        public List<uint> Equipped { get; set; } = [];

        /// <summary>
        /// Whether <see cref="Items"/> really is only the bags.
        /// </summary>
        /// <remarks>
        /// False on any entry written before the two were told apart, which is not something an empty
        /// <see cref="Equipped"/> could distinguish - a retainer wearing nothing looks the same. Absent
        /// from an old file, so it reads back false, which is exactly right.
        /// </remarks>
        public bool EquippedSplitOut { get; set; }

        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class CacheFile
    {
        public ulong ContentId { get; set; }
        public string CharacterName { get; set; } = string.Empty;
        public List<uint> DresserDirect { get; set; } = [];
        public Dictionary<uint, List<uint>> DresserOutfits { get; set; } = [];
        public List<uint> StoredOutfits { get; set; } = [];
        public int DresserSlotsUsed { get; set; }
        public int DresserSlotCapacity { get; set; }
        public List<uint> Armoire { get; set; } = [];
        public DateTime? DresserUpdatedUtc { get; set; }
        public DateTime? ArmoireUpdatedUtc { get; set; }
        public List<RetainerCacheEntry> Retainers { get; set; } = [];
    }
}
