// REGRESSION — every MCP tool result stays under 64 KiB at 200 seeded Work Items (spaarkeai-mcp-bff-api-r1 task 025, FR-05, FR-11, NFR-02).
// KEEP path: regression (a bound that was once exceeded in design review: 30-50 full rows exceed Cowork's 64 KiB inline cap).
//
// WHAT RUNS: the five PRODUCTION tools on the production MCP host (DecisionToolsTestHost: AddMcpModule, MapMcpEndpoints,
// RateLimitingModule, real JwtBearer, a real Kestrel listener) over module-boundary doubles (McpFiveToolFixtures.cs): the 200-item lane,
// the largest catalog plan, 25 lookup candidates and a commit that names all 200 Signals. The size is the UTF-8 length of the JSON-RPC
// result of tools/call.
// NOT HERE: a bound on spaarke_get_decision_plan's evidenceRefs. The tool does not cap them, so a test asserting a bound would assert a
// guess; the largest line count that fits is MEASURED (McpToolMeasurementTests) and reported in notes/nfr-measurements.md.

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

    /// <summary>50 evidence lines (about 12 KB stored, 19 KB in the result): far more than a rule's witnesses produce today. The largest
    /// catalog plan plus these fits; the measured crossing is about 100 lines (notes/nfr-measurements.md).</summary>
    private const int PlausibleEvidenceLines = 50;

    private static Task<DecisionToolsTestHost> Start(FiveToolCapability capability) =>
        DecisionToolsTestHost.StartAsync(capability, new FiveToolLookup());

    [Fact]
    public async Task ShowDecisions_At200Items_EveryCategory_IsUnder64KiB()
    {
        await using var host = await Start(new FiveToolCapability(DecideLaneSeed.Build()));

        foreach (var category in DecideLaneCategories.Keys)
        {
            var result = await host.CallAsync(ShowDecisionsTool.Name, new { category }, Token);

            McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes, $"category '{category}'");
        }
    }

    [Fact]
    public async Task ListDecisionPage_At200Items_EveryPageOfTheLane_IsUnder64KiB()
    {
        await using var host = await Start(new FiveToolCapability(DecideLaneSeed.Build()));
        string? cursor = null;
        var pages = 0;
        do
        {
            var result = await host.CallAsync(ListDecisionPageTool.Name, cursor is null ? new { } : new { cursor }, Token);

            McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes, $"page {pages + 1}");
            cursor = result.GetProperty("structuredContent").TryGetProperty("nextCursor", out var next) ? next.GetString() : null;
            pages++;
        }
        while (cursor is not null);

        pages.Should().Be(4, "200 items at 50 per page");
    }

    [Fact]
    public async Task GetDecisionPlan_TheLargestCatalogPlan_WithA200ItemLaneAndPlausibleEvidence_IsUnder64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build(), PlausibleEvidenceLines);
        await using var host = await Start(capability);

        var result = await host.CallAsync(GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, Token);

        result.TryGetProperty("isError", out var isError).Should().BeFalse(isError.ToString());
        result.GetProperty("structuredContent").GetProperty("row").GetProperty("evidenceRefs").GetString()!
            .Should().Be(capability.EvidenceRefsJson, "the row carried the evidence being measured");
        McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes);
    }

    [Fact]
    public async Task SearchDecisionLookup_With25LongCandidates_IsUnder64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build());
        await using var host = await Start(capability);

        var result = await host.CallAsync(
            SearchDecisionLookupTool.ToolName, new { signalId = capability.TargetSignalId, parameterCode = "firm", query = "co" }, Token);

        result.GetProperty("structuredContent").GetProperty("candidates").GetArrayLength().Should().Be(25);
        McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes);
    }

    [Fact]
    public async Task CompleteDecision_NamingAll200Signals_IsUnder64KiB()
    {
        var capability = new FiveToolCapability(DecideLaneSeed.Build());
        await using var host = await Start(capability);

        var result = await host.CallAsync(
            CompleteDecisionTool.ToolName,
            new { signalId = capability.TargetSignalId, review = new { reviewId = Guid.NewGuid(), confirmed = true, actions = Array.Empty<object>() } },
            Token);

        result.GetProperty("structuredContent").GetProperty("signals").GetArrayLength().Should().Be(200);
        McpResult.Bytes(result).Should().BeLessThan(McpResult.MaxBytes);
    }

    [Fact]
    public async Task WorklistReadsPerCall_AreTwoForShow_OneForAPage_OneForThePlan()
    {
        // wave 6 review (022): "opening the widget costs three worklist reads". Measured: spaarke_show_decisions reads the lane twice (the
        // category counts, then page 1; the widget continues from its cursor), and opening an item adds one (the plan tool reads the row).
        // Pinned as counted, so a change to it is seen; it is not a performance bound.
        var capability = new FiveToolCapability(DecideLaneSeed.Build());
        await using var host = await Start(capability);

        await host.CallAsync(ShowDecisionsTool.Name, new { category = "all" }, Token);
        var afterShow = capability.WorklistReads;
        await host.CallAsync(ListDecisionPageTool.Name, new { }, Token);
        var afterPage = capability.WorklistReads;
        await host.CallAsync(GetDecisionPlanTool.ToolName, new { signalId = capability.TargetSignalId }, Token);

        afterShow.Should().Be(2, "opening the widget");
        (afterPage - afterShow).Should().Be(1, "each further page");
        (capability.WorklistReads - afterPage).Should().Be(1, "opening an item");
    }
}
