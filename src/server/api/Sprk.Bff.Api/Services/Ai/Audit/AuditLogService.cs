using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Audit;

/// <summary>
/// Writes append-only compliance audit records for AI interactions to the Cosmos DB <c>audit</c> container.
///
/// ADR-015 Tier 2 compliance enforced in code:
/// - Only <see cref="Container.CreateItemAsync{T}"/> is used — no upsert, replace, or delete.
/// - Partition key is <c>/tenantId</c> for all writes.
/// - SHA-256 response hash stored instead of verbatim text.
///
/// INFRASTRUCTURE REQUIREMENT (must be applied at provisioning time):
/// The <c>audit</c> container MUST be created with an immutable policy so that data-plane
/// updates and deletes are impossible even with Cosmos DB Built-in Data Contributor access.
/// See <c>infrastructure/cosmos/audit-container-policy.json</c> for the required policy definition.
///
/// Fire-and-forget pattern:
/// <see cref="LogInteractionAsync"/> enqueues the write on the thread pool via <see cref="Task.Run"/>.
/// Cosmos write failures are caught, logged at Error level, and never propagated to callers.
/// This ensures audit writes do not add measurable latency to streaming responses (ADR-015 goal).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private const string ContainerName = "audit";

    private readonly Container _container;
    private readonly ILogger<AuditLogService> _logger;

    /// <summary>
    /// Initializes the <see cref="AuditLogService"/>.
    /// </summary>
    /// <param name="cosmosClient">Singleton Cosmos DB client authenticated via DefaultAzureCredential.</param>
    /// <param name="databaseName">Cosmos DB database name (from CosmosPersistence:DatabaseName config).</param>
    /// <param name="logger">Logger for audit write failures.</param>
    public AuditLogService(CosmosClient cosmosClient, string databaseName, ILogger<AuditLogService> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(logger);

        _container = cosmosClient.GetContainer(databaseName, ContainerName);
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The Cosmos DB write is dispatched to the thread pool via <see cref="Task.Run"/> so that the
    /// caller is not blocked. The returned <see cref="ValueTask"/> completes as soon as the write
    /// has been dispatched — not when Cosmos DB confirms receipt.
    ///
    /// Design rationale: audit writes are compliance obligations but must not delay streaming
    /// AI responses to end users. A background write on the thread pool satisfies both concerns.
    /// The calling code should invoke this method after streaming completes.
    /// </remarks>
    public ValueTask LogInteractionAsync(AuditEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        // Fire-and-forget: dispatch to thread pool and return immediately.
        // CancellationToken is passed so the write respects graceful shutdown, but
        // OperationCanceledException is caught and swallowed like other write failures.
        _ = Task.Run(() => WriteAuditEntryAsync(entry, ct), ct);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Performs the actual Cosmos DB write. Runs on the thread pool.
    /// All exceptions are caught and logged — never propagated.
    /// </summary>
    private async Task WriteAuditEntryAsync(AuditEntry entry, CancellationToken ct)
    {
        try
        {
            // APPEND-ONLY: CreateItemAsync is the ONLY Cosmos DB write operation permitted.
            // UpsertItemAsync, ReplaceItemAsync, and DeleteItemAsync are intentionally absent
            // from this service. The infrastructure immutable policy provides the second layer
            // of enforcement (see infrastructure/cosmos/audit-container-policy.json).
            await _container.CreateItemAsync(
                item: entry,
                partitionKey: new PartitionKey(entry.TenantId),
                cancellationToken: ct);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown — swallow silently. The audit write was abandoned during teardown.
            _logger.LogDebug(
                "Audit write cancelled for session {SessionId} (graceful shutdown).",
                entry.SessionId);
        }
        catch (Exception ex)
        {
            // Audit write failure must never surface to the user or affect streaming.
            // Log at Error so SRE/compliance teams can investigate Cosmos connectivity issues.
            _logger.LogError(
                ex,
                "Failed to write audit entry for session {SessionId}, tenant {TenantId}, action {Action}. " +
                "Entry id: {EntryId}. Audit write failures do not affect the user response.",
                entry.SessionId,
                entry.TenantId,
                entry.Action,
                entry.Id);
        }
    }
}
