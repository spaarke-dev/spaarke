using Sprk.Bff.Api.Services.RecordMatching;

namespace Sprk.Bff.Api.Api.Admin;

/// <summary>
/// Admin endpoints for Record Matching service management.
/// These endpoints allow administrators to sync Dataverse records to the Azure AI Search index.
/// </summary>
public static class RecordMatchingAdminEndpoints
{
    public static IEndpointRouteBuilder MapRecordMatchingAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin/record-matching")
            .RequireAuthorization()
            .WithTags("Admin");

        // POST /api/admin/record-matching/sync - Trigger a bulk sync of all records
        group.MapPost("/sync", TriggerBulkSync)
            .WithName("TriggerBulkSync")
            .WithSummary("Trigger bulk sync of Dataverse records to search index")
            .WithDescription("Syncs all supported Dataverse record types (Matters, Projects, Invoices) to Azure AI Search index.")
            .Produces<IndexSyncResult>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/admin/record-matching/sync-incremental - Trigger an incremental sync
        group.MapPost("/sync-incremental", TriggerIncrementalSync)
            .WithName("TriggerIncrementalSync")
            .WithSummary("Trigger incremental sync of recently modified records")
            .WithDescription("Syncs Dataverse records modified since the specified time to Azure AI Search index.")
            .Produces<IndexSyncResult>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // GET /api/admin/record-matching/status - Get index sync status
        group.MapGet("/status", GetSyncStatus)
            .WithName("GetSyncStatus")
            .WithSummary("Get search index status and statistics")
            .WithDescription("Returns the current state of the Azure AI Search index including document counts by record type.")
            .Produces<IndexSyncStatus>(StatusCodes.Status200OK)
            .ProducesProblem(401)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Trigger a bulk sync of all Dataverse records to the search index.
    /// </summary>
    private static async Task<IResult> TriggerBulkSync(
        IDataverseIndexSyncService syncService,
        BulkSyncRequest? request,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Admin triggered bulk sync for record types: {Types}",
            request?.RecordTypes != null ? string.Join(", ", request.RecordTypes) : "all");

        try
        {
            var result = await syncService.BulkSyncAsync(request?.RecordTypes, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Bulk sync failed");
            return Results.Problem(
                title: "Bulk sync failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Trigger an incremental sync of records modified since a given time.
    /// </summary>
    private static async Task<IResult> TriggerIncrementalSync(
        IDataverseIndexSyncService syncService,
        IncrementalSyncRequest request,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (request.Since == default)
        {
            return Results.BadRequest("'since' parameter is required");
        }

        logger.LogInformation("Admin triggered incremental sync since {Since}", request.Since);

        try
        {
            var result = await syncService.IncrementalSyncAsync(request.Since, request.RecordTypes, cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Incremental sync failed");
            return Results.Problem(
                title: "Incremental sync failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Get the current status of the search index.
    /// </summary>
    private static async Task<IResult> GetSyncStatus(
        IDataverseIndexSyncService syncService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await syncService.GetStatusAsync(cancellationToken);
            return Results.Ok(status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get sync status");
            return Results.Problem(
                title: "Failed to get sync status",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

/// <summary>
/// Request model for bulk sync operation.
/// </summary>
public class BulkSyncRequest
{
    /// <summary>
    /// Optional list of record types to sync. If null, syncs all supported types.
    /// Valid values: "sprk_matter", "sprk_project", "sprk_invoice"
    /// </summary>
    public IEnumerable<string>? RecordTypes { get; set; }
}

/// <summary>
/// Request model for incremental sync operation.
/// </summary>
public class IncrementalSyncRequest
{
    /// <summary>
    /// Only sync records modified after this timestamp (required).
    /// </summary>
    public DateTimeOffset Since { get; set; }

    /// <summary>
    /// Optional list of record types to sync. If null, syncs all supported types.
    /// </summary>
    public IEnumerable<string>? RecordTypes { get; set; }
}
