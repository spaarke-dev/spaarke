namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Manages persistent per-matter structured AI memory stored in the Cosmos DB <c>memory</c> container.
///
/// Responsibilities:
/// - Load existing structured facts for a matter when a new AI session starts.
/// - Append new facts extracted or confirmed during a session.
/// - Serialise facts into a concise system prompt fragment injected into each LLM call.
/// - Delete all facts for a matter when the user explicitly requests it (GDPR Art. 17).
///
/// ADR-015 Tier 3: matter memory is user-owned work history. It may contain full structured
/// content (party names, dates, conclusions). Deletion via <see cref="ClearMemoryAsync"/> is
/// intentional and supported — distinct from the append-only Tier 2 compliance audit log.
///
/// Lifetime: Scoped (one instance per HTTP request). CosmosClient is singleton.
/// </summary>
public interface IMatterMemoryService
{
    /// <summary>
    /// Retrieves the matter memory document for the given tenant and matter.
    /// Returns <see langword="null"/> when no memory exists (first visit to this matter).
    /// </summary>
    /// <param name="tenantId">Tenant identifier (Cosmos partition key).</param>
    /// <param name="matterId">Matter/case identifier within the tenant.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="MatterMemory"/> document, or <see langword="null"/> if not found.</returns>
    Task<MatterMemory?> GetMemoryAsync(string tenantId, string matterId, CancellationToken ct = default);

    /// <summary>
    /// Appends a single structured fact to the matter memory document, creating the document
    /// if it does not yet exist. Uses ETag-based optimistic concurrency to prevent lost updates
    /// when multiple sessions modify the same matter simultaneously.
    ///
    /// On each successful write, <see cref="MatterMemory.Version"/> is incremented.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (Cosmos partition key).</param>
    /// <param name="matterId">Matter/case identifier within the tenant.</param>
    /// <param name="fact">The structured fact to append.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="Microsoft.Azure.Cosmos.CosmosException">
    /// Thrown with <c>HttpStatusCode.PreconditionFailed</c> (412) if a concurrent write modified
    /// the document between the read and this write. Callers should retry with a fresh read.
    /// </exception>
    Task SaveFactAsync(string tenantId, string matterId, MemoryFact fact, CancellationToken ct = default);

    /// <summary>
    /// Permanently deletes the matter memory document from Cosmos DB (GDPR Art. 17 erasure).
    ///
    /// Design note: this is an intentional user- or admin-initiated action. It is NOT a
    /// compliance violation — the Tier 2 audit log (separate container, immutable policy) is
    /// unaffected. Tier 3 work history is explicitly subject to user erasure rights (ADR-015).
    ///
    /// Idempotent: silently succeeds if the document does not exist.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (Cosmos partition key).</param>
    /// <param name="matterId">Matter/case identifier within the tenant.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ClearMemoryAsync(string tenantId, string matterId, CancellationToken ct = default);

    /// <summary>
    /// Serialises the current matter memory into a concise text block for injection into an LLM system prompt.
    ///
    /// Target output: 200–500 tokens depending on fact density.
    ///
    /// Sections emitted (only if facts of that type exist):
    /// <code>
    /// ### Matter Context (from prior sessions)
    /// **Parties**: Company X (plaintiff); Company Y (defendant)
    /// **Key Dates**: Markman hearing — July 15, 2026
    /// **Prior Analyses**: 3 weak claims identified (May 12, 2026)
    /// **Key Facts**: Contract value $2.4M; 3-year term
    /// </code>
    ///
    /// Returns an empty string when no memory exists for the matter.
    /// Facts with <see cref="MemoryFact.ConfirmedByUser"/> == false and
    /// <see cref="MemoryFact.Confidence"/> below 0.7 are excluded from the output.
    /// Lowest-confidence facts are dropped first if the rendered fragment would exceed 500 tokens.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="matterId">Matter/case identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A formatted system prompt fragment, or <see cref="string.Empty"/> if no memory exists.</returns>
    Task<string> ToSystemPromptFragmentAsync(string tenantId, string matterId, CancellationToken ct = default);
}
