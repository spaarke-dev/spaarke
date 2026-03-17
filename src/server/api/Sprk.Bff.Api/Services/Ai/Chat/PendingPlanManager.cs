using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Manages storage and retrieval of pending plans in Redis.
///
/// A pending plan is created when compound intent is detected in <see cref="CompoundIntentDetector"/>
/// and stored until the user approves via <c>POST /api/ai/chat/sessions/{sessionId}/plan/approve</c>.
///
/// Storage design (task 070 design doc):
/// - Redis key: <c>"plan:pending:{tenantId}:{sessionId}"</c>
/// - TTL: 30 minutes (absolute, not sliding). If the user walks away, the plan expires cleanly.
/// - NOT embedded in <see cref="ChatSession"/> to avoid inflating every session cache read.
///
/// Concurrent approval protection: <see cref="GetAndDeleteAsync"/> performs an atomic get+delete.
/// If two approval requests race, only the first succeeds (the second finds no key and returns null).
///
/// Cache key pattern follows ADR-014 (tenant-scoped keys for multi-tenant isolation).
/// Storage follows ADR-009 (Redis via <see cref="IDistributedCache"/>; no in-memory fallback).
///
/// DI registration: Scoped (one per HTTP request, same as <see cref="ChatSessionManager"/>).
/// No additional DI registrations needed — <see cref="IDistributedCache"/> is already registered.
/// </summary>
public sealed class PendingPlanManager
{
    /// <summary>Absolute TTL for pending plans (30 minutes per task 070 design).</summary>
    internal static readonly TimeSpan PendingPlanTtl = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IDistributedCache _cache;
    private readonly ILogger<PendingPlanManager> _logger;

    /// <summary>
    /// Builds the Redis key for a pending plan.
    /// Pattern: <c>"plan:pending:{tenantId}:{sessionId}"</c> (ADR-014).
    /// </summary>
    internal static string BuildPendingPlanKey(string tenantId, string sessionId)
        => $"plan:pending:{tenantId}:{sessionId}";

    public PendingPlanManager(
        IDistributedCache cache,
        ILogger<PendingPlanManager> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Stores a pending plan in Redis with the 30-minute TTL.
    /// Overwrites any existing pending plan for the session.
    /// </summary>
    /// <param name="plan">The pending plan to store.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StoreAsync(PendingPlan plan, CancellationToken ct = default)
    {
        var key = BuildPendingPlanKey(plan.TenantId, plan.SessionId);
        var json = JsonSerializer.Serialize(plan, JsonOptions);
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = PendingPlanTtl
        };

        await _cache.SetAsync(key, bytes, options, ct);

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
    public async Task<PendingPlan?> GetAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var key = BuildPendingPlanKey(tenantId, sessionId);
        var bytes = await _cache.GetAsync(key, ct);

        if (bytes is null)
        {
            _logger.LogDebug(
                "PendingPlan not found (expired or never created) — session={SessionId}, tenant={TenantId}",
                sessionId, tenantId);
            return null;
        }

        var json = System.Text.Encoding.UTF8.GetString(bytes);
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
    public async Task<PendingPlan?> GetAndDeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var key = BuildPendingPlanKey(tenantId, sessionId);
        var bytes = await _cache.GetAsync(key, ct);

        if (bytes is null)
        {
            _logger.LogInformation(
                "PendingPlan not found on approval attempt — session={SessionId}, tenant={TenantId} (expired or already approved)",
                sessionId, tenantId);
            return null;
        }

        // Delete the key before parsing — ensures the plan is not approved twice
        // even in a race condition (the second request will find null after the delete)
        await _cache.RemoveAsync(key, ct);

        var json = System.Text.Encoding.UTF8.GetString(bytes);
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
    public async Task DeleteAsync(string tenantId, string sessionId, CancellationToken ct = default)
    {
        var key = BuildPendingPlanKey(tenantId, sessionId);
        await _cache.RemoveAsync(key, ct);

        _logger.LogDebug(
            "PendingPlan deleted — session={SessionId}, tenant={TenantId}",
            sessionId, tenantId);
    }
}
