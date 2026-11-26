// CONTRACT — spaarke_start_analysis and spaarke_get_analysis (spaarkeai-mcp-bff-api-r2 task 031, spec FR-06, NFR-06; ADR-002, ADR-015).
// KEEP path: contract.
//
// WHAT RUNS: the tools' RunAsync with an HttpContext of the REAL AnalysisAuthHost (the REAL MapAnalysisEndpoints, AuthorizationService
// and RecordCreationService; doubles only at the access-data boundary, the privilege seam, the owner resolver and the app-only writer).
// The REST route and the tool are driven in the SAME host, so "the tool-created row equals the REST-created row" compares two rows the
// one create wrote, captured at the app-only writer (RecordCreationService's boundary).

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Protocol;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Analysis;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Analysis;

public sealed class AnalysisToolsContractTests
{
    internal const string EnvironmentUrl = "https://spaarke-test.crm.dynamics.com";
    private const string Documents = "sprk_documents";
    private const AccessRights ReadAppendTo = AccessRights.Read | AccessRights.AppendTo;

    private static readonly Guid DocumentId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000011");
    private static readonly Guid PlaybookId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000012");

    internal static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    internal static Task<CallToolResult> StartAsync(AnalysisAuthHost host, object arguments) =>
        host.WithCallerContextAsync(ctx => StartAnalysisTool.RunAsync(
            Args(arguments), ctx, ctx.RequestServices.GetRequiredService<ITenantCache>(), EnvironmentUrl, NullLogger.Instance, CancellationToken.None));

    internal static Task<CallToolResult> GetAsync(AnalysisAuthHost host, Guid analysisId) =>
        host.WithCallerContextAsync(ctx => GetAnalysisTool.RunAsync(
            Args(new { analysisId }), ctx, EnvironmentUrl, NullLogger.Instance, CancellationToken.None));

    /// <summary>The Web API body the one create wrote, as sorted "key=value" lines, with the new id replaced by a marker.</summary>
    private static string RowOf(Dictionary<string, object?> fields, Guid createdId) =>
        string.Join("\n", fields
            .OrderBy(f => f.Key, StringComparer.Ordinal)
            .Select(f => $"{f.Key}={(f.Value is JsonElement e ? e.GetRawText() : JsonSerializer.Serialize(f.Value))}"))
            .Replace(createdId.ToString("D"), "{id}", StringComparison.OrdinalIgnoreCase);

    private static AnalysisAuthHost Readied(AnalysisAuthHost host)
    {
        host.Access.Grant(Documents, DocumentId, ReadAppendTo);
        host.SetPlaybook(PlaybookId, new PlaybookResponse { Id = PlaybookId, IsPublic = true });
        return host;
    }

    // ---- start: the same row as REST ---------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 start: the tool-created row equals the REST-created row for the same inputs, and the link opens the Analysis Workspace")]
    public async Task Start_WritesTheRestRow_AndLinksTheWorkspace()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/ai/analysis/create")
        {
            Content = JsonContent.Create(new { name = "Analysis - NDA.pdf", documentId = DocumentId, playbookId = PlaybookId }),
        };
        request.Headers.Add(AnalysisAuthzTestAuthHandler.CallerHeader, "1");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "caller-token");
        (await host.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await StartAsync(host, new
        {
            documentId = DocumentId.ToString(),
            name = "Analysis - NDA.pdf",
            playbookId = PlaybookId.ToString(),
            idempotencyKey = "k-1",
        });

        result.IsError.Should().NotBe(true, result.StructuredContent.ToString());
        host.AppOnlyCreates.Should().HaveCount(2);
        var (restTable, restId, restFields) = host.AppOnlyCreates[0];
        var (toolTable, toolId, toolFields) = host.AppOnlyCreates[1];
        toolTable.Should().Be(restTable);
        RowOf(toolFields, toolId).Should().Be(RowOf(restFields, restId), "FR-06: one create, the same row");

        var body = result.StructuredContent!.Value;
        body.GetProperty("analysisId").GetString().Should().Be(toolId.ToString("D"));
        body.GetProperty("replayed").GetBoolean().Should().BeFalse();
        body.GetProperty("link").GetString().Should().Be(new HandoffUrlBuilder(EnvironmentUrl).BuildAnalysisWorkspaceUrl(toolId, DocumentId));
        ((TextContentBlock)result.Content.Single()).Text.Should().Contain("has not been run").And.Contain(body.GetProperty("link").GetString()!);
    }

    [Fact(DisplayName = "031 start: the tool never runs the analysis (no orchestration or playbook engine call)")]
    public async Task Start_NeverCallsTheOrchestration()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());

        var result = await StartAsync(host, new { documentId = DocumentId.ToString(), name = "A", playbookId = PlaybookId.ToString(), idempotencyKey = "k" });

        result.IsError.Should().NotBe(true);
        host.Orchestration.VerifyNoOtherCalls();
        host.VerifyNoRunSideEffect();
    }

    // ---- start: the retry key ----------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 start: a retry with the same key and inputs creates ONE record and returns the first id")]
    public async Task Start_RetrySameKey_CreatesOneRecord()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "retry-1" };

        var first = await StartAsync(host, args);
        var second = await StartAsync(host, args);

        host.AppOnlyCreates.Should().ContainSingle();
        second.IsError.Should().NotBe(true);
        second.StructuredContent!.Value.GetProperty("analysisId").GetString()
            .Should().Be(first.StructuredContent!.Value.GetProperty("analysisId").GetString());
        second.StructuredContent!.Value.GetProperty("replayed").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "031 start: the same key with different inputs is refused and creates nothing more")]
    public async Task Start_SameKeyDifferentInputs_IsRefused()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        await StartAsync(host, new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "reuse" });

        var reused = await StartAsync(host, new { documentId = DocumentId.ToString(), name = "B", idempotencyKey = "reuse" });

        reused.IsError.Should().BeTrue();
        reused.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(StartAnalysisTool.KeyReusedReasonCode);
        host.AppOnlyCreates.Should().ContainSingle();
    }

    [Fact(DisplayName = "031 start: different keys are different starts (control)")]
    public async Task Start_DifferentKeys_CreateTwo()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());

        await StartAsync(host, new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "one" });
        await StartAsync(host, new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "two" });

        host.AppOnlyCreates.Should().HaveCount(2);
    }

    [Fact(DisplayName = "031 start: a start already in flight under the key is refused with try-again, and nothing is created")]
    public async Task Start_KeyInFlight_IsRefused()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        await host.WithCallerContextAsync(async ctx =>
        {
            var cache = ctx.RequestServices.GetRequiredService<ITenantCache>();
            await cache.SetAsync(AnalysisAuthHost.TenantId, StartAnalysisTool.LockResource, StartAnalysisTool.RetryIdOf(ctx, "busy"), 1, "locked");
            return 0;
        });

        var result = await StartAsync(host, new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "busy" });

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(StartAnalysisTool.KeyInProgressReasonCode);
        host.AssertServiceNotReached();
    }

    [Fact(DisplayName = "031 start: a refused start is not remembered, so the same key succeeds once the caller may start it")]
    public async Task Start_RefusedThenAllowed_SameKey_Creates()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        host.Access.Grant(Documents, DocumentId, AccessRights.Read);
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "later" };

        var refused = await StartAsync(host, args);
        host.Access.Grant(Documents, DocumentId, ReadAppendTo);
        var allowed = await StartAsync(host, args);

        refused.IsError.Should().BeTrue();
        allowed.IsError.Should().NotBe(true);
        allowed.StructuredContent!.Value.GetProperty("replayed").GetBoolean().Should().BeFalse();
        host.AppOnlyCreates.Should().ContainSingle();
    }

    // ---- get ---------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 get: a readable analysis is the slim route analysis: status, document, action, at most 500 characters of output, the link; no chat")]
    public async Task Get_Readable_IsTheSlimAnalysis()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var analysisId = Guid.NewGuid();
        host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", DocumentId) });
        host.Access.Grant(Documents, DocumentId, AccessRights.Read);
        host.Orchestration
            .Setup(o => o.GetAnalysisAsync(analysisId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisDetailResult
            {
                Id = analysisId,
                DocumentId = DocumentId,
                DocumentName = "NDA.pdf",
                Action = new AnalysisActionInfo(Guid.NewGuid(), "Review NDA"),
                Status = "Completed",
                FinalOutput = "Line one\nline two " + new string('x', 2000),
                WorkingDocument = "working draft",
                ChatHistory = [new ChatMessageInfo("user", "chat-secret", DateTime.UtcNow)],
                TokenUsage = new TokenUsage(10, 20),
                StartedOn = new DateTime(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc),
            });

        var result = await GetAsync(host, analysisId);

        result.IsError.Should().NotBe(true, result.StructuredContent.ToString());
        var body = result.StructuredContent!.Value;
        body.GetProperty("analysisId").GetString().Should().Be(analysisId.ToString("D"));
        body.GetProperty("status").GetString().Should().Be("Completed");
        body.GetProperty("documentName").GetString().Should().Be("NDA.pdf");
        body.GetProperty("actionName").GetString().Should().Be("Review NDA");
        body.GetProperty("startedOn").GetString().Should().Be("2026-11-26T08:00:00Z");
        var summary = body.GetProperty("summary").GetString()!;
        summary.Length.Should().BeLessThanOrEqualTo(GetAnalysisTool.SummaryMaxChars);
        summary.Should().StartWith("Line one line two").And.NotContain("\n");
        body.GetProperty("link").GetString().Should().Be(new HandoffUrlBuilder(EnvironmentUrl).BuildAnalysisWorkspaceUrl(analysisId, DocumentId));
        var all = body.GetRawText() + ((TextContentBlock)result.Content.Single()).Text;
        all.Should().NotContain("chat-secret").And.NotContain("working draft").And.NotContain("tokenUsage");
    }
}
