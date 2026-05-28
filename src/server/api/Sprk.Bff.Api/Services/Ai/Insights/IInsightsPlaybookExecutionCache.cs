using Sprk.Bff.Api.Models.Insights;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Cache abstraction for Insights-mode playbook execution results (D-P13).
/// Implementations wrap <see cref="IPlaybookExecutionEngine"/> invocations with a
/// distributed cache keyed on <c>(playbookId, subject, parameters, accessibleScopeHash)</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A interface</b> per SPEC §3.5 — exposed only to Zone A callers
/// (currently the future <c>InsightsOrchestrator</c> in task 042). Zone B code never
/// imports this interface — it consumes <c>IInsightsAi</c> instead.
/// </para>
/// <para>
/// The interface is registered as a DI seam (rather than the concrete class)
/// so the <c>InsightsOrchestrator</c> can be unit-tested with an in-memory fake.
/// This is one of the ADR-010 §Exceptions cases: an interface that exists for a
/// real testability reason.
/// </para>
/// </remarks>
public interface IInsightsPlaybookExecutionCache
{
    /// <summary>
    /// Return the cached <see cref="InsightArtifact"/> for <paramref name="request"/> if one
    /// exists within the TTL window; otherwise invoke <paramref name="engineInvocation"/>,
    /// extract the artifact from the resulting stream, cache it, and return it.
    /// </summary>
    /// <param name="request">Cache lookup tuple + per-call options (TTL).</param>
    /// <param name="engineInvocation">Factory that produces a fresh
    /// <see cref="IAsyncEnumerable{PlaybookStreamEvent}"/> from the engine. Called only
    /// on cache miss; the cache fully drains the resulting stream.</param>
    /// <param name="cancellationToken">Cancellation token (honoured for both the cache
    /// lookup and the engine invocation).</param>
    /// <returns>The cached or freshly-computed <see cref="InsightArtifact"/>, or
    /// <c>null</c> if the engine completed without producing one (e.g., decline path).</returns>
    Task<InsightArtifact?> GetOrExecuteAsync(
        InsightsPlaybookExecutionRequest request,
        Func<CancellationToken, IAsyncEnumerable<PlaybookStreamEvent>> engineInvocation,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Explicitly remove a cached entry. Mainly used by admin tooling and tests; routine
    /// invalidation happens through TTL expiry plus the <c>accessibleScopeHash</c>
    /// component of the cache key (which naturally rotates entries when access changes).
    /// </summary>
    /// <param name="playbookId">Playbook identifier.</param>
    /// <param name="subject">Subject identifier.</param>
    /// <param name="parameters">Parameters used in the original invocation.</param>
    /// <param name="accessibleScopeHash">Accessible-scope hash from the original invocation.</param>
    /// <param name="tenantId">Tenant identifier (for telemetry attribution).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task EvictAsync(
        Guid playbookId,
        string subject,
        IReadOnlyDictionary<string, string>? parameters,
        string accessibleScopeHash,
        string tenantId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request shape for <see cref="IInsightsPlaybookExecutionCache.GetOrExecuteAsync"/>.
/// All four cache-key components plus optional per-call options.
/// </summary>
/// <param name="PlaybookId">Insights-mode playbook identifier.</param>
/// <param name="Subject">The subject the playbook is being asked about
/// (e.g., <c>matter:M-1234</c>). Required.</param>
/// <param name="Parameters">Playbook parameters (template substitution values).
/// May be null or empty. Keys are sorted for cache-key stability.</param>
/// <param name="AccessibleScopeHash">Hash of the caller's accessible-scope set (DEP-3).
/// Required — within-tenant access changes invalidate the cache via this component.</param>
/// <param name="TenantId">Tenant identifier (for telemetry attribution). Required.</param>
/// <param name="Ttl">Optional per-invocation TTL override. When null, the cache uses
/// <see cref="InsightsPlaybookExecutionCache.DefaultTtl"/> (5 min per D-P13).
/// The intended source is the Insights-mode playbook metadata
/// (<c>cacheTtl</c>) read by the orchestrator (task 042).</param>
public sealed record InsightsPlaybookExecutionRequest(
    Guid PlaybookId,
    string Subject,
    IReadOnlyDictionary<string, string>? Parameters,
    string AccessibleScopeHash,
    string TenantId,
    TimeSpan? Ttl = null);
