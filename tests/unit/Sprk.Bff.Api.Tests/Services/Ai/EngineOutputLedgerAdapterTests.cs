using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// FR-P1-05 acceptance tests for <see cref="EngineOutputLedgerAdapter"/> — the E-2
/// engine-output→ledger adapter (spaarke-ai-architecture-redesign-r1 task 024, ADR-040).
///
/// <para>
/// Module boundary per ADR-038: the router inside the adapter is the REAL
/// <see cref="OutputRouter"/> (the task-021 universal write path — proving engine-origin
/// entries flow through it, not around it); the only test double is the
/// <see cref="ChatSessionManager"/> persistence/lookup seam. The round-trip fact swaps the
/// double for a REAL <see cref="ChatSessionManager"/> over <see cref="TrackingTenantCache"/>
/// so the entry crosses genuine JSON wire bytes (the production Redis leg).
/// </para>
/// <para>
/// <b>KEEP rationale (maintain-class)</b>: each fact anchors an E-2 contract later phases
/// build on — the <c>{playbookId}@t{n}</c> interim addressing (P3 task 040 re-points the
/// Binding source but the key contract stays), the interim <c>engine-playbook</c> UcId
/// marker, identifiers-only sourceRefs (NFR-07), the failed-runs-never-enter-the-ledger
/// rule, and the session-scope boundary (record-context runs join at P3 FR-P3-08).
/// </para>
/// </summary>
public class EngineOutputLedgerAdapterTests
{
    private const string TenantId = "tenant-engine-ledger";

    private static readonly Guid PlaybookId = Guid.Parse("7d1e42c3-bbbb-f222-9c1f-81b9b6a1d62d");
    private static readonly Guid SessionGuid = Guid.Parse("3a5b70e2-cccc-f333-8d2a-92cac7b2e73e");

    // ─── Addressability: the FR-P1-05 acceptance criterion ─────────────────────────────────────

    [Fact]
    public async Task RecordAsync_SuccessfulCompositeRun_WritesAddressableLedgerEntry_ThroughUniversalWritePath()
    {
        var sessionManager = new StubSessionManager(BuildSession());
        var sut = CreateSut(sessionManager);
        var result = BuildSuccessResult(
            text: "Matter health composite summary.",
            structuredJson: """{"healthScore":0.82,"riskFactors":["budget-overrun"]}""",
            citations: new[]
            {
                new ToolResultCitation(ChunkId: "chunk-1", SourceName: "Brief.pdf", Excerpt: "verbatim excerpt MUST NOT leak"),
                new ToolResultCitation(ChunkId: "chunk-2", SourceName: "Policy.pdf"),
                new ToolResultCitation(ChunkId: "chunk-1", SourceName: "Brief.pdf"), // duplicate id
            });

        var entry = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, result);

        // Addressable per the task-021 key contract: {bindingId}@t{n} with the interim
        // binding identity = the invoked playbook (the frozen flow's identity).
        entry.Should().NotBeNull();
        entry!.Key.Should().Be(SessionLedger.BuildOutputKey(PlaybookId.ToString(), 1));
        entry.BindingId.Should().Be(PlaybookId.ToString());
        entry.UcId.Should().Be(EngineOutputLedgerAdapter.InterimConsumerType,
            "engine-origin entries carry the interim 'engine-playbook' marker until P3 FR-P3-01 Binding rows exist");
        entry.Disposition.Should().Be("informational",
            "the engine flow's actual rendering behavior today is the Assistant pane");

        // The write went THROUGH the task-021 seam (real OutputRouter → session persistence),
        // before RecordAsync returned (storage precedes rendering).
        sessionManager.PersistedSessions.Should().ContainSingle()
            .Which.Outputs.Should().ContainSingle(o => o.Key == entry.Key);

        // Payload carries the composite output + run identity.
        entry.Payload.GetProperty("runId").GetGuid().Should().Be(result.RunId);
        entry.Payload.GetProperty("playbookId").GetGuid().Should().Be(PlaybookId);
        entry.Payload.GetProperty("textContent").GetString().Should().Be("Matter health composite summary.");
        entry.Payload.GetProperty("structuredData").GetProperty("healthScore").GetDouble().Should().Be(0.82);
        entry.Payload.GetProperty("citationCount").GetInt32().Should().Be(3);

        // sourceRefs: citation ids ONLY (NFR-07), de-duplicated — never excerpts.
        entry.SourceRefs.Should().BeEquivalentTo(new[] { "chunk-1", "chunk-2" });
        entry.Payload.GetRawText().Should().NotContain("verbatim excerpt",
            "citation excerpts are content — the ledger payload carries the output + identifiers, sourceRefs carry ids");
    }

    [Fact]
    public async Task RecordAsync_SequentialRunsOfSamePlaybook_IncrementTurnOrdinal()
    {
        var sessionManager = new StubSessionManager(BuildSession());
        var sut = CreateSut(sessionManager);

        var first = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, BuildSuccessResult());
        var second = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, BuildSuccessResult());

        first!.Key.Should().Be(SessionLedger.BuildOutputKey(PlaybookId.ToString(), 1));
        second!.Key.Should().Be(SessionLedger.BuildOutputKey(PlaybookId.ToString(), 2),
            "sequential engine runs of the same composite must stay uniquely addressable (t{n} monotonic)");
    }

    // ─── Session-scope boundary (task-024 decision; P3 FR-P3-08 joins record-context runs) ────

    [Fact]
    public async Task RecordAsync_NoResolvableSession_ReturnsNullAndWritesNothing()
    {
        var sessionManager = new StubSessionManager(session: null);
        var sut = CreateSut(sessionManager);

        var entry = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, BuildSuccessResult());

        entry.Should().BeNull("record-context engine runs have no session ledger — they join at P3 FR-P3-08");
        sessionManager.PersistedSessions.Should().BeEmpty();
    }

    // ─── Failed runs never enter the ledger (ADR-040: the ledger carries OUTPUTS) ──────────────

    [Fact]
    public async Task RecordAsync_FailedEngineRun_Throws_AndWritesNothing()
    {
        var sessionManager = new StubSessionManager(BuildSession());
        var sut = CreateSut(sessionManager);
        var failed = new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = false,
            ErrorMessage = "Node failed.",
        };

        var act = () => sut.RecordAsync(TenantId, SessionGuid, PlaybookId, failed);

        await act.Should().ThrowAsync<ArgumentException>(
            "the ledger carries capability outputs, not failure diagnostics");
        sessionManager.PersistedSessions.Should().BeEmpty();
    }

    // ─── Round-trip: engine-origin entries survive the production serialization leg ────────────

    [Fact]
    public async Task RecordAsync_EngineOriginEntry_SurvivesProductionSerializationRoundTrip_AndIsRetrievableByKey()
    {
        // REAL ChatSessionManager over TrackingTenantCache/InMemoryTenantCache — the entry
        // crosses genuine System.Text.Json wire bytes exactly like the production Redis leg
        // (the Cosmos camelCase leg for the identical SessionOutput shape is contract-anchored
        // by ChatSessionLedgerRoundTripTests, task 001).
        var manager = new ChatSessionManager(
            new TrackingTenantCache(),
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);
        var session = BuildSession();
        await manager.UpdateSessionCacheAsync(session);

        var sut = new EngineOutputLedgerAdapter(
            manager,
            new OutputRouter(manager, Mock.Of<ILogger<OutputRouter>>()),
            Mock.Of<ILogger<EngineOutputLedgerAdapter>>());
        var result = BuildSuccessResult(
            text: "Composite output crossing the wire.",
            structuredJson: """{"nested":{"deep":[1,2,3]}}""");

        var written = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, result);

        // Retrieve on a FRESH read — deserializes a new instance from real JSON bytes.
        var restored = await manager.GetSessionAsync(TenantId, session.SessionId);
        restored.Should().NotBeNull();
        var restoredEntry = restored!.Outputs.Should()
            .ContainSingle(o => o.Key == written!.Key).Subject;

        restoredEntry.BindingId.Should().Be(PlaybookId.ToString());
        restoredEntry.UcId.Should().Be(EngineOutputLedgerAdapter.InterimConsumerType);
        restoredEntry.Turn.Should().Be(written!.Turn);
        restoredEntry.Payload.GetProperty("textContent").GetString()
            .Should().Be("Composite output crossing the wire.");
        restoredEntry.Payload.GetProperty("structuredData").GetProperty("nested")
            .GetProperty("deep").GetArrayLength().Should().Be(3);
    }

    // ─── NFR-07: identifiers only in telemetry — never content ─────────────────────────────────

    [Fact]
    public async Task RecordAsync_Telemetry_NeverLogsOutputContent()
    {
        const string sentinel = "PRIVILEGED-COMPOSITE-CONTENT-AcmeCorp-Trade-Secret-9917";
        var adapterLog = new CapturingLogger<EngineOutputLedgerAdapter>();
        var routerLog = new CapturingLogger<OutputRouter>();
        var sessionManager = new StubSessionManager(BuildSession());
        var sut = new EngineOutputLedgerAdapter(
            sessionManager,
            new OutputRouter(sessionManager, routerLog),
            adapterLog);
        var result = BuildSuccessResult(
            text: $"Summary containing {sentinel}.",
            structuredJson: $$"""{"finding":"{{sentinel}}"}""");

        await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, result);

        var allMessages = adapterLog.Messages.Concat(routerLog.Messages).ToList();
        allMessages.Should().NotBeEmpty();
        allMessages.Should().OnlyContain(m => !m.Contains(sentinel),
            "NFR-07 / ADR-015: telemetry carries identifiers, counts and sizes only — never output content");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static EngineOutputLedgerAdapter CreateSut(StubSessionManager sessionManager) => new(
        sessionManager,
        new OutputRouter(sessionManager, Mock.Of<ILogger<OutputRouter>>()),
        Mock.Of<ILogger<EngineOutputLedgerAdapter>>());

    /// <summary>Session id in the ChatSessionManager "N" format — the adapter's Guid → session-id resolution contract.</summary>
    private static ChatSession BuildSession() => new(
        SessionId: SessionGuid.ToString("N"),
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>());

    private static PlaybookInvocationResult BuildSuccessResult(
        string? text = "Aggregated engine composite output.",
        string? structuredJson = null,
        IReadOnlyList<ToolResultCitation>? citations = null)
    {
        JsonElement? structured = null;
        if (structuredJson is not null)
        {
            using var doc = JsonDocument.Parse(structuredJson);
            structured = doc.RootElement.Clone();
        }

        return new PlaybookInvocationResult
        {
            RunId = Guid.NewGuid(),
            Success = true,
            TextContent = text,
            StructuredData = structured,
            Citations = citations ?? Array.Empty<ToolResultCitation>(),
            Confidence = 0.9,
            Duration = TimeSpan.FromMilliseconds(200),
        };
    }

    /// <summary>
    /// Recording/lookup double over the production-virtual <see cref="ChatSessionManager"/>
    /// seams (same pattern as <c>OutputRouterTests.RecordingChatSessionManager</c>). Serves
    /// the configured session for the matching session-id and records every persisted write;
    /// reads always reflect the latest write so sequential turn allocation is exercised.
    /// </summary>
    private sealed class StubSessionManager : ChatSessionManager
    {
        private ChatSession? _current;

        public StubSessionManager(ChatSession? session) : base(
            cache: Mock.Of<ITenantCache>(),
            dataverseRepository: Mock.Of<IChatDataverseRepository>(),
            logger: Mock.Of<ILogger<ChatSessionManager>>(),
            persistence: null,
            cleanupSignal: null)
        {
            _current = session;
        }

        public List<ChatSession> PersistedSessions { get; } = new();

        public override Task<ChatSession?> GetSessionAsync(
            string tenantId, string sessionId, CancellationToken ct = default)
            => Task.FromResult(
                _current is not null
                && string.Equals(tenantId, _current.TenantId, StringComparison.Ordinal)
                && string.Equals(sessionId, _current.SessionId, StringComparison.Ordinal)
                    ? _current
                    : null);

        internal override Task UpdateSessionCacheAsync(ChatSession session, CancellationToken ct = default)
        {
            PersistedSessions.Add(session);
            _current = session;
            return Task.CompletedTask;
        }
    }

    /// <summary>Minimal capturing logger for the NFR-07 content-scan assertion.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
