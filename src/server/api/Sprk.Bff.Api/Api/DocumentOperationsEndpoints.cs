using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services;

namespace Sprk.Bff.Api.Api;

/// <summary>
/// Document checkout/checkin/discard operations endpoints.
/// Implements check-out/check-in version control for document editing.
/// </summary>
public static class DocumentOperationsEndpoints
{
    public static IEndpointRouteBuilder MapDocumentOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/documents/{documentId:guid}")
            .RequireAuthorization()
            .WithTags("Document Operations");

        // POST /api/documents/{documentId}/checkout
        group.MapPost("/checkout", CheckoutDocument)
            .AddDocumentAuthorizationFilter("write")
            .WithName("CheckoutDocument")
            .WithDescription("Locks a document for editing and returns the edit URL")
            .Produces<CheckoutResponse>(200)
            .Produces<DocumentLockedError>(409)
            .ProducesProblem(404)
            .ProducesProblem(401);

        // POST /api/documents/{documentId}/checkin
        group.MapPost("/checkin", CheckInDocument)
            .AddDocumentAuthorizationFilter("write")
            .WithName("CheckInDocument")
            .WithDescription("Releases the document lock and creates a new version")
            .Produces<CheckInResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(404)
            .ProducesProblem(401);

        // POST /api/documents/{documentId}/discard
        group.MapPost("/discard", DiscardCheckout)
            .AddDocumentAuthorizationFilter("write")
            .WithName("DiscardCheckout")
            .WithDescription("Cancels the checkout without saving changes")
            .Produces<DiscardResponse>(200)
            .ProducesProblem(400)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(401);

        // DELETE /api/documents/{documentId}
        group.MapDelete("", DeleteDocument)
            .AddDocumentAuthorizationFilter("delete")
            .WithName("DeleteDocument")
            .WithDescription("Deletes a document from both Dataverse and SharePoint Embedded")
            .Produces<DeleteDocumentResponse>(200)
            .Produces<DocumentLockedError>(409)
            .ProducesProblem(404)
            .ProducesProblem(401);

        return app;
    }

    /// <summary>
    /// POST /api/documents/{documentId}/checkout
    /// Locks a document for editing by the current user.
    /// </summary>
    private static async Task<IResult> CheckoutDocument(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Checkout endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.CheckoutAsync(documentId, user, correlationId, ct);

            return result switch
            {
                SuccessCheckoutResult success => TypedResults.Ok(success.Response),
                NotFoundCheckoutResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                ConflictCheckoutResult conflict => TypedResults.Json(
                    conflict.ConflictError,
                    statusCode: 409
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during checkout"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Checkout failed for document {DocumentId}", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Checkout Failed",
                detail: "An error occurred while checking out the document",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
            );
        }
    }

    /// <summary>
    /// POST /api/documents/{documentId}/checkin
    /// Releases the lock and creates a new version.
    /// </summary>
    private static async Task<IResult> CheckInDocument(
        Guid documentId,
        [FromBody] CheckInRequest? request,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Check-in endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.CheckInAsync(
                documentId,
                request?.Comment,
                user,
                correlationId,
                ct
            );

            return result switch
            {
                SuccessCheckInResult success => TypedResults.Ok(success.Response),
                NotFoundCheckInResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotCheckedOutCheckInResult => TypedResults.Problem(
                    statusCode: 400,
                    title: "Document Not Checked Out",
                    detail: "Cannot check in a document that is not checked out",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during check-in"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Check-in failed for document {DocumentId}", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Check-in Failed",
                detail: "An error occurred while checking in the document",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
            );
        }
    }

    /// <summary>
    /// POST /api/documents/{documentId}/discard
    /// Cancels the checkout without saving changes.
    /// </summary>
    private static async Task<IResult> DiscardCheckout(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;
        var user = httpContext.User;

        logger.LogInformation(
            "Discard endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.DiscardAsync(documentId, user, correlationId, ct);

            return result switch
            {
                SuccessDiscardResult success => TypedResults.Ok(success.Response),
                NotFoundDiscardResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotCheckedOutDiscardResult => TypedResults.Problem(
                    statusCode: 400,
                    title: "Document Not Checked Out",
                    detail: "Cannot discard checkout for a document that is not checked out",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                NotAuthorizedDiscardResult => TypedResults.Problem(
                    statusCode: 403,
                    title: "Not Authorized",
                    detail: "You can only discard checkouts that you initiated",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during discard"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Discard failed for document {DocumentId}", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Discard Failed",
                detail: "An error occurred while discarding the checkout",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
            );
        }
    }

    /// <summary>
    /// DELETE /api/documents/{documentId}
    /// Deletes a document from both Dataverse and SharePoint Embedded.
    /// </summary>
    private static async Task<IResult> DeleteDocument(
        Guid documentId,
        HttpContext httpContext,
        [FromServices] DocumentCheckoutService checkoutService,
        [FromServices] ILogger<Program> logger,
        CancellationToken ct)
    {
        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Delete endpoint called for document {DocumentId} [{CorrelationId}]",
            documentId, correlationId);

        try
        {
            var result = await checkoutService.DeleteAsync(documentId, correlationId, ct);

            return result switch
            {
                SuccessDeleteResult success => TypedResults.Ok(success.Response),
                NotFoundDeleteResult => TypedResults.Problem(
                    statusCode: 404,
                    title: "Document Not Found",
                    detail: $"Document {documentId} was not found",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                CheckedOutDeleteResult checkedOut => TypedResults.Json(
                    checkedOut.CheckedOutError,
                    statusCode: 409
                ),
                FailedDeleteResult failed => TypedResults.Problem(
                    statusCode: 500,
                    title: "Delete Failed",
                    detail: "An error occurred while deleting the document",
                    extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
                ),
                _ => TypedResults.Problem(
                    statusCode: 500,
                    title: "Unexpected Error",
                    detail: "An unexpected error occurred during delete"
                )
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Delete failed for document {DocumentId}", documentId);
            return TypedResults.Problem(
                statusCode: 500,
                title: "Delete Failed",
                detail: "An error occurred while deleting the document",
                extensions: new Dictionary<string, object?> { ["correlationId"] = correlationId }
            );
        }
    }
}
