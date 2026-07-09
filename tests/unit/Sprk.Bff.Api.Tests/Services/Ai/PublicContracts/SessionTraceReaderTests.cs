using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.PublicContracts;

/// <summary>
/// AIR2-038 / FR-A1-09 acceptance tests for the NET-NEW server decision-traceability
/// READ surface (<see cref="ISessionTraceReader"/> / <see cref="SessionTraceReader"/>).
///
/// KEEP-path integration style (ADR-038): exercises the REAL Redis-leg round-trip
/// (<see cref="ChatSessionManager.UpdateSessionCacheAsync"/> → System.Text.Json →
/// <see cref="ChatSessionManager.GetSessionAsync"/>) then projects through the reader —
/// no <c>Mock&lt;HttpMessageHandler&gt;</c>, no DI-registration/ctor-null tests. Pins:
///   - hard-refresh REHYDRATION: a trace survives persistence and is rebuilt from the
///     durable ledger via the server surface (closing the client buffer mount-gap);
///   - NFR-07: every projected event carries only sanctioned identifier/count fields;
///   - the ContextEnvelope-fingerprint entry carries id + count only (no content);
///   - TRUTHFULNESS: the projection emits events ONLY for real ledger markers — a marker
///     that never fired has no event (no fabrication).
/// </summary>
public class SessionTraceReaderTests
{
    private const string TenantId = "tenant-trace";

    private static ChatSessionManager NewManager(TrackingTenantCache cache) =>
        new(cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);

    private static ChatSession NewSession(
        string sessionId,
        IReadOnlyList<SessionToolChain>? toolChains = null,
        IReadOnlyList<SessionGate>? gates = null,
        IReadOnlyList<SessionContextFingerprint>? fingerprints = null)
    {
        var t0 = DateTimeOffset.Parse("2026-07-09T09:00:00+00:00");
        return new ChatSession(
            SessionId: sessionId,
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: t0,
            LastActivity: t0.AddMinutes(1),
            Messages: new List<ChatMessage>
            {
                new(MessageId: "m-1", SessionId: sessionId, Role: ChatMessageRole.User,
                    Content: "hi", TokenCount: 1, CreatedAt: t0, SequenceNumber: 0),
            })
        {
            ToolChains = toolChains,
            Gates = gates,
            ContextFingerprints = fingerprints,
        };
    }

    private static SessionToolChain OneCallChain(int turn) => new()
    {
        Turn = turn,
        CreatedAt = DateTimeOffset.Parse("2026-07-09T09:01:00+00:00"),
        Calls = new List<SessionToolCall>
        {
            new()
            {
                ToolId = "sprk_document_search",
                ArgsSummary = "matterId=123; top=5",
                ResultCount = 3,
                Citations = new[] { "d-1", "d-2" },
                DurationMs = 120,
            },
        },
    };

    private static SessionGate PendingWriteGate(int turn) => new()
    {
        GateId = "g-1",
        Kind = "confirmation",
        Status = "pending",
        Turn = turn,
        SideEffectClass = "write",
        BindingId = "chat-update",
        CreatedAt = DateTimeOffset.Parse("2026-07-09T09:02:00+00:00"),
    };

    private static SessionContextFingerprint Fingerprint(int turn) => new()
    {
        Turn = turn,
        FingerprintId = "fp-abc123",
        SliceCount = 4,
        CreatedAt = DateTimeOffset.Parse("2026-07-09T09:00:30+00:00"),
    };

    // -----------------------------------------------------------------------
    // Rehydration — the hard-refresh mount-gap close.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadTrace_AfterRoundTrip_RehydratesContextToolAndGate_InLineageOrder()
    {
        var cache = new TrackingTenantCache();
        var manager = NewManager(cache);
        var sessionId = Guid.NewGuid().ToString("N");

        var session = NewSession(
            sessionId,
            toolChains: new[] { OneCallChain(1) },
            gates: new[] { PendingWriteGate(1) },
            fingerprints: new[] { Fingerprint(1) });

        // Persist through the REAL write path, then read back through the server surface.
        await manager.UpdateSessionCacheAsync(session);
        var reader = new SessionTraceReader(manager);
        var trace = await reader.ReadTraceAsync(TenantId, sessionId);

        // Full lineage for turn 1: context → tool_chain → tool_call → gate.
        trace.Select(e => e.Kind).Should().ContainInOrder(
            TraceEventKind.Context,
            TraceEventKind.ToolChain,
            TraceEventKind.ToolCall,
            TraceEventKind.Gate);

        // Sequence is monotonic + 0-based (consumers order by it alone).
        trace.Select(e => e.Sequence).Should().BeInAscendingOrder();
        trace.First().Sequence.Should().Be(0);

        // The context leg is the rehydrated fingerprint (id + count).
        var contextEvent = trace.Single(e => e.Kind == TraceEventKind.Context);
        contextEvent.FingerprintId.Should().Be("fp-abc123");
        contextEvent.ContextSliceCount.Should().Be(4);

        // The gate leg is the rehydrated pending write gate.
        var gateEvent = trace.Single(e => e.Kind == TraceEventKind.Gate);
        gateEvent.Status.Should().Be("pending");
        gateEvent.SideEffectClass.Should().Be("write");
    }

    [Fact]
    public async Task AppendContextFingerprint_ThenReadTrace_SurfacesTheContextEvent()
    {
        // Exercises the write seam the Context Binder (task 053) will call.
        var cache = new TrackingTenantCache();
        var manager = NewManager(cache);
        var sessionId = Guid.NewGuid().ToString("N");
        await manager.UpdateSessionCacheAsync(NewSession(sessionId));

        await manager.AppendContextFingerprintAsync(
            TenantId, sessionId,
            new SessionContextFingerprint { Turn = 1, FingerprintId = "fp-live", SliceCount = 6 });

        var reader = new SessionTraceReader(manager);
        var trace = await reader.ReadTraceAsync(TenantId, sessionId);

        var contextEvent = trace.Should().ContainSingle(e => e.Kind == TraceEventKind.Context).Subject;
        contextEvent.FingerprintId.Should().Be("fp-live");
        contextEvent.ContextSliceCount.Should().Be(6);
    }

    // -----------------------------------------------------------------------
    // NFR-07 — no-content telemetry.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadTrace_EveryEvent_CarriesOnlySanctionedFields()
    {
        var cache = new TrackingTenantCache();
        var manager = NewManager(cache);
        var sessionId = Guid.NewGuid().ToString("N");
        await manager.UpdateSessionCacheAsync(NewSession(
            sessionId,
            toolChains: new[] { OneCallChain(1) },
            gates: new[] { PendingWriteGate(1) },
            fingerprints: new[] { Fingerprint(1) }));

        var trace = await new SessionTraceReader(manager).ReadTraceAsync(TenantId, sessionId);

        trace.Should().NotBeEmpty();
        foreach (var e in trace)
        {
            var json = JsonSerializer.SerializeToElement(e, TraceEventContract.SerializerOptions);
            TraceEventContract.CarriesOnlySanctionedFields(json)
                .Should().BeTrue("no TraceEvent may carry a field outside the NFR-07 sanctioned set");
        }
    }

    [Fact]
    public async Task ReadTrace_ContextFingerprintEvent_CarriesIdAndCountOnly_NoContentFields()
    {
        var cache = new TrackingTenantCache();
        var manager = NewManager(cache);
        var sessionId = Guid.NewGuid().ToString("N");
        await manager.UpdateSessionCacheAsync(NewSession(sessionId, fingerprints: new[] { Fingerprint(2) }));

        var trace = await new SessionTraceReader(manager).ReadTraceAsync(TenantId, sessionId);
        var contextEvent = trace.Should().ContainSingle(e => e.Kind == TraceEventKind.Context).Subject;

        contextEvent.FingerprintId.Should().Be("fp-abc123");
        contextEvent.ContextSliceCount.Should().Be(4);
        // No tool/gate content bleeds onto a context event.
        contextEvent.ArgsSummary.Should().BeNull();
        contextEvent.ToolId.Should().BeNull();
        contextEvent.GateId.Should().BeNull();
        contextEvent.OutputKey.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // Truthfulness — projection emits events ONLY for real markers.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadTrace_ProjectsExactlyTheRealMarkers_NoFabrication()
    {
        // The session REALLY has one tool call and nothing else — no gate, no
        // context fingerprint. The projection must NOT invent a gate or context
        // event that never occurred.
        var cache = new TrackingTenantCache();
        var manager = NewManager(cache);
        var sessionId = Guid.NewGuid().ToString("N");
        await manager.UpdateSessionCacheAsync(NewSession(sessionId, toolChains: new[] { OneCallChain(1) }));

        var trace = await new SessionTraceReader(manager).ReadTraceAsync(TenantId, sessionId);

        trace.Select(e => e.Kind).Should().Equal(TraceEventKind.ToolChain, TraceEventKind.ToolCall);
        trace.Should().NotContain(e => e.Kind == TraceEventKind.Gate);
        trace.Should().NotContain(e => e.Kind == TraceEventKind.Context);
    }

    // -----------------------------------------------------------------------
    // Read-safety — unknown session + Null-object.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadTrace_UnknownSession_ReturnsEmpty_NotThrow()
    {
        var reader = new SessionTraceReader(NewManager(new TrackingTenantCache()));
        var trace = await reader.ReadTraceAsync(TenantId, "does-not-exist");
        trace.Should().BeEmpty();
    }

    [Fact]
    public async Task NullSessionTraceReader_ReturnsEmpty()
    {
        ISessionTraceReader reader = new NullSessionTraceReader();
        var trace = await reader.ReadTraceAsync(TenantId, "any");
        trace.Should().BeEmpty();
    }
}
