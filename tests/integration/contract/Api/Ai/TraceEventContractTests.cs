using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Ai;

/// <summary>
/// Contract test for <b>TraceEvent v1</b> (spec FR-A0-05, design D-F4). Locks the READ
/// projection over the ADR-040 session ledger's <see cref="SessionToolChain"/> /
/// <see cref="SessionToolCall"/> / <see cref="SessionGate"/> markers into a stable, ordered,
/// versioned, tolerant-reader stream and ASSERTS no-content telemetry (NFR-07).
/// </summary>
/// <remarks>
/// Self-contained by construction — a pure projection over in-memory ledger markers. No DI,
/// no <c>WebApplicationFactory</c>, no <c>Mock&lt;HttpMessageHandler&gt;</c>, no
/// DI-registration assertions (ADR-038 KEEP-path contract test). The behavior it protects:
/// the FR-A1-09 traceability view (task 038) and live plan narration bind to this shape, and
/// NFR-07 forbids prompt/response content ever appearing in the trace.
/// </remarks>
public class TraceEventContractTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.Parse("2026-07-08T12:00:00Z");

    /// <summary>A realistic, NFR-07-safe ledger slice: two turns, a chain of two tool calls, one gate.</summary>
    private static (List<TraceContextFingerprint> Context, List<SessionToolChain> Chains, List<SessionGate> Gates) RealLedgerMarkers()
    {
        var context = new List<TraceContextFingerprint>
        {
            new() { Turn = 1, FingerprintId = "ctx-fp-abc123", SliceCount = 4, CreatedAt = T0 },
        };

        var chains = new List<SessionToolChain>
        {
            new()
            {
                Turn = 1,
                CreatedAt = T0.AddSeconds(1),
                Calls = new List<SessionToolCall>
                {
                    new() { ToolId = "sprk_analysistool:document_search", ArgsSummary = "matterId=123; top=5", ResultCount = 5, Citations = new[] { "doc-1", "doc-2" }, DurationMs = 42 },
                    new() { ToolId = "sprk_analysistool:knowledge_retrieval", ArgsSummary = "topic=indemnity", ResultCount = 3, DurationMs = 30 },
                },
            },
        };

        var gates = new List<SessionGate>
        {
            new()
            {
                GateId = "gate-1",
                Kind = "confirmation",
                Status = "confirmed",
                Turn = 2,
                BindingId = "create-task",
                SideEffectClass = "write",
                OutputKey = "create-task@t2",
                CreatedAt = T0.AddSeconds(2),
                ResolvedAt = T0.AddSeconds(3),
            },
        };

        return (context, chains, gates);
    }

    [Fact]
    public void Project_FromRealLedgerMarkers_ProducesOrderedVersionedStream()
    {
        var (context, chains, gates) = RealLedgerMarkers();

        var stream = TraceEventProjection.Project(context, chains, gates);

        // Sequence is monotonic 0..n-1 (consumers order by Sequence alone).
        stream.Select(e => e.Sequence).Should().BeInAscendingOrder().And.Equal(Enumerable.Range(0, stream.Count));

        // Ordered request→context→tools selected→tools executed→gate: turn 1 (context, chain, 2 calls) then turn 2 (gate).
        stream.Select(e => e.Kind).Should().Equal(
            TraceEventKind.Context,
            TraceEventKind.ToolChain,
            TraceEventKind.ToolCall,
            TraceEventKind.ToolCall,
            TraceEventKind.Gate);

        stream.Select(e => e.Turn).Should().Equal(1, 1, 1, 1, 2);

        // Version field present on every event and equal to the v1 stamp.
        stream.Should().OnlyContain(e => e.Version == TraceEventContract.SchemaVersion);
        TraceEventContract.SchemaVersion.Should().Be("trace-event/v1");
    }

    [Fact]
    public void Project_NamesExistingMarkers_WithoutInventingDuplicateEventTypes()
    {
        var (context, chains, gates) = RealLedgerMarkers();

        var stream = TraceEventProjection.Project(context, chains, gates);

        // Only the four sanctioned kinds appear; three name existing ledger markers and
        // exactly one (context) is the justified new type.
        stream.Select(e => e.Kind).Distinct().Should().BeSubsetOf(new[]
        {
            TraceEventKind.Context, TraceEventKind.ToolChain, TraceEventKind.ToolCall, TraceEventKind.Gate,
        });

        // ToolChain names SessionToolChain (call count carried through).
        stream.Single(e => e.Kind == TraceEventKind.ToolChain).ToolCallCount.Should().Be(2);

        // ToolCall names SessionToolCall (ids/filters/counts carried through).
        var firstCall = stream.First(e => e.Kind == TraceEventKind.ToolCall);
        firstCall.ToolId.Should().Be("sprk_analysistool:document_search");
        firstCall.ResultCount.Should().Be(5);
        firstCall.Citations.Should().Equal("doc-1", "doc-2");

        // Gate names SessionGate (approval-path identifiers carried through).
        var gate = stream.Single(e => e.Kind == TraceEventKind.Gate);
        gate.GateId.Should().Be("gate-1");
        gate.Status.Should().Be("confirmed");
        gate.OutputKey.Should().Be("create-task@t2");
    }

    [Fact]
    public void Project_UnresolvedGate_ProjectsAsPartialEvent()
    {
        var gates = new List<SessionGate>
        {
            new() { GateId = "g-pending", Kind = "elicitation", Status = "pending", Turn = 1, MissingFields = new[] { "due_date", "assign_to" }, CreatedAt = T0 },
        };

        var stream = TraceEventProjection.Project(gates: gates);

        var gate = stream.Single();
        gate.Status.Should().Be("pending");
        gate.OutputKey.Should().BeNull("a pending gate has no outcome yet — partial state is preserved, not dropped");
        gate.MissingFields.Should().Equal("due_date", "assign_to");
    }

    [Fact]
    public void TraceEvent_TolerantReader_IgnoresUnknownField()
    {
        var evt = TraceEventProjection.Project(toolChains: RealLedgerMarkers().Chains).First();

        // Simulate a future additive v1.x adding a field this v1 reader doesn't know.
        var json = JsonSerializer.Serialize(evt, TraceEventContract.SerializerOptions);
        using var doc = JsonDocument.Parse(json);
        var withUnknown = new Dictionary<string, JsonElement>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            withUnknown[p.Name] = p.Value.Clone();
        }
        var augmented = JsonSerializer.Serialize(new
        {
            version = withUnknown["version"].GetString(),
            sequence = withUnknown["sequence"].GetInt32(),
            turn = withUnknown["turn"].GetInt32(),
            kind = withUnknown["kind"].GetString(),
            timestamp = withUnknown["timestamp"].GetString(),
            futureField = "some-additive-v1.x-value", // unknown to v1
        });

        // Tolerant reader: deserialization succeeds and known fields survive.
        var roundTripped = JsonSerializer.Deserialize<TraceEvent>(augmented, TraceEventContract.SerializerOptions);
        roundTripped.Should().NotBeNull();
        roundTripped!.Kind.Should().Be(evt.Kind);
        roundTripped.Turn.Should().Be(evt.Turn);
        roundTripped.Version.Should().Be(TraceEventContract.SchemaVersion);
    }

    [Fact]
    public void ProducerConsumer_RoundTrip_RendersTraceAsIdentifiersAndCounts()
    {
        var (context, chains, gates) = RealLedgerMarkers();

        var stream = TraceEventProjection.Project(context, chains, gates);
        var lines = TraceEventRenderer.Render(stream);

        // Round-trip: one rendered line per event, in order.
        lines.Should().HaveCount(stream.Count);
        lines[0].Should().Contain("context").And.Contain("fingerprint=ctx-fp-abc123").And.Contain("slices=4");
        lines.Should().Contain(l => l.Contains("tool=sprk_analysistool:document_search") && l.Contains("results=5"));
        lines.Should().Contain(l => l.Contains("gate confirmation") && l.Contains("status=confirmed"));
    }

    [Fact]
    public void EveryProjectedEvent_CarriesOnlySanctionedFields_NoContentLeaks()
    {
        var (context, chains, gates) = RealLedgerMarkers();
        var stream = TraceEventProjection.Project(context, chains, gates);

        foreach (var evt in stream)
        {
            var json = JsonSerializer.Serialize(evt, TraceEventContract.SerializerOptions);
            using var doc = JsonDocument.Parse(json);

            TraceEventContract.CarriesOnlySanctionedFields(doc.RootElement)
                .Should().BeTrue("NFR-07: a TraceEvent may carry only sanctioned identifier/count fields — no field where prompt/response content could live");
        }
    }

    [Fact]
    public void CarriesOnlySanctionedFields_WhenEventHasContentField_ReturnsFalse()
    {
        // NEGATIVE (NFR-07): an event that smuggles message content past the identifier/count
        // contract must fail the no-content guard.
        var evt = TraceEventProjection.Project(toolChains: RealLedgerMarkers().Chains).First();
        var json = JsonSerializer.Serialize(evt, TraceEventContract.SerializerOptions);
        using var doc = JsonDocument.Parse(json);

        var tampered = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            tampered[p.Name] = p.Value.ToString();
        }
        tampered["content"] = "The tenant asked whether the indemnity clause survives termination..."; // forbidden content
        var tamperedJson = JsonSerializer.Serialize(tampered);
        using var tamperedDoc = JsonDocument.Parse(tamperedJson);

        TraceEventContract.CarriesOnlySanctionedFields(tamperedDoc.RootElement)
            .Should().BeFalse("a content-bearing field is outside the sanctioned identifier/count set and MUST be rejected (NFR-07)");
    }
}
