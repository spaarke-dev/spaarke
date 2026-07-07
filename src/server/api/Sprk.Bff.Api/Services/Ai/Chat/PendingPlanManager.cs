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
/// rejects it: <b>generalized invocations</b> (<see cref="PendingInvocation"/>) —
/// write/communicate tool calls suspended by the agent-turn loop, must-click capability
/// confirmations (FR-48), and any other side-effect awaiting explicit user consent.
/// Keyed per gate id; suspend/resume/reject via <see cref="SuspendInvocationAsync"/> /
/// <see cref="ResumeInvocationAsync"/> / <see cref="RejectInvocationAsync"/>.
/// (The legacy plan-shaped session-singleton entries were DELETED by task 035 / FR-P2-06
/// with the dispatcher stack that produced them.)
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
    /// <summary>Absolute TTL for pending gate entries (task 070 design).</summary>
    internal static readonly TimeSpan PendingPlanTtl = TimeSpan.FromMinutes(30);

    /// <summary>Tenant-cache resource name for generalized pending-invocation payloads (D12).</summary>
    internal const string GateCacheResource = "pending-gate";

    /// <summary>Tenant-cache schema version for pending gate payloads.</summary>
    internal const int CacheVersion = 1;

    /// <summary>Ledger vocabulary: gate kind for side-effect confirmations (ADR-040).</summary>
    public const string GateKindConfirmation = "confirmation";

    /// <summary>Ledger vocabulary: gate kind for in-flight elicitations (FR-P2-03 / ADR-040).</summary>
    public const string GateKindElicitation = "elicitation";

    /// <summary>Ledger vocabulary: gate status <c>pending</c> (ADR-040 <c>pending | confirmed | rejected | expired | superseded</c>).</summary>
    public const string GateStatusPending = "pending";

    /// <summary>Ledger vocabulary: gate status <c>confirmed</c>.</summary>
    public const string GateStatusConfirmed = "confirmed";

    /// <summary>Ledger vocabulary: gate status <c>rejected</c>.</summary>
    public const string GateStatusRejected = "rejected";

    /// <summary>Ledger vocabulary: gate status <c>expired</c> (resumable payload TTL lapsed).</summary>
    public const string GateStatusExpired = "expired";

    /// <summary>Ledger vocabulary: gate status <c>superseded</c> (user broke out — hard slash / explicit restart / new work).</summary>
    public const string GateStatusSuperseded = "superseded";

    /// <summary>
    /// Ledger vocabulary: gate status <c>confirmed-unexecutable</c> — the user CONFIRMED
    /// the gate, but the invocation has no resolvable execution target from this surface
    /// (since FR-P3-03 / task 042 landed typed-handler confirm-resume, this is the
    /// fallback for invocations whose tool row is not chat-available, has no registered
    /// handler, or when the compound-AI services are off).
    /// Honest terminal state (G-P2 UAT round-1 finding 6, 2026-07-06): a plain
    /// <c>confirmed</c> marker would falsely record an executed side effect, and leaving
    /// the gate <c>pending</c> would contradict the user's explicit click. ADR-040
    /// vocabulary extension — append-only resolution correlated by gate id, like every
    /// other terminal status.
    /// </summary>
    public const string GateStatusConfirmedUnexecutable = "confirmed-unexecutable";

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
                Kind = invocation.Kind,
                Status = GateStatusPending,
                Turn = invocation.Turn,
                BindingId = invocation.BindingId,
                SideEffectClass = invocation.SideEffectClass,
                MissingFields = invocation.MissingFields,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ct).ConfigureAwait(false);

        if (appended is null)
        {
            // Task 032 W1 tightening (Step 9.5 review of task 031): a suspension whose pending
            // marker cannot land in the ledger MUST NOT proceed — ADR-040 storage-precedes-
            // rendering means an unmarked suspended payload would be invisible ledger state.
            throw new InvalidOperationException(
                $"Cannot suspend invocation '{invocation.GateId}': chat session " +
                $"'{invocation.SessionId}' no longer exists — the ADR-040 pending Gate marker " +
                "could not be written, so the suspension is aborted.");
        }

        var suspended = invocation with
        {
            Turn = appended.Value.Entry.Turn,
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
            "gate_suspended — gateId={GateId}, kind={Kind}, tool={ToolId}, sideEffectClass={SideEffectClass}, " +
            "risk={Risk}, bindingId={BindingId}, missingFieldCount={MissingFieldCount}, turn={Turn}, " +
            "session={SessionId}, tenant={TenantId}, ttl=30m",
            suspended.GateId, suspended.Kind, suspended.ToolId, suspended.SideEffectClass,
            suspended.Risk, suspended.BindingId, suspended.MissingFields?.Count ?? 0,
            suspended.Turn, suspended.SessionId, suspended.TenantId);

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
    /// presentations whose resolution happens outside the payload store (e.g. resolved
    /// directly by a user click). Task 032 (loop-native elicitation) uses this for its
    /// in-flight <c>elicitation</c> markers.
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
        IReadOnlyList<string>? missingFields = null,
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
                MissingFields = missingFields,
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
                Kind = invocation.Kind,
                Status = status,
                Turn = invocation.Turn,
                BindingId = invocation.BindingId,
                SideEffectClass = invocation.SideEffectClass,
                MissingFields = invocation.MissingFields,
                CreatedAt = DateTimeOffset.UtcNow,
                ResolvedAt = DateTimeOffset.UtcNow,
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes a suspended invocation with an arbitrary terminal status
    /// (<see cref="GateStatusSuperseded"/> for deterministic break-outs — hard slash /
    /// explicit restart, FR-P2-03; <see cref="GateStatusExpired"/> when a caller detects
    /// TTL lapse while the payload still exists). Deletes the resumable payload and
    /// appends the resolution marker. Idempotent — closing an expired/resolved gate is
    /// a no-op returning false (callers holding only the ledger marker use
    /// <see cref="WriteGateMarkerAsync"/> for the marker-only close).
    /// </summary>
    public virtual async Task<bool> CloseInvocationAsync(
        string tenantId,
        string sessionId,
        string gateId,
        string status,
        CancellationToken ct = default)
    {
        var invocation = await TakeInvocationAsync(tenantId, sessionId, gateId, ct).ConfigureAwait(false);
        if (invocation is null)
        {
            return false;
        }

        await AppendResolutionAsync(invocation, status, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "gate_closed — gateId={GateId}, kind={Kind}, status={Status}, tool={ToolId}, " +
            "turn={Turn}, session={SessionId}, tenant={TenantId}",
            invocation.GateId, invocation.Kind, status, invocation.ToolId,
            invocation.Turn, sessionId, tenantId);

        return true;
    }

    /// <summary>
    /// THE deterministic pending-gate ledger query (FR-P2-03): the most recent gate of
    /// <paramref name="kind"/> (optionally scoped to <paramref name="bindingId"/>) whose
    /// LATEST entry is still <c>pending</c>. Append-only correlation: entries group by
    /// <see cref="SessionGate.GateId"/>; the last-appended entry per gate id is its
    /// current state. Pure — no store access, no classification.
    /// </summary>
    public static SessionGate? FindPendingGate(
        IReadOnlyList<SessionGate>? gates,
        string kind,
        string? bindingId = null)
    {
        if (gates is not { Count: > 0 })
        {
            return null;
        }

        // Ledger order IS append order (AppendGateAsync appends); the last entry per
        // gate id is the gate's current state.
        SessionGate? latestPending = null;
        var currentByGate = new Dictionary<string, SessionGate>(StringComparer.Ordinal);
        foreach (var entry in gates)
        {
            currentByGate[entry.GateId] = entry;
        }

        foreach (var entry in gates)
        {
            var current = currentByGate[entry.GateId];
            if (!string.Equals(current.Status, GateStatusPending, StringComparison.Ordinal) ||
                !string.Equals(current.Kind, kind, StringComparison.Ordinal))
            {
                continue;
            }

            if (bindingId is not null &&
                !string.Equals(current.BindingId, bindingId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            latestPending = current; // later appends win — the newest pending gate
        }

        return latestPending;
    }

    /// <summary>
    /// Resolves the session's pending <c>elicitation</c> gate for <paramref name="bindingId"/>
    /// after a successful dispatch of that Binding (FR-P2-03). Called by
    /// <see cref="SessionDispatchOrchestrator"/> at the ONE dispatch seam — a completed
    /// dispatch of the awaited Binding IS the elicitation's answer, whether it arrived via
    /// the loop re-invoking the capability tool with completed args, the wizard surface
    /// (capture_mode modal) posting through <c>dispatchConsumer</c>, or a chip click.
    /// No-op when no elicitation is pending for the Binding.
    /// </summary>
    /// <returns>The confirmed resolution marker, or null when nothing was pending.</returns>
    public virtual async Task<SessionGate?> ResolveElicitationOnDispatchAsync(
        string tenantId,
        string sessionId,
        Guid bindingId,
        CancellationToken ct = default)
    {
        var session = await _sessionManager.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        var pending = FindPendingGate(session?.Gates, GateKindElicitation, bindingId.ToString());
        if (pending is null)
        {
            return null;
        }

        // Best-effort payload cleanup (may already be TTL-expired — that's fine).
        await _cache.RemoveAsync(
            tenantId, GateCacheResource, BuildGateKey(sessionId, pending.GateId), CacheVersion, ct: ct)
            .ConfigureAwait(false);

        var resolved = await WriteGateMarkerAsync(
            tenantId,
            sessionId,
            pending.GateId,
            GateKindElicitation,
            GateStatusConfirmed,
            bindingId: pending.BindingId,
            sideEffectClass: pending.SideEffectClass,
            missingFields: pending.MissingFields,
            turn: pending.Turn,
            ct: ct).ConfigureAwait(false);

        _logger.LogInformation(
            "elicitation_resolved — gateId={GateId}, bindingId={BindingId}, turn={Turn}, " +
            "session={SessionId}, tenant={TenantId}",
            pending.GateId, pending.BindingId, pending.Turn, sessionId, tenantId);

        return resolved;
    }

    private static string BuildGateKey(string sessionId, string gateId) => $"{sessionId}:{gateId}";

    // FR-P2-06 (task 035): the plan-shaped session-singleton entries (StoreAsync /
    // GetAsync / GetAndDeleteAsync / DeleteAsync over the legacy plan record) were
    // DELETED with the dispatcher stack and the /plan/approve endpoint — nothing
    // produced or consumed them after the FR-P2-05 cutover. The generalized
    // PendingInvocation store above IS the ONE gate.

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
}
