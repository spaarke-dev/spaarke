using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Api.SpeAdmin;

/// <summary>
/// Audit log query endpoint for the SPE Admin app.
/// Provides read access to the sprk_speauditlog Dataverse table so administrators
/// can search and filter the compliance audit trail.
///
/// All container mutations, permission changes, and file operations are recorded in
/// sprk_speauditlog. This endpoint surfaces them with date range, category, and
/// pagination filters for operational and compliance visibility.
/// </summary>
/// <remarks>
/// Follows ADR-001: Minimal API — MapGet on a RouteGroupBuilder, no controllers.
/// Follows ADR-008: Authorization inherited from the /api/spe route group in SpeAdminEndpoints.
/// Follows ADR-019: ProblemDetails for all error responses.
/// </remarks>
public static class AuditLogEndpoints
{
    private const string AuditLogEntitySet = "sprk_speauditlogs";

    /// <summary>
    /// Registers the audit log query endpoint on the /api/spe route group.
    /// Called from <see cref="SpeAdminEndpoints.MapSpeAdminEndpoints"/>.
    /// </summary>
    /// <param name="group">The /api/spe route group (auth already applied).</param>
    /// <returns>The route group for chaining.</returns>
    public static RouteGroupBuilder MapAuditLogEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/audit", QueryAuditLogAsync)
            .WithName("QueryAuditLog")
            .WithSummary("Query SPE audit log entries")
            .WithDescription(
                "Returns audit log entries from sprk_speauditlog filtered by configId, " +
                "date range, and category. Supports pagination via $top and $skip.")
            .Produces<AuditLogPageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// GET /api/spe/audit
    ///
    /// Query parameters:
    ///   configId  — (required) GUID of the sprk_specontainertypeconfig record to scope results.
    ///   from      — (optional) ISO 8601 UTC date-time lower bound (inclusive).
    ///   to        — (optional) ISO 8601 UTC date-time upper bound (inclusive).
    ///   category  — (optional) Category string to match (e.g. "Configuration", "Permission").
    ///   $top      — (optional) Page size; defaults to 50, max 200.
    ///   $skip     — (optional) Number of records to skip for pagination; defaults to 0.
    /// </summary>
    private static async Task<IResult> QueryAuditLogAsync(
        [FromQuery] Guid? configId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? category,
        [FromQuery(Name = "$top")] int? top,
        [FromQuery(Name = "$skip")] int? skip,
        DataverseWebApiClient dataverseClient,
        ILogger<Program> logger,
        HttpContext context,
        CancellationToken ct)
    {
        // configId is required — return 400 if missing
        if (configId is null || configId == Guid.Empty)
        {
            return TypedResults.Problem(
                detail: "The 'configId' query parameter is required and must be a valid GUID.",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.audit.query.missing_config_id",
                    ["traceId"] = context.TraceIdentifier
                });
        }

        // Clamp and default pagination values
        var pageSize = Math.Clamp(top ?? 50, 1, 200);
        var pageSkip = Math.Max(skip ?? 0, 0);

        // Build OData $filter expression
        var filter = BuildODataFilter(configId.Value, from, to, category);

        var select = string.Join(",",
            "sprk_speauditlogid",
            "sprk_operation",
            "sprk_category",
            "sprk_targetresource",
            "sprk_responsestatus",
            "sprk_performedby",
            "sprk_performedon");

        logger.LogInformation(
            "Querying audit log: ConfigId={ConfigId} From={From} To={To} Category={Category} Top={Top} Skip={Skip} TraceId={TraceId}",
            configId, from, to, category, pageSize, pageSkip, context.TraceIdentifier);

        try
        {
            var entries = await dataverseClient.QueryAsync<AuditLogEntry>(
                AuditLogEntitySet,
                filter: filter,
                select: select,
                top: pageSize,
                skip: pageSkip,
                cancellationToken: ct);

            var response = new AuditLogPageResponse
            {
                Items = entries,
                Top = pageSize,
                Skip = pageSkip,
                Count = entries.Count
            };

            return TypedResults.Ok(response);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(
                ex,
                "Dataverse query failed for audit log. ConfigId={ConfigId} TraceId={TraceId}",
                configId, context.TraceIdentifier);

            return TypedResults.Problem(
                detail: "Failed to retrieve audit log entries from Dataverse.",
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Audit Log Query Failed",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "spe.audit.query.dataverse_error",
                    ["traceId"] = context.TraceIdentifier
                });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OData filter builder
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an OData $filter expression for the sprk_speauditlog entity set.
    ///
    /// configId is required; from, to, and category are optional.
    /// The sprk_ContainerTypeConfigId navigation property is filtered via its lookup id
    /// using the Dataverse OData syntax: sprk_ContainerTypeConfigId eq {guid}.
    /// </summary>
    private static string BuildODataFilter(
        Guid configId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? category)
    {
        var clauses = new List<string>
        {
            // configId is required — filter on the lookup FK
            $"_sprk_containertypeconfigid_value eq {configId}"
        };

        if (from.HasValue)
        {
            // ISO 8601 UTC — Dataverse OData filter uses datetime literal without quotes
            clauses.Add($"sprk_performedon ge {from.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
        }

        if (to.HasValue)
        {
            clauses.Add($"sprk_performedon le {to.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            // Escape single quotes in category value per OData 4.0 spec
            var escapedCategory = category.Replace("'", "''");
            clauses.Add($"sprk_category eq '{escapedCategory}'");
        }

        return string.Join(" and ", clauses);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response DTOs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Paginated response envelope for audit log queries.
    /// </summary>
    private sealed class AuditLogPageResponse
    {
        /// <summary>Audit log entries for the requested page.</summary>
        [JsonPropertyName("items")]
        public List<AuditLogEntry> Items { get; set; } = [];

        /// <summary>Number of entries returned in this page.</summary>
        [JsonPropertyName("count")]
        public int Count { get; set; }

        /// <summary>Effective page size used for this query.</summary>
        [JsonPropertyName("top")]
        public int Top { get; set; }

        /// <summary>Number of records skipped (offset).</summary>
        [JsonPropertyName("skip")]
        public int Skip { get; set; }
    }

    /// <summary>
    /// Audit log entry deserialized from the sprk_speauditlog Dataverse entity set.
    /// Property names match Dataverse logical attribute names returned by the Web API.
    /// </summary>
    private sealed class AuditLogEntry
    {
        [JsonPropertyName("sprk_speauditlogid")]
        public Guid? Id { get; set; }

        [JsonPropertyName("sprk_operation")]
        public string? Operation { get; set; }

        [JsonPropertyName("sprk_category")]
        public string? Category { get; set; }

        [JsonPropertyName("sprk_targetresource")]
        public string? TargetResource { get; set; }

        [JsonPropertyName("sprk_responsestatus")]
        public int? ResponseStatus { get; set; }

        [JsonPropertyName("sprk_performedby")]
        public string? PerformedBy { get; set; }

        [JsonPropertyName("sprk_performedon")]
        public DateTimeOffset? PerformedOn { get; set; }
    }
}
