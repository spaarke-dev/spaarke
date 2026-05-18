namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Restores a persisted AI chat session from Cosmos DB, checks Dataverse entity staleness,
/// and reconstructs the LLM context window ready for injection into the next streaming request.
///
/// Performance target: &lt;500ms p95 total restore time (load + parallel ETag checks + reconstruct).
///
/// Lifetime: Scoped — one instance per HTTP request. Injected by the session-resume endpoint.
///
/// Tenant isolation: all operations are scoped by <c>tenantId</c> (ADR-015, NFR-09).
/// </summary>
public interface ISessionRestoreService
{
    /// <summary>
    /// Restores the session identified by <paramref name="sessionId"/> within <paramref name="tenantId"/>.
    ///
    /// Restore steps:
    /// <list type="number">
    ///   <item>Load session from Cosmos DB via <see cref="ISessionPersistenceService.LoadSessionAsync"/>.</item>
    ///   <item>If not found, return <c>null</c> — caller handles the new-session path.</item>
    ///   <item>Check Dataverse entity ETags in parallel (Task.WhenAll) to detect stale references.</item>
    ///   <item>Reconstruct the context window: summary (if present) + last 10 verbatim messages.</item>
    ///   <item>Return a <see cref="RestoredSession"/> with all artefacts ready for injection.</item>
    /// </list>
    ///
    /// Entity staleness (ADR-015 D-08): stale entity refs are surfaced in
    /// <see cref="RestoredSession.StaleEntityRefs"/> — never silently ignored.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (partition key, ADR-015).</param>
    /// <param name="sessionId">Session identifier to restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A populated <see cref="RestoredSession"/> when the session exists, or <c>null</c> when not found.
    /// </returns>
    Task<RestoredSession?> RestoreSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default);
}
