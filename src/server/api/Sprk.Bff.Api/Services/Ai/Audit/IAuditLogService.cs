namespace Sprk.Bff.Api.Services.Ai.Audit;

/// <summary>
/// Writes append-only compliance audit records for AI interactions to Cosmos DB (ADR-015 Tier 2).
///
/// Implementations MUST:
/// - Use CreateItemAsync only (never upsert, replace, or delete)
/// - Partition by tenantId
/// - Never store verbatim prompts or AI responses (SHA-256 hash only)
/// - Not propagate write failures to callers (fire-and-forget)
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Writes an audit entry for an AI interaction.
    ///
    /// This method is fire-and-forget: Cosmos DB write failures are logged at Error level
    /// but do not throw or affect the response pipeline.
    /// </summary>
    /// <param name="entry">The audit entry to write. Must have all required fields populated.</param>
    /// <param name="ct">Cancellation token (used for the background write; cancellation is handled gracefully).</param>
    ValueTask LogInteractionAsync(AuditEntry entry, CancellationToken ct = default);
}
