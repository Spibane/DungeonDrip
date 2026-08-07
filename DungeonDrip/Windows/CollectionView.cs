using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using DungeonDrip.Core;
using DungeonDrip.Presentation;

namespace DungeonDrip.Windows;

/// <summary>
/// The main window's other half: the collection as a whole, with no duty involved.
/// </summary>
/// <remarks>
/// Composed into <see cref="MissingItemsWindow"/> rather than being a window of its own. That one is
/// already long and its spine is entirely duty-shaped - pinning, auto-open, a title taken from the
/// report - so this keeps the two apart without adding a second thing to open and find.
///
/// Every tab here sweeps the whole collection, which is far too much to do per frame, so none of it
/// is computed here at all: <see cref="CollectionSnapshot"/> holds the reports and rebuilds them
/// only when something behind them has moved. It builds every tab rather than only the visible one,
/// so switching tabs costs nothing and the counts on the strip cannot go stale behind it.
/// </remarks>
public sealed class CollectionView(Plugin plugin)
{
    /// <summary>
    /// Prefix on the remembered heading state, so nothing here can collide with a slot or role
    /// heading from the duty list, which share the same store.
    ///
    /// Only the collapsibles inside a tab use this now. The four top-level sections that used to be
    /// headings are tabs, so their old keys linger in the config doing nothing - harmless, and not worth
    /// a migration to sweep up.
    /// </summary>
    private const string CollapsePrefix = "collection:";

    private readonly CollectionSnapshot snapshot = new(plugin);
    private readonly HeaderState headers = new(plugin.Configuration);

    /// <summary>Above this share of the box, the count is worth colouring.</summary>
    private const float CrowdedAt = 0.9f;

    /// <summary>Sets listed before the "show all" toggle earns its place.</summary>
    private const int InitialSetsShown = 25;

    /// <summary>
    /// Pieces listed per currency before the same toggle earns its place.
    /// </summary>
    /// <remarks>
    /// Lower than the set cap because there can be a dozen currency groups where there is one set list,
    /// and because Wolf Marks alone buy 779 pieces. The plugin has no list clipper anywhere, so a cap
    /// with a toggle is the established answer to a long list rather than a new mechanism.
    /// </remarks>
    private const int InitialOffersShown = 10;

    /// <summary>
    /// Per-session, not a setting. The config file is documentation of what the user chose, and
    /// "I expanded a list once" is not a choice worth writing down.
    /// </summary>
    private bool showAllSets;

    /// <summary>Which currency groups have been expanded past the cap, per session and for the same reason.</summary>
    private readonly HashSet<uint> showAllOffers = [];

    /// <summary>
    /// Whether the stored tab has been forced open yet.
    /// </summary>
    /// <remarks>
    /// Per instance rather than saved, because it is about this window's lifetime and not the user's
    /// choice. False again after a plugin reload, which is exactly when the stored tab needs restoring;
    /// within a session ImGui keeps the selection itself.
    /// </remarks>
    private bool tabRestored;

    /// <summary>
    /// Rebuilds anything stale, then draws the three tabs.
    /// </summary>
    /// <remarks>
    /// Tabs rather than the stack of collapsing headers this used to be. Four headers one under another,
    /// each with its own collapsibles inside it, made every list look like a row in the list above it -
    /// there was no reading of the window that told a section apart from a group within one. A tab strip
    /// says outright that these are three separate questions.
    ///
    /// The order is what each one costs to act on: an outfit part way done is a duty to run, gear a held
    /// currency covers is a walk to a counter, and the dresser is housekeeping.
    ///
    /// <b>The dresser tab holds two lists on purpose.</b> How full the box is and which spare copies
    /// could go are the same errand from both ends - the first says the box is filling and what would
    /// free space, the second is the gear that could actually be got rid of. Splitting them would put a
    /// problem on one tab and its lever on another.
    /// </remarks>
    public void Draw()
    {
        snapshot.Refresh();

        using var tabs = ImRaii.TabBar("##collectionTabs");
        if (!tabs.Success)
            return;

        // Restoring is a one-shot, and has to be: the flag that selects a tab wins over a click, so
        // asking for it every frame would pin the stored tab open and make the strip unusable.
        var restoring = !tabRestored;

        // Read before any tab is drawn, and not from the configuration inside the loop. On the restoring
        // frame ImGui reports the first tab active until the stored one is submitted with its flag, so a
        // tab that recorded itself as it went would overwrite the target before reaching it.
        var target = plugin.Configuration.CollectionTab;

        Tab("Sets in progress", CollectionTab.Sets, DrawSetsInProgress, restoring, target);
        Tab("Ready to buy", CollectionTab.Buy, DrawShoppingList, restoring, target);
        Tab("Glamour Dresser", CollectionTab.Dresser, DrawDresser, restoring, target);

        tabRestored = true;
    }

    /// <summary>
    /// One tab, its body in a region that scrolls by itself.
    /// </summary>
    /// <remarks>
    /// The child is what keeps the tab strip in place. Letting the window scroll instead would carry the
    /// strip off the top edge, so reaching another tab would mean scrolling up first - the same reason
    /// the settings window wraps its tabs this way.
    ///
    /// The selection is written to the configuration on the frame it changes rather than polled, and only
    /// when it actually changed, so an unchanged tab costs no save.
    /// </remarks>
    /// <param name="restoring">
    /// True only on the first frame the strip is drawn, when the stored tab is forced open.
    /// <see cref="ImGuiTabItemFlags.SetSelected"/> overrides a click, so passing it on later frames would
    /// undo every attempt to switch away.
    /// </param>
    /// <param name="target">
    /// The stored tab as it was before any tab was drawn. Nothing is recorded while
    /// <paramref name="restoring"/> is true, since the selection ImGui reports that frame is not yet the
    /// one being restored to.
    /// </param>
    private void Tab(
        string label, CollectionTab tab, System.Action body, bool restoring, CollectionTab target)
    {
        var flags = restoring && target == tab
            ? ImGuiTabItemFlags.SetSelected
            : ImGuiTabItemFlags.None;

        using var item = ImRaii.TabItem(label, flags);
        if (!item.Success)
            return;

        if (!restoring && plugin.Configuration.CollectionTab != tab)
        {
            plugin.Configuration.CollectionTab = tab;
            plugin.Configuration.Save();
        }

        using var child = ImRaii.Child($"collection{tab}", Vector2.Zero, false);
        if (!child.Success)
            return;

        body();
    }

    /// <summary>
    /// The dresser tab: how full the box is, then the spare copies that could empty some of it.
    /// </summary>
    /// <remarks>
    /// The second list keeps its own heading and its rule, because the two answer different questions and
    /// ran under separate headings before this became a tab. Losing the heading would leave a reader no
    /// way to see where the occupancy figures stop and the disposal list starts.
    /// </remarks>
    private void DrawDresser()
    {
        DrawDresserPressure();

        Rule();
        ImGui.TextColored(Palette.For(ColourRole.Muted), "Already in your collection");
        ImGui.Spacing();

        DrawAlreadyStored();
    }

    /// <summary>A rule with room around it, for dividing two lists inside one tab.</summary>
    private static void Rule()
    {
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>
    /// Outfit sets part way through, closest to done first.
    /// </summary>
    /// <remarks>
    /// One count per row. Reporting the dresser copy's filled slots beside it was tried and read as
    /// an error rather than as extra information - the two are out of different totals and only one
    /// of them obeys the storage scope, so they disagreed for correct reasons that no row has space
    /// to explain.
    /// </remarks>
    /// <summary>
    /// What each currency held will buy that is not collected yet, most spendable currency first.
    /// </summary>
    /// <remarks>
    /// One group per currency actually in the Currency tab - see <see cref="Game.CurrencyReader"/> on why
    /// the tab is the definition rather than a category filter. A currency held at zero is therefore
    /// absent, which is the accepted limit of the arrangement: this answers what can be spent, not what
    /// farming something would eventually be worth.
    ///
    /// Gil starts collapsed. It prices roughly five thousand storable pieces, more than every other
    /// currency together, so an open gil group would be the section as far as anyone scrolled.
    /// </remarks>
    private void DrawShoppingList()
    {
        if (!plugin.Configuration.ShowAcquisitionSources)
        {
            // Named as the setting reads, since that is what has to be found and switched on.
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "Needs \"Say how gear is obtained besides dropping\", under Settings - Data.");
            return;
        }

        if (snapshot.Shopping == null)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted), "Reading what the shops sell...");
            return;
        }

        DrawOutfitsOnlyToggle();
        DrawSnapshotCaveats();

        if (snapshot.Shopping.IsEmpty)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted), plugin.Configuration.ReadyToBuyOutfitsOnly
                ? "Nothing your currencies buy towards an outfit set is still missing."
                : "Nothing your currencies buy is still missing - or there is nothing in the Currency tab yet.");
            return;
        }

        foreach (var group in snapshot.Shopping.Groups)
            DrawCurrencyGroup(group);
    }

    /// <summary>
    /// The section's own view filter, on the section rather than only in Settings.
    /// </summary>
    /// <remarks>
    /// Here because it is a filter on one list and gets flipped while reading it, the same argument as
    /// the duty window's toolbar toggles - a trip to Settings to narrow the list in front of you is a
    /// trip nobody makes twice. It still writes the setting, so it survives a restart.
    ///
    /// Saved here rather than reported upward, because this view has no changed-flag to report through:
    /// the remembered headings already save themselves the same way.
    /// </remarks>
    private void DrawOutfitsOnlyToggle()
    {
        var outfitsOnly = plugin.Configuration.ReadyToBuyOutfitsOnly;
        if (UiParts.Toggle("Outfit sets only", ref outfitsOnly,
                "Hides pieces that are not part of an outfit set.\n\n" +
                "Most priced gear is single pieces - only about one in seven belongs to a set, and for " +
                "gil it is fewer than one in ten - so this is the switch for collecting whole outfits.\n" +
                "Hunting one particular piece is quicker through /dungeondrip item <name>."))
        {
            plugin.Configuration.ReadyToBuyOutfitsOnly = outfitsOnly;
            plugin.Configuration.Save();
        }
    }

    /// <summary>
    /// Says when the collection behind this list cannot be trusted, before the list rather than after.
    /// </summary>
    /// <remarks>
    /// <b>Every row here is a "not owned" claim, which is the half of a snapshot that goes wrong.</b> The
    /// plugin says so elsewhere and ambers a red x rather than a grey tick for exactly this reason:
    /// glamours are added far more often than removed, so an old snapshot's "owned" almost always still
    /// holds while its "not owned" is what has drifted. This section is nothing but that claim, so it
    /// carries the caveat above the list where the other sections carry theirs below.
    ///
    /// The Armoire line earns its place on a real report: a piece sitting in the Armoire was offered for
    /// sale because the cached read predated it, with a timestamp recent enough to look trustworthy.
    /// Opening the Armoire fixed it, which is what this line asks for.
    /// </remarks>
    private void DrawSnapshotCaveats()
    {
        var tracker = plugin.Ownership;

        if (!tracker.HasDresserData)
        {
            ImGui.TextColored(Palette.For(ColourRole.Warning),
                "No Glamour Dresser data yet - open one once, or everything here is listed as missing.");
        }
        else if (tracker.IsDresserStale)
        {
            ImGui.TextColored(Palette.For(ColourRole.Warning),
                $"Dresser snapshot {Format.Age(tracker.DresserUpdatedUtc!.Value)} old - " +
                "anything collected since is still listed.");
        }

        // Its own line rather than folded into the dresser's, because the Armoire is read on a different
        // occasion and can be the stale one on its own.
        if (tracker.ArmoireUpdatedUtc == null)
        {
            ImGui.TextColored(Palette.For(ColourRole.Warning),
                "Armoire never read - anything stored there is still listed. Open it once.");
        }
        else if (plugin.Configuration.Scope != CollectionScope.DresserOnly)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                $"Armoire read {Format.Age(tracker.ArmoireUpdatedUtc.Value)} ago. " +
                "Open it again if something here is already in it.");
        }
    }

    private void DrawCurrencyGroup(CurrencyGroup group)
    {
        // The count belongs on the label and must stay off the key, or spending a currency renames the
        // heading and loses whether it was open.
        var affordable = group.Affordable > 0
            ? $"{group.Affordable} affordable"
            : $"{group.Pieces.Count} to save for";

        var label = $"{group.Name} - {group.Balance:N0}  ({affordable})";
        var key = CollapsePrefix + "currency:" + group.CurrencyItemId;

        var gil = group.CurrencyItemId == Game.CurrencyReader.GilItemId;
        // Uncoloured. The heading used to go blue when something was affordable, which was a third copy
        // of what the label already says in words and what the rows already say on the price. Tinting a
        // heading now reads as "this is a section", which the tabs say instead.
        if (!UiParts.CollapsingHeader(headers, label, key, defaultOpen: !gil))
            return;

        var all = showAllOffers.Contains(group.CurrencyItemId);
        var shown = all ? group.Pieces.Count : System.Math.Min(group.Pieces.Count, InitialOffersShown);

        for (var i = 0; i < shown; i++)
            DrawOffer(group.Pieces[i]);

        if (group.Pieces.Count > InitialOffersShown)
        {
            ImGui.Spacing();
            if (ImGui.SmallButton(all
                    ? $"Show the cheapest {InitialOffersShown}###offers{group.CurrencyItemId}"
                    : $"Show all {group.Pieces.Count}###offers{group.CurrencyItemId}"))
            {
                if (!showAllOffers.Remove(group.CurrencyItemId))
                    showAllOffers.Add(group.CurrencyItemId);
            }
        }

        ImGui.Spacing();
    }

    /// <remarks>
    /// The same row as <see cref="DrawMissingPiece"/> - icon, name, context menu, one trailing muted
    /// fact - so a piece looks the same wherever this view lists it. Only the cost is coloured, and only
    /// when the balance covers it, so the eye lands on what can be bought now.
    /// </remarks>
    private void DrawOffer(ShoppingPiece piece)
    {
        UiParts.ItemIcon(piece.IconId, 18);
        ImGui.Text(piece.Name);

        UiParts.ItemContextMenu(plugin, piece.ItemId, piece.Name);

        ImGui.SameLine();
        ImGui.TextColored(
            piece.Affordable ? Palette.For(ColourRole.Focus) : Palette.For(ColourRole.Muted), $"- {piece.Cost:N0}");
    }

    private void DrawSetsInProgress()
    {
        if (!plugin.Ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "No Glamour Dresser data yet - open a dresser once so this can be answered.");
            return;
        }

        if (snapshot.Sets.Count == 0)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "No outfit set is part way done - every set is either finished or untouched.");
            return;
        }

        var shown = showAllSets ? snapshot.Sets.Count : System.Math.Min(snapshot.Sets.Count, InitialSetsShown);

        for (var i = 0; i < shown; i++)
            DrawSetRow(snapshot.Sets[i]);

        if (snapshot.Sets.Count <= InitialSetsShown)
            return;

        ImGui.Spacing();
        if (ImGui.SmallButton(showAllSets
                ? $"Show the closest {InitialSetsShown}###setsAll"
                : $"Show all {snapshot.Sets.Count}###setsAll"))
        {
            showAllSets = !showAllSets;
        }
    }

    private void DrawSetRow(SetStanding standing)
    {
        UiParts.ItemIcon(standing.IconId, 20);

        var open = ImGui.TreeNodeEx(
            $"{standing.Name}###set{standing.SetId}", ImGuiTreeNodeFlags.SpanAvailWidth);

        ImGui.SameLine();
        ImGui.TextColored(Palette.For(ColourRole.Muted), $"{standing.Owned} of {standing.Total}");

        if (!open)
            return;

        foreach (var piece in standing.Missing)
            DrawMissingPiece(piece);

        ImGui.TreePop();
    }

    private void DrawMissingPiece(SetPieceState piece)
    {
        UiParts.ItemIcon(piece.IconId, 18);
        ImGui.Text(piece.Name);

        UiParts.ItemContextMenu(plugin, piece.ItemId, piece.Name);

        // One line, so one route: the duty if there is one, otherwise the best non-duty route. The
        // duty wins because this view is about finishing a set and a duty can be queued for from here,
        // where a vendor cannot. Both being absent leaves the row bare, which is the honest state -
        // see the drop index and the source index on why neither can say "nowhere".
        var drops = plugin.Drops?.For(piece.ItemId);
        if (drops is { Count: > 0 })
        {
            var best = drops[0];
            ImGui.SameLine();
            ImGui.TextColored(Palette.For(ColourRole.Muted), best.Level > 0
                ? $"- {best.DutyName} (Lv. {best.Level})"
                : $"- {best.DutyName}");
            return;
        }

        var acquisitions = plugin.SourcesFor(piece.ItemId);
        if (acquisitions is not { Count: > 0 })
            return;

        ImGui.SameLine();
        ImGui.TextColored(Palette.For(ColourRole.Muted), $"- {acquisitions[0].Describe()}");
    }

    private void DrawDresserPressure()
    {
        if (snapshot.Pressure is not { HasData: true })
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted), "No dresser snapshot to measure.");
            return;
        }

        DrawOccupancy(snapshot.Pressure);

        if (snapshot.Pressure.Reclaimable > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.For(ColourRole.Focus), $"Up to {snapshot.Pressure.Reclaimable} slots could be freed:");
        }

        DrawCollapsible(snapshot.Pressure);
        DrawArmoireLevers(snapshot.Pressure);
    }

    /// <summary>
    /// How full the box is, with a denominator only when there is an honest one to give.
    /// </summary>
    /// <remarks>
    /// The capacity read off the client is the box's structural size, which is not the same as how
    /// many slots a given character has actually unlocked, and the client offers no way to tell. So
    /// a bare count is the claim that can always be stood behind; the "of N" is worded as the most
    /// the box can hold rather than as the room left.
    ///
    /// A stale snapshot only ever undercounts, because a dresser is added to far more often than it is
    /// taken from, so the number is prefixed rather than suppressed.
    /// </remarks>
    private void DrawOccupancy(DresserPressureReport report)
    {
        var stale = plugin.Ownership.IsDresserStale;
        var prefix = stale ? "At least " : string.Empty;
        var crowded = report.Capacity > 0 && report.Used >= report.Capacity * CrowdedAt;

        if (report.Capacity > 0)
        {
            ImGui.TextColored(
                crowded ? Palette.For(ColourRole.Warning) : Palette.For(ColourRole.Muted),
                $"{prefix}{report.Used} slots used, of the {report.Capacity} the box holds.");

            ImGui.ProgressBar(
                (float)report.Used / report.Capacity,
                new Vector2(-1, 6 * ImGuiHelpers.GlobalScale),
                string.Empty);
        }
        else
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted), $"{prefix}{report.Used} slots used.");
        }
    }

    private void DrawCollapsible(DresserPressureReport report)
    {
        if (report.Collapsible.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.TextColored(Palette.For(ColourRole.Muted), report.Collapsible.Count == 1
            ? "1 outfit is stored a piece at a time:"
            : $"{report.Collapsible.Count} outfits are stored a piece at a time:");

        foreach (var set in report.Collapsible)
        {
            // Conditional on purpose. Storing a set needs the outfit item - a tradeable attire box -
            // and owning all eleven pieces does not produce one; plenty of sets have no such item
            // at all. Telling someone to do something they may have no way to do is worse than
            // telling them what it would be worth if they could.
            var line = set.HoldingOutfitItem
                ? $"   {set.Name} - you have the outfit item; storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots"
                : $"   {set.Name} - if you have the outfit item, storing it instead of these " +
                  $"{set.Pieces.Count} pieces frees {set.SlotsReclaimed} slots";

            ImGui.TextColored(set.HoldingOutfitItem ? Palette.For(ColourRole.Good) : Palette.For(ColourRole.Muted), line);
        }

        ImGui.TextColored(Palette.For(ColourRole.Muted),
            "   The loose copies come back to your bags, so the space appears once you clear them.");
    }

    private void DrawArmoireLevers(DresserPressureReport report)
    {
        if (!report.ArmoireKnown)
        {
            ImGui.Spacing();
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "Open your Armoire once and this can also say what it would take off your hands.");
            return;
        }

        DrawResidents(
            report.DuplicateInArmoire,
            "In your Armoire as well as the dresser",
            "The dresser copy is doing nothing the Armoire is not already doing.",
            Palette.For(ColourRole.Focus));

        DrawResidents(
            report.ArmoireWouldTake,
            "The Armoire would take these, at no dresser slot",
            "Each has to be taken out of the dresser and deposited, so this one is work.",
            Palette.For(ColourRole.Muted));
    }

    /// <summary>
    /// Names the pieces rather than only counting them.
    /// </summary>
    /// <remarks>
    /// "3 pieces are duplicated" is not something anybody can act on without being told which
    /// three. Collapsed by default because these lists get long on a full dresser, and the count
    /// on the heading is the part worth seeing at a glance.
    /// </remarks>
    private void DrawResidents(
        IReadOnlyList<DresserResident> residents, string heading, string note, Vector4 colour)
    {
        if (residents.Count == 0)
            return;

        ImGui.Spacing();

        if (!ImGui.TreeNodeEx($"{heading} ({residents.Count})###{heading}"))
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted), $"   {note}");
            return;
        }

        ImGui.TextColored(Palette.For(ColourRole.Muted), note);

        foreach (var resident in residents)
        {
            UiParts.ItemIcon(resident.IconId, 18);
            ImGui.TextColored(colour, resident.Name);
            UiParts.ItemContextMenu(plugin, resident.ItemId, resident.Name);
        }

        ImGui.TreePop();
    }

    /// <summary>
    /// Merely held gear that the collection already holds.
    /// </summary>
    /// <remarks>
    /// The heading is "already in your collection" rather than "safe to discard", and that is the
    /// whole design of the section. It reports a fact the plugin can stand behind - this piece is
    /// also in a box - and leaves the conclusion to the reader, because the conclusion is
    /// irreversible and is being drawn from a snapshot that may be days old.
    ///
    /// Retainers are one of the places looked at, not one of the boxes compared against. A piece with
    /// a retainer is no more wearable as a glamour than one in a bag, so it belongs on the left of
    /// this question rather than the right.
    /// </remarks>
    private void DrawAlreadyStored()
    {
        if (!plugin.Ownership.HasDresserData)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "No Glamour Dresser data yet - open a dresser once so this can be answered.");
            return;
        }

        if (snapshot.Carried == null)
            return;

        var stale = plugin.Ownership.IsDresserStale;

        // The premise, then where it reads from. Both are worth the two lines: the premise is what a
        // row is reporting and was being left for the reader to infer from a location heading and a
        // trailing clause, and where it reads from is not guessable from the contents and changes what
        // an empty list means.
        ImGui.TextColored(Palette.For(ColourRole.Muted), "Every row is two copies of one piece:");

        ImGui.TextColored(Palette.For(ColourRole.Muted), plugin.Ownership.Retainers.Count > 0
            ? "one in your bags, armoury chest, saddlebag or with a retainer, and one the collection has."
            : "one in your bags, armoury chest or saddlebag, and one the collection has.");

        if (snapshot.Carried.AlreadyStored.Count == 0 && snapshot.Carried.OnlyInsideAnOutfit.Count == 0)
        {
            ImGui.TextColored(Palette.For(ColourRole.Good), "Nothing held has a second copy in the collection.");
            DrawCaveats(stale);
            return;
        }

        DrawCarriedGroup("In your bags", snapshot.HeldIn(CarryLocation.Bags), null, stale);

        DrawCarriedGroup(
            "In your armoury chest", snapshot.HeldIn(CarryLocation.Armoury),
            "A gearset may be using these.", stale);

        DrawCarriedGroup("In your saddlebag", snapshot.HeldIn(CarryLocation.Saddlebag), null, stale);

        DrawRetainerGroups(stale);

        DrawCaveats(stale);
    }

    /// <summary>
    /// A heading per retainer holding something the collection accounts for.
    /// </summary>
    /// <remarks>
    /// Named rather than lumped into one retainer heading, because the whole difficulty with a spare
    /// copy at a retainer is which retainer, and a list that will not say leaves all of them to check.
    /// Ordered by name here rather than by how stale the snapshot is, which is what the Data tab sorts
    /// by: this is a list to find a name in.
    /// </remarks>
    private void DrawRetainerGroups(bool stale)
    {
        foreach (var group in snapshot.ByRetainer(CarryLocation.Retainer))
        {
            DrawCarriedGroup(
                $"In {CollectionSnapshot.Possessive(group.Key)} bags", group,
                "Only reachable at a bell, and only as fresh as your last visit.", stale);
        }

        // Separate headings, and this is the whole point of the split. "With Ysayle" for a coat Ysayle
        // is wearing is a true sentence that leads to seven pages of her bags being searched for it.
        foreach (var group in snapshot.ByRetainer(CarryLocation.RetainerEquipped))
        {
            DrawCarriedGroup(
                $"Worn by {CollectionSnapshot.Name(group.Key)}", group,
                "On the retainer, not in their bags - and their gear decides what ventures bring back.",
                stale);
        }
    }

    private void DrawCarriedGroup(
        string label, IEnumerable<CarriedPiece> pieces, string? note, bool stale)
    {
        var listed = pieces.ToList();
        if (listed.Count == 0)
            return;

        ImGui.Spacing();

        // Collapsible per place, because the places are separate errands and a full armoury chest is
        // dozens of rows - enough to push the retainer headings below it off the bottom of the window,
        // where somebody looking for which retainer has a spare copy will not find them. Remembered,
        // since the chest is the one nobody wants to see again tomorrow either.
        //
        // The count is in the label and not in the key, so collecting something does not lose the
        // state of the heading it is under.
        // Amber only, and only when the snapshot behind it is old. The blue this used to carry otherwise
        // was there to tell one section from the next while they were all stacked in one column; the tabs
        // do that now, so a coloured heading would only be competing with the warning that means something.
        if (!UiParts.CollapsingHeader(
                headers, $"{label} ({listed.Count})", CollapsePrefix + label,
                colour: stale ? ColourRole.Warning : null))
        {
            return;
        }

        if (note != null)
            ImGui.TextColored(Palette.For(ColourRole.Muted), $"   {note}");

        foreach (var piece in listed)
        {
            UiParts.ItemIcon(piece.IconId, 20);

            var quantity = piece.Quantity > 1 ? $"  x{piece.Quantity}" : string.Empty;
            ImGui.Text($"{piece.Name}{quantity}");

            var hovered = ImGui.IsItemHovered();
            UiParts.ItemContextMenu(plugin, piece.ItemId, piece.Name);

            // "also", because that word is the whole row. Every piece here exists twice - the one being
            // held, under the heading that says where, and a second the collection has - and without it
            // the row reads as a claim about where this copy is, which the heading has already answered.
            var insideOutfit = piece.StoredIn == OwnershipSource.Outfit;

            ImGui.SameLine();
            ImGui.TextColored(
                insideOutfit ? Palette.For(ColourRole.Warning) : Palette.For(ColourRole.Muted),
                $"- also {MissingItems.Describe(piece.StoredIn).ToLowerInvariant()}");

            if (!hovered)
                continue;

            using var tooltip = ImRaii.Tooltip();
            ImGui.Text(piece.Name);

            // Both places, in the hover, even though the heading has one of them. A tooltip that
            // repeated only the collection half never said what the row is actually reporting - two
            // copies, and here is each one.
            ImGui.TextColored(Palette.For(ColourRole.Muted), $"{label}.");
            ImGui.TextColored(Palette.For(ColourRole.Muted), CollectionSnapshot.SecondCopy(piece.StoredIn));

            if (insideOutfit)
            {
                // Why this one is amber rather than grey. The dresser's copy is not a piece sitting in
                // the box, it is a slot of a whole set sitting in the box, so it is only there for as
                // long as the set is - which the two flat statements above cannot say on their own.
                ImGui.TextColored(Palette.For(ColourRole.Warning),
                    "That second copy belongs to the set, so taking the set out takes it too.");
            }

            if (piece.ArmoireWouldTake)
                ImGui.TextColored(Palette.For(ColourRole.Muted), "The Armoire would take this too.");
        }
    }

    /// <summary>
    /// What the list cannot see, in as few words as it can be put.
    /// </summary>
    /// <remarks>
    /// Each of these changes what the list means, which is why they are on screen at all - but
    /// none of them needs a sentence. The note about reading the bags regardless of the
    /// count-inventory setting is gone: it explained the plugin's reasoning to someone who only
    /// wanted to know what was on the list.
    /// </remarks>
    private void DrawCaveats(bool stale)
    {
        ImGui.Spacing();

        if (stale)
        {
            ImGui.TextColored(Palette.For(ColourRole.Warning),
                $"Dresser snapshot {Format.Age(plugin.Ownership.DresserUpdatedUtc!.Value)} old.");
        }

        // What the amber rows mean, said only when there are some. The row itself says which pieces
        // they are and the hover says what to do about it; this is here so the colour is not a thing
        // to work out.
        if (snapshot.Carried!.OnlyInsideAnOutfit.Count > 0)
        {
            ImGui.TextColored(Palette.For(ColourRole.Muted),
                "Amber: the second copy is a slot of a stored outfit set rather than a piece of its own.");
        }

        if (!snapshot.Carried.SaddlebagReadable)
            ImGui.TextColored(Palette.For(ColourRole.Muted), "Saddlebag unreadable away from a bell.");

        // Which retainers this could not look at, since an unvisited one is indistinguishable from an
        // empty one on the list itself.
        if (plugin.Ownership.Retainers.Count == 0)
            ImGui.TextColored(Palette.For(ColourRole.Muted), "No retainer has been read - open one at a bell to include it.");
    }
}
