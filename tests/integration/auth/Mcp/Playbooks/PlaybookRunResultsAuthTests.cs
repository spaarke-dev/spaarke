// AUTH — spaarke_get_playbook_run_results returns a run's results only to the caller who started it, only when that caller can read the
// run's analysis and its subject, and gives every other case the one not-found of spaarke_get_playbook_run (spaarkeai-mcp-bff-api-r2
// task 037b; D-037-results; spec NFR-07; ADR-015, ADR-028). The tool is app-only, and startup refuses it declared otherwise.
// KEEP path: auth.
//
// WHAT RUNS: the tool's RunAsync over the REAL DetachedPlaybookRunner, the REAL run record store, the REAL analysis-read rule and task
// 007's REAL authorization (PlaybookRunWorld); McpToolAuthorization.ValidateTool on the real tool.

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Moq;
using Sprk.Bff.Api.Api.Mcp;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Documents;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Playbooks;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Tests.Api.Mcp.Playbooks;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Mcp.Playbooks;

public sealed class PlaybookRunResultsAuthTests
{
    /// <summary>Text the caller must never see from a run they may not read.</summary>
    private const string Secret = "SECRET-RESULTS-037b";

    private static string Serialized(CallToolResult result) => JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions);

    /// <summary>The not-found spaarke_get_playbook_run gives an unknown id: the one answer every refusal here must equal, byte for byte.</summary>
    private static async Task<string> StatusToolNotFoundAsync(PlaybookRunWorld world) =>
        Serialized(await world.GetToolAsync(world.CallerContext(), Guid.NewGuid()));

    private static void AssertTheUniformNotFound(CallToolResult result, string expected)
    {
        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(DocumentToolSupport.NotFound.ReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status404NotFound);
        Serialized(result).Should().Be(expected, "NFR-07: the same not-found as spaarke_get_playbook_run, no existence oracle");
        Serialized(result).Should().NotContain(Secret);
    }

    [Fact(DisplayName = "037b auth: an unknown run id is spaarke_get_playbook_run's not-found, byte for byte")]
    public async Task UnknownRun_IsTheStatusToolsNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), Guid.NewGuid());

        AssertTheUniformNotFound(result, expected);
        world.DataverseUser.Verify(d => d.GetAsync(It.Is<string>(p => p != "WhoAmI"), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Fact(DisplayName = "037b auth: another caller's live run is the same not-found; the analysis is never read")]
    public async Task AnotherCallersLiveRun_IsTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var (playbookId, documentId) = world.AllowedRun();
        world.SeedAnalysisAnchor(PlaybookRunWorld.AnalysisId, documentId);
        world.CallerReadsAnalysis(PlaybookRunWorld.AnalysisId, documentId, Secret);
        world.CallerReadsDocuments((documentId, Secret));
        var started = await world.RunToolAsync(world.CallerContext(), new { playbookId, documentIds = new[] { documentId }, idempotencyKey = "mine" });
        var runId = Guid.Parse(started.StructuredContent!.Value.GetProperty("runId").GetString()!);
        world.Gate.TrySetResult();
        await world.RunFinished();
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(PlaybookRunWorld.OtherOid), runId);

        AssertTheUniformNotFound(result, expected);
        world.AnalysisReads.Should().BeEmpty("another caller's run reaches no analysis read");

        // Control: the starter does get the results.
        var own = await world.ResultsToolAsync(world.CallerContext(), runId);
        Serialized(own).Should().Contain(Secret);
    }

    [Fact(DisplayName = "037b auth: another caller's stored run is the same not-found; its link is never followed")]
    public async Task AnotherCallersStoredRun_IsTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.OtherOid, ("sprk_analysis", analysisId), documentId);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerReadsAnalysis(analysisId, documentId, Secret);
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        AssertTheUniformNotFound(result, expected);
        world.AnalysisReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "037b auth: a run whose subject (the analysis's document) the caller cannot read is the same not-found; the analysis row is never read")]
    public async Task UnreadableSubject_IsTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var unreadableDocument = Guid.NewGuid(); // never granted
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), unreadableDocument);
        world.SeedAnalysisAnchor(analysisId, unreadableDocument);
        world.CallerReadsAnalysis(analysisId, unreadableDocument, Secret);
        world.CallerReadsDocuments((unreadableDocument, Secret));
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        AssertTheUniformNotFound(result, expected);
        world.AnalysisReads.Should().BeEmpty("the analysis-read rule refuses before the caller's own read");
    }

    [Theory(DisplayName = "037b auth: an analysis the caller's own Dataverse read refuses (403 or 404) is the same not-found")]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    public async Task UnreadableAnalysis_IsTheNotFound(int status)
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerAnalysisReadRefused(analysisId, status);
        world.CallerReadsDocuments((documentId, Secret));
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        AssertTheUniformNotFound(result, expected);
    }

    [Fact(DisplayName = "037b auth: an analysis the rule cannot find (no row for the app-only anchor read) is the same not-found")]
    public async Task AnalysisTheRuleCannotFind_IsTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId);
        world.CallerReadsAnalysis(analysisId, documentId, Secret); // the caller's read would answer: the rule must still refuse first
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        AssertTheUniformNotFound(result, expected);
        world.AnalysisReads.Should().BeEmpty();
    }

    [Fact(DisplayName = "037b auth: a run with no analysis whose first document the caller can no longer read is the same not-found")]
    public async Task RunWithNoAnalysis_UnreadableDocument_IsTheNotFound()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, regarding: null, documentId);
        world.CallerReadsDocuments(); // the caller reads none of them
        var expected = await StatusToolNotFoundAsync(world);

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        AssertTheUniformNotFound(result, expected);
    }

    [Fact(DisplayName = "037b auth: a source document the caller cannot read is left out of the list, never named")]
    public async Task UnreadableSecondDocument_IsLeftOut()
    {
        await using var world = await PlaybookRunWorld.StartAsync();
        var documentId = world.Host.ReadableDocument();
        var hidden = Guid.NewGuid();
        var analysisId = Guid.NewGuid();
        var runId = world.SeedStoredRun(Guid.NewGuid(), PlaybookRunState.Completed, PlaybookRunWorld.CallerOid, ("sprk_analysis", analysisId), documentId, hidden);
        world.SeedAnalysisAnchor(analysisId, documentId);
        world.CallerReadsAnalysis(analysisId, documentId, "Text.");
        world.CallerReadsDocuments((documentId, "Readable.docx"));

        var result = await world.ResultsToolAsync(world.CallerContext(), runId);

        result.IsError.Should().NotBe(true, result.StructuredContent?.ToString());
        result.StructuredContent!.Value.GetProperty("documents").EnumerateArray().Select(d => d.GetProperty("documentId").GetString())
            .Should().Equal([documentId.ToString("D")]);
        Serialized(result).Should().NotContain(hidden.ToString("D"));
    }

    [Fact(DisplayName = "037b auth: no signed-in caller is UserContextRequired before any read; nothing falls back to an app-only client")]
    public async Task NoCaller_IsUserContextRequired()
    {
        await using var world = await PlaybookRunWorld.StartAsync();

        var result = await world.ResultsToolAsync(world.CallerContext(principal: new ClaimsPrincipal(new ClaimsIdentity())), Guid.NewGuid());

        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(DataverseUserClientErrorCodes.UserContextRequired);
        world.DataverseUser.Verify(d => d.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never());
        world.RunRecords.VerifyNoOtherCalls();
    }

    // ---- startup: app-only or the app does not start ----------------------------------------------------------------------------

    [Fact(DisplayName = "037b startup: the tool as built passes; declared model-visible, with no visibility, or with an empty list, startup refuses it")]
    public void Startup_RefusesTheToolUnlessAppOnly()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var definition = typeof(GetPlaybookRunResultsTool);

        var asBuilt = new GetPlaybookRunResultsTool().Create(services);
        var accept = () => McpToolAuthorization.ValidateTool(asBuilt, definition);
        accept.Should().NotThrow();
        McpToolAuthorization.AppOnlyTools.Should().Contain(GetPlaybookRunResultsTool.ToolName);

        foreach (var meta in new JsonObject?[]
                 {
                     new() { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("model", "app") } },
                     new() { ["ui"] = new JsonObject { ["visibility"] = new JsonArray("model") } },
                     new() { ["ui"] = new JsonObject { ["visibility"] = new JsonArray() } },
                     new() { ["ui"] = new JsonObject() },
                     null,
                 })
        {
            var tool = new GetPlaybookRunResultsTool().Create(services);
            tool.ProtocolTool.Meta = meta;
            var refuse = () => McpToolAuthorization.ValidateTool(tool, definition);
            refuse.Should().Throw<InvalidOperationException>().WithMessage("*AppOnlyTools*", meta?.ToJsonString() ?? "null");
        }
    }

    [Fact(DisplayName = "037b startup (must-not-fire): a read-only model-visible tool not in AppOnlyTools still starts")]
    public void Startup_LeavesOtherReadOnlyToolsAlone()
    {
        var services = new ServiceCollection().BuildServiceProvider();

        var status = new GetPlaybookRunTool().Create(services);
        var act = () => McpToolAuthorization.ValidateTool(status, typeof(GetPlaybookRunTool));

        act.Should().NotThrow();
        McpToolAuthorization.AppOnlyTools.Should().NotContain(GetPlaybookRunTool.ToolName);
    }
}
