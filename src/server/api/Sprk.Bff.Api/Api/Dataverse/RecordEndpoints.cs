using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Api.Dataverse;

/// <summary>
/// Maps the <c>GET /api/dataverse/record/{entityLogicalName}/{id}</c> endpoint (FR-BFF-05 of the
/// Spaarke DataGrid Framework R1). Returns a single Dataverse record projected to the caller-supplied
/// <c>$select</c> field set, with NO caching (per FR-BFF-05 — record reads are real-time).
/// </summary>
/// <remarks>
/// <para>
/// Authorization: <c>DataverseAuthorizationFilter</c> (task 011) is applied with
/// <see cref="EntitySource.FromRouteValueWithRecord"/> using the <c>entityLogicalName</c> route key.
/// The filter validates the caller has Read privilege on the entity before the handler runs and
/// returns 403 ProblemDetails with <c>errorCode=DV_PRIVILEGE_DENIED</c> on deny.
/// </para>
/// <para>
/// Row-level access is enforced by Dataverse server-side via the impersonated <c>CallerId</c> path
/// on the underlying ServiceClient. A row the caller cannot read surfaces as a not-found error,
/// which <see cref="RecordService"/> translates to <see cref="RecordNotFoundException"/> and this
/// endpoint maps to 404 ProblemDetails per ADR-019. This conflation is intentional — exposing a 403
/// for unreadable rows would leak the existence of records that the caller has no business knowing about.
/// </para>
/// <para>
/// Used by:
/// <list type="bullet">
///   <item>Filter-chip "current value" lookups (the framework's chip UI fetches the display label for
///   a foreign-key chip value).</item>
///   <item>Host extension code that needs a single record's projected fields on demand without going
///   through a full FetchXML query.</item>
/// </list>
/// </para>
/// </remarks>
public static class RecordEndpoints
{
    public static IEndpointRouteBuilder MapRecordEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dataverse")
            .WithTags("Dataverse")
            .RequireAuthorization();

        group.MapGet("/record/{entityLogicalName}/{id:guid}", GetRecordAsync)
            .WithName("GetDataverseRecord")
            .WithSummary("Returns a single Dataverse record's projected fields (no cache).")
            .WithDescription("FR-BFF-05. NO caching — real-time. Supports $select=field1,field2 for field projection. Read-privilege checked by DataverseAuthorizationFilter; row-level access enforced server-side.")
            .AddDataverseAuthorizationFilter(EntitySource.FromRouteValueWithRecord);

        return app;
    }

    private static async Task<IResult> GetRecordAsync(
        string entityLogicalName,
        Guid id,
        [FromQuery(Name = "$select")] string? select,
        RecordService recordService,
        ILogger<RecordService> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Entity logical name is required in the route.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_MISSING_ENTITY",
                    ["correlationId"] = correlationId
                });
        }

        if (id == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "Record id must be a non-empty GUID.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_MISSING_ID",
                    ["correlationId"] = correlationId
                });
        }

        var selectFields = ParseSelect(select);

        try
        {
            var record = await recordService
                .GetRecordAsync(entityLogicalName, id, selectFields, ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Retrieved Dataverse record {EntityType} {RecordId} ({FieldCount} fields, selectProvided={SelectProvided})",
                entityLogicalName, id, record.Count, selectFields is not null);

            return Results.Ok(record);
        }
        catch (RecordNotFoundException ex)
        {
            logger.LogInformation(
                "Dataverse record {EntityType} {RecordId} not found",
                entityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: ex.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_NOT_FOUND",
                    ["correlationId"] = correlationId
                });
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex,
                "Invalid argument retrieving Dataverse record {EntityType} {RecordId}",
                entityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_INVALID_ARGUMENT",
                    ["correlationId"] = correlationId
                });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected; let the framework handle it.
            throw;
        }
        catch (Exception ex) when (IsInvalidSelectField(ex.Message))
        {
            // Dataverse rejects unknown $select fields with a descriptive error.
            // Per task 014 brief: surface as 400 with the underlying message.
            logger.LogWarning(ex,
                "Invalid $select field for Dataverse record {EntityType} {RecordId}",
                entityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: $"One or more fields in $select are invalid for entity '{entityLogicalName}': {ex.Message}",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_INVALID_SELECT",
                    ["correlationId"] = correlationId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Unexpected error retrieving Dataverse record {EntityType} {RecordId}",
                entityLogicalName, id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "An unexpected error occurred while retrieving the Dataverse record.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = "DV_RECORD_INTERNAL_ERROR",
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Parses the <c>$select</c> query string parameter into a trimmed string array.
    /// Tolerates whitespace and empty entries; returns <c>null</c> for null/empty/whitespace input so
    /// the service applies its primary-id + primary-name default projection.
    /// </summary>
    private static string[]? ParseSelect(string? select)
    {
        if (string.IsNullOrWhiteSpace(select))
        {
            return null;
        }

        var fields = select
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .ToArray();

        return fields.Length == 0 ? null : fields;
    }

    /// <summary>
    /// Heuristic to detect "unknown attribute / column" errors from the Dataverse SDK so we can map
    /// them to 400 Bad Request rather than 500.
    /// </summary>
    private static bool IsInvalidSelectField(string? message) =>
        !string.IsNullOrEmpty(message) &&
        (message.Contains("could not be found", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("is not a valid column", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("invalid attribute", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("invalid column", StringComparison.OrdinalIgnoreCase) ||
         (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) &&
          message.Contains("attribute", StringComparison.OrdinalIgnoreCase)));
}
