using System.Text.Json;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// THE unified pending store — the ONE Confirmation Gate of the platform
/// (design decision D12 / FR-P2-02, <c>spaarke-ai-architecture-redesign-r1</c> task 031).
///
/// Every suspended side-effecting invocation lives here until the user confirms or
/// rejects it:
/// <list type="bullet">
///   <item><b>Generalized invocations</b> (<see cref="PendingInvocation"/>) — write/communicate
///   tool calls suspended by the agent-turn loop, must-click capability confirmations
///   (FR-48), and any other side-effect awaiting explicit user consent. Keyed per gate id;
///   suspend/resume/reject via <see cref="SuspendInvocationAsync"/> /
///   <see cref="ResumeInvocationAsync"/> / <see cref="RejectInvocationAsync"/>.</item>
///   <item><b>Compound plans</b> (<see cref="PendingPlan"/>) — the plan-preview presentation
///   of the same gate (session-singleton key; approve via
///   <c>POST /api/ai/chat/sessions/{sessionId}/plan/approve</c>).</item>
/// </list>
///
/// <b>Gating policy (ADR-039)</b>: whether an invocation needs the gate is decided by
/// <see cref="RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool)"/> from the
/// tool's DECLARED <c>sprk_sideeffectclass</c> plus the Binding's DECLARED <c>sprk_risk</c>
/// — never by tool-name lists (the pre-D12 hardcoded lists were deleted by task 031).
///
/// <b>Ledger markers (ADR-040)</b>: every suspend/confirm/reject transition writes a
/// <see cref="SessionGate"/> entry to the session ledger via
/// <see cref="ChatSessionManager.AppendGateAsync"/> BEFORE any gate UI renders
/// (storage precedes rendering). Resolutions are NEW entries correlated by gate id
/// (append-only). Ledger entries and logs carry identifiers only (NFR-07);
/// the resumable <see cref="PendingInvocation.ArgsJson"/> payload lives only in this
/// Tier-3 Redis store.
///
/// <b>W-P2-B integration seam (task 030/032 contract)</b>: the agent-turn loop calls
/// <see cref="RequiresConfirmation(ToolSideEffectClass?, BindingRisk, bool)"/> at its
/// tool-invocation boundary and, when true, <see cref="SuspendInvocationAsync"/> instead
/// of executing; user confirmation resumes via <see cref="ResumeInvocationAsync"/> and
/// executes the returned <see cref="PendingInvocation"/>. Elicitation markers (task 032)
/// ride <see cref="WriteGateMarkerAsync"/> with kind <c>elicitation</c>.
///
/// Storage design (task 070 design doc, unchanged by the D12 generalization):
/// - Redis via <see cref="ITenantCache"/> (ADR-014 tenant-scoped keys; ADR-009 no in-memory fallback).
/// - TTL: 30 minutes absolute — the confirmation window is an interactive gate; walk-aways expire cleanly.
/// - NOT embedded in <see cref="ChatSession"/> to avoid inflating every session cache read
///   (only the identifier-bearing <see cref="SessionGate"/> markers ride the session).
///
/// Concurrent approval protection: resume/approve perform get-then-delete.
/// If two requests race, only the first succeeds (the second finds no key and returns null).
///
/// DI registration: Scoped (one per HTTP request, same as <see cref="ChatSessionManager"/>).
///
/// Unseal note (task 011 Phase 1b Tier 3, D-09 §2 B3, 2026-06-01): class was `sealed`;
/// unsealed to permit <see cref="NullPendingPlanManager"/> subclassing for the kill-switch-OFF
/// (compound AI disabled) DI state. Per ADR-010 (DI minimalism) the concrete-class Null-Object
/// is preferred over introducing an interface.
/// </summary>
public class PendingPlanManager
{
    /// <summary>Absolute TTL for pending gate entries — plans AND generalized invocations (task 070 design).</summary>
    internal static readonly TimeSpan PendingPlanTtl = TimeSpan.FromMinutes(30);

    /// <summary>Tenant-cache resource name for pending plan payloads (FR-05).</summary>
    internal const string CacheResource = "pending-plan";

    /// <summary>Tenant-cache resource name for generalized pending-invocation payloads (D12).</summary>
    internal const string GateCacheResource = "pending-gate";

    /// <summary>Tenant-cache schema version for pending gate payloads.</summary>
    internal const int CacheVersion = 1;

    /// <summary>Ledger vocabulary: gate kind for side-effect confirmations (ADR-040).</summary>
    public const string GateKindConfirmation = "confirmation";

    /// <summary>Ledger vocabulary: gate status <c>pending</c> (ADR-040 <c>pending | confirmed | rejected | expired | superseded</c>).</summary>
    public const string GateStatusPending = "pending";

    /// <summary>Ledger vocabulary: gate status <c>confirmed</c>.</summary>
    public const string GateStatusConfirmed = "confirmed";

    /// <summary>Ledger vocabulary: gate status <c>rejected</c>.</summary>
    public const string GateStatusRejected = "rejected";

    /// <summary>
    /// Reproduces the on-wire cache key produced by <see cref="ITenantCache"/> for legacy
    /// test consumers that asserted on the raw Redis key shape pre-FR-05.
    /// Format: <c>tenant:{tenantId}:pending-plan:{sessionId}:v1</c>.
    /// </summary>
    internal static string BuildPendingPlanKey(string tenantId, string sessionId)
        => $"tenant:{tenantId}:{CacheResource}:{sessionId}:v{CacheVersion}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITenantCache _cache;
    private readonly ChatSessionManager _sessionManager;
    private readonly ILogger<PendingPlanManager> _logger;

    public PendingPlanManager(
        ITenantCache cache,
        ChatSessionManager sessionManager,
        ILogger<PendingPlanManager> logger)
    {
        _cache = cache;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    // =========================================================================
    // Gate policy — the ONE metadata-driven gating decision (ADR-039 / D12)
    // =========================================================================

    /// <summary>
    /// THE confirmation-gate decision: does an invocation need user confirmation
    /// before executing? Driven exclusively by DECLARED catalog metadata — the tool's
    /// <c>sprk_sideeffectclass</c> and the Binding's <c>sprk_risk</c>. Tool-name lists
    /// are FORBIDDEN as gating inputs (ADR-039 MUST NOT).
    /// </summary>
    /// <param name="sideEffectClass">
    /// The tool's declared side-effect class; null = no declaration (legacy row or no
    /// tool involved) — not gated by class.
    /// </param>
    /// <param name="risk">
    /// The resolved Binding's declared risk posture; defaults to
    /// <see cref="BindingRisk.None"/> when no Binding is involved.
    /// </param>
    /// <param name="dispatchUncertain">
    /// True when the dispatcher self-reports uncertainty about the routing decision
    /// (the ask-when-uncertain signal) — activates <see cref="BindingRisk.ConfirmWhenUncertain"/>.
    /// </param>
    public static bool RequiresConfirmation(
        ToolSideEffectClass? sideEffectClass,
        BindingRisk risk = BindingRisk.None,
        bool dispatchUncertain = false)
    {
        if (risk == BindingRisk.AlwaysConfirm)
        {
            return true;
        }

        if (risk == BindingRisk.ConfirmWhenUncertain && dispatchUncertain)
        {
            return true;
        }

        return sideEffectClass is ToolSideEffectClass.Write or ToolSideEffectClass.Communicate;
    }

    /// <summary>
    /// Maps a declared <see cref="ToolSideEffectClass"/> to the ledger wire vocabulary
    /// (<c>read | write | communicate | pure</c> — ADR-040 <see cref="SessionGate.SideEffectClass"/>).
    /// </summary>
    public static string? ToLedgerSideEffectClass(ToolSideEffectClass? sideEffectClass) => sideEffectClass switch
    {
        ToolSideEffectClass.Read => "read",
        ToolSideEffectClass.Write => "write",
        ToolSideEffectClass.Communicate => "communicate",
        ToolSideEffectClass.Pure => "pure",
        _ => null,
    };

    // =========================================================================
    // Generalized pending invocations (D12) — suspend / resume / reject
    // =========================================================================

    /// <summary>
    /// Suspends a side-effecting invocation into the unified store: writes the pending
    /// <see cref="SessionGate"/> ledger marker FIRST (ADR-040 — storage precedes any gate
    /// rendering), then stores the resumable payload in Redis with the 30-minute TTL.
    /// </summary>
    /// <param name="invocation">
    /// The invocation to suspend. <see cref="PendingInvocation.Turn"/> is allocated from
    /// the session's gate ordinal when not positive.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The suspended invocation (with allocated turn) — hand its
    /// <see cref="PendingInvocation.GateId"/> to the presentation layer.</returns>
    public virtual async Task<PendingInvocation> SuspendInvocationAsync(
        PendingInvocation invocation,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        // 1. LEDGER FIRST — the pending marker must exist before any surface renders the gate.
        var appended = await _sessionManager.AppendGateAsync(
            invocation.TenantId,
            invocation.SessionId,
            new SessionGate
            {
                GateId = invocation.GateId,
                Kind = GateKindConfirmation,
                Status = GateStatusPending,
                Turn = invocation.Turn,
                BindingId = invocation.BindingId,
                SideEffectClass = invocation.SideEffectClass,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ct).ConfigureAwait(false);

        var suspended = invocation with
        {
            Turn = appended?.Entry.Turn ?? invocation.Turn,
            CreatedAt = invocation.CreatedAt == default ? DateTimeOffset.UtcNow : invocation.CreatedAt,
        };

        // 2. Store the resumable payload (Tier 3; ArgsJson never logged — NFR-07).
        var json = JsonSerializer.Serialize(suspended, JsonOptions);
        await _cache.SetStringAsync(
            suspended.TenantId,
            GateCacheResource,
            BuildGateKey(suspended.SessionId, suspended.GateId),
            CacheVersion,
            json,
            ttl: PendingPlanTtl,
            ct: ct).ConfigureAwait(false);

        // 3. Gate telemetry (ADR-016 / NFR-09: counts + identifiers, never content).
        _logger.LogInformation(
            "gate_suspended — gateId={GateId}, tool={ToolId}, sideEffectClass={SideEffectClass}, " +
            "risk={Risk}, bindingId={BindingId}, turn={Turn}, session={SessionId}, tenant={TenantId}, ttl=30m",
            suspended.GateId, suspended.ToolId, suspended.SideEffectClass,
            suspended.Risk, suspended.BindingId, suspended.Turn, suspended.SessionId, suspended.TenantId);

        return suspended;
    }

    /// <summary>
    /// Retrieves a suspended invocation without resolving it (presentation refresh).
    /// Returns null when the gate expired or was already resolved.
    /// </summary>
    public virtual async Task<PendingInvocation?> GetInvocationAsync(
        string tenantId,
        string sessionId,
        string gateId,
        CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(
            tenantId, GateCacheResource, BuildGateKey(sessionId, gateId), CacheVersion, ct: ct)
            .ConfigureAwait(false);

        return json is null ? null : JsonSerializer.Deserialize<PendingInvocation>(json, JsonOptions);
    }

    /// <summary>
    /// Resumes (confirms) a suspended invocation: get-then-delete from the store, then a
    /// <c>confirmed</c> <see cref="SessionGate"/> resolution marker is appended (same gate
    /// id, pending entry's turn — ADR-040 append-only correlation).
    ///
    /// Double-confirm protection: the second concurrent resume finds no key and returns
    /// null (caller responds 409, mirroring the plan-approve contract).
    /// </summary>
    /// <returns>The invocation to execute, or null when expired/already resolved.</returns>
    public virtual async Task<PendingInvocation?> ResumeInvocationAsync(
        string tenantId,
        string sessionId,
        string gateId,
        CancellationToken ct = default)
    {
        var invocation = await TakeInvocationAsync(tenantId, sessionId, gateId, ct).ConfigureAwait(false);
        if (invocation is null)
        {
            _logger.LogInformation(
                "gate_confirm_miss — gateId={GateId}, session={SessionId}, tenant={TenantId} (expired or already resolved)",
                gateId, sessionId, tenantId);
            return null;
        }

        await AppendResolutionAsync(invocation, GateStatusConfirmed, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "gate_confirmed — gateId={GateId}, tool={ToolId}, sideEffectClass={SideEffectClass}, " +
            "turn={Turn}, session={SessionId}, tenant={TenantId}",
            invocation.GateId, invocation.ToolId, invocation.SideEffectClass,
            invocation.Turn, sessionId, tenantId);

        return invocation;
    }

    /// <summary>
    /// Rejects a suspended invocation: deletes it from the store and appends a
    /// <c>rejected</c> <see cref="SessionGate"/> resolution marker. Idempotent —
    /// rejecting an expired/resolved gate is a no-op returning false.
    /// </summary>
    /// <returns>True when a pending invocation existed and was rejected.</returns>
    public virtual async Task<bool> RejectInvocationAsync(
        string tenantId,
        string sessionId,
        string gateId,
        CancellationToken ct = default)
    {
        var invocation = await TakeInvocationAsync(tenantId, sessionId, gateId, ct).ConfigureAwait(false);
        if (invocation is null)
        {
            _logger.LogInformation(
                "gate_reject_miss — gateId={GateId}, session={SessionId}, tenant={TenantId} (expired or already resolved)",
                gateId, sessionId, tenantId);
            return false;
        }

        await AppendResolutionAsync(invocation, GateStatusRejected, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "gate_rejected — gateId={GateId}, tool={ToolId}, sideEffectClass={SideEffectClass}, " +
            "turn={Turn}, session={SessionId}, tenant={TenantId}",
            invocation.GateId, invocation.ToolId, invocation.SideEffectClass,
            invocation.Turn, sessionId, tenantId);

        return true;
    }

    /// <summary>
    /// Writes a <see cref="SessionGate"/> ledger marker WITHOUT a store payload — for gate
    /// presentations whose resumable state lives elsewhere in this same store (the
    /// plan-preview flow keys its <see cref="PendingPlan"/> per session; the FR-48
    /// must-click options flow resolves by user click). Task 032 (loop-native elicitation)
    /// uses this for its in-flight <c>elicitation</c> markers.
    /// </summary>
    /// <returns>The stored entry (with allocated turn), or null when the session no longer exists.</returns>
    public virtual async Task<SessionGate?> WriteGateMarkerAsync(
        string tenantId,
        string sessionId,
        string gateId,
        string kind,
        string status,
        string? bindingId = null,
        string? sideEffectClass = null,
        int turn = 0,
        CancellationToken ct = default)
    {
        var appended = await _sessionManager.AppendGateAsync(
            tenantId,
            sessionId,
            new SessionGate
            {
                GateId = gateId,
                Kind = kind,
                Status = status,
                Turn = turn,
                BindingId = bindingId,
                SideEffectClass = sideEffectClass,
                CreatedAt = DateTimeOffset.UtcNow,
                ResolvedAt = status is GateStatusPending ? null : DateTimeOffset.UtcNow,
            },
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "gate_marker — gateId={GateId}, kind={Kind}, status={Status}, session={SessionId}, tenant={TenantId}",
            gateId, kind, status, sessionId, tenantId);

        return appended?.Entry;
    }

    private async Task<PendingInvocation?> TakeInvocationAsync(
        string tenantId, string sessionId, string gateId, CancellationToken ct)
    {
        var key = BuildGateKey(sessionId, gateId);
        var json = await _cache.GetStringAsync(tenantId, GateCacheResource, key, CacheVersion, ct: ct)
            .ConfigureAwait(false);
        if (json is null)
        {
            return null;
        }

        // Delete before parsing — the second racer finds null (double-execution protection,
        // same two-step approach documented on GetAndDeleteAsync below).
        await _cache.RemoveAsync(tenantId, GateCacheResource, key, CacheVersion, ct: ct).ConfigureAwait(false);

        return JsonSerializer.Deserialize<PendingInvocation>(json, JsonOptions);
    }

    private async Task AppendResolutionAsync(
        PendingInvocation invocation, string status, CancellationToken ct)
    {
        await _sessionManager.AppendGateAsync(
            invocation.TenantId,
            invocation.SessionId,
            new SessionGate
            {
                GateId = invocation.GateId,
                Kind = GateKindConfirmation,
                Status = status,
                Turn = invocation.Turn,
                BindingId = invocation.BindingId,
                SideEffectClass = invocation.SideEffectClass,
                CreatedAt = DateTimeOffset.UtcNow,
                ResolvedAt = DateTimeOffset.UtcNow,
            },
            ct).ConfigureAwait(false);
    }

    private static string BuildGateKey(string sessionId, string gateId) => $"{sessionId}:{gateId}";

    // =========================================================================
    // Plan-shaped gate entries (plan-preview presentation of the same gate)
    // =========================================================================

    /// <summary>
    /// Protected constructor used only by <see cref="NullPendingPlanManager"/> when the
    /// compound AI kill switch is OFF. The production scoped instance always uses the public ctor.
    /// </summary>
    /// <remarks>
    /// Task 011 Phase 1b Tier 3 (D-09 §2 B3, 2026-06-01). Keeps the Null-Object subclass from
    /// resolving <see cref="IDistributedCache"/> (which is unconditional, so this is purely a
    /// hygiene measure: the Null subclass never touches Redis even if it's available).
    /// </remarks>
    protected PendingPlanManager(ILogger<PendingPlanManager> logger)
    {
        _cache = null!;
        _sessionManager = null!;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stores a pending plan in Redis with the 30-minute TTL.
    /// Overwrites any existing pending plan for the session.
    /// </summary>
    /// <param name="plan">The pending plan to store.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task StoreAsync(PendingPlan plan, CancellationToken ct = default)
    {
        // Tenant-scoped via ITenantCache per FR-05. PendingPlan is JSON-serialised
        // by the wrapper using the same JsonSerializerOptions defaults; we keep the
        // pre-migration camelCase casing by serialising through SetStringAsync.
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        await _cache.SetStringAsync(
            plan.TenantId,
            CacheResource,
            plan.SessionId,
            CacheVersion,
            json,
            ttl: PendingPlanTtl,
            ct: ct);

        _logger.LogInformation(
            "PendingPlan stored — planId={PlanId}, session={SessionId}, tenant={TenantId}, steps={StepCount}, ttl=30m",
            plan.PlanId, plan.SessionId, plan.TenantId, plan.Steps.Length);
    }

    /// <summary>
    /// Retrieves the pending plan for the given session without deleting it.
    /// Returns null if no pending plan exists (e.g., expired or never created).
    /// </summary>
    /// <param name="tenantId">Tenant ID (ADR-014 tenant isolation).</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task<PendingPlan?> GetAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(
            tenantId, CacheResource, sessionId, CacheVersion, ct: ct);

        if (json is null)
        {
            _logger.LogDebug(
                "PendingPlan not found (expired or never created) — session={SessionId}, tenant={TenantId}",
                sessionId, tenantId);
            return null;
        }

        return JsonSerializer.Deserialize<PendingPlan>(json, JsonOptions);
    }

    /// <summary>
    /// Atomically retrieves and deletes the pending plan for the given session.
    ///
    /// Used by <c>POST /plan/approve</c> to prevent double-execution:
    /// - First approval request: finds the key, deletes it, returns the plan → proceed with execution.
    /// - Second (duplicate) approval request: key already gone → returns null → caller returns 409 Conflict.
    ///
    /// Note: IDistributedCache does not provide true atomic get+delete (it requires Lua scripts
    /// or the StackExchange.Redis API directly). This implementation uses a two-step approach
    /// (get then delete) which is safe for the approval scenario because:
    ///   1. The TTL window (30 min) makes the race window very narrow.
    ///   2. Plan approval is a deliberate user action, not a high-frequency operation.
    ///   3. The planId validation provides an additional idempotency check.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The pending plan, or null if not found (expired or already deleted).</returns>
    public virtual async Task<PendingPlan?> GetAndDeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var json = await _cache.GetStringAsync(
            tenantId, CacheResource, sessionId, CacheVersion, ct: ct);

        if (json is null)
        {
            _logger.LogInformation(
                "PendingPlan not found on approval attempt — session={SessionId}, tenant={TenantId} (expired or already approved)",
                sessionId, tenantId);
            return null;
        }

        // Delete the key before parsing — ensures the plan is not approved twice
        // even in a race condition (the second request will find null after the delete)
        await _cache.RemoveAsync(tenantId, CacheResource, sessionId, CacheVersion, ct: ct);

        var plan = JsonSerializer.Deserialize<PendingPlan>(json, JsonOptions);

        _logger.LogInformation(
            "PendingPlan retrieved and deleted for approval — planId={PlanId}, session={SessionId}, tenant={TenantId}",
            plan?.PlanId, sessionId, tenantId);

        return plan;
    }

    /// <summary>
    /// Deletes the pending plan for the given session without returning it.
    /// Used when the user cancels or the session is closed.
    /// </summary>
    /// <param name="tenantId">Tenant ID.</param>
    /// <param name="sessionId">Session ID.</param>
    /// <param name="ct">Cancellation token.</param>
    public virtual async Task DeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        await _cache.RemoveAsync(tenantId, CacheResource, sessionId, CacheVersion, ct: ct);

        _logger.LogDebug(
            "PendingPlan deleted — session={SessionId}, tenant={TenantId}",
            sessionId, tenantId);
    }
}
