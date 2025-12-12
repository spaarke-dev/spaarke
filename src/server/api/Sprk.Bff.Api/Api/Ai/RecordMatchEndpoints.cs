using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.RecordMatching;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Record matching endpoints for AI-powered document-to-record association.
/// These endpoints use extracted document entities to find matching Dataverse records.
/// </summary>
public static class RecordMatchEndpoints
{
    public static IEndpointRouteBuilder MapRecordMatchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/ai/document-intelligence")
            .RequireAuthorization()
            .WithTags("AI");

        // POST /api/ai/document-intelligence/match-records - Find matching Dataverse records
        group.MapPost("/match-records", MatchRecords)
            .WithName("MatchRecords")
            .WithSummary("Find matching Dataverse records based on extracted entities")
            .WithDescription("Uses Azure AI Search to find Dataverse records (Matters, Projects, Invoices) that match the provided entities.")
            .Produces<RecordMatchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(500);

        // POST /api/ai/document-intelligence/associate-record - Associate document with a record
        group.MapPost("/associate-record", AssociateRecord)
            .WithName("AssociateRecord")
            .WithSummary("Associate a document with a Dataverse record")
            .WithDescription("Updates the Document record in Dataverse to associate it with the specified Matter, Project, or Invoice.")
            .Produces<AssociateRecordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(404)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// Find matching Dataverse records based on extracted document entities.
    /// </summary>
    private static async Task<IResult> MatchRecords(
        MatchRecordsApiRequest request,
        IRecordMatchService matchService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (request.Entities == null)
        {
            return Results.BadRequest("Entities object is required");
        }

        logger.LogInformation(
            "Match records request: filter={Filter}, maxResults={Max}",
            request.RecordTypeFilter ?? "all",
            request.MaxResults ?? 5);

        try
        {
            var matchRequest = new RecordMatchRequest
            {
                Organizations = request.Entities.Organizations ?? [],
                People = request.Entities.People ?? [],
                ReferenceNumbers = request.Entities.References ?? [],
                Keywords = request.Entities.Keywords ?? [],
                RecordTypeFilter = request.RecordTypeFilter ?? "all",
                MaxResults = request.MaxResults ?? 5
            };

            var result = await matchService.MatchAsync(matchRequest, cancellationToken);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error matching records");
            return Results.Problem(
                title: "Record matching failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Associate a document with a Dataverse record by updating the lookup field.
    /// </summary>
    private static async Task<IResult> AssociateRecord(
        AssociateRecordRequest request,
        IDataverseService dataverseService,
        ILogger<Program> logger,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentId))
        {
            return Results.BadRequest("DocumentId is required");
        }
        if (string.IsNullOrWhiteSpace(request.RecordId))
        {
            return Results.BadRequest("RecordId is required");
        }
        if (string.IsNullOrWhiteSpace(request.RecordType))
        {
            return Results.BadRequest("RecordType is required");
        }

        logger.LogInformation(
            "Associate document {DocumentId} with {RecordType} {RecordId}",
            request.DocumentId,
            request.RecordType,
            request.RecordId);

        try
        {
            var recordGuid = Guid.Parse(request.RecordId);

            // Build the update request with the appropriate lookup field
            var updateRequest = new UpdateDocumentRequest();

            switch (request.RecordType.ToLowerInvariant())
            {
                case "sprk_matter":
                    updateRequest.MatterLookup = recordGuid;
                    break;
                case "sprk_project":
                    updateRequest.ProjectLookup = recordGuid;
                    break;
                case "sprk_invoice":
                    updateRequest.InvoiceLookup = recordGuid;
                    break;
                default:
                    return Results.BadRequest($"Unsupported record type: {request.RecordType}");
            }

            // Update the Document record
            await dataverseService.UpdateDocumentAsync(request.DocumentId, updateRequest, cancellationToken);

            logger.LogInformation(
                "Successfully associated document {DocumentId} with {RecordType} {RecordId}",
                request.DocumentId,
                request.RecordType,
                request.RecordId);

            return Results.Ok(new AssociateRecordResponse
            {
                Success = true,
                Message = $"Document associated with {GetRecordTypeDisplayName(request.RecordType)}"
            });
        }
        catch (FormatException)
        {
            return Results.BadRequest("Invalid DocumentId or RecordId format (must be valid GUIDs)");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error associating document with record");
            return Results.Problem(
                title: "Association failed",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static string GetRecordTypeDisplayName(string? recordType)
    {
        return recordType?.ToLowerInvariant() switch
        {
            "sprk_matter" => "Matter",
            "sprk_project" => "Project",
            "sprk_invoice" => "Invoice",
            _ => "Record"
        };
    }
}

/// <summary>
/// API request model for match-records endpoint.
/// </summary>
public class MatchRecordsApiRequest
{
    /// <summary>
    /// Extracted entities from the document.
    /// </summary>
    public ExtractedEntities? Entities { get; set; }

    /// <summary>
    /// Filter by record type. Use "sprk_matter", "sprk_project", "sprk_invoice", or "all".
    /// </summary>
    public string? RecordTypeFilter { get; set; }

    /// <summary>
    /// Maximum number of suggestions to return (default: 5).
    /// </summary>
    public int? MaxResults { get; set; }
}

/// <summary>
/// Extracted entities from document analysis.
/// </summary>
public class ExtractedEntities
{
    /// <summary>
    /// Organization names found in the document.
    /// </summary>
    public IEnumerable<string>? Organizations { get; set; }

    /// <summary>
    /// Person names found in the document.
    /// </summary>
    public IEnumerable<string>? People { get; set; }

    /// <summary>
    /// Reference numbers (invoice numbers, matter IDs, etc.) found in the document.
    /// </summary>
    public IEnumerable<string>? References { get; set; }

    /// <summary>
    /// Keywords extracted from the document.
    /// </summary>
    public IEnumerable<string>? Keywords { get; set; }
}

/// <summary>
/// Request model for associate-record endpoint.
/// </summary>
public class AssociateRecordRequest
{
    /// <summary>
    /// The Dataverse Document record ID to update.
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// The target Dataverse record ID to associate with.
    /// </summary>
    public string? RecordId { get; set; }

    /// <summary>
    /// The record type (e.g., "sprk_matter", "sprk_project", "sprk_invoice").
    /// </summary>
    public string? RecordType { get; set; }

    /// <summary>
    /// The lookup field name on the Document entity to populate.
    /// </summary>
    public string? LookupFieldName { get; set; }
}

/// <summary>
/// Response model for associate-record endpoint.
/// </summary>
public class AssociateRecordResponse
{
    /// <summary>
    /// Whether the association was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Human-readable message about the result.
    /// </summary>
    public string? Message { get; set; }
}
