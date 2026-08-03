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

/// <summary>An immutable view of what one character owns, safe to hand to pure query code.</summary>
public sealed record OwnershipView(
    IReadOnlySet<uint> DresserDirect,
    IReadOnlyDictionary<uint, HashSet<uint>> DresserOutfits,
    IReadOnlySet<uint> Armoire,
    IReadOnlySet<uint>? Inventory,
    IReadOnlySet<uint> StoredOutfits,
    // Defaulted so the existing construction sites keep compiling; null means no dresser data,
    // matching how Inventory says "not counted".
    DresserSpace? Space = null)
{
    public static readonly OwnershipView Empty = new(
        new HashSet<uint>(), new Dictionary<uint, HashSet<uint>>(), new HashSet<uint>(), null, new HashSet<uint>());
}

/// <summary>
/// Keeps a per-character record of the Glamour Dresser and Armoire on disk.
/// </summary>
/// <remarks>
/// This cache is not an optimisation, it is the only way the feature can work: the client wipes
/// dresser data on every zone change and never loads the armoire unless you open it, so by the time
/// you are standing in a dungeon there is nothing live to read. We snapshot whenever the data
/// happens to be loaded and report how old the snapshot is.
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

    public OwnershipView Current => new(
        dresser?.DirectItems ?? OwnershipView.Empty.DresserDirect,
        dresser?.ItemsInStoredOutfits ?? OwnershipView.Empty.DresserOutfits,
        armoire ?? OwnershipView.Empty.Armoire,
        configuration.CountInventoryAndEquipped ? inventory : null,
        dresser?.StoredOutfits ?? OwnershipView.Empty.StoredOutfits,
        dresser == null ? null : new DresserSpace(DresserSlotsUsed, DresserSlotCapacity));

    /// <summary>Makes the next <see cref="Update"/> re-read everything immediately.</summary>
    public void RequestRefresh() => nextPoll = DateTime.MinValue;

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

        // Both stores stay loaded until you zone, so from the moment you open one it is re-read on
        // every poll. Reading is cheap; treating each read as news is not - it rewrote the cache
        // file and invalidated every derived list once a second. The timestamps move regardless,
        // because those record when we last looked, but only a change earns a save.
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

    private void LoadFromDisk()
    {
        dresser = null;
        armoire = null;
        DresserUpdatedUtc = null;
        ArmoireUpdatedUtc = null;
        DresserSlotsUsed = 0;
        DresserSlotCapacity = 0;
        Revision++;

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
        DresserSlotsUsed = dto.DresserSlotsUsed;
        DresserSlotCapacity = capacity;
        DresserUpdatedUtc = dto.DresserUpdatedUtc;
        ArmoireUpdatedUtc = dto.ArmoireUpdatedUtc;

        if (!DresserUpdatedUtc.HasValue)
            dresser = null;

        Plugin.Log.Information(
            $"Loaded collection cache for {dto.CharacterName}: {dto.DresserDirect.Count} dresser items, " +
            $"{dto.DresserOutfits.Count} outfit pieces, {dto.Armoire.Count} armoire items");
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
        };

        Data.JsonStore.Write(CachePath, dto);
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
    }
}
