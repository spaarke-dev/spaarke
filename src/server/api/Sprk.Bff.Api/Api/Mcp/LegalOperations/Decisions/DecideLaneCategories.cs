using Sprk.Bff.Api.Services.Signals;

namespace Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;

// =====================================================================================================================
// spaarkeai-mcp-bff-api-r1 task 022 (spec FR-03): the Decide lane's category cards, on the server.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11).
//   Existing: bucketLaneItems in laneCountFilters.ts (the Console's cards, in the browser). Nothing on the server partitions the lane.
//   Extension: the browser function cannot run in the BFF; this is a line-for-line mirror of it, and the shared corpus
//     tests/fixtures/decide-lane-category-parity/cases.json is asserted against both (xunit and jest), so the two cannot drift.
//   Cost of doing nothing: spaarke_show_decisions could not tell Copilot the counts the Console shows (FR-03), and its closed
//     category enum would have no server-side meaning.
// =====================================================================================================================

/// <summary>One Decide-lane card: its key (a <c>LaneFilterKey</c> value), its label and how many items it holds.</summary>
internal sealed record DecideLaneCategory(string Key, string Label, int Count);

/// <summary>What the partition reads of one Work Item: its lane, its work type and whether it is a billing item (has <c>invoiceReview</c>).</summary>
internal readonly record struct DecideLaneMember(string? Lane, string? WorkType, bool HasInvoiceReview)
{
    /// <summary>A worklist row. <c>hasInvoiceReview</c> in the browser is "an invoice id is present"; the server always writes one.</summary>
    public static DecideLaneMember From(WorklistItemRow row) => new(row.Lane, row.WorkType, row.InvoiceReview is not null);

    /// <summary>A slim row (task 015).</summary>
    public static DecideLaneMember From(WorklistItemSummary row) => new(row.Lane, row.WorkType, row.InvoiceReview is not null);
}

/// <summary>
/// The Decide lane's category cards, exactly as <c>bucketLaneItems(items, 'Decide')</c> in <c>laneCountFilters.ts</c> builds them:
/// <c>all</c> first; then <c>invoiceDecisions</c> (the billing items, decided in Invoice Review, D-207) only when it holds an item; then one
/// card per Decide work type, in display order, over the NON-billing items; then <c>other</c> (a non-billing item whose work type is
/// missing, empty or not a Decide work type, compared case-sensitively) only when it holds an item. The non-<c>all</c> cards partition the
/// lane. Items of another lane are ignored (the lane is compared exactly, as the browser does).
/// </summary>
internal static class DecideLaneCategories
{
    /// <summary>The lane this partition is for, as <see cref="WorklistItemRow.Lane"/> spells it.</summary>
    public const string Lane = "Decide";

    public const string All = "all";
    public const string InvoiceDecisions = "invoiceDecisions";
    public const string Other = "other";

    /// <summary><c>LANE_WORK_TYPES.Decide</c> in <c>laneCountFilters.ts</c>: key and label, in display order.</summary>
    public static readonly IReadOnlyList<(string Key, string Label)> WorkTypes =
    [
        ("askOutsideCounsel", "Ask outside counsel"),
        ("approveOrRebudget", "Approve or rebudget"),
        ("chaseReply", "Chase a reply"),
    ];

    /// <summary>
    /// The closed set of the Decide lane's <c>LaneFilterKey</c> values, in card order: the only values <c>spaarke_show_decisions</c>'
    /// <c>category</c> accepts.
    /// </summary>
    public static readonly IReadOnlyList<string> Keys = [All, InvoiceDecisions, .. WorkTypes.Select(t => t.Key), Other];

    /// <summary>The card label of <paramref name="key"/>, or null for a key outside <see cref="Keys"/>.</summary>
    public static string? LabelOf(string key) =>
        key switch
        {
            All => "All",
            InvoiceDecisions => "Invoice decisions",
            Other => "Other",
            _ => WorkTypes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal)).Label,
        };

    /// <summary>The cards of <paramref name="items"/>' Decide lane, in display order (see the class remarks).</summary>
    public static IReadOnlyList<DecideLaneCategory> Partition(IEnumerable<DecideLaneMember> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var inLane = 0;
        var billing = 0;
        var other = 0;
        var byType = WorkTypes.ToDictionary(t => t.Key, _ => 0, StringComparer.Ordinal);
        foreach (var item in items)
        {
            var category = CategoryOf(item);
            if (category is null)
            {
                continue;
            }

            inLane++;
            if (category == InvoiceDecisions)
            {
                billing++;
            }
            else if (category == Other)
            {
                other++;
            }
            else
            {
                byType[category]++;
            }
        }

        var cards = new List<DecideLaneCategory>(WorkTypes.Count + 3) { new(All, LabelOf(All)!, inLane) };
        if (billing > 0)
        {
            cards.Add(new DecideLaneCategory(InvoiceDecisions, LabelOf(InvoiceDecisions)!, billing));
        }

        cards.AddRange(WorkTypes.Select(t => new DecideLaneCategory(t.Key, t.Label, byType[t.Key])));
        if (other > 0)
        {
            cards.Add(new DecideLaneCategory(Other, LabelOf(Other)!, other));
        }

        return cards;
    }

    /// <summary>
    /// The one non-<c>all</c> card <paramref name="item"/> is counted in: <c>invoiceDecisions</c> for a billing item, its work type when it
    /// is a Decide work type, else <c>other</c>; null when the item is not in the Decide lane.
    /// </summary>
    public static string? CategoryOf(DecideLaneMember item)
    {
        if (!string.Equals(item.Lane, Lane, StringComparison.Ordinal))
        {
            return null;
        }

        if (item.HasInvoiceReview)
        {
            return InvoiceDecisions;
        }

        return !string.IsNullOrEmpty(item.WorkType) && WorkTypes.Any(t => string.Equals(t.Key, item.WorkType, StringComparison.Ordinal))
            ? item.WorkType
            : Other;
    }

    /// <summary>
    /// <c>resolveLaneFilterKey</c>: <paramref name="key"/> when its card holds an item, else <c>all</c> (a lens never points at an empty card).
    /// </summary>
    public static string Resolve(IReadOnlyList<DecideLaneCategory> cards, string key) =>
        key == All || cards.Any(c => c.Key == key && c.Count > 0) ? key : All;

    /// <summary>True when <paramref name="item"/> is shown under the lens <paramref name="key"/> (<c>all</c> shows every Decide item).</summary>
    public static bool IsIn(DecideLaneMember item, string key) =>
        CategoryOf(item) is { } category && (key == All || category == key);
}
