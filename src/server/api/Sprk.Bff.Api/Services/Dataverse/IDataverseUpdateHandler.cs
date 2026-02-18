using Sprk.Bff.Api.Services.Ai;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Handles Dataverse entity updates with type conversion, optimistic concurrency, and retry logic.
/// </summary>
/// <remarks>
/// This is NOT a tool handler - it's a code component called by OutputOrchestrator.
/// It provides low-level update operations with advanced features:
/// - Type conversions for Money, EntityReference, DateTime
/// - Optimistic concurrency control with row version checking
/// - Retry logic with exponential backoff
/// - Conflict detection and handling
/// </remarks>
public interface IDataverseUpdateHandler
{
    /// <summary>
    /// Update a Dataverse entity with field values.
    /// </summary>
    /// <param name="entityLogicalName">Entity logical name (e.g., "sprk_invoice", "sprk_matter")</param>
    /// <param name="recordId">Record ID to update</param>
    /// <param name="fields">Field name → value dictionary (values can be Money, EntityReference, etc.)</param>
    /// <param name="concurrencyMode">Concurrency mode (None or Optimistic)</param>
    /// <param name="maxRetries">Max retries for optimistic concurrency conflicts (default: 3)</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="InvalidOperationException">Thrown if optimistic concurrency fails after max retries</exception>
    Task UpdateAsync(
        string entityLogicalName,
        Guid recordId,
        Dictionary<string, object?> fields,
        ConcurrencyMode concurrencyMode,
        int maxRetries,
        CancellationToken ct);
}
