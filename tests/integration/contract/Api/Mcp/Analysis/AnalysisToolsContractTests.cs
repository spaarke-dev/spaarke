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
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ModelContextProtocol.Protocol;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Api.Mcp.LegalOperations.Analysis;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Handoff;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Api.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Mcp.Analysis;

public sealed class AnalysisToolsContractTests
{
    internal const string EnvironmentUrl = "https://spaarke-test.crm.dynamics.com";

    /// <summary>THE analysis start the tool is given in production (task 037a: the create core moved here from AnalysisEndpoints).</summary>
    internal static readonly IAnalysisStartAi Starts = new AnalysisStartService();
    private const string Documents = "sprk_documents";
    private const AccessRights ReadAppendTo = AccessRights.Read | AccessRights.AppendTo;

    private static readonly Guid DocumentId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000011");
    private static readonly Guid PlaybookId = Guid.Parse("0a0a0a0a-0031-4031-8031-000000000012");

    internal static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    internal static Task<CallToolResult> StartAsync(AnalysisAuthHost host, object arguments) =>
        host.WithCallerContextAsync(ctx => StartAnalysisTool.RunAsync(
            Args(arguments), ctx, ctx.RequestServices.GetRequiredService<ITenantCache>(), Starts, EnvironmentUrl, NullLogger.Instance, CancellationToken.None));

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

    // ---- start: the retry key, verifier fixes ----------------------------------------------------------------------------------

    private static Task<CallToolResult> StartWithAsync(
        AnalysisAuthHost host, object arguments, Func<HttpContext, ITenantCache, ITenantCache> wrap, CancellationToken ct) =>
        host.WithCallerContextAsync(ctx => StartAnalysisTool.RunAsync(
            Args(arguments), ctx, wrap(ctx, ctx.RequestServices.GetRequiredService<ITenantCache>()), Starts, EnvironmentUrl, NullLogger.Instance, ct));

    [Fact(DisplayName = "031 fix F1: a cancelled caller after the create still leaves the retry key, so the same-key retry replays and ONE row exists")]
    public async Task Start_CallerCancelsAfterTheCreate_RetryReplays_OneRow()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        using var cancelled = new CancellationTokenSource();
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "cancel-1" };

        // The create has written; the caller goes away as the key is about to be stored. A cache that honours the token (as Redis does) throws.
        var first = await StartWithAsync(
            host, args,
            (_, inner) => new HookedTenantCache(inner)
            {
                BeforeSet = (resource, ct) =>
                {
                    if (resource == StartAnalysisTool.RetryResource)
                    {
                        cancelled.Cancel();
                        ct.ThrowIfCancellationRequested();
                    }

                    return Task.CompletedTask;
                },
            },
            cancelled.Token);
        var retry = await StartAsync(host, args);

        first.IsError.Should().NotBe(true, "the row was created: its id is returned even though the caller went away");
        retry.IsError.Should().NotBe(true);
        retry.StructuredContent!.Value.GetProperty("replayed").GetBoolean().Should().BeTrue();
        retry.StructuredContent!.Value.GetProperty("analysisId").GetString()
            .Should().Be(first.StructuredContent!.Value.GetProperty("analysisId").GetString());
        host.AppOnlyCreates.Should().ContainSingle();
    }

    [Fact(DisplayName = "031 fix K1: a call that took the lock after the first finished re-reads the entry and replays, creating nothing")]
    public async Task Start_FirstFinishedBetweenReadAndLock_Replays()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        var earlierId = Guid.NewGuid();

        var result = await StartWithAsync(
            host, new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "race-1" },
            (ctx, inner) => new HookedTenantCache(inner)
            {
                // The first call finishes (stores its entry) just as this call's lock is written.
                BeforeSet = async (resource, ct) =>
                {
                    if (resource == StartAnalysisTool.LockResource)
                    {
                        var fingerprint = StartAnalysisTool.FingerprintOf(new StartAnalysisTool.StartInput(DocumentId, "A", null, "race-1"));
                        await inner.SetAsync(
                            AnalysisAuthHost.TenantId, StartAnalysisTool.RetryResource, StartAnalysisTool.RetryIdOf(ctx, "race-1"), 1,
                            new StartAnalysisTool.RetryEntry(earlierId, DocumentId, fingerprint), ct: ct);
                    }
                },
            },
            CancellationToken.None);

        result.IsError.Should().NotBe(true);
        result.StructuredContent!.Value.GetProperty("replayed").GetBoolean().Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("analysisId").GetString().Should().Be(earlierId.ToString("D"));
        host.AppOnlyCreates.Should().BeEmpty("the entry was there once the lock was ours: no second create");
    }

    [Fact(DisplayName = "031 fix F1: a replay does not say the analysis has not been run, and carries no warningCount; the first call does")]
    public async Task Start_Replay_SaysNothingAboutRunState_AndNoWarningCount()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "text-1" };

        var first = await StartAsync(host, args);
        var replay = await StartAsync(host, args);

        var firstText = ((TextContentBlock)first.Content.Single()).Text;
        firstText.Should().Contain("It has not been run yet");
        first.StructuredContent!.Value.GetProperty("warningCount").GetInt32().Should().Be(0);
        var replayText = ((TextContentBlock)replay.Content.Single()).Text;
        replayText.Should().Contain("Open it to see its status.").And.NotContain("not been run");
        replay.StructuredContent!.Value.TryGetProperty("warningCount", out _).Should().BeFalse();
        replay.StructuredContent!.Value.GetProperty("replayed").GetBoolean().Should().BeTrue();
    }

    [Fact(DisplayName = "031 fix F2: the same key from a different caller of the same tenant is a separate start (per-caller key)")]
    public async Task Start_SameKeyDifferentCaller_IsAnotherStart()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "k" };
        var otherOid = Guid.Parse("6f0c1a52-0000-4000-8000-0000000000b2").ToString();

        var callerA = await StartAsync(host, args);
        var callerB = await host.WithCallerContextAsync(ctx =>
        {
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", otherOid), new Claim("tid", AnalysisAuthHost.TenantId)], AnalysisAuthzTestAuthHandler.SchemeName));
            return StartAnalysisTool.RunAsync(
                Args(args), ctx, ctx.RequestServices.GetRequiredService<ITenantCache>(), Starts, EnvironmentUrl, NullLogger.Instance, CancellationToken.None);
        });

        callerA.IsError.Should().NotBe(true);
        callerB.IsError.Should().NotBe(true, callerB.StructuredContent.ToString());
        host.AppOnlyCreates.Should().HaveCount(2, "caller B's key 'k' is not caller A's");
        var a = callerA.StructuredContent!.Value;
        var b = callerB.StructuredContent!.Value;
        b.GetProperty("analysisId").GetString().Should().NotBe(a.GetProperty("analysisId").GetString());
        b.GetProperty("replayed").GetBoolean().Should().BeFalse();
    }

    [Fact(DisplayName = "031 fix F2: the cache id is the caller's oid and a hash of the key, never the key (per-caller key shape unchanged)")]
    public async Task RetryId_IsOidAndKeyHash_NeverTheKey()
    {
        await using var host = await AnalysisAuthHost.StartAsync();

        var id = await host.WithCallerContextAsync(ctx => Task.FromResult(StartAnalysisTool.RetryIdOf(ctx, "my-secret-key")));

        id.Should().StartWith(AnalysisAuthzTestAuthHandler.CallerObjectId + ":");
        id.Should().NotContain("my-secret-key");
        id[(AnalysisAuthzTestAuthHandler.CallerObjectId.Length + 1)..].Should().MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact(DisplayName = "031 fix K: a create that faults is the tool's analysis.create_failed 500 with fixed text, nothing remembered")]
    public async Task Start_CreateFault_IsCreateFailed_AndNotRemembered()
    {
        await using var host = Readied(await AnalysisAuthHost.StartAsync());
        host.AppOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), It.IsAny<Guid?>()))
            .ThrowsAsync(new InvalidOperationException("dataverse said: secret detail"));
        var args = new { documentId = DocumentId.ToString(), name = "A", idempotencyKey = "fault-1" };

        var result = await StartAsync(host, args);

        result.IsError.Should().BeTrue();
        result.StructuredContent!.Value.GetProperty("reasonCode").GetString().Should().Be(AnalysisEndpoints.CreateFailedReasonCode);
        result.StructuredContent!.Value.GetProperty("status").GetInt32().Should().Be(500);
        JsonSerializer.Serialize(result).Should().NotContain("secret detail");
    }

    // ---- descriptions (D-209) --------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 fix D-209: the start tool says JPS playbook (AI analysis recipe), rules out legal / contract-review playbooks, and fits 1,024 characters")]
    public void Start_Description_QualifiesPlaybook()
    {
        StartAnalysisTool.Description.Length.Should().BeLessThanOrEqualTo(1024);
        StartAnalysisTool.Description.Should()
            .Contain("JPS playbook (AI analysis recipe)")
            .And.Contain("legal or contract-review playbooks (Markdown or Microsoft contract-review playbooks)")
            .And.Contain("Not for:");
        GetAnalysisTool.Description.Length.Should().BeLessThanOrEqualTo(1024);
    }

    [Fact(DisplayName = "031 fix D-209: the playbookId schema text and the refusal text say JPS playbook, never a bare playbook")]
    public async Task Start_SchemaAndRefusalText_QualifyPlaybook()
    {
        await using var host = await AnalysisAuthHost.StartAsync();
        var tool = new StartAnalysisTool().Create(new ServiceCollection().BuildServiceProvider());

        var schema = tool.ProtocolTool.InputSchema.GetProperty("properties").GetProperty("playbookId").GetProperty("description").GetString();
        var refusal = StartAnalysisTool.ErrorOf(await StartRefusedResultAsync(host));

        schema.Should().Contain("JPS playbook (AI analysis recipe)");
        refusal.Message.Should().Contain("JPS playbook (AI analysis recipe)");
    }

    private static async Task<AnalysisStartResult> StartRefusedResultAsync(AnalysisAuthHost host)
    {
        // A document the caller may only Read: the route's insufficient_rights 403.
        host.Access.Grant(Documents, DocumentId, AccessRights.Read);
        return await host.WithCallerContextAsync(ctx => Starts.StartAsCallerAsync(ctx, "A", DocumentId, null, CancellationToken.None));
    }

    // ---- get ---------------------------------------------------------------------------------------------------------------

    [Fact(DisplayName = "031 fix F1: an analysis that was never run is NOT reported Completed: the real loader's fabricated status, name and completion time are not shown")]
    public async Task Get_NeverRunAnalysis_OverTheRealLoader_DoesNotSayCompleted()
    {
        var analysisId = Guid.NewGuid();
        // The REAL AnalysisOrchestrationService.GetAnalysisAsync over the REAL AnalysisDocumentLoader (the doubles are Dataverse and the cache).
        await using var host = await AnalysisAuthHost.StartAsync(services => services.AddSingleton<IAnalysisOrchestrationService>(sp =>
            new AnalysisOrchestrationService(
                null!, null!, null!, null!, sp, null!, sp.GetRequiredService<AnalysisDocumentLoader>(), null!, null!,
                NullLogger<AnalysisOrchestrationService>.Instance)));
        host.SeedAnalysis(analysisId, new() { ["sprk_documentid"] = ("sprk_document", DocumentId) });
        host.Access.Grant(Documents, DocumentId, AccessRights.Read);
        host.Analysis
            .Setup(a => a.GetAnalysisAsync(analysisId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisEntity
            {
                Id = analysisId,
                Name = "Analysis - NDA.pdf",
                DocumentId = DocumentId,
                StatusCode = 1, // Active: the value the create writes; nothing ever moves it
                CreatedOn = new DateTime(2026, 11, 26, 8, 0, 0, DateTimeKind.Utc),
                ModifiedOn = new DateTime(2026, 11, 26, 9, 30, 0, DateTimeKind.Utc),
            });

        var result = await GetAsync(host, analysisId);

        result.IsError.Should().NotBe(true, result.StructuredContent.ToString());
        var body = result.StructuredContent!.Value;
        var all = body.GetRawText() + ((TextContentBlock)result.Content.Single()).Text;
        all.Should().NotContain("Completed").And.NotContain("completed").And.NotContain("Analysis - NDA.pdf");
        body.TryGetProperty("status", out _).Should().BeFalse();
        body.TryGetProperty("completedOn", out _).Should().BeFalse();
        body.TryGetProperty("documentName", out _).Should().BeFalse();
        body.GetProperty("startedOn").GetString().Should().Be("2026-11-26T08:00:00Z", "the row's creation time is real");
        body.GetProperty("link").GetString().Should().Be(new HandoffUrlBuilder(EnvironmentUrl).BuildAnalysisWorkspaceUrl(analysisId, DocumentId));
        ((TextContentBlock)result.Content.Single()).Text.Should().Contain("status is not reported");
    }

    [Fact(DisplayName = "031 fix F1 (precondition, D-031-2): the route's own loader does fabricate Completed for that row; delete this when the loader is fixed")]
    public async Task Precondition_TheRestLoaderFabricatesCompleted_ForANeverRunRow()
    {
        var analysisId = Guid.NewGuid();
        await using var host = await AnalysisAuthHost.StartAsync();
        host.Analysis
            .Setup(a => a.GetAnalysisAsync(analysisId.ToString(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnalysisEntity { Id = analysisId, Name = "Analysis - NDA.pdf", DocumentId = DocumentId, StatusCode = 1 });

        var loaded = await host.WithCallerContextAsync(ctx =>
            ctx.RequestServices.GetRequiredService<AnalysisDocumentLoader>().GetOrReloadFromDataverseAsync(analysisId, CancellationToken.None));

        loaded.Status.Should().Be("Completed", "this is the REST contract task 031 does not change; the tool must not repeat it");
        loaded.DocumentName.Should().Be("Analysis - NDA.pdf");
    }

    [Fact(DisplayName = "031 get: a readable analysis is the slim route analysis: document, action, at most 500 characters of output, the link; no status, no chat")]
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
                CompletedOn = new DateTime(2026, 11, 26, 9, 0, 0, DateTimeKind.Utc),
            });

        var result = await GetAsync(host, analysisId);

        result.IsError.Should().NotBe(true, result.StructuredContent.ToString());
        var body = result.StructuredContent!.Value;
        body.GetProperty("analysisId").GetString().Should().Be(analysisId.ToString("D"));
        // Whatever the route's read says (here "Completed", "NDA.pdf", a completion time), the tool shows none of the three (D-031-2).
        body.TryGetProperty("status", out _).Should().BeFalse();
        body.TryGetProperty("documentName", out _).Should().BeFalse();
        body.TryGetProperty("completedOn", out _).Should().BeFalse();
        body.GetProperty("actionName").GetString().Should().Be("Review NDA");
        body.GetProperty("startedOn").GetString().Should().Be("2026-11-26T08:00:00Z");
        var summary = body.GetProperty("summary").GetString()!;
        summary.Length.Should().BeLessThanOrEqualTo(GetAnalysisTool.SummaryMaxChars);
        summary.Should().StartWith("Line one line two").And.NotContain("\n");
        body.GetProperty("link").GetString().Should().Be(new HandoffUrlBuilder(EnvironmentUrl).BuildAnalysisWorkspaceUrl(analysisId, DocumentId));
        var all = body.GetRawText() + ((TextContentBlock)result.Content.Single()).Text;
        all.Should().NotContain("chat-secret").And.NotContain("working draft").And.NotContain("tokenUsage").And.NotContain("Completed");
    }
}

/// <summary>
/// An <see cref="ITenantCache"/> that delegates to another and runs <see cref="BeforeSet"/> first on every write (the resource it writes),
/// so a test can make a write happen at an exact moment (a caller going away, another call finishing).
/// </summary>
internal sealed class HookedTenantCache(ITenantCache inner) : ITenantCache
{
    public Func<string, CancellationToken, Task>? BeforeSet { get; init; }

    public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
        inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);

    public async Task SetAsync<T>(
        string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
    {
        if (BeforeSet is { } before)
        {
            await before(resource, ct);
        }

        await inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
    }

    public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default) =>
        inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);

    public Task<T> GetOrCreateAsync<T>(
        string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null,
        string cacheInstance = "default", CancellationToken ct = default) =>
        inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);
}
