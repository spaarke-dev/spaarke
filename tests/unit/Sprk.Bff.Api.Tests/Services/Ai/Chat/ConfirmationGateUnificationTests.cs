using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Chat;

/// <summary>
/// FR-P2-02 / D12 acceptance tests (spaarke-ai-architecture-redesign-r1 task 031):
/// the ONE Confirmation Gate.
///
/// Contract anchors (maintain-class per ADR-038 — each protects a production behavior):
///  1. Gating decisions are driven by DECLARED metadata (tool sprk_sideeffectclass +
///     Binding sprk_risk) via <see cref="PendingPlanManager.RequiresConfirmation"/> —
///     the ADR-039 MUST ("no tool-name-list gating").
///  2. Write tools suspend into and resume through THE unified pending store
///     (<see cref="PendingPlanManager"/>), with double-confirm protection.
///  3. Every suspend/confirm/reject transition writes a <see cref="SessionGate"/>
///     ledger entry BEFORE the gate renders, correlated by gate id (ADR-040).
/// (The former section 4 — detector-level declared-class gating — was deleted by
/// task 035 / FR-P2-06 with its subject; anchor 1 covers the ADR-039 contract.)
/// </summary>
public class ConfirmationGateUnificationTests
{
    private const string TenantId = "tenant-gate-tests";

    private readonly InMemoryTenantCache _cache = new();
    private readonly ChatSessionManager _sessionManager;
    private readonly PendingPlanManager _sut;

    public ConfirmationGateUnificationTests()
    {
        _sessionManager = new ChatSessionManager(
            _cache,
            new Mock<IChatDataverseRepository>().Object,
            new Mock<ILogger<ChatSessionManager>>().Object);
        _sut = new PendingPlanManager(
            _cache,
            _sessionManager,
            new Mock<ILogger<PendingPlanManager>>().Object);
    }

    // =========================================================================
    // 1. Metadata-driven gate policy (ADR-039 — side_effect_class + Binding risk)
    // =========================================================================

    [Theory]
    [InlineData(ToolSideEffectClass.Write, true)]
    [InlineData(ToolSideEffectClass.Communicate, true)]
    [InlineData(ToolSideEffectClass.Read, false)]
    [InlineData(ToolSideEffectClass.Pure, false)]
    [InlineData(null, false)]
    public void RequiresConfirmation_ByDeclaredSideEffectClass_GatesWriteAndCommunicateOnly(
        ToolSideEffectClass? declaredClass, bool expected)
    {
        PendingPlanManager.RequiresConfirmation(declaredClass).Should().Be(expected);
    }

    [Fact]
    public void RequiresConfirmation_AlwaysConfirmRisk_GatesEvenDeclaredReadTools()
    {
        PendingPlanManager.RequiresConfirmation(
            ToolSideEffectClass.Read, BindingRisk.AlwaysConfirm).Should().BeTrue();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void RequiresConfirmation_ConfirmWhenUncertainRisk_GatesOnlyWhenDispatchUncertain(
        bool dispatchUncertain, bool expected)
    {
        PendingPlanManager.RequiresConfirmation(
            ToolSideEffectClass.Read, BindingRisk.ConfirmWhenUncertain, dispatchUncertain)
            .Should().Be(expected);
    }

    // =========================================================================
    // 2 + 3. Suspend / resume / reject through THE store, with ledger markers
    // =========================================================================

    [Fact]
    public async Task SuspendInvocation_WritesPendingLedgerMarker_AndStoresResumablePayload()
    {
        var session = await SeedSessionAsync();
        var invocation = BuildInvocation(session.SessionId, "gate-001");

        var suspended = await _sut.SuspendInvocationAsync(invocation);

        // Ledger pending marker exists BEFORE any rendering could occur (ADR-040).
        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        stored!.Gates.Should().ContainSingle(g =>
            g.GateId == "gate-001" &&
            g.Kind == PendingPlanManager.GateKindConfirmation &&
            g.Status == PendingPlanManager.GateStatusPending &&
            g.SideEffectClass == "write");

        // The resumable payload is retrievable from the ONE store.
        var pending = await _sut.GetInvocationAsync(TenantId, session.SessionId, "gate-001");
        pending.Should().NotBeNull();
        pending!.ToolId.Should().Be("dataverse.create_record");
        pending.ArgsJson.Should().Be("""{"table":"sprk_matter"}""");
        pending.Turn.Should().Be(suspended.Turn).And.BePositive();
    }

    [Fact]
    public async Task ResumeInvocation_ReturnsSuspendedInvocation_AndWritesConfirmedMarker()
    {
        var session = await SeedSessionAsync();
        await _sut.SuspendInvocationAsync(BuildInvocation(session.SessionId, "gate-002"));

        var resumed = await _sut.ResumeInvocationAsync(TenantId, session.SessionId, "gate-002");

        resumed.Should().NotBeNull("the first confirm must return the invocation for execution");
        resumed!.ToolId.Should().Be("dataverse.create_record");

        // Resolution marker: NEW ledger entry, same gate id, correlated turn (append-only).
        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        var entries = stored!.Gates!.Where(g => g.GateId == "gate-002").ToList();
        entries.Should().HaveCount(2, "pending + confirmed entries correlate by gate id");
        entries.Should().ContainSingle(g => g.Status == PendingPlanManager.GateStatusConfirmed);
        entries.Select(g => g.Turn).Distinct().Should().HaveCount(1, "resolution reuses the pending turn");
    }

    [Fact]
    public async Task ResumeInvocation_SecondConfirm_ReturnsNull_DoubleExecutionProtection()
    {
        var session = await SeedSessionAsync();
        await _sut.SuspendInvocationAsync(BuildInvocation(session.SessionId, "gate-003"));

        var first = await _sut.ResumeInvocationAsync(TenantId, session.SessionId, "gate-003");
        var second = await _sut.ResumeInvocationAsync(TenantId, session.SessionId, "gate-003");

        first.Should().NotBeNull();
        second.Should().BeNull("the store entry is deleted on first confirm — a racer gets 409 semantics");
    }

    [Fact]
    public async Task RejectInvocation_RemovesPayload_AndWritesRejectedMarker()
    {
        var session = await SeedSessionAsync();
        await _sut.SuspendInvocationAsync(BuildInvocation(session.SessionId, "gate-004"));

        var rejected = await _sut.RejectInvocationAsync(TenantId, session.SessionId, "gate-004");
        var afterReject = await _sut.GetInvocationAsync(TenantId, session.SessionId, "gate-004");
        var rejectAgain = await _sut.RejectInvocationAsync(TenantId, session.SessionId, "gate-004");

        rejected.Should().BeTrue();
        afterReject.Should().BeNull("a rejected invocation must never be executable");
        rejectAgain.Should().BeFalse("reject is idempotent on resolved gates");

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        stored!.Gates.Should().ContainSingle(g =>
            g.GateId == "gate-004" && g.Status == PendingPlanManager.GateStatusRejected);
    }

    // =========================================================================
    // G-P2 UAT round-1 finding 6 (2026-07-06): honest close for typed-handler confirms
    // =========================================================================

    [Fact]
    public async Task CloseInvocation_ConfirmedUnexecutable_WritesHonestMarkerAndRemovesPayload()
    {
        // A typed-handler (non-Binding) invocation confirmed by the user has NO execution
        // seam until FR-P3-03 — the gate-resolve endpoint closes it `confirmed-unexecutable`
        // (approval recorded, execution honestly unavailable) instead of the pre-fix
        // `confirmed` marker that falsely recorded an executed side effect.
        var session = await SeedSessionAsync();
        await _sut.SuspendInvocationAsync(BuildInvocation(session.SessionId, "gate-006"));

        var closed = await _sut.CloseInvocationAsync(
            TenantId, session.SessionId, "gate-006",
            PendingPlanManager.GateStatusConfirmedUnexecutable);
        var afterClose = await _sut.GetInvocationAsync(TenantId, session.SessionId, "gate-006");
        var closeAgain = await _sut.CloseInvocationAsync(
            TenantId, session.SessionId, "gate-006",
            PendingPlanManager.GateStatusConfirmedUnexecutable);

        closed.Should().BeTrue();
        afterClose.Should().BeNull("the payload is removed — the invocation can never execute later by accident");
        closeAgain.Should().BeFalse("close is idempotent — a raced second resolve maps to 409 semantics");

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        var entries = stored!.Gates!.Where(g => g.GateId == "gate-006").ToList();
        entries.Should().HaveCount(2, "pending + terminal entries correlate by gate id (ADR-040 append-only)");
        entries.Should().ContainSingle(g => g.Status == PendingPlanManager.GateStatusConfirmedUnexecutable);
        entries.Should().NotContain(g => g.Status == PendingPlanManager.GateStatusConfirmed,
            "no plain `confirmed` marker — nothing executed, and the ledger must not claim otherwise");
    }

    [Fact]
    public async Task WriteGateMarker_PendingThenConfirmed_CorrelatesByGateId()
    {
        var session = await SeedSessionAsync();

        var pending = await _sut.WriteGateMarkerAsync(
            TenantId, session.SessionId, "options-abc",
            PendingPlanManager.GateKindConfirmation, PendingPlanManager.GateStatusPending);
        var confirmed = await _sut.WriteGateMarkerAsync(
            TenantId, session.SessionId, "options-abc",
            PendingPlanManager.GateKindConfirmation, PendingPlanManager.GateStatusConfirmed,
            turn: pending!.Turn);

        pending.ResolvedAt.Should().BeNull();
        confirmed!.ResolvedAt.Should().NotBeNull();

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        stored!.Gates!.Where(g => g.GateId == "options-abc").Should().HaveCount(2);
    }

    // =========================================================================
    // 4. Gate-outcome evidence (G-P3 UAT round-2 R2-A/R2-C, 2026-07-07)
    //    A confirmed-then-FAILED execution must leave ledger + transcript
    //    evidence — the round-2 create_record confirms failed with NOTHING
    //    beyond the `confirmed` approval marker, so the model kept guessing.
    // =========================================================================

    [Fact]
    public async Task WriteGateMarker_DispatchFailed_AppendsAfterConfirmed_SameGateId()
    {
        var session = await SeedSessionAsync();
        var invocation = BuildInvocation(session.SessionId, "confirmation-outcome-1");
        await _sut.SuspendInvocationAsync(invocation);
        (await _sut.ResumeInvocationAsync(TenantId, session.SessionId, invocation.GateId))
            .Should().NotBeNull();

        var failed = await _sut.WriteGateMarkerAsync(
            TenantId, session.SessionId, invocation.GateId,
            PendingPlanManager.GateKindConfirmation, PendingPlanManager.GateStatusDispatchFailed,
            sideEffectClass: invocation.SideEffectClass);

        failed.Should().NotBeNull();
        failed!.Status.Should().Be("dispatch-failed");
        failed.ResolvedAt.Should().NotBeNull();

        var stored = await _sessionManager.GetSessionAsync(TenantId, session.SessionId);
        var statuses = stored!.Gates!
            .Where(g => g.GateId == invocation.GateId)
            .Select(g => g.Status)
            .ToList();
        statuses.Should().ContainInOrder(
            PendingPlanManager.GateStatusPending,
            PendingPlanManager.GateStatusConfirmed,
            PendingPlanManager.GateStatusDispatchFailed);
    }

    // Task 053 (FR-B-04): the BuildGateOutcomeMessage producer tests moved to
    // ContextSliceProducersTests (the gate-outcome string production moved to
    // ContextSliceProducers.GateOutcomeProducer). The gate-RESOLUTION-FLOW tests stay here.

    [Fact]
    public void BuildGateDispatchFailedProblem_MapsTo422_WithStableErrorCodeAndDetail()
    {
        // G-P3 UAT round-3 R3-2 (2026-07-07): confirmed-gate dispatch failures
        // (write-mapper validation, Dataverse 400) previously surfaced as 502 Bad
        // Gateway — a false gateway-fault signal for a correctable request-content
        // problem. Pin the 422 + stable errorCode + preserved detail contract for
        // BOTH resolve legs (single construction site).
        var result = Sprk.Bff.Api.Api.Ai.ChatEndpoints.BuildGateDispatchFailedProblem(
            "Column 'sprk_assignedattorney1': lookup objects require a 'recordId' GUID on the native transport.");

        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status422UnprocessableEntity,
            because: "a validation/dispatch rejection is Unprocessable Entity, never a 5xx");
        problem.ProblemDetails.Detail.Should().Contain("recordId",
            because: "the handler's instructive detail must reach the client verbatim");
        problem.ProblemDetails.Extensions["errorCode"].Should().Be("gate.dispatch-failed",
            because: "ADR-019: stable errorCode survives the status-code change");

        var fallback = Sprk.Bff.Api.Api.Ai.ChatEndpoints.BuildGateDispatchFailedProblem(null);
        fallback.Should().BeOfType<ProblemHttpResult>()
            .Which.ProblemDetails.Detail.Should().Be("The confirmed action failed.");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<ChatSession> SeedSessionAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new ChatSession(
            SessionId: Guid.NewGuid().ToString("N"),
            TenantId: TenantId,
            DocumentId: null,
            PlaybookId: null,
            CreatedAt: now,
            LastActivity: now,
            Messages: new List<Sprk.Bff.Api.Models.Ai.Chat.ChatMessage>());
        await _sessionManager.UpdateSessionCacheAsync(session);
        return session;
    }

    private static PendingInvocation BuildInvocation(string sessionId, string gateId) => new()
    {
        GateId = gateId,
        SessionId = sessionId,
        TenantId = TenantId,
        ToolId = "dataverse.create_record",
        SideEffectClass = "write",
        Risk = "none",
        ArgsJson = """{"table":"sprk_matter"}""",
    };
}
