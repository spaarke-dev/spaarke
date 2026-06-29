using System.Runtime.CompilerServices;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Sprk.Bff.Api.Infrastructure.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// Path A.5 integration tests for <see cref="SessionSummarizeOrchestrator"/> covering the
/// FR-17 acceptance scenarios from task 090 design (§5.2). These exercise the orchestrator
/// end-to-end through the canonical <see cref="IPlaybookOrchestrationService"/> dispatch
/// triangle with the SSE adapter producing wire-shape <see cref="AnalysisChunk"/> sequences.
///
/// <para>
/// <b>KEEP-protected per ADR-038 + tests/CLAUDE.md</b>: each [Fact] anchors a concrete
/// contract behavior that would regress without the test — routing-table dispatch
/// (FR-17 / FR-1R-05), kill-switch fail-fast (ADR-030 P3), and mid-stream failure
/// terminator (chat client requires explicit FromError terminator, not silent disconnect).
/// </para>
///
/// <para>
/// <b>Why orchestrator-level integration (not WebApplicationFactory)</b>: the chat-summarize
/// dispatch lives entirely below the endpoint boundary
/// (<see cref="Api.Ai.SummarizeSessionEndpoint"/> is a thin SSE writer that calls into the
/// orchestrator + writes its <see cref="AnalysisChunk"/> output verbatim to the wire).
/// Exercising the orchestrator with a real <see cref="ChatSessionManager"/> stub + the real
/// SSE adapter + a mock <see cref="IPlaybookOrchestrationService"/> at the dispatch
/// boundary covers the integration contract per ADR-038 §1 (integration-heavy pyramid)
/// while keeping the test honest (no transport-level mocks per ADR-038 ban B1).
/// </para>
///
/// <para>
/// <b>Scenarios covered</b> (3 of 7 from task 090 design §5.2 — per user task 091 instructions):
/// <list type="bullet">
///   <item>Scenario 1 — routing-table HIT → IPlaybookOrchestrationService dispatch (FR-17 / FR-1R-05)</item>
///   <item>Scenario 4 — AI kill-switch OFF → NullSessionSummarizeOrchestrator → FeatureDisabledException</item>
///   <item>Scenario 7 — mid-stream LLM failure → RunFailed event → terminal AnalysisChunk.FromError</item>
/// </list>
/// Additional scenarios (2 typed-options fallback, 3 fail-fast, 5 NFR-02 cap, 6 FR-04 interjection)
/// are covered by <see cref="SessionSummarizeOrchestratorTests"/> in the sibling file.
/// </para>
/// </summary>
public class SessionSummarizeOrchestratorPathA5IntegrationTest
{
    private const string TenantId = "tenant-integration";
    private const string SessionId = "session-integration";
    private const string FileId1 = "file-int-001";
    private const string FileId2 = "file-int-002";
    private const string ConfiguredChatSummarizePlaybookId = "44285d15-1360-f111-ab0b-70a8a59455f4";
    private static readonly Guid ResolvedChatSummarizePlaybookGuid =
        Guid.Parse(ConfiguredChatSummarizePlaybookId);

    /// <summary>
    /// Scenario 1 — routing-table HIT path (FR-17 / FR-1R-05). Validates that with a
    /// seeded sprk_playbookconsumer row, the orchestrator: (a) consults
    /// <see cref="IConsumerRoutingService"/> with <see cref="ConsumerTypes.ChatSummarize"/>,
    /// (b) dispatches through <see cref="IPlaybookOrchestrationService.ExecuteAsync"/> with
    /// the routing-table-returned playbook GUID, (c) emits a per-token
    /// <see cref="AnalysisChunk.FromContent"/> sequence as <c>NodeProgress</c> events arrive,
    /// and (d) emits a terminal <see cref="AnalysisChunk.Completed(DocumentAnalysisResult)"/>
    /// when the playbook's DeliverOutput node finalizes.
    /// </summary>
    [Fact]
    public async Task PathA5_RoutingTableHit_DispatchesViaOrchestrationServiceAndPreservesSseShape()
    {
        // Arrange — set up a routing-table HIT with a distinct GUID (proves dispatch uses
        // the routing-table value, not the typed-options fallback).
        var routingTableGuid = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var (sut, orchestrationMock, _, _, _) = CreateOrchestratorWithRoutingHit(routingTableGuid);

        // Playbook emits a realistic event sequence: RunStarted → NodeStarted → 3 NodeProgress
        // (per-token deltas) → terminal NodeCompleted with DeliverOutput → RunCompleted.
        var runId = Guid.NewGuid();
        var aiNodeId = Guid.NewGuid();
        var deliverNodeId = Guid.NewGuid();
        var structuredResult = JsonSerializer.SerializeToElement(new DocumentAnalysisResult
        {
            Summary = "Integration test summary",
            TlDr = new[] { "Point A", "Point B" },
            ParsedSuccessfully = true
        });

        orchestrationMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.RunStarted(runId, routingTableGuid, nodeCount: 2),
                PlaybookStreamEvent.NodeStarted(runId, routingTableGuid, aiNodeId, "ChatSummarizeAi"),
                PlaybookStreamEvent.NodeProgress(runId, routingTableGuid, aiNodeId, "Document "),
                PlaybookStreamEvent.NodeProgress(runId, routingTableGuid, aiNodeId, "discusses "),
                PlaybookStreamEvent.NodeProgress(runId, routingTableGuid, aiNodeId, "key terms."),
                PlaybookStreamEvent.NodeCompleted(runId, routingTableGuid, deliverNodeId, "DeliverOutput",
                    new NodeOutput
                    {
                        NodeId = deliverNodeId,
                        OutputVariable = "summary",
                        Success = true,
                        IsDeliverOutput = true,
                        TextContent = "Integration test summary",
                        StructuredData = structuredResult,
                        Metrics = new NodeExecutionMetrics()
                    }),
                PlaybookStreamEvent.RunCompleted(runId, routingTableGuid, new PlaybookRunMetrics
                {
                    TotalNodes = 2,
                    CompletedNodes = 2
                })));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: "executive",
            Path: SummarizeInvocationPath.DirectEndpoint,
            CorrelationId: "integration-corr-001");

        // Act — drive the orchestrator end-to-end.
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        // Assert — verify the SSE shape that SummarizeSessionEndpoint will write to the wire.
        chunks.Should().HaveCount(4,
            "3 NodeProgress per-token deltas + 1 terminal Completed chunk; lifecycle events (RunStarted/" +
            "NodeStarted/RunCompleted) filtered out by the SSE adapter per task 090 design §3.5");

        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Be("Document ", "per-token FromContent preserves chat-client progressive UX");
        chunks[1].Content.Should().Be("discusses ");
        chunks[2].Content.Should().Be("key terms.");

        chunks[3].Type.Should().Be("complete");
        chunks[3].Done.Should().BeTrue();
        chunks[3].Result.Should().NotBeNull();
        chunks[3].Result!.Summary.Should().Be("Integration test summary");
        chunks[3].Result!.TlDr.Should().BeEquivalentTo(new[] { "Point A", "Point B" });

        // Verify dispatch used the routing-table GUID (not the typed-options fallback).
        orchestrationMock.Verify(
            o => o.ExecuteAsync(
                It.Is<PlaybookRunRequest>(r => r.PlaybookId == routingTableGuid),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "FR-17 — dispatch routes through IPlaybookOrchestrationService with the routing-table-resolved GUID");
    }

    /// <summary>
    /// Scenario 4 — AI kill-switch OFF (compound-AI feature disabled).
    /// <see cref="NullSessionSummarizeOrchestrator"/> short-circuits at the first
    /// <c>MoveNextAsync()</c> with <see cref="Configuration.FeatureDisabledException"/>
    /// per ADR-030 P3. The endpoint catches this BEFORE setting SSE headers and emits a
    /// 503 ProblemDetails. This integration test confirms the kill-switch contract is
    /// preserved after the R7 refactor — the null subclass continues to throw without
    /// dereferencing any of the new DI dependencies (<see cref="IPlaybookOrchestrationService"/>,
    /// <see cref="IHttpContextAccessor"/>).
    /// </summary>
    [Fact]
    public async Task PathA5_NullKillSwitchSubclass_ThrowsFeatureDisabledOnFirstMoveNext()
    {
        // Arrange — construct the Null subclass directly (mirrors AnalysisServicesModule's
        // P3 fail-fast registration when compound-AI is OFF).
        var loggerMock = new Mock<ILogger<SessionSummarizeOrchestrator>>();
        var sut = new NullSessionSummarizeOrchestrator(loggerMock.Object);

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        // Act + Assert — first MoveNextAsync throws FeatureDisabledException.
        var act = async () => { await foreach (var _ in sut.SummarizeSessionFilesAsync(request)) { } };
        var thrown = await act.Should().ThrowAsync<FeatureDisabledException>();
        thrown.Which.ErrorCode.Should().Be("ai.summarize.disabled",
            "ADR-030 P3 contract — error code drives ProblemDetails errorCode extension in the endpoint");
        thrown.Which.Message.Should().Contain("Analysis:Enabled",
            "operator diagnostic must reference the gating config keys");
    }

    /// <summary>
    /// Scenario 7 — mid-stream LLM failure. Orchestration emits one NodeProgress chunk then
    /// a <see cref="PlaybookEventType.RunFailed"/> event. The SSE adapter MUST emit a
    /// terminal <see cref="AnalysisChunk.FromError"/> rather than silently terminating the
    /// stream. The chat client relies on the explicit error chunk to render a failure-state
    /// UX — a silent disconnect would leave the user staring at a partial summary.
    /// </summary>
    [Fact]
    public async Task PathA5_MidStreamRunFailed_EmitsTerminalFromErrorChunk()
    {
        // Arrange — routing-table HIT + orchestration emits partial stream then RunFailed.
        var routingTableGuid = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var (sut, orchestrationMock, _, _, _) = CreateOrchestratorWithRoutingHit(routingTableGuid);

        var runId = Guid.NewGuid();
        var nodeId = Guid.NewGuid();
        const string failureMessage = "Azure OpenAI service returned HTTP 503 after retries exhausted.";

        orchestrationMock
            .Setup(o => o.ExecuteAsync(
                It.IsAny<PlaybookRunRequest>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EventStream(
                PlaybookStreamEvent.NodeProgress(runId, routingTableGuid, nodeId, "Partial output before failure..."),
                PlaybookStreamEvent.RunFailed(runId, routingTableGuid, failureMessage)));

        var request = new SummarizeSessionFilesRequest(
            TenantId, SessionId, new[] { FileId1 }, StyleHint: null,
            Path: SummarizeInvocationPath.DirectEndpoint);

        // Act
        var chunks = await Collect(sut.SummarizeSessionFilesAsync(request));

        // Assert — partial token chunk followed by terminal error chunk.
        chunks.Should().HaveCount(2,
            "partial NodeProgress emission + terminal RunFailed must both reach the chat client");

        chunks[0].Type.Should().Be("text");
        chunks[0].Content.Should().Be("Partial output before failure...");
        chunks[0].Done.Should().BeFalse("intermediate chunks are not terminal");

        chunks[1].Type.Should().Be("error");
        chunks[1].Error.Should().Be(failureMessage,
            "SSE adapter MUST surface the orchestration-layer error message verbatim (no rewrites)");
        chunks[1].Done.Should().BeTrue("error chunks are terminal per the AnalysisChunk envelope contract");
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a fully-wired <see cref="SessionSummarizeOrchestrator"/> with a routing-table
    /// HIT on <see cref="ConsumerTypes.ChatSummarize"/> returning the supplied GUID. The
    /// session contains 1 file (avoids the FR-04 multi-file interjection so scenario assertions
    /// can focus on dispatch behavior).
    /// </summary>
    private static (
        SessionSummarizeOrchestrator Sut,
        Mock<IPlaybookOrchestrationService> Orchestration,
        Mock<IConsumerRoutingService> Routing,
        Mock<IPlaybookLookupService> Lookup,
        TestableChatSessionManager SessionManager)
        CreateOrchestratorWithRoutingHit(Guid playbookId)
    {
        var sessionManager = new TestableChatSessionManager
        {
            Session = BuildSession(FileId1)
        };

        var orchestration = new Mock<IPlaybookOrchestrationService>();
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.SetupGet(a => a.HttpContext).Returns(new DefaultHttpContext());
        var lookup = new Mock<IPlaybookLookupService>();
        var routing = new Mock<IConsumerRoutingService>();

        routing
            .Setup(r => r.ResolveAsync(
                ConsumerTypes.ChatSummarize,
                It.IsAny<string?>(),
                It.IsAny<IRoutingContext?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbookId);

        var workspaceOptions = Options.Create(new WorkspaceOptions
        {
            ChatSummarizePlaybookId = ConfiguredChatSummarizePlaybookId
        });
        var logger = new Mock<ILogger<SessionSummarizeOrchestrator>>();

        var sut = new SessionSummarizeOrchestrator(
            sessionManager,
            orchestration.Object,
            httpAccessor.Object,
            lookup.Object,
            routing.Object,
            workspaceOptions,
            logger.Object);

        return (sut, orchestration, routing, lookup, sessionManager);
    }

    private static async Task<List<AnalysisChunk>> Collect(IAsyncEnumerable<AnalysisChunk> source)
    {
        var list = new List<AnalysisChunk>();
        await foreach (var chunk in source)
        {
            list.Add(chunk);
        }
        return list;
    }

    private static async IAsyncEnumerable<PlaybookStreamEvent> EventStream(
        params PlaybookStreamEvent[] events)
    {
        foreach (var ev in events)
        {
            await Task.Yield();
            yield return ev;
        }
    }

    private static ChatSession BuildSession(params string[] fileIds)
    {
        var files = fileIds
            .Select(id => new ChatSessionFile(
                FileId: id,
                FileName: $"{id}.pdf",
                ContentType: "application/pdf",
                SizeBytes: 1024,
                SearchDocumentIdsCsv: $"doc-{id}-1",
                UploadedAt: DateTimeOffset.UtcNow))
            .ToList();
        return new ChatSession(
            SessionId: SessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastActivity: DateTimeOffset.UtcNow,
            Messages: Array.Empty<ChatMessage>(),
            HostContext: null,
            AdditionalDocumentIds: null,
            UploadedFiles: files);
    }

    private sealed class TestableChatSessionManager : ChatSessionManager
    {
        public TestableChatSessionManager() : base(
            cache: Mock.Of<ITenantCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
        }

        public ChatSession? Session { get; set; }

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(Session);
    }
}
