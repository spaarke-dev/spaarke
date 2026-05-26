using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Feedback;

/// <summary>
/// Writes per-response user feedback to the Cosmos DB <c>feedback</c> container and
/// performs aggregation queries for playbook and capability quality reporting (AIPU2-036).
///
/// Storage decisions:
/// - Partition key: <c>/tenantId</c> — all queries are tenant-scoped (ADR-015).
/// - Container: <c>feedback</c> — separate from <c>audit</c> and <c>sessions</c> containers.
/// - Retention: configured at provisioning time (90 days, ADR-015 Tier 3).
///
/// Aggregation:
/// - <see cref="GetAggregateByPlaybookAsync"/> and <see cref="GetAggregateByCapabilityAsync"/>
///   issue cross-partition SQL queries because the grouping key (playbookId / capabilityId)
///   does not equal the partition key (tenantId). This is intentional — we always filter by
///   tenantId first to bound the fan-out to a single tenant's logical partition range.
///
/// Lifetime: Scoped — one instance per HTTP request (ADR-010).
/// CosmosClient is Singleton; Container handle is resolved per call (mirrors AuditLogService).
/// </summary>
public sealed class FeedbackService : IFeedbackService
{
    private const string ContainerName = "feedback";
    private const int MaxCommentLength = 500;
    private const int TopNegativeCommentsCount = 10;

    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<FeedbackService> _logger;

    /// <summary>
    /// Initialises the <see cref="FeedbackService"/>.
    /// </summary>
    /// <param name="cosmosClient">Singleton Cosmos DB client authenticated via DefaultAzureCredential.</param>
    /// <param name="configuration">Application configuration (CosmosPersistence:DatabaseName).</param>
    /// <param name="logger">Logger for write/query failures.</param>
    public FeedbackService(
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<FeedbackService> logger)
    {
        _cosmosClient = cosmosClient ?? throw new ArgumentNullException(nameof(cosmosClient));
        _databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException("CosmosPersistence:DatabaseName is not configured.");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // =========================================================================
    // IFeedbackService — Submit
    // =========================================================================

    /// <inheritdoc/>
    public async Task<string> SubmitAsync(
        string tenantId,
        FeedbackEntry entry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(entry);

        // Enforce comment length cap before storage.
        // FeedbackEntry is a class (not a record) — re-construct when truncation is needed.
        var storedEntry = entry.Comment is { Length: > MaxCommentLength }
            ? new FeedbackEntry
            {
                Id = entry.Id,
                TenantId = entry.TenantId,
                UserId = entry.UserId,
                SessionId = entry.SessionId,
                TurnIndex = entry.TurnIndex,
                Rating = entry.Rating,
                Comment = entry.Comment[..MaxCommentLength],
                PlaybookId = entry.PlaybookId,
                CapabilityId = entry.CapabilityId,
                Timestamp = entry.Timestamp
            }
            : entry;

        var container = GetContainer();

        await container.CreateItemAsync(
            item: storedEntry,
            partitionKey: new PartitionKey(tenantId),
            cancellationToken: ct);

        _logger.LogDebug(
            "FeedbackService.SubmitAsync: stored feedback {Id} for session {SessionId}, turn {TurnIndex}, " +
            "rating {Rating}, tenant {TenantId}",
            storedEntry.Id, storedEntry.SessionId, storedEntry.TurnIndex,
            storedEntry.Rating, tenantId);

        return storedEntry.Id;
    }

    // =========================================================================
    // IFeedbackService — Aggregate by Playbook
    // =========================================================================

    /// <inheritdoc/>
    public async Task<FeedbackAggregate?> GetAggregateByPlaybookAsync(
        string tenantId,
        string playbookId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(playbookId);

        var (thumbsUp, thumbsDown, negativeComments) =
            await QueryAggregateAsync(tenantId, "playbookId", playbookId, from, to, ct);

        var total = thumbsUp + thumbsDown;
        if (total == 0)
            return null;

        return new FeedbackAggregate
        {
            EntityId = playbookId,
            EntityType = "playbook",
            TotalCount = total,
            ThumbsUpCount = thumbsUp,
            ThumbsDownCount = thumbsDown,
            DateRange = new FeedbackDateRange { From = from, To = to },
            TopNegativeComments = negativeComments
        };
    }

    // =========================================================================
    // IFeedbackService — Aggregate by Capability
    // =========================================================================

    /// <inheritdoc/>
    public async Task<FeedbackAggregate?> GetAggregateByCapabilityAsync(
        string tenantId,
        string capabilityId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityId);

        var (thumbsUp, thumbsDown, negativeComments) =
            await QueryAggregateAsync(tenantId, "capabilityId", capabilityId, from, to, ct);

        var total = thumbsUp + thumbsDown;
        if (total == 0)
            return null;

        return new FeedbackAggregate
        {
            EntityId = capabilityId,
            EntityType = "capability",
            TotalCount = total,
            ThumbsUpCount = thumbsUp,
            ThumbsDownCount = thumbsDown,
            DateRange = new FeedbackDateRange { From = from, To = to },
            TopNegativeComments = negativeComments
        };
    }

    // =========================================================================
    // Private — shared aggregation query
    // =========================================================================

    /// <summary>
    /// Runs two Cosmos SQL queries against the <c>feedback</c> container:
    /// <list type="number">
    ///   <item>COUNT per rating value for the supplied entity field/value pair and date range.</item>
    ///   <item>Last <see cref="TopNegativeCommentsCount"/> non-null comments from thumbs-down entries.</item>
    /// </list>
    /// Returns (thumbsUpCount, thumbsDownCount, topNegativeComments).
    /// </summary>
    private async Task<(int thumbsUp, int thumbsDown, IReadOnlyList<string> negativeComments)>
        QueryAggregateAsync(
            string tenantId,
            string entityField,
            string entityValue,
            DateTimeOffset? from,
            DateTimeOffset? to,
            CancellationToken ct)
    {
        var container = GetContainer();

        // Build the shared WHERE clause (tenant + entity + optional date window).
        // Parameters are bound to prevent injection even though Cosmos SQL parameterisation
        // already protects against structural injection.
        var whereClause = BuildWhereClause(entityField, from, to);

        // ------------------------------------------------------------------
        // Query 1: count thumbs-up and thumbs-down ratings
        // Cosmos DB does not support GROUP BY with SUM across a filtered set in
        // a single pass without VALUE. We use two scalar COUNT queries instead —
        // simpler and well within RU budget for per-tenant scoped queries.
        // ------------------------------------------------------------------
        var countSql =
            $"SELECT VALUE COUNT(1) FROM c WHERE c.tenantId = @tenantId AND c.{entityField} = @entityValue" +
            $"{whereClause} AND c.rating = @rating";

        var thumbsUp = await ExecuteScalarCountAsync(
            container,
            countSql,
            tenantId,
            entityValue,
            from, to,
            rating: (int)FeedbackRating.ThumbsUp,
            ct);

        var thumbsDown = await ExecuteScalarCountAsync(
            container,
            countSql,
            tenantId,
            entityValue,
            from, to,
            rating: (int)FeedbackRating.ThumbsDown,
            ct);

        // ------------------------------------------------------------------
        // Query 2: top-N thumbs-down comments (most recent first)
        // Only fetch when there are thumbs-down entries to save RUs.
        // ------------------------------------------------------------------
        List<string> negativeComments = [];
        if (thumbsDown > 0)
        {
            negativeComments = await QueryNegativeCommentsAsync(
                container, tenantId, entityField, entityValue, whereClause, from, to, ct);
        }

        return (thumbsUp, thumbsDown, negativeComments);
    }

    /// <summary>
    /// Executes a scalar COUNT query and returns the integer result.
    /// Returns 0 on any Cosmos exception (logged at Warning).
    /// </summary>
    private async Task<int> ExecuteScalarCountAsync(
        Container container,
        string sql,
        string tenantId,
        string entityValue,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int rating,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryDefinition(sql)
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@entityValue", entityValue)
                .WithParameter("@rating", rating);

            if (from.HasValue)
                query = query.WithParameter("@from", from.Value.ToString("O"));
            if (to.HasValue)
                query = query.WithParameter("@to", to.Value.ToString("O"));

            using var iterator = container.GetItemQueryIterator<int>(query);
            if (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                return page.FirstOrDefault();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FeedbackService: COUNT query failed for tenant {TenantId}, rating {Rating}.",
                tenantId, rating);
        }

        return 0;
    }

    /// <summary>
    /// Queries the last <see cref="TopNegativeCommentsCount"/> non-null thumbs-down comments,
    /// ordered by timestamp descending.
    /// </summary>
    private async Task<List<string>> QueryNegativeCommentsAsync(
        Container container,
        string tenantId,
        string entityField,
        string entityValue,
        string whereClause,
        DateTimeOffset? from,
        DateTimeOffset? to,
        CancellationToken ct)
    {
        var results = new List<string>();
        try
        {
            var sql =
                $"SELECT TOP {TopNegativeCommentsCount} VALUE c.comment " +
                $"FROM c " +
                $"WHERE c.tenantId = @tenantId AND c.{entityField} = @entityValue" +
                $"{whereClause} AND c.rating = @rating AND IS_DEFINED(c.comment) AND c.comment != null " +
                $"ORDER BY c.timestamp DESC";

            var query = new QueryDefinition(sql)
                .WithParameter("@tenantId", tenantId)
                .WithParameter("@entityValue", entityValue)
                .WithParameter("@rating", (int)FeedbackRating.ThumbsDown);

            if (from.HasValue)
                query = query.WithParameter("@from", from.Value.ToString("O"));
            if (to.HasValue)
                query = query.WithParameter("@to", to.Value.ToString("O"));

            using var iterator = container.GetItemQueryIterator<string>(query);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync(ct);
                results.AddRange(page.Resource.Where(c => c is not null));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FeedbackService: negative comments query failed for tenant {TenantId}.",
                tenantId);
        }

        return results;
    }

    // =========================================================================
    // Private helpers
    // =========================================================================

    private Container GetContainer() => _cosmosClient.GetContainer(_databaseName, ContainerName);

    /// <summary>
    /// Builds the optional date-range fragment for the WHERE clause.
    /// Parameters are named <c>@from</c> and <c>@to</c> and must be bound by the caller.
    /// Returns an empty string when neither bound is specified.
    /// </summary>
    private static string BuildWhereClause(string entityField, DateTimeOffset? from, DateTimeOffset? to)
    {
        // entityField is supplied by the service internally (not user input),
        // so interpolating it directly into SQL is safe here.
        _ = entityField; // used in the outer query, not here

        var parts = new List<string>();
        if (from.HasValue) parts.Add(" AND c.timestamp >= @from");
        if (to.HasValue) parts.Add(" AND c.timestamp <= @to");
        return string.Concat(parts);
    }
}
