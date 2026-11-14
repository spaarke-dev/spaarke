using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Api.Signals;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.Api.Mcp;

namespace Sprk.Bff.Api.Tests.Regression.Mcp;

/// <summary>
/// TESTS ONLY (spaarkeai-mcp-bff-api-r1 task 025). Module-boundary doubles that let the five PRODUCTION tools answer at the largest
/// plausible size: the 200-item lane of <see cref="DecideLaneSeed"/> (the 022 double, whose list and paging reads run the PRODUCTION
/// <see cref="DecideLanePaging"/>), the LARGEST plan the closed action catalog can produce, 25 lookup candidates (the route's cap), and
/// a commit result that names all 200 Signals. The Signal the plan is read for carries <see cref="EvidenceRefsJson"/> in its worklist row,
/// which is the input <c>spaarke_get_decision_plan</c> bounds (the stored row's <c>evidenceRefs</c> has no cap; the tool's result does).
/// </summary>
internal sealed class FiveToolCapability : IDecisionCapability
{
    private readonly DecideLaneDouble _lane;

    private readonly bool _maxLengthRow;

    /// <param name="maxLengthRow">The target item's row carries the longest text every member can hold (escape-heavy, so each clip budget is
    /// reached) and its evidence lines are the longest the shape can write: the worst case of <c>spaarke_get_decision_plan</c>.</param>
    public FiveToolCapability(DecideLaneSeed seed, int evidenceLines = 0, bool maxLengthRow = false)
    {
        Seed = seed;
        _lane = new DecideLaneDouble(seed);
        _maxLengthRow = maxLengthRow;
        TargetSignalId = seed.ConsoleOrder[20]; // an ordinary visible item of the Alpha matter
        EvidenceRefsJson = evidenceLines == 0 ? null : maxLengthRow ? MaxLengthEvidenceLines(evidenceLines) : EvidenceLines(evidenceLines);
    }

    /// <summary>
    /// <paramref name="count"/> evidence lines at their worst: every string member 1,500 characters of quote-and-accent text (each is escaped
    /// in the stored string and again in the result), 16 members, a nested value and a long member name beyond what the tool keeps.
    /// </summary>
    public static string MaxLengthEvidenceLines(int count) => JsonSerializer.Serialize(
        Enumerable.Range(0, count).Select(i => new Dictionary<string, object?>
        {
            ["kind"] = HeavyText(1_500),
            ["ref"] = HeavyText(1_500),
            ["tier"] = HeavyText(1_500),
            ["confidence"] = 0.92,
            ["text"] = HeavyText(1_500),
            ["source"] = HeavyText(1_500),
            ["asOf"] = HeavyText(1_500),
            ["clause"] = HeavyText(1_500),
            ["withheld"] = false,
            ["detail"] = new { a = HeavyText(1_500) },
            ["x01"] = HeavyText(1_500),
            ["x02"] = HeavyText(1_500),
            ["x03"] = HeavyText(1_500),
            ["x04"] = HeavyText(1_500),
            ["x05"] = HeavyText(1_500),
            ["x06"] = HeavyText(1_500),
            [new string('n', 100)] = "a name past the limit",
        }));

    /// <summary>Text whose every character is escaped on the wire: a quote and an accent alternate (2 and 6 bytes), so any byte budget is reached early.</summary>
    public static string HeavyText(int characters) => string.Concat(Enumerable.Repeat("\"é", characters / 2));

    public DecideLaneSeed Seed { get; }

    public DecideLaneDouble Lane => _lane;

    public Guid TargetSignalId { get; }

    public string? EvidenceRefsJson { get; }

    /// <summary>Lane reads plus page reads: each is one worklist read as the caller (the cost the widget's opening pays).</summary>
    public int WorklistReads => _lane.LaneReads + _lane.PageReads;

    /// <summary><paramref name="count"/> evidence lines as <c>SignalWriter</c> writes them (kind, ref, tier, confidence), plus the read-time detail (text, source).</summary>
    public static string EvidenceLines(int count) => JsonSerializer.Serialize(
        Enumerable.Range(0, count).Select(i => new
        {
            kind = "fact",
            @ref = $"invoice.line[{i}].amount",
            tier = "detected",
            confidence = 0.92,
            text = $"Line {i}: the billed amount exceeds the approved budget line by the amount shown on the matter's budget schedule.",
            source = "sprk_invoiceline",
        }));

    public async Task<SignalReadResult> ListDecideAsync(ClaimsPrincipal? user, CancellationToken ct, WorklistSet set = WorklistSet.Lanes)
    {
        var read = await _lane.ListDecideAsync(user, ct, set);
        if (EvidenceRefsJson is null || read.Response is null)
        {
            return read;
        }

        WorklistItemRow Carry(WorklistItemRow row) =>
            row.SignalId != TargetSignalId ? row
            : _maxLengthRow
                ? row with
                {
                    EvidenceRefs = EvidenceRefsJson,
                    Sentence = HeavyText(2_000), // the stored maximum: 2,000 characters, every one escaped
                    Title = HeavyText(400),
                    SubjectName = HeavyText(400),
                    RuleShortName = HeavyText(400),
                    WorkType = HeavyText(400),
                    DueDate = HeavyText(400),
                }
                : row with { EvidenceRefs = EvidenceRefsJson, Sentence = new string('s', 2000) };

        return read with
        {
            Response = read.Response with
            {
                Lanes = read.Response.Lanes
                    .Select(l => l with { Groups = l.Groups.Select(g => g with { Items = g.Items.Select(Carry).ToList() }).ToList() })
                    .ToList(),
            },
        };
    }

    public Task<DecidePageResult> ListDecidePageAsync(ClaimsPrincipal? user, string? cursor, CancellationToken ct) =>
        _lane.ListDecidePageAsync(user, cursor, ct);

    /// <summary>The largest plan the catalog can build: every catalog action as an action and again as a next step, every dismissal reason.</summary>
    public Task<DecisionPlanReadResult> GetPlanAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
        Task.FromResult(DecisionPlanReadResult.Ok(DecisionPlanResponse.From(
            signalId,
            DecisionLane.Decide,
            Guid.Parse("cccccccc-0000-4000-8000-000000000001"),
            new ResolvedDecisionPlan(DecisionActionCatalog.All, DecisionActionCatalog.All, DecisionActionCatalog.DismissalReasons(DecisionLane.Decide)))));

    public Task<SuppressionReadResult> GetSuppressionAsync(HttpContext? http, Guid signalId, CancellationToken ct) =>
        Task.FromResult(SuppressionReadResult.Ok(new SuppressionResponse(signalId, 2, 3, 3, 90, DateTime.Parse("2026-12-01T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime(), true)));

    /// <summary>A recorded decision that also resolved every one of the 200 seeded Signals, each with a warning.</summary>
    public Task<DecisionCommitResult> CommitAsync(HttpContext? http, Guid signalId, DecisionReview review, CancellationToken ct) =>
        Task.FromResult(new DecisionCommitResult
        {
            Status = DecisionCommitStatus.Recorded,
            ReviewId = review.ReviewId,
            DecisionRecordId = Guid.Parse("dddddddd-0000-4000-8000-000000000001"),
            RecordClass = "Decision",
            Signals = Seed.ConsoleOrder.Select(id => new DecisionCommitSignal(id, "Acted", "The Work Item closed; a follow-on could not be created.")).ToList(),
            Warnings = Enumerable.Range(0, 20).Select(i => $"Warning {i}: one follow-on was skipped.").ToList(),
        });
}

/// <summary>25 candidates (<c>DecisionLookupSearch.MaxResults</c>), each with a long name and an email.</summary>
internal sealed class FiveToolLookup : IDecisionLookupSearch
{
    public Task<DecisionLookupResult> SearchAsync(HttpContext? http, Guid signalId, string? parameterCode, string? query, CancellationToken ct) =>
        Task.FromResult(new DecisionLookupResult(
            DecisionLookupOutcome.Ok,
            new DecisionLookupResponse(
                parameterCode ?? "firm",
                "sprk_organization",
                Enumerable.Range(0, DecisionLookupSearch.MaxResults)
                    .Select(i => new DecisionLookupCandidate(Guid.NewGuid(), $"Outside counsel firm number {i} {new string('x', 100)}", $"contact{i}@firm-{i}.example.com"))
                    .ToList())));
}

/// <summary>Reads of a tool result: its size on the wire and its parts.</summary>
internal static class McpResult
{
    public const int MaxBytes = 64 * 1024;

    /// <summary>UTF-8 bytes of the JSON-RPC <c>result</c> of the call (<c>content</c> and <c>structuredContent</c>): what a host counts against its inline cap.</summary>
    public static int Bytes(JsonElement result) => Encoding.UTF8.GetByteCount(result.GetRawText());

    /// <summary>True when the call answered <c>isError</c>: a size test of an error result would measure nothing, so every size test checks this first.</summary>
    public static bool IsError(JsonElement result) => result.TryGetProperty("isError", out var e) && e.GetBoolean();
}
