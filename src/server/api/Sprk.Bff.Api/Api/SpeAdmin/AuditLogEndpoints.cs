using Sprk.Bff.Api.Services.SpeAdmin;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;

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
        // Plain `top`/`skip`, NOT `$top`/`$skip`. The client has always sent the plain names, as does
        // every other SpeAdmin endpoint — this handler was the only `$`-prefixed binding in the app, so
        // the client's requested page size never bound and the server silently substituted its own
        // default of 50. The UI then labelled the result "1–25 of 50" and paginated within it as though
        // 50 were the whole answer. Nothing errored; the number was just quietly someone else's.
        [FromQuery] int? top,
        [FromQuery] int? skip,
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

        // Column names verified against the live Dataverse schema 2026-08-21 (task 005).
        // `sprk_targetresource` was in this list and does not exist — that alone 400'd every query.
        var select = string.Join(",",
            "sprk_speauditlogid",
            "sprk_operation",
            "sprk_category",
            "sprk_targetresourceid",
            "sprk_targetresourcename",
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
                Items = entries.Select(AuditLogEntryDto.FromDataverse).ToList(),
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
                detail: ProblemDetailsHelper.Explain("Failed to retrieve audit log entries from Dataverse.", ex),
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
    /// The <c>sprk_containertypeconfig</c> lookup is filtered via its value field,
    /// <c>_sprk_containertypeconfig_value</c>, with a BARE GUID literal.
    /// </summary>
    /// <remarks>
    /// Two defects were fixed here in task 005 (spec FR-A05), each of which 400'd every query on its own:
    /// <list type="number">
    /// <item>the lookup was named <c>_sprk_containertypeconfigid_value</c>; the schema lookup is
    /// <c>sprk_containertypeconfig</c>, so the value field has no "id" segment;</item>
    /// <item>the GUID was single-quoted. A <c>_x_value</c> field is <c>Edm.Guid</c>; a quoted literal is
    /// <c>Edm.String</c>, which Dataverse rejects with "incompatible operand types". The removed comment
    /// asserted the opposite rule, and was the only place in the codebase that did — 29 of the other 30
    /// lookup filters in <c>src/</c> already used a bare literal.</item>
    /// </list>
    /// ADR-044: <c>Guid.ToString("D")</c> is bare-lowercase, the required key-predicate form.
    /// </remarks>
    private static string BuildODataFilter(
        Guid configId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        string? category)
    {
        var clauses = new List<string>
        {
            // configId is required — filter on the lookup FK (bare Edm.Guid literal, ADR-044 bare-lowercase)
            $"_sprk_containertypeconfig_value eq {configId:D}"
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
            // sprk_category is a CHOICE (option set), not a string — filtering it with a quoted literal
            // was a third 400. Map the caller's text onto the option-set value the same way the write path
            // does, so a category filter matches what was actually stored.
            clauses.Add($"sprk_category eq {SpeAuditService.MapCategory(category)}");
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
        public List<AuditLogEntryDto> Items { get; set; } = [];

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
    /// The client-facing audit row. Field names are the contract the SPE Admin app has always been
    /// written against (<c>src/solutions/SpeAdminApp/src/types/spe.ts</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this type exists.</b> Before it, the handler serialized <see cref="AuditLogEntry"/> —
    /// the Dataverse <i>deserialization</i> target — straight to the browser, so the wire carried
    /// <c>sprk_operation</c>, <c>sprk_performedby</c>, <c>sprk_speauditlogid</c> and so on, while the
    /// client read <c>operation</c>, <c>performedBy</c>, <c>id</c>. <b>Every field name differed</b>,
    /// which means the grid could only ever have rendered blank rows with undefined React keys — a
    /// full page of confident, well-formed nothing. Projecting explicitly also stops a future Dataverse
    /// column rename from silently reshaping a public response.
    /// </para>
    /// <para>
    /// <b>On <c>responseSummary</c>:</b> the client renders it in a tooltip, but
    /// <c>sprk_responsesummary</c> is deliberately NOT in this handler's <c>$select</c> and is not added
    /// here. Task 005 established empirically that naming a column Dataverse does not have 400s the
    /// entire query (<c>sprk_targetresource</c> did exactly that), and this column has not been verified
    /// against the live schema. The client falls back to the status code, so the cost of leaving it out
    /// is a slightly less informative tooltip; the cost of guessing wrong is the whole screen.
    /// </para>
    /// </remarks>
    private sealed class AuditLogEntryDto
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = "";

        [JsonPropertyName("operation")]
        public string Operation { get; init; } = "";

        /// <summary>Human-readable category label, not the raw option-set integer.</summary>
        [JsonPropertyName("category")]
        public string Category { get; init; } = "";

        [JsonPropertyName("targetResourceId")]
        public string TargetResourceId { get; init; } = "";

        [JsonPropertyName("targetResourceName")]
        public string TargetResourceName { get; init; } = "";

        [JsonPropertyName("responseStatus")]
        public int ResponseStatus { get; init; }

        [JsonPropertyName("performedBy")]
        public string PerformedBy { get; init; } = "";

        [JsonPropertyName("performedOn")]
        public string PerformedOn { get; init; } = "";

        /// <summary>
        /// Projects a Dataverse row onto the client contract. Nulls collapse to empty strings
        /// deliberately: the client's column comparators call <c>.localeCompare</c> unguarded, so a null
        /// arriving here would throw inside the grid's sort handler rather than at this boundary.
        /// </summary>
        public static AuditLogEntryDto FromDataverse(AuditLogEntry e) => new()
        {
            Id = e.Id?.ToString() ?? "",
            Operation = e.Operation ?? "",
            Category = e.CategoryLabel,
            TargetResourceId = e.TargetResourceId ?? "",
            TargetResourceName = e.TargetResourceName ?? "",
            ResponseStatus = e.ResponseStatus ?? 0,
            PerformedBy = e.PerformedBy ?? "",
            PerformedOn = e.PerformedOn?.UtcDateTime.ToString("o") ?? "",
        };
    }

    /// <summary>
    /// Audit log entry deserialized from the sprk_speauditlog Dataverse entity set.
    /// Property names match Dataverse logical attribute names returned by the Web API.
    /// This type is an INTERNAL deserialization target — it must never be serialized to a client;
    /// project it through <see cref="AuditLogEntryDto"/> instead.
    /// </summary>
    private sealed class AuditLogEntry
    {
        [JsonPropertyName("sprk_speauditlogid")]
        public Guid? Id { get; set; }

        [JsonPropertyName("sprk_operation")]
        public string? Operation { get; set; }

        /// <summary>Option-set value; <see cref="CategoryLabel"/> is what the client renders.</summary>
        [JsonPropertyName("sprk_category")]
        public int? Category { get; set; }

        /// <summary>
        /// Human-readable category, derived from the option-set value.
        /// </summary>
        /// <remarks>
        /// The client previously received a raw string here because the code assumed `sprk_category` was
        /// text. It is a CHOICE, so the client would now get a bare integer with nothing to render. This
        /// keeps the response self-describing without a second Dataverse round-trip for option metadata.
        /// </remarks>
        [JsonPropertyName("categoryLabel")]
        public string CategoryLabel => Category switch
        {
            SpeAuditService.CategoryContainerType => "Container type",
            SpeAuditService.CategoryContainer => "Container",
            SpeAuditService.CategoryPermission => "Permission",
            SpeAuditService.CategoryFile => "File",
            SpeAuditService.CategorySearch => "Search",
            SpeAuditService.CategorySecurity => "Security",
            _ => "Unknown",
        };

        [JsonPropertyName("sprk_targetresourceid")]
        public string? TargetResourceId { get; set; }

        [JsonPropertyName("sprk_targetresourcename")]
        public string? TargetResourceName { get; set; }

        [JsonPropertyName("sprk_responsestatus")]
        public int? ResponseStatus { get; set; }

        [JsonPropertyName("sprk_performedby")]
        public string? PerformedBy { get; set; }

        [JsonPropertyName("sprk_performedon")]
        public DateTimeOffset? PerformedOn { get; set; }
    }
}
