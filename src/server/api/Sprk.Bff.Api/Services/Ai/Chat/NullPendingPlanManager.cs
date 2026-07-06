using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Null-Object subclass of <see cref="PendingPlanManager"/> registered when the compound AI
/// kill switch is OFF (<c>Analysis:Enabled=false</c> or <c>DocumentIntelligence:Enabled=false</c>).
/// </summary>
/// <remarks>
/// <para>
/// P3 Fail-Fast pattern per D-09 §2 B3 (task 011 Phase 1b Tier 3, 2026-06-01). Pending plans gate
/// compound-intent multi-tool chains; silently returning <c>null</c> from <see cref="GetAsync"/>
/// would let chat believe no plan is pending and execute single-tool defaults, masking the
/// disabled state. Every public entry point throws <see cref="FeatureDisabledException"/>;
/// consumer endpoints (<c>ChatEndpoints.SendMessageAsync</c>, <c>ChatEndpoints.ApprovePlanAsync</c>)
/// catch the exception in their try-blocks and emit an SSE <c>error</c> chunk per ADR-018 + ADR-019.
/// </para>
/// <para>
/// Construction: uses the protected base ctor that bypasses Redis injection — kept consistent
/// with <see cref="NullSprkChatAgentFactory"/>'s pattern for the kill-switch-OFF DI graph.
/// </para>
/// </remarks>
public sealed class NullPendingPlanManager : PendingPlanManager
{
    private const string ErrorCode = "ai.chat.compound-intent.disabled";
    private const string DetailMessage =
        "AI compound-intent plan management requires Analysis:Enabled=true AND DocumentIntelligence:Enabled=true.";

    private readonly ILogger<PendingPlanManager> _logger;

    public NullPendingPlanManager(ILogger<PendingPlanManager> logger)
        : base(logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public override Task StoreAsync(PendingPlan plan, CancellationToken ct = default)
    {
        LogDisabled(nameof(StoreAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<PendingPlan?> GetAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        LogDisabled(nameof(GetAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<PendingPlan?> GetAndDeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        LogDisabled(nameof(GetAndDeleteAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task DeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        LogDisabled(nameof(DeleteAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    // === Generalized unified-gate surface (D12 / FR-P2-02, task 031) ===
    // ADR-032: the Null peer stays consistent with the generalized store contract —
    // every unified-gate entry point fails fast under the kill-switch-OFF DI state.

    public override Task<PendingInvocation> SuspendInvocationAsync(
        PendingInvocation invocation, CancellationToken ct = default)
    {
        LogDisabled(nameof(SuspendInvocationAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<PendingInvocation?> GetInvocationAsync(
        string tenantId, string sessionId, string gateId, CancellationToken ct = default)
    {
        LogDisabled(nameof(GetInvocationAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<PendingInvocation?> ResumeInvocationAsync(
        string tenantId, string sessionId, string gateId, CancellationToken ct = default)
    {
        LogDisabled(nameof(ResumeInvocationAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<bool> RejectInvocationAsync(
        string tenantId, string sessionId, string gateId, CancellationToken ct = default)
    {
        LogDisabled(nameof(RejectInvocationAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<SessionGate?> WriteGateMarkerAsync(
        string tenantId, string sessionId, string gateId, string kind, string status,
        string? bindingId = null, string? sideEffectClass = null,
        IReadOnlyList<string>? missingFields = null, int turn = 0,
        CancellationToken ct = default)
    {
        LogDisabled(nameof(WriteGateMarkerAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<bool> CloseInvocationAsync(
        string tenantId, string sessionId, string gateId, string status, CancellationToken ct = default)
    {
        LogDisabled(nameof(CloseInvocationAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    public override Task<SessionGate?> ResolveElicitationOnDispatchAsync(
        string tenantId, string sessionId, Guid bindingId, CancellationToken ct = default)
    {
        LogDisabled(nameof(ResolveElicitationOnDispatchAsync));
        throw new FeatureDisabledException(ErrorCode, DetailMessage);
    }

    private void LogDisabled(string method)
    {
        _logger.LogDebug(
            "NullPendingPlanManager.{Method} invoked while AI compound-intent feature is disabled (errorCode={ErrorCode}).",
            method, ErrorCode);
    }
}
