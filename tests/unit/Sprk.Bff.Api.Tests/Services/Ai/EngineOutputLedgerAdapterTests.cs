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
/// build on — the FR-P3-01 (task 040) re-point onto the REAL reverse-resolved Binding row
/// (<c>{bindingId}@t{n}</c> + catalog identity), the <c>{playbookId}@t{n}</c> +
/// <c>engine-playbook</c> DEGRADE identity for playbooks no Binding row targets,
/// identifiers-only sourceRefs (NFR-07), the failed-runs-never-enter-the-ledger
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
            citationChunkIds: new[] { "chunk-1", "chunk-2", "chunk-1" /* duplicate id */ });

        var entry = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, result);

        // Addressable per the task-021 key contract: {bindingId}@t{n}. No Binding row
        // targets this playbook (routing mock resolves null), so the FR-P3-01 degrade
        // identity applies: bindingId = the invoked playbook (the frozen flow's identity).
        entry.Should().NotBeNull();
        entry!.Key.Should().Be(SessionLedger.BuildOutputKey(PlaybookId.ToString(), 1));
        entry.BindingId.Should().Be(PlaybookId.ToString());
        entry.UcId.Should().Be(EngineOutputLedgerAdapter.InterimConsumerType,
            "engine-origin entries for playbooks with no Binding row carry the 'engine-playbook' degrade marker (FR-P3-01)");
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

        // sourceRefs: citation ids ONLY (NFR-07), de-duplicated. (The EngineRunOutput
        // contract carries chunk IDS only by construction since task 044 — excerpts can
        // no longer reach the adapter at the type level.)
        entry.SourceRefs.Should().BeEquivalentTo(new[] { "chunk-1", "chunk-2" });
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

    // ─── FR-P3-01 (task 040): registered playbooks key on the REAL reverse-resolved Binding ────

    [Fact]
    public async Task RecordAsync_PlaybookWithBindingRow_KeysOnResolvedBindingIdentity()
    {
        // Shaped like the seeded spaarkedev1 insights-ask default row: an enabled
        // sprk_playbookconsumer row targets the invoked playbook.
        var resolvedBindingId = Guid.Parse("f32a7931-8079-f111-ab0e-7ced8ddc4cc6");
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.GetBindingByPlaybookIdAsync(PlaybookId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Binding
            {
                BindingId = resolvedBindingId,
                ConsumerType = ConsumerTypes.InsightsAsk,
                ConsumerCode = "default",
                PlaybookId = PlaybookId,
                Ucid = "UC-C-2",
                Disposition = BindingDisposition.Informational,
            });
        var sessionManager = new StubSessionManager(BuildSession());
        var sut = CreateSut(sessionManager, routing.Object);

        var entry = await sut.RecordAsync(TenantId, SessionGuid, PlaybookId, BuildSuccessResult());

        // The ledger entry carries the CATALOG identity of the run — not the interim one.
        entry.Should().NotBeNull();
        entry!.Key.Should().Be(SessionLedger.BuildOutputKey(resolvedBindingId.ToString(), 1),
            "FR-P3-01: engine-origin entries for registered playbooks key on the resolved Binding row id");
        entry.BindingId.Should().Be(resolvedBindingId.ToString());
        entry.UcId.Should().Be("UC-C-2",
            "the resolved row's sprk_ucid replaces the interim 'engine-playbook' marker");
        entry.Disposition.Should().Be("informational");
        entry.Payload.GetProperty("playbookId").GetGuid().Should().Be(PlaybookId,
            "the payload still records WHICH playbook ran (run identity is payload data, key identity is the Binding)");
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

    // ─── Failed runs never enter the ledger (ADR-040) ───────────────────────────────────────────
    // Task 044: the success gate moved to the CALLER contract — EngineRunOutput carries
    // successful outputs only by construction (AnalysisExecutionHandler records only after
    // a successful engine drain and before any render).

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
            CreateNoRowRouting(),
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
            CreateNoRowRouting(),
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

    private static EngineOutputLedgerAdapter CreateSut(
        StubSessionManager sessionManager,
        IConsumerRoutingService? consumerRouting = null) => new(
        sessionManager,
        new OutputRouter(sessionManager, Mock.Of<ILogger<OutputRouter>>()),
        consumerRouting ?? CreateNoRowRouting(),
        Mock.Of<ILogger<EngineOutputLedgerAdapter>>());

    /// <summary>
    /// Routing double for the FR-P3-01 degrade path: no Binding row targets any playbook
    /// (reverse lookup resolves null), so the adapter records under the interim identity.
    /// </summary>
    private static IConsumerRoutingService CreateNoRowRouting()
    {
        var routing = new Mock<IConsumerRoutingService>();
        routing
            .Setup(r => r.GetBindingByPlaybookIdAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Binding?)null);
        return routing.Object;
    }

    /// <summary>Session id in the ChatSessionManager "N" format — the adapter's Guid → session-id resolution contract.</summary>
    private static ChatSession BuildSession() => new(
        SessionId: SessionGuid.ToString("N"),
        TenantId: TenantId,
        DocumentId: null,
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>());

    private static EngineRunOutput BuildSuccessResult(
        string? text = "Aggregated engine composite output.",
        string? structuredJson = null,
        IReadOnlyList<string>? citationChunkIds = null)
    {
        JsonElement? structured = null;
        if (structuredJson is not null)
        {
            using var doc = JsonDocument.Parse(structuredJson);
            structured = doc.RootElement.Clone();
        }

        return new EngineRunOutput
        {
            RunId = Guid.NewGuid(),
            TextContent = text,
            StructuredData = structured,
            CitationChunkIds = citationChunkIds ?? Array.Empty<string>(),
            Confidence = 0.9,
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
