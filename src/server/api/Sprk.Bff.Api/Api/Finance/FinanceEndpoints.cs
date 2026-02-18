using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Finance endpoint group following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// Applies FinanceAuthorizationFilter to all finance endpoints for resource-level authorization.
/// </summary>
public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .AddFinanceAuthorizationFilter("finance.read")
            .WithTags("Finance");

        // POST /api/finance/invoice-review/confirm — Confirm document as invoice and enqueue extraction
        group.MapPost("/invoice-review/confirm", ConfirmInvoiceReview)
            .AddFinanceAuthorizationFilter("finance.confirm")
            .WithName("ConfirmInvoiceReview")
            .WithSummary("Confirm a document as an invoice and enqueue extraction")
            .WithDescription(
                "Confirms a classified document as an invoice, creates an sprk_invoice record, " +
                "and enqueues an InvoiceExtraction background job. Returns 202 Accepted with job tracking info.")
            .Produces<InvoiceReviewResult>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // POST /api/finance/invoice-review/reject — Reject document as not an invoice
        group.MapPost("/invoice-review/reject", RejectInvoiceReview)
            .AddFinanceAuthorizationFilter("finance.confirm")
            .WithName("RejectInvoiceReview")
            .WithSummary("Reject a document as not an invoice")
            .WithDescription(
                "Marks a classified document as not an invoice. " +
                "Updates the document status to RejectedNotInvoice. No invoice record is created.")
            .Produces<InvoiceReviewRejectResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/finance/invoices/search — Semantic invoice search
        group.MapGet("/invoices/search", SearchInvoices)
            .AddFinanceAuthorizationFilter("finance.read")
            .WithName("SearchInvoices")
            .WithSummary("Search invoices using semantic search")
            .WithDescription(
                "Performs semantic search across invoices using vector embeddings and semantic reranking. " +
                "Supports optional filtering by matter ID. Returns top N results with relevance scores and highlights.")
            .Produces<InvoiceSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/finance/matters/{matterId}/summary — Financial summary for a matter
        group.MapGet("/matters/{matterId:guid}/summary", GetFinanceSummary)
            .AddFinanceAuthorizationFilter("finance.read")
            .WithName("GetFinanceSummary")
            .WithSummary("Get financial summary for a matter")
            .WithDescription(
                "Returns aggregated financial data for a matter: current spend, budget variance, " +
                "active signals, and recent invoices. Results are cached for performance.")
            .Produces<FinanceSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return app;
    }

    /// <summary>
    /// Confirm a document as an invoice, create invoice record, and enqueue extraction.
    /// POST /api/finance/invoice-review/confirm
    /// </summary>
    private static async Task<IResult> ConfirmInvoiceReview(
        InvoiceReviewConfirmRequest request,
        IInvoiceReviewService invoiceReviewService,
        HttpContext httpContext,
        ILogger<InvoiceReviewService> logger,
        CancellationToken cancellationToken)
    {
        // Validate required fields
        var validationErrors = ValidateConfirmRequest(request);
        if (validationErrors.Count > 0)
        {
            return ProblemDetailsHelper.ValidationProblem(validationErrors);
        }

        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Invoice review confirm request received. DocumentId={DocumentId}, MatterId={MatterId}, " +
            "CorrelationId={CorrelationId}",
            request.DocumentId, request.MatterId, correlationId);

        try
        {
            var result = await invoiceReviewService.ConfirmInvoiceAsync(request, correlationId, cancellationToken);

            logger.LogInformation(
                "Invoice review confirmed. InvoiceId={InvoiceId}, JobId={JobId}, CorrelationId={CorrelationId}",
                result.InvoiceId, result.JobId, correlationId);

            return Results.Accepted(result.StatusUrl, result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Invoice review confirmation failed. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                request.DocumentId, correlationId);

            return Results.Problem(
                title: "Invoice Review Error",
                detail: "An error occurred while confirming the invoice review",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Reject a document as not an invoice.
    /// POST /api/finance/invoice-review/reject
    /// </summary>
    private static async Task<IResult> RejectInvoiceReview(
        InvoiceReviewRejectRequest request,
        IInvoiceReviewService invoiceReviewService,
        HttpContext httpContext,
        ILogger<InvoiceReviewService> logger,
        CancellationToken cancellationToken)
    {
        // Validate required fields
        var validationErrors = ValidateRejectRequest(request);
        if (validationErrors.Count > 0)
        {
            return ProblemDetailsHelper.ValidationProblem(validationErrors);
        }

        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Invoice review reject request received. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
            request.DocumentId, correlationId);

        try
        {
            var result = await invoiceReviewService.RejectInvoiceAsync(request, correlationId, cancellationToken);

            logger.LogInformation(
                "Invoice review rejected. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                result.DocumentId, correlationId);

            return Results.Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Invoice review rejection failed. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                request.DocumentId, correlationId);

            return Results.Problem(
                title: "Invoice Review Rejection Error",
                detail: "An error occurred while rejecting the invoice review",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Validate the confirm request, returning field-level errors per ADR-019.
    /// </summary>
    private static Dictionary<string, string[]> ValidateConfirmRequest(InvoiceReviewConfirmRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.DocumentId == Guid.Empty)
        {
            errors["documentId"] = ["DocumentId is required and must be a valid non-empty GUID."];
        }

        if (request.MatterId == Guid.Empty)
        {
            errors["matterId"] = ["MatterId is required and must be a valid non-empty GUID."];
        }

        if (request.VendorOrgId == Guid.Empty)
        {
            errors["vendorOrgId"] = ["VendorOrgId is required and must be a valid non-empty GUID."];
        }

        return errors;
    }

    /// <summary>
    /// Validate the reject request, returning field-level errors per ADR-019.
    /// </summary>
    private static Dictionary<string, string[]> ValidateRejectRequest(InvoiceReviewRejectRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.DocumentId == Guid.Empty)
        {
            errors["documentId"] = ["DocumentId is required and must be a valid non-empty GUID."];
        }

        return errors;
    }

    /// <summary>
    /// Search invoices using semantic search.
    /// GET /api/finance/invoices/search?query={text}&amp;matterId={guid}&amp;top={int}
    /// </summary>
    private static async Task<IResult> SearchInvoices(
        string query,
        Guid? matterId,
        int? top,
        IInvoiceSearchService searchService,
        HttpContext httpContext,
        ILogger<InvoiceSearchService> logger,
        CancellationToken cancellationToken)
    {
        // Validate query parameter
        if (string.IsNullOrWhiteSpace(query))
        {
            return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
            {
                ["query"] = ["Query parameter is required and cannot be empty."]
            });
        }

        // Validate top parameter
        var topValue = top ?? 10;
        if (topValue < 1 || topValue > 50)
        {
            return ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
            {
                ["top"] = ["Top parameter must be between 1 and 50."]
            });
        }

        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Invoice search request received. Query length={QueryLength}, MatterId={MatterId}, " +
            "Top={Top}, CorrelationId={CorrelationId}",
            query.Length, matterId, topValue, correlationId);

        try
        {
            var result = await searchService.SearchAsync(query, matterId, topValue, cancellationToken);

            logger.LogInformation(
                "Invoice search completed. ResultCount={ResultCount}, TotalCount={TotalCount}, " +
                "Duration={Duration}ms, CorrelationId={CorrelationId}",
                result.Results.Count, result.TotalCount, result.DurationMs, correlationId);

            return Results.Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError(ex,
                "Invoice search failed: {Error}, CorrelationId={CorrelationId}",
                ex.Message, correlationId);

            return Results.Problem(
                title: "Search Error",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Invoice search failed unexpectedly: {Error}, CorrelationId={CorrelationId}",
                ex.Message, correlationId);

            return Results.Problem(
                title: "Search Error",
                detail: "An error occurred while searching invoices",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId
                });
        }
    }

    /// <summary>
    /// Get financial summary for a matter.
    /// GET /api/finance/matters/{matterId}/summary
    /// </summary>
    private static async Task<IResult> GetFinanceSummary(
        Guid matterId,
        IFinanceSummaryService financeSummaryService,
        HttpContext httpContext,
        ILogger<FinanceSummaryService> logger,
        CancellationToken cancellationToken)
    {
        var correlationId = httpContext.TraceIdentifier;

        logger.LogInformation(
            "Finance summary request received. MatterId={MatterId}, CorrelationId={CorrelationId}",
            matterId, correlationId);

        try
        {
            var summary = await financeSummaryService.GetSummaryAsync(matterId, cancellationToken);

            if (summary == null)
            {
                logger.LogInformation(
                    "No financial data found for matter {MatterId}, CorrelationId={CorrelationId}",
                    matterId, correlationId);

                return Results.Problem(
                    title: "Financial Data Not Found",
                    detail: $"No financial data exists for matter {matterId}",
                    statusCode: StatusCodes.Status404NotFound,
                    extensions: new Dictionary<string, object?>
                    {
                        ["correlationId"] = correlationId,
                        ["matterId"] = matterId
                    });
            }

            logger.LogInformation(
                "Finance summary retrieved. MatterId={MatterId}, CurrentSpend={CurrentSpend:C}, " +
                "ActiveSignals={SignalCount}, RecentInvoices={InvoiceCount}, CorrelationId={CorrelationId}",
                matterId, summary.CurrentSpend, summary.ActiveSignals.Count,
                summary.RecentInvoices.Count, correlationId);

            return Results.Ok(summary);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Finance summary retrieval failed. MatterId={MatterId}, CorrelationId={CorrelationId}",
                matterId, correlationId);

            return Results.Problem(
                title: "Finance Summary Error",
                detail: "An error occurred while retrieving the financial summary",
                statusCode: StatusCodes.Status500InternalServerError,
                extensions: new Dictionary<string, object?>
                {
                    ["correlationId"] = correlationId,
                    ["matterId"] = matterId
                });
        }
    }
}
