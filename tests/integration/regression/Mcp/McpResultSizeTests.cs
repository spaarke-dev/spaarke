// REGRESSION — every MCP tool result stays under 64 KiB at 200 seeded Work Items (spaarkeai-mcp-bff-api-r1 task 025, FR-05, FR-11, NFR-02).
// KEEP path: regression (a bound that was once exceeded in design review: 30-50 full rows exceed Cowork's 64 KiB inline cap; and
// spaarke_get_decision_plan passed a row's evidenceRefs and sentence through uncapped, so about 100 evidence lines dropped the result).
//
// WHAT RUNS: the five PRODUCTION tools on the production MCP host (DecisionToolsTestHost: AddMcpModule, MapMcpEndpoints,
// RateLimitingModule, real JwtBearer, a real Kestrel listener) over module-boundary doubles (McpFiveToolFixtures.cs): the 200-item lane at
// its WORST for size (DecideLaneSeed.BuildWorstCase: 50 matters, escape-heavy text at every clipped member, non-null sentences, a billing and
// privilege mix), the largest catalog plan, 25 lookup candidates and a commit that names all 200 Signals. The size is the UTF-8 length of the
// JSON-RPC result of tools/call, and every size test first asserts the call was a success (an error result is small and would pass).

using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Decisions;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Tests.Api.Mcp;
using Xunit;

namespace Sprk.Bff.Api.Tests.Regression.Mcp;

public sealed class McpResultSizeTests
{
    private static readonly string Token = DecisionToolsTestHost.MintToken(Guid.Parse("0250aaaa-0000-4000-8000-000000000025"));

    /// <summary>50 evidence lines (about 12 KB stored, 19 KB in the result): far more than a rule's witnesses produce today; inside the plan
    /// tool's bound, so none is left out.</summary>
    private const int PlausibleEvidenceLines = 50;

    private static Task<DecisionToolsTestHost> Start(FiveToolCapability capability) =>
        DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());

    private static void ShouldBeASuccessUnder64KiB(JsonElement result, string because = "")
    {
        McpResult.IsError(result).Should().BeFalse($"a size test of an error result measures nothing: {result.GetRawText()} {because}");
        McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes, because);
    }

    [Fact]
    public async Task ShowDecisions_At200WorstCaseItems_EveryCategory_IsASuccessUnder64KiB()
    {
        await using var host = await Start(new FiveToolCapability(DecideLaneSeed.BuildWorstCase()));

        foreach (var category in DecideLaneCategories.Keys)
        {
            var result = await host.CallAsync(ShowDecisionsTool.Name, new { category }, Token);

            ShouldBeASuccessUnder64KiB(result, $"category '{category}'");
        }
    }

    [Fact]
    public async Task ListDecisionPage_At200WorstCaseItems_EveryPageOfTheLane_IsASuccessUnder64KiB()
    {
        await using var host = await Start(new FiveToolCapability(DecideLaneSeed.BuildWorstCase()));
        string? cursor = null;
        var pages = 0;
        var rows = 0;
        do
        {
            var result = await host.CallAsync(ListDecisionPageTool.Name, cursor is null ? new { } : new { cursor }, Token);

            ShouldBeASuccessUnder64KiB(result, $"page {pages + 1}");
            var content = result.GetProperty("structuredContent");
            rows += content.GetProperty("items").GetArrayLength();
            cursor = content.TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            pages++;
        }
        while (cursor is not null);

        pages.Should().Be(4, "200 items at 50 per page");
        rows.Should().Be(200, "every seeded item was returned, so the size above is the size of real rows");
    }

    [Fact]
    public async Task GetDecisionPlan_TheLargestCatalogPlan_WithA200ItemLaneAndPlausibleEvidence_ReturnsEveryLineUnchanged_Under64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build(), PlausibleEvidenceLines);
        await using var host = await Start(capability);

        var result = await host.CallAsync(GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, Token);

        ShouldBeASuccessUnder64KiB(result);
        var body = result.GetProperty("structuredContent");
        body.GetProperty("row").GetProperty("evidenceRefs").GetString()!
            .Should().Be(capability.EvidenceRefsJson, "inside the bound the row carries the stored evidence string, byte for byte");
        body.GetProperty("evidenceTotal").GetInt32().Should().Be(PlausibleEvidenceLines);
        body.GetProperty("evidenceTruncated").GetBoolean().Should().BeFalse("nothing was left out");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().NotContain("evidence", "a result that left nothing out says nothing about evidence");
    }

    [Fact]
    public async Task GetDecisionPlan_At1000MaxLengthEvidenceLines_WithMaxLengthText_IsUnder64KiB_AndSaysItWasTruncated()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.BuildWorstCase(), evidenceLines: 1_000, maxLengthRow: true);
        await using var host = await Start(capability);

        var result = await host.CallAsync(GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, Token);

        ShouldBeASuccessUnder64KiB(result, "the worst plan the catalog builds, plus the longest row text and 1,000 lines of the longest evidence");
        var body = result.GetProperty("structuredContent");
        body.GetProperty("evidenceTotal").GetInt32().Should().Be(1_000, "the total is what the item stores, not what was returned");
        body.GetProperty("evidenceTruncated").GetBoolean().Should().BeTrue();
        var row = body.GetProperty("row");
        using var kept = JsonDocument.Parse(row.GetProperty("evidenceRefs").GetString()!);
        kept.RootElement.GetArrayLength().Should().BeInRange(1, PlanAndLookupToolSupport.MaxEvidenceLines);
        kept.RootElement.GetArrayLength().Should().BeLessThan(1_000);
        foreach (var line in kept.RootElement.EnumerateArray())
        {
            line.EnumerateObject().Count().Should().BeLessThanOrEqualTo(PlanAndLookupToolSupport.EvidenceMaxMembers);
            line.GetProperty("text").GetString()!.Should().EndWith("…", "an over-long member is cut with an ellipsis, never dropped silently");
        }

        row.GetProperty("sentence").GetString()!.Should().EndWith("…");
        row.GetProperty("title").GetString()!.Should().EndWith("…");
        result.GetProperty("content")[0].GetProperty("text").GetString().Should().Contain("1000 evidence line(s)");
    }

    [Fact]
    public async Task SearchDecisionLookup_With25LongCandidates_IsASuccessUnder64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.BuildWorstCase());
        await using var host = await Start(capability);

        var result = await host.CallAsync(
            SearchDecisionLookupTool.ToolName, new { signalId = capability.TargetSignalId, parameterCode = "firm", query = "co" }, Token);

        ShouldBeASuccessUnder64KiB(result);
        result.GetProperty("structuredContent").GetProperty("candidates").GetArrayLength().Should().Be(25);
    }

    [Fact]
    public async Task CompleteDecision_NamingAll200Signals_IsASuccessUnder64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.BuildWorstCase());
        await using var host = await Start(capability);

        var result = await host.CallAsync(
            CompleteDecisionTool.ToolName,
            new { signalId = capability.TargetSignalId, review = new { reviewId = Guid.NewGuid(), confirmed = true, actions = Array.Empty<object>() } },
            Token);

        ShouldBeASuccessUnder64KiB(result);
        result.GetProperty("structuredContent").GetProperty("signals").GetArrayLength().Should().Be(200);
    }

    [Fact]
    public async Task WorklistReadsPerCall_AreOneForShow_OneForAPage_NoneForThePlan_WhichReadsOneRow()
    {
        // wave 6 review (022): "opening the widget costs three worklist reads". NFR-01 (2026-11-25, dev App Insights: show p95 6.5 s, two
        // ~2 s lane reads per call; plan p95 4.7 s): spaarke_show_decisions now reads the lane once (page 1 carries the whole lanes response
        // the category counts come from; the widget continues from its cursor), and opening an item reads only that item's row (the lanes
        // read narrowed to one Signal, ontology task 222 B), never the whole lane.
        // Pinned as counted, so a whole-lane read creeping back is seen; it is not a performance bound.
        var capability = new FiveToolCapability(DecideLaneSeed.Build());
        await using var host = await Start(capability);

        await host.CallAsync(ShowDecisionsTool.Name, new { category = "all" }, Token);
        var afterShow = capability.WorklistReads;
        await host.CallAsync(ListDecisionPageTool.Name, new { }, Token);
        var afterPage = capability.WorklistReads;
        await host.CallAsync(GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, Token);

        afterShow.Should().Be(1, "opening the widget reads the lane once");
        (afterPage - afterShow).Should().Be(1, "each further page");
        (capability.WorklistReads - afterPage).Should().Be(0, "opening an item never reads the whole lane");
        capability.RowReads.Should().Be(1, "opening an item reads its one row");
    }
}
