using System.Buffers.Text;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Signals;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 015 (B-5; spec FR-05): the Decide lane in pages, with an opaque stateless cursor.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: SignalReadService orders the lane (D-42, Shape) and WorklistItemSummary is the slim row. Nothing pages a worklist.
//   Extension: the order stays SignalReadService's; this slices the lane it returns and shares its comparators
//     (SignalReadService.CompareRankKeys), so the page order cannot drift from the Console's.
//   Cost of doing nothing: a lane of 70+ items cannot fit one tool result under 64 KiB (NFR-02), and the widget has no way to fetch
//     the rest (FR-05).
// =====================================================================================================================

/// <summary>
/// Where a page stopped: the last returned item's place in the Console order, as the group it sits in (the group's core record id and
/// its best item's rank keys) and the item itself (rank keys, then Signal id), and the core record ids of the groups returned in full
/// before it. No server state, and no record text: a cursor is handed back to the model in a model-visible result, so it carries only
/// ids, ranks and times (FR-04).
/// </summary>
/// <remarks>
/// <para><b>Resuming.</b> The next page re-reads the lane as the caller, so the list is current. It lists, in this order: what is left of
/// the cursor's own group (found by core record, not by rank) after the cursor's item; then every other group in the current Console
/// order that is not in <see cref="Returned"/>, wherever it now ranks. An item resolved between pages is simply no
/// longer in the list. A group is ordered by its best item, so resolving the best item of a group already returned moves that group
/// later in the list; <see cref="Returned"/> is what keeps its remaining items from being listed a second time, and finishing the
/// cursor's own group first keeps its remainder from waiting behind groups that moved ahead of it. A group not yet returned that moved
/// ahead of the cursor's group (a new Critical item on its core record, a subject turned high priority) is still listed. Without any
/// change between pages that is exactly the Console order. Only the last <see cref="MaxReturnedGroups"/> groups are remembered; a cursor
/// holding that many may have forgotten earlier ones, so it also passes over every group whose best item ranks ahead of the cursor
/// group's (a lane with more groups than that may then list the remainder of a long-ago group again, or miss a group that moved ahead
/// between pages).</para>
/// <para><b>Wire form.</b> base64url (no padding) of a small versioned JSON object. It is <i>opaque</i>, not secret: it carries no
/// record text and no key; it is not signed, because it only positions a window in the list the caller's own read returns, so a
/// hand-edited cursor can move the window but never show a row the caller cannot read. Anything that does not decode to exactly the
/// shape below is invalid (<see cref="TryDecode"/>).</para>
/// </remarks>
internal sealed record DecideCursor(RankKey GroupBest, Guid? GroupCoreId, RankKey Item, Guid ItemId, IReadOnlyList<Guid> Returned)
{
    private const int Version = 1;
    private const int MaxEncodedLength = 8192;

    /// <summary>The most groups a cursor remembers as returned in full (16 bytes each).</summary>
    internal const int MaxReturnedGroups = 256;

    /// <summary>The wire form of this cursor.</summary>
    public string Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("v", Version);
            writer.WriteStartObject("g");
            WriteRank(writer, GroupBest);
            if (GroupCoreId is { } core) writer.WriteString("c", core); else writer.WriteNull("c");
            writer.WriteEndObject();
            writer.WriteStartObject("i");
            WriteRank(writer, Item);
            writer.WriteString("id", ItemId);
            writer.WriteEndObject();
            writer.WriteString("r", Base64Url.EncodeToString(Returned.TakeLast(MaxReturnedGroups).SelectMany(g => g.ToByteArray()).ToArray()));
            writer.WriteEndObject();
        }

        return Base64Url.EncodeToString(stream.ToArray());
    }

    /// <summary>True when <paramref name="cursor"/> is exactly a cursor this build wrote the shape of. Never throws.</summary>
    public static bool TryDecode(string? cursor, out DecideCursor decoded)
    {
        decoded = null!;
        if (string.IsNullOrEmpty(cursor) || cursor.Length > MaxEncodedLength)
        {
            return false;
        }

        try
        {
            var bytes = Base64Url.DecodeFromChars(cursor);
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !Members(root, "v", "g", "i", "r")
                || !root.GetProperty("v").TryGetInt32(out var version) || version != Version
                || !TryObject(root.GetProperty("g"), out var g) || !Members(g, "s", "h", "f", "c")
                || !TryObject(root.GetProperty("i"), out var i) || !Members(i, "s", "h", "f", "id")
                || !TryRank(g, out var groupBest) || !TryRank(i, out var item)
                || !TryGuid(i.GetProperty("id"), out var itemId) || itemId == Guid.Empty
                || !TryReturned(root.GetProperty("r"), out var returned))
            {
                return false;
            }

            Guid? groupCoreId = null;
            var core = g.GetProperty("c");
            if (core.ValueKind != JsonValueKind.Null)
            {
                if (!TryGuid(core, out var coreId) || coreId == Guid.Empty)
                {
                    return false;
                }

                groupCoreId = coreId;
            }

            decoded = new DecideCursor(groupBest, groupCoreId, item, itemId, returned);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return false;
        }
    }

    private static void WriteRank(Utf8JsonWriter writer, RankKey rank)
    {
        writer.WriteNumber("s", rank.SeverityRank);
        writer.WriteBoolean("h", rank.HighPriority);
        if (rank.FirstDetected is { } at) writer.WriteNumber("f", at.UtcTicks); else writer.WriteNull("f");
    }

    /// <summary>True when the object has exactly these members (no more, no fewer, no repeats).</summary>
    private static bool Members(JsonElement element, params string[] names) =>
        element.EnumerateObject().Count() == names.Length && names.All(n => element.TryGetProperty(n, out _));

    private static bool TryObject(JsonElement element, out JsonElement obj)
    {
        obj = element;
        return element.ValueKind == JsonValueKind.Object;
    }

    private static bool TryRank(JsonElement element, out RankKey rank)
    {
        rank = default;
        var s = element.GetProperty("s");
        var h = element.GetProperty("h");
        var f = element.GetProperty("f");
        if (s.ValueKind != JsonValueKind.Number || !s.TryGetInt32(out var severity) || severity is < 0 or > 3
            || h.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        DateTimeOffset? firstDetected = null;
        if (f.ValueKind == JsonValueKind.Number)
        {
            if (!f.TryGetInt64(out var ticks) || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks)
            {
                return false;
            }

            firstDetected = new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        else if (f.ValueKind != JsonValueKind.Null)
        {
            return false;
        }

        rank = new RankKey(severity, h.ValueKind == JsonValueKind.True, firstDetected);
        return true;
    }

    private static bool TryGuid(JsonElement element, out Guid id)
    {
        id = Guid.Empty;
        return element.ValueKind == JsonValueKind.String && Guid.TryParse(element.GetString(), out id);
    }

    private static bool TryReturned(JsonElement element, out IReadOnlyList<Guid> returned)
    {
        returned = [];
        if (element.ValueKind != JsonValueKind.String || element.GetString() is not { } text)
        {
            return false;
        }

        var bytes = Base64Url.DecodeFromChars(text);
        if (bytes.Length % 16 != 0 || bytes.Length / 16 > MaxReturnedGroups)
        {
            return false;
        }

        returned = Enumerable.Range(0, bytes.Length / 16).Select(n => new Guid(bytes.AsSpan(n * 16, 16))).ToList();
        return true;
    }
}

/// <summary>The Decide lane sliced into pages of <see cref="WorklistItemSummary"/>.</summary>
internal static class DecideLanePaging
{
    /// <summary>The most summaries one page holds (FR-05: 50 rows of at most <see cref="WorklistItemSummary.MaxJsonBytes"/> bytes).</summary>
    internal const int PageSize = 50;

    /// <summary>
    /// The page after <paramref name="after"/> (the first page when null) of the lane's <paramref name="groups"/>, which are in Console
    /// order as <see cref="SignalReadService"/> returned them, and the cursor for the next page (null on the last).
    /// </summary>
    /// <param name="extras">For every item of <paramref name="groups"/>: its rank keys and privilege flag.</param>
    internal static (IReadOnlyList<WorklistItemSummary> Items, string? NextCursor) Page(
        IReadOnlyList<WorklistGroup> groups, IReadOnlyDictionary<Guid, WorklistItemExtras> extras, DecideCursor? after)
    {
        var returned = new List<Guid>(after?.Returned ?? []);
        var skip = new HashSet<Guid>(returned);

        // Every Decide group has a core record (no-core rows are Do-only, SignalReadService step 6), so while the cursor remembers every
        // group returned in full, Returned alone says what is done, and a group not in it is listed wherever it now ranks (a new
        // Critical item, a subject turned high priority or a policy severity change can move it ahead of the cursor's group). Only a
        // cursor at its limit may have forgotten groups; then a group whose best item ranks ahead of the cursor's group is taken as done.
        var rememberedOnlyInPart = after is not null && after.Returned.Count >= DecideCursor.MaxReturnedGroups;

        // What is left of the cursor's own group goes first, then the other groups in the current Console order. Without a resolved
        // item that is the Console order itself: every group before the cursor's has been returned.
        var cursorGroup = after is null ? null : groups.FirstOrDefault(g => g.Core?.RecordId == after.GroupCoreId);
        IReadOnlyList<WorklistGroup> sequence = groups;
        if (cursorGroup is not null)
        {
            sequence = [cursorGroup, .. groups.Where(g => !ReferenceEquals(g, cursorGroup))];
        }

        var page = new List<WorklistItemSummary>(PageSize);
        (RankKey Best, Guid? CoreId, RankKey Rank, Guid Id) last = default;
        foreach (var group in sequence)
        {
            if (group.Items.Count == 0)
            {
                continue;
            }

            var best = extras[group.Items[0].SignalId].Rank;
            var isCursorGroup = ReferenceEquals(group, cursorGroup);
            if (after is not null && !isCursorGroup
                && ((group.Core is { } core && skip.Contains(core.RecordId))
                    || (rememberedOnlyInPart && SignalReadService.CompareRankKeys(best, after.GroupBest) < 0)))
            {
                continue;
            }

            var listed = 0;
            foreach (var item in group.Items)
            {
                var extra = extras[item.SignalId];
                if (isCursorGroup && !IsAfter(extra.Rank, item.SignalId, after!))
                {
                    continue;
                }

                if (page.Count == PageSize)
                {
                    return (page, new DecideCursor(last.Best, last.CoreId, last.Rank, last.Id, returned).Encode());
                }

                page.Add(WorklistItemSummary.From(item, extra.PrivilegeFlagged));
                last = (best, group.Core?.RecordId, extra.Rank, item.SignalId);
                listed++;
            }

            // The group is done: nothing of it is left for a later page, so a later page must not list it again, even if resolving its
            // best item moves it behind the cursor's group. (The cursor's own group counts as done even when it had nothing left.)
            if ((listed > 0 || isCursorGroup) && group.Core is { } done && skip.Add(done.RecordId))
            {
                returned.Add(done.RecordId);
            }
        }

        return (page, null);
    }

    private static bool IsAfter(RankKey rank, Guid signalId, DecideCursor cursor)
    {
        var byRank = SignalReadService.CompareRankKeys(rank, cursor.Item);
        return byRank != 0 ? byRank > 0 : signalId.CompareTo(cursor.ItemId) > 0;
    }
}
