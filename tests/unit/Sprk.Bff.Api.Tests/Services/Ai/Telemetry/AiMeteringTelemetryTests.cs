using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Telemetry;

/// <summary>
/// FR-P4-05 / NFR-05 (spaarke-ai-architecture-redesign-r1 task 054) — the per-tenant
/// metering counter contract. These tests anchor the instrument names + dimension keys
/// the KQL pack (<c>scripts/kql/ai-metering/</c>) queries against dev App Insights:
/// a rename or dimension drift here silently breaks every per-tenant rollup, so the
/// contract is asserted at the meter boundary (captured measurements — ADR-038: no
/// <c>Mock&lt;HttpMessageHandler&gt;</c>, no mocked collaborators).
/// NFR-07 is asserted structurally: the dimension key set is closed and carries
/// identifiers/counts only.
/// </summary>
public class AiMeteringTelemetryTests : IDisposable
{
    private readonly AiTelemetry _telemetry = new();
    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<(string Instrument, long Value, Dictionary<string, object?> Tags)> _measurements = new();

    public AiMeteringTelemetryTests()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Sprk.Bff.Api.Ai" &&
                instrument.Name.StartsWith("ai.metering.", StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
        {
            var tagDict = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in tags)
            {
                tagDict[tag.Key] = tag.Value;
            }
            _measurements.Add((instrument.Name, value, tagDict));
        });
        _listener.Start();
    }

    public void Dispose()
    {
        _listener.Dispose();
        _telemetry.Dispose();
    }

    /// <summary>Measurements for a given instrument scoped to THIS test's unique tenant (parallel-safe).</summary>
    private List<(string Instrument, long Value, Dictionary<string, object?> Tags)> For(string instrument, string tenantId)
        => _measurements.Where(m => m.Instrument == instrument &&
                                    m.Tags.TryGetValue("tenant.id", out var t) &&
                                    (string?)t == tenantId).ToList();

    // ─────────────────────────────────────────────────────────────────────────
    // ai.metering.turns — per-turn counter + ADR-016/NFR-09 consumed-vs-cap
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordMeteredTurn_WithBudgetFigures_EmitsTenantUserAndConsumedVsCapDimensions()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredTurn(tenantId, userId, toolBudgetSpent: 3, toolBudgetCap: 8, toolBudgetDenied: 1);

        var emitted = For("ai.metering.turns", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Value.Should().Be(1);
        emitted[0].Tags.Should().Contain(new KeyValuePair<string, object?>("user.id", userId));
        emitted[0].Tags["tool_budget.spent"].Should().Be(3,
            "ADR-016/NFR-09: the per-turn tool budget must be observable as consumed-vs-cap");
        emitted[0].Tags["tool_budget.cap"].Should().Be(8);
        emitted[0].Tags["tool_budget.denied"].Should().Be(1);
    }

    [Fact]
    public void RecordMeteredTurn_WithoutUser_OmitsTheUserDimension()
    {
        var tenantId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredTurn(tenantId, userId: null, 0, 8, 0);

        var emitted = For("ai.metering.turns", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Tags.Should().NotContainKey("user.id",
            "omission (not a sentinel) is the null representation so KQL can filter empties explicitly");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ai.metering.tool_calls
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordMeteredToolCall_Executed_EmitsToolIdAndOutcomeDimensions()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredToolCall(tenantId, userId, "capability_chat_summarize");

        var emitted = For("ai.metering.tool_calls", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Value.Should().Be(1);
        emitted[0].Tags["tool.id"].Should().Be("capability_chat_summarize");
        emitted[0].Tags["outcome"].Should().Be("executed");
        emitted[0].Tags["user.id"].Should().Be(userId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ai.metering.tokens — explicit + ambient-scope attribution
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordMeteredTokens_WithExplicitIdentity_EmitsInputAndOutputIncrements()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredTokens(tenantId, userId, 1200, 340, source: "loop", entryPath: "text");

        var emitted = For("ai.metering.tokens", tenantId);
        emitted.Should().HaveCount(2, "input and output ride the same counter as token.type dimensions");

        var input = emitted.Single(m => (string?)m.Tags["token.type"] == "input");
        input.Value.Should().Be(1200);
        input.Tags["source"].Should().Be("loop");
        input.Tags["entry.path"].Should().Be("text");
        input.Tags["user.id"].Should().Be(userId);

        var output = emitted.Single(m => (string?)m.Tags["token.type"] == "output");
        output.Value.Should().Be(340);
    }

    [Fact]
    public void RecordMeteredTokens_WithNullIdentity_ResolvesTenantUserAndEntryPathFromAmbientScope()
    {
        // The executor path: OpenAiClient observes usage with no tenant/user parameters —
        // attribution MUST flow from the AiMeteringContext scope set at the entry seam.
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        using (AiMeteringContext.Begin(tenantId, userId, AiMeteringContext.EntryPathEvent))
        {
            _telemetry.RecordMeteredTokens(null, null, 500, 100, source: "executor", model: "gpt-4o-mini");
        }

        var emitted = For("ai.metering.tokens", tenantId);
        emitted.Should().HaveCount(2);
        emitted.Should().OnlyContain(m => (string?)m.Tags["user.id"] == userId);
        emitted.Should().OnlyContain(m => (string?)m.Tags["entry.path"] == "event");
        emitted.Should().OnlyContain(m => (string?)m.Tags["ai.model"] == "gpt-4o-mini");
    }

    [Fact]
    public void RecordMeteredTokens_WithZeroTokens_EmitsNothing()
    {
        var tenantId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredTokens(tenantId, null, 0, 0, source: "loop");

        For("ai.metering.tokens", tenantId).Should().BeEmpty(
            "turns whose provider reported no usage must not pollute the rollup with zero rows");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ai.metering.capability_invocations — dispatch/event/coded seams
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordCapabilityInvocation_EventPath_CarriesBudgetCapForConsumedVsCapViews()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        _telemetry.RecordCapabilityInvocation(
            tenantId, userId, AiMeteringContext.EntryPathEvent,
            capability: "chat-classify", outcome: "success", budgetCap: 50);

        var emitted = For("ai.metering.capability_invocations", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Tags["entry.path"].Should().Be("event");
        emitted[0].Tags["capability"].Should().Be("chat-classify");
        emitted[0].Tags["outcome"].Should().Be("success");
        emitted[0].Tags["budget.cap"].Should().Be(50,
            "NFR-09: the per-user daily Event-path budget must be queryable as consumed-vs-cap");
        emitted[0].Tags["user.id"].Should().Be(userId);
    }

    [Fact]
    public void RecordCapabilityInvocation_AtTheDispatchSeamInsideATextTurn_AttributesToTheTextEntryPath()
    {
        // The loop's BindingCapabilityTool converges on the dispatch seam in-process:
        // the seam passes null user/entry-path and the OUTER "text" scope must win.
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        using (AiMeteringContext.Begin(tenantId, userId, AiMeteringContext.EntryPathText))
        {
            _telemetry.RecordCapabilityInvocation(
                tenantId, userId: null, entryPath: null,
                capability: "chat-summarize", outcome: "success");
        }

        var emitted = For("ai.metering.capability_invocations", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Tags["entry.path"].Should().Be("text",
            "an in-turn capability dispatch belongs to the text turn that invoked it");
        emitted[0].Tags["user.id"].Should().Be(userId);
    }

    [Fact]
    public void RecordCapabilityInvocation_WithNoAmbientScope_DefaultsToTheClickEntryPath()
    {
        var tenantId = Guid.NewGuid().ToString();

        _telemetry.RecordCapabilityInvocation(
            tenantId, userId: null, entryPath: null,
            capability: "chat-summarize", outcome: "failed");

        var emitted = For("ai.metering.capability_invocations", tenantId);
        emitted.Should().HaveCount(1);
        emitted[0].Tags["entry.path"].Should().Be("click");
        emitted[0].Tags["outcome"].Should().Be("failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // NFR-07 — the dimension key set is closed (identifiers/counts only)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AllMeteringCounters_EmitOnlyTheClosedIdentifierDimensionSet()
    {
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid().ToString();

        _telemetry.RecordMeteredTurn(tenantId, userId, 2, 8, 0);
        _telemetry.RecordMeteredToolCall(tenantId, userId, "search_documents");
        _telemetry.RecordMeteredTokens(tenantId, userId, 10, 20, source: "loop", model: "gpt-4o", entryPath: "text");
        _telemetry.RecordCapabilityInvocation(tenantId, userId, "event", "chat-classify", "success", budgetCap: 50);

        var allowedKeys = new[]
        {
            "tenant.id", "user.id", "entry.path", "capability", "outcome", "budget.cap",
            "tool.id", "token.type", "source", "ai.model",
            "tool_budget.spent", "tool_budget.cap", "tool_budget.denied",
        };

        var mine = _measurements.Where(m =>
            m.Tags.TryGetValue("tenant.id", out var t) && (string?)t == tenantId).ToList();
        mine.Should().NotBeEmpty();
        mine.SelectMany(m => m.Tags.Keys).Distinct()
            .Should().BeSubsetOf(allowedKeys,
                "NFR-07/ADR-015: metering dimensions carry identifiers and counts ONLY — " +
                "a new dimension key must be reviewed against the no-content rule before the " +
                "KQL pack consumes it");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AiMeteringContext — scope semantics
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AiMeteringContext_Dispose_RestoresThePreviousScope()
    {
        AiMeteringContext.Current.Should().BeNull();

        using (AiMeteringContext.Begin("t-outer", "u-outer", AiMeteringContext.EntryPathText))
        {
            using (AiMeteringContext.Begin("t-inner", "u-inner", AiMeteringContext.EntryPathClick))
            {
                AiMeteringContext.Current!.TenantId.Should().Be("t-inner");
            }

            AiMeteringContext.Current!.TenantId.Should().Be("t-outer",
                "disposing an inner scope must restore the outer one");
            AiMeteringContext.Current.EntryPath.Should().Be(AiMeteringContext.EntryPathText);
        }

        AiMeteringContext.Current.Should().BeNull();
    }

    [Fact]
    public async Task AiMeteringContext_FlowsAcrossAwaits_WithinTheBeganFlow()
    {
        using var scope = AiMeteringContext.Begin("t-flow", "u-flow", AiMeteringContext.EntryPathEvent);

        await Task.Yield();
        await Task.Run(() =>
        {
            AiMeteringContext.Current.Should().NotBeNull(
                "AsyncLocal must flow into awaited work so OpenAiClient sees the entry seam's scope");
            AiMeteringContext.Current!.UserId.Should().Be("u-flow");
        });

        AiMeteringContext.Current!.TenantId.Should().Be("t-flow");
    }
}
