using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

/// <summary>
/// Unit tests for <see cref="ComposeService.GetActionHistory"/> — FR-31's read-only
/// action-history QUERY over the existing session ledger (task 061).
///
/// <para>
/// <b>ADR-038 KEEP category</b>: <c>unit/domain</c>. <see cref="ComposeService.GetActionHistory"/>
/// is a pure, dependency-free projection over <see cref="ChatSession"/> data already in hand
/// (no constructor, no DI, no I/O, nothing to mock). Every test exercises the real static method
/// with real in-memory ledger objects — no <c>Mock&lt;HttpMessageHandler&gt;</c>, no
/// DI-registration tests, no ctor null-check tests (all banned per ADR-038 / <c>tests/CLAUDE.md</c>).
/// </para>
///
/// <para>
/// <b>FR-31 / ADR-040 anti-component contract</b>: this suite ALSO proves (via
/// <see cref="ChatSession_And_ComposeServiceAssembly_CarryNoActionLogOrDerivedInsightStoredStructure"/>)
/// that no parallel <c>actionLog</c>/<c>derivedInsight</c> stored structure exists anywhere in the
/// BFF assembly — the 2026-07-03 design draft's <c>actionLog: ComposeAction[]</c> and
/// <c>derivedInsights: DerivedInsight[]</c> were explicitly deleted (design.md §8) in favor of
/// querying <see cref="ChatSession.Outputs"/> / <see cref="ChatSession.ToolChains"/> directly.
/// </para>
/// </summary>
public class ActionHistoryLedgerQueryTests
{
    private const string BindingId = "3f2504e0-4f89-41d3-9a0c-0305e82c3301";
    private const string OtherBindingId = "11111111-1111-1111-1111-111111111111";
    private const string UcId = "compose-draft-alternative";

    private static SessionOutput BuildOutput(
        string bindingId,
        int turn,
        string disposition = "informational",
        DateTimeOffset? createdAt = null) => new()
        {
            Key = SessionLedger.BuildOutputKey(bindingId, turn),
            BindingId = bindingId,
            UcId = UcId,
            Turn = turn,
            Disposition = disposition,
            Payload = JsonSerializer.SerializeToElement(new { note = $"turn-{turn}" }),
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        };

    private static SessionToolChain BuildToolChain(int turn, params SessionToolCall[] calls) => new()
    {
        Turn = turn,
        Calls = calls,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static ChatSession BuildSession(
        IReadOnlyList<SessionOutput>? outputs = null,
        IReadOnlyList<SessionToolChain>? toolChains = null) => new(
        SessionId: "session-1",
        TenantId: "tenant-1",
        DocumentId: "doc-1",
        PlaybookId: null,
        CreatedAt: DateTimeOffset.UtcNow,
        LastActivity: DateTimeOffset.UtcNow,
        Messages: Array.Empty<ChatMessage>())
        {
            Outputs = outputs,
            ToolChains = toolChains,
        };

    // ── Projection: binding, args, output ref, timestamp — sourced from ToolChain + SessionOutput ──

    [Fact]
    public void GetActionHistory_WithCorrelatedToolChain_ProjectsBindingArgsOutputRefAndTimestamp()
    {
        var createdAt = DateTimeOffset.Parse("2026-07-09T12:00:00Z");
        var output = BuildOutput(BindingId, turn: 1, createdAt: createdAt);
        var toolChain = BuildToolChain(
            turn: 1,
            new SessionToolCall { ToolId = "compose-tool", ArgsSummary = "matterId=123; top=5" });

        var session = BuildSession(
            outputs: new[] { output },
            toolChains: new[] { toolChain });

        var history = ComposeService.GetActionHistory(session);

        history.Should().HaveCount(1);
        var entry = history[0];
        entry.OutputRef.Should().Be(output.Key);              // {bindingId}@t{n} — addressable ledger ref
        entry.BindingId.Should().Be(BindingId);
        entry.UcId.Should().Be(UcId);
        entry.Disposition.Should().Be("informational");
        entry.Turn.Should().Be(1);
        entry.CreatedAt.Should().Be(createdAt);
        entry.Args.Should().ContainSingle().Which.Should().Be("matterId=123; top=5");
        entry.IsSuperseded.Should().BeFalse();                 // only output for this binding — current
    }

    [Fact]
    public void GetActionHistory_WhenNoToolChainSharesTurn_ArgsIsNull()
    {
        // No SessionToolChain entry correlates with turn 1 — Args must be null, not empty/throwing.
        var output = BuildOutput(BindingId, turn: 1);
        var session = BuildSession(outputs: new[] { output }, toolChains: null);

        var history = ComposeService.GetActionHistory(session);

        history.Should().HaveCount(1);
        history[0].Args.Should().BeNull();
    }

    // ── Supersession (ADR-040 / FR-31 acceptance criterion 3): query reflects CURRENT ledger state ──

    [Fact]
    public void GetActionHistory_AfterSupersession_MarksPriorSupersededAndLatestCurrent()
    {
        // Same binding produced twice (a retry/refinement) — turn 3 supersedes turn 1.
        // Append order deliberately non-monotonic; supersession is by Turn, not append position.
        var t1 = BuildOutput(BindingId, turn: 1, createdAt: DateTimeOffset.Parse("2026-07-09T10:00:00Z"));
        var t3 = BuildOutput(BindingId, turn: 3, createdAt: DateTimeOffset.Parse("2026-07-09T10:05:00Z"));
        var session = BuildSession(outputs: new[] { t3, t1 });

        var history = ComposeService.GetActionHistory(session);

        history.Should().HaveCount(2);

        // Oldest-first ordering by turn, independent of append order.
        history[0].Turn.Should().Be(1);
        history[1].Turn.Should().Be(3);

        var superseded = history.Single(e => e.Turn == 1);
        var current = history.Single(e => e.Turn == 3);

        superseded.IsSuperseded.Should().BeTrue(
            "an earlier-turn output for the same binding is no longer the authoritative action");
        current.IsSuperseded.Should().BeFalse(
            "the highest-turn output for the binding is CURRENT — the query reflects ledger state " +
            "after the supersession, never a stale copy (ADR-040)");
    }

    [Fact]
    public void GetActionHistory_WithDifferentBindingsAtSameTurn_DoesNotCrossContaminateSupersession()
    {
        // Two different bindings, each producing exactly once — neither is superseded by the other's turn.
        var ownOutput = BuildOutput(BindingId, turn: 2);
        var otherOutput = BuildOutput(OtherBindingId, turn: 5);
        var session = BuildSession(outputs: new[] { ownOutput, otherOutput });

        var history = ComposeService.GetActionHistory(session);

        history.Should().HaveCount(2);
        history.Should().OnlyContain(e => !e.IsSuperseded);
    }

    // ── Binding filter ───────────────────────────────────────────────────────────────────

    [Fact]
    public void GetActionHistory_WithBindingIdFilter_ReturnsOnlyMatchingBindingActions()
    {
        var mine = BuildOutput(BindingId, turn: 1);
        var other = BuildOutput(OtherBindingId, turn: 2);
        var session = BuildSession(outputs: new[] { mine, other });

        var history = ComposeService.GetActionHistory(session, bindingId: BindingId);

        history.Should().ContainSingle();
        history[0].BindingId.Should().Be(BindingId);
    }

    // ── Anti-component contract (FR-31 acceptance criterion 2 / ADR-040) ────────────────────

    [Fact]
    public void ChatSession_And_ComposeServiceAssembly_CarryNoActionLogOrDerivedInsightStoredStructure()
    {
        // FR-31 acceptance: the 2026-07-03 design draft's `actionLog: ComposeAction[]` and
        // `derivedInsights: DerivedInsight[]` stored structures were explicitly DELETED
        // (design.md §8 — "it IS the session ledger"). Action history is QUERIED from
        // ChatSession.Outputs / ChatSession.ToolChains (see ComposeService.GetActionHistory
        // above), never duplicated into a second stored structure. This is a reflection-based
        // static assertion — the grep-equivalent that is immune to false positives from prose
        // in doc comments (which legitimately discuss the deleted concept by name).
        var bffAssembly = typeof(ComposeService).Assembly;

        string[] bannedTypeNames = ["ComposeAction", "DerivedInsight"];
        var offendingTypes = bffAssembly.GetTypes()
            .Where(t => bannedTypeNames.Any(name =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.FullName)
            .ToList();

        offendingTypes.Should().BeEmpty(
            "the action-log and derived-insight structures proposed 2026-07-03 were deleted " +
            "(design.md §8) in favor of querying the ledger — reintroducing either type " +
            "resurrects the two-sources-of-truth failure ADR-040 exists to prevent");

        string[] bannedPropertyNames = ["ActionLog", "DerivedInsight", "DerivedInsights"];
        var offendingSessionProperties = typeof(ChatSession)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => bannedPropertyNames.Any(name =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToList();

        offendingSessionProperties.Should().BeEmpty(
            "ChatSession MUST NOT carry a parallel actionLog/derivedInsight stored collection — " +
            "action history is a QUERY over ChatSession.Outputs + ChatSession.ToolChains " +
            "(ComposeService.GetActionHistory), never a second persisted surface (ADR-040 / FR-31)");
    }
}
