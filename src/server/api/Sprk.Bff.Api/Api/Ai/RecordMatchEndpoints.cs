using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
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
        //
        // unified-access-control-r2 task 146 r1 (verifier item 1): associating FILES the document under the record — a
        // reparent that re-derives its OWNER, moving it into or out of a secure record's named team. This route was
        // authentication-only, so any signed-in user who knew a secure document's GUID could re-file it under an
        // ordinary matter and hand it to that matter's business unit. Both ids come from the BODY, so the decision is
        // declared by ResolveAssociateTargets and enforced by the body-declared per-record filter before the handler:
        // Write on the document (the gate PUT /api/v1/documents/{id} carries) and AppendTo on the record it is filed to.
        group.MapPost("/associate-record", AssociateRecord)
            .AddFinanceAuthorizationFilter(ResolveAssociateTargets)
            .WithName("AssociateRecord")
            .WithSummary("Associate a document with a Dataverse record")
            .WithDescription("Updates the Document record in Dataverse to associate it with the specified Matter, Project, or Invoice.")
            .Produces<AssociateRecordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(500);

        return app;
    }

    /// <summary>
    /// The per-record authorization of <c>associate-record</c> (task 146 r1, verifier item 1), evaluated AS THE CALLER on
    /// the very ids the handler binds (the bound body argument): Write on the document, through the document path PUT
    /// /api/v1/documents/{id} uses, and AppendTo (<c>entity.associate_document</c>) on the record it is filed to, whose
    /// entity set comes from the ONE type → set table (<see cref="Filters.EntityAccessFilter"/>). An unreadable id or an
    /// unsupported type is a 400 before any rights query.
    /// </summary>
    /// <remarks>Reuses the body-declared multi-check filter the finance routes established (CLAUDE.md §11: the one filter
    /// that authorizes several BODY ids per route) rather than adding another.</remarks>
    internal static Filters.FinanceAuthorizationTargets ResolveAssociateTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<AssociateRecordRequest>().FirstOrDefault();
        if (request is null)
            return Filters.FinanceAuthorizationTargets.Reject(AssociateValidationProblem("body", "A JSON request body is required."));

        if (!Guid.TryParse(request.DocumentId, out var documentId) || documentId == Guid.Empty)
            return Filters.FinanceAuthorizationTargets.Reject(AssociateValidationProblem("documentId", "DocumentId must be a GUID."));

        if (!Guid.TryParse(request.RecordId, out var recordId) || recordId == Guid.Empty)
            return Filters.FinanceAuthorizationTargets.Reject(AssociateValidationProblem("recordId", "RecordId must be a GUID."));

        if (!Filters.EntityAccessFilter.TryResolveEntitySet(request.RecordType, out var targetSet))
        {
            return Filters.FinanceAuthorizationTargets.Reject(
                AssociateValidationProblem("recordType", $"Unsupported record type: {request.RecordType}"));
        }

        return Filters.FinanceAuthorizationTargets.Authorize(
            new Filters.FinanceAuthorizationCheck
            {
                Path = Filters.FinanceCheckPath.Document,
                EntitySetName = Filters.FinanceAuthorizationFilter.DocumentEntitySet,
                RecordId = documentId,
                Operation = "write",
                Source = "body.documentId",
            },
            new Filters.FinanceAuthorizationCheck
            {
                Path = Filters.FinanceCheckPath.Record,
                EntitySetName = targetSet,
                RecordId = recordId,
                Operation = AssociateTargetOperation,
                Source = "body.recordId",
            });
    }

    /// <summary>The right associating costs on the TARGET record: AppendTo — the document is attached to it.</summary>
    internal const string AssociateTargetOperation = "entity.associate_document";

    private static IResult AssociateValidationProblem(string field, string message) =>
        Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.ValidationProblem(
            new Dictionary<string, string[]> { [field] = [message] });

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
    /// <remarks>Internal (not private) so the test assembly can drive the handler directly (task 156: the re-file cascade).</remarks>
    internal static async Task<IResult> AssociateRecord(
        AssociateRecordRequest request,
        IDocumentDataverseService dataverseService,
        [Microsoft.AspNetCore.Mvc.FromServices] Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper restamper,
        Sprk.Bff.Api.Services.Dataverse.IRecordOwnershipResolver ownershipResolver,
        [Microsoft.AspNetCore.Mvc.FromServices] Sprk.Bff.Api.Infrastructure.ExternalAccess.CallerRecordAccessProbe callerAccessProbe,
        HttpContext httpContext,
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
            var documentGuid = Guid.Parse(request.DocumentId); // before the write: a bad id never fails after it

            // Build the update request with the appropriate lookup field
            var updateRequest = new UpdateDocumentRequest();

            // Shared map — see Spaarke.Dataverse.DocumentAssociationMap. This site keeps its
            // fail-closed miss behaviour: it is a request handler, so an unsupported type is a
            // caller error to report, not a warning to log and continue past.
            if (!DocumentAssociationMap.TryApply(updateRequest, request.RecordType, recordGuid))
            {
                return Results.BadRequest($"Unsupported record type: {request.RecordType}");
            }

            // Update the Document record. Task 146: associating FILES the document under the record — a reparent. Its
            // owner is re-derived (secure-if-any over every parent it will have) BEFORE the lookup is written, and
            // reassigned when it moves; a refusal writes nothing (409 + reason code).
            var reparent = await ownershipResolver.ReparentAsync(
                new Sprk.Bff.Api.Services.Dataverse.RecordReparent
                {
                    EntityLogicalName = "sprk_document",
                    RecordId = Guid.Parse(request.DocumentId),
                    ParentChanges = Sprk.Bff.Api.Services.Dataverse.RecordReparent.ParentChangesOf(updateRequest),
                    // Owner round 10 item 7 (task 146 c1): associating replaces a lookup, so it can move the document
                    // OUT of a secure root — an un-secure, decided by THIS caller's F3 rights before anything is written.
                    SecureExitCaller = Sprk.Bff.Api.Services.Access.SecureRemovalCaller.ForRequest(callerAccessProbe, httpContext),
                },
                token => dataverseService.UpdateDocumentAsync(request.DocumentId, updateRequest, token),
                cancellationToken);
            if (reparent.IsRefused)
            {
                return Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsHelper.RecordOwnerRefused(
                    reparent, "association", httpContext.TraceIdentifier);
            }

            // Task 156 (owner round 4 item 5, option b): associating a document with a different matter / project / work
            // assignment re-files it, so every to-do and analysis filed under it is re-stamped in this same request.
            // Never thrown: a child that fails is logged and the reconciliation job repairs it.
            await restamper.AfterWriteAsync(
                "sprk_document", documentGuid,
                Sprk.Bff.Api.Services.Dataverse.CoreAncestorRestamper.DocumentColumnsWritten(updateRequest),
                CancellationToken.None);

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
