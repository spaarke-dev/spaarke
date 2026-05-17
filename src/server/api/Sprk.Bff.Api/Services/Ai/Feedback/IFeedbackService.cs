namespace Sprk.Bff.Api.Services.Ai.Feedback;

/// <summary>
/// Stores and aggregates per-response user feedback (AIPU2-036).
///
/// Implementations write to the Cosmos DB <c>feedback</c> container
/// (partition key: <c>/tenantId</c>).
/// </summary>
public interface IFeedbackService
{
    /// <summary>
    /// Stores a single feedback entry for an AI response.
    /// The <see cref="FeedbackEntry.Comment"/> is truncated to 500 characters before write.
    /// </summary>
    /// <param name="tenantId">Tenant scope for partition isolation.</param>
    /// <param name="entry">Feedback entry to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The stored entry id.</returns>
    Task<string> SubmitAsync(string tenantId, FeedbackEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Aggregates feedback for a specific playbook over the supplied date range.
    /// </summary>
    /// <param name="tenantId">Tenant scope.</param>
    /// <param name="playbookId">Playbook identifier to aggregate.</param>
    /// <param name="from">Optional inclusive start date (UTC).</param>
    /// <param name="to">Optional inclusive end date (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated statistics, or null when no feedback exists.</returns>
    Task<FeedbackAggregate?> GetAggregateByPlaybookAsync(
        string tenantId,
        string playbookId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);

    /// <summary>
    /// Aggregates feedback for a specific AI capability (tool/action) over the supplied date range.
    /// </summary>
    /// <param name="tenantId">Tenant scope.</param>
    /// <param name="capabilityId">Capability identifier to aggregate.</param>
    /// <param name="from">Optional inclusive start date (UTC).</param>
    /// <param name="to">Optional inclusive end date (UTC).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Aggregated statistics, or null when no feedback exists.</returns>
    Task<FeedbackAggregate?> GetAggregateByCapabilityAsync(
        string tenantId,
        string capabilityId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default);
}
