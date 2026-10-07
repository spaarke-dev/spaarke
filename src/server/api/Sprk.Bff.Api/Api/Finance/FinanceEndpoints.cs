using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Finance endpoint group following ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Every route carries its OWN authorization filter</b> (unified-access-control-r2 task 130,
/// defect C8). The group used to carry a "finance.read" filter that ran on every route and authorized the
/// first id it could find in a route → query fallback chain, always against <c>sprk_documents</c>: the
/// summary route therefore denied every caller (a matter id is never a document id), and confirm, reject and
/// search could be made to authorize a query-string id while acting on a different one. Each route now
/// authorizes exactly the id(s) its handler consumes, from the same source the handler binds:</para>
/// <list type="bullet">
///   <item>summary → Read on <c>sprk_matters(route matterId)</c>;</item>
///   <item>search → <c>matterId</c> query REQUIRED (400 when absent), Read on <c>sprk_matters(matterId)</c>;</item>
///   <item>reject → Write on the BODY DocumentId via the document path;</item>
///   <item>confirm → Write on the BODY DocumentId; Write+Append on that document (it HOLDS the new invoice's
///   lookup, <c>sprk_document.sprk_invoice</c>); AppendTo on the matter and vendor organization the invoice's own
///   lookups point at; and the caller's Create privilege on <c>sprk_invoice</c> (owner decision G5, 2026-10-01:
///   "check as the user, create as the app, owned by the team").</item>
/// </list>
/// </remarks>
public static class FinanceEndpoints
{
    public static IEndpointRouteBuilder MapFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/finance")
            .RequireAuthorization()
            .WithTags("Finance");

        // POST /api/finance/invoice-review/confirm — Confirm document as invoice and enqueue extraction
        group.MapPost("/invoice-review/confirm", ConfirmInvoiceReview)
            .AddFinanceAuthorizationFilter(ResolveConfirmTargets)
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
            .AddFinanceAuthorizationFilter(ResolveRejectTargets)
            .WithName("RejectInvoiceReview")
            .WithSummary("Reject a document as not an invoice")
            .WithDescription(
                "Marks a classified document as not an invoice. " +
                "Updates the document status to RejectedNotInvoice. No invoice record is created.")
            .Produces<InvoiceReviewRejectResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/finance/invoices/search — Semantic invoice search
        group.MapGet("/invoices/search", SearchInvoices)
            .AddFinanceAuthorizationFilter(ResolveSearchTargets)
            .WithName("SearchInvoices")
            .WithSummary("Search invoices using semantic search")
            .WithDescription(
                "Performs semantic search across one matter's invoices using vector embeddings and semantic " +
                "reranking. matterId is REQUIRED and the caller must be able to read that matter. Returns top " +
                "N results with relevance scores and highlights.")
            .Produces<InvoiceSearchResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // GET /api/finance/matters/{matterId}/summary — Financial summary for a matter
        group.MapGet("/matters/{matterId:guid}/summary", GetFinanceSummary)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.MatterEntitySet, routeKey: "matterId")
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

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declarations — one per body/query route (task 130, defect C8)
    // ═══════════════════════════════════════════════════════════════════════════
    //
    // Each resolver reads the SAME source its handler binds and returns every (entity set, id, operation) the
    // handler acts on. The body ids are read from the BOUND argument — the very instance the handler receives —
    // so the filter cannot authorize one id while the handler acts on another. Validation runs first, so an
    // empty id is a field-level 400 with no rights query made.

    /// <summary>
    /// Confirm — owner decision G5 (2026-10-01): every right is checked AS THE CALLER, then the APP creates the
    /// invoice, owned by the matter's team. The checks, each on the id the handler consumes:
    /// <list type="bullet">
    ///   <item>Write on the body DocumentId through the document path (its review status is updated);</item>
    ///   <item>Write+Append on that document (<c>finance.link_invoice</c>) — the live schema puts the link on the
    ///   DOCUMENT (<c>sprk_document.sprk_invoice</c>), and the record that holds a lookup needs Append;</item>
    ///   <item>AppendTo on the matter and on the vendor organization (<c>finance.attach_invoice</c>) — the new
    ///   invoice's own <c>sprk_matter</c> / <c>sprk_vendororg</c> lookups point at them;</item>
    ///   <item>the caller's Create privilege on <c>sprk_invoice</c> — the invoice is created app-only, so without
    ///   this a user who could never create an invoice would get one by confirming.</item>
    /// </list>
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveConfirmTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<InvoiceReviewConfirmRequest>().FirstOrDefault();
        if (request is null)
        {
            return FinanceAuthorizationTargets.Reject(RequestBodyRequired());
        }

        var errors = ValidateConfirmRequest(request);
        if (errors.Count > 0)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(errors));
        }

        return FinanceAuthorizationTargets.Authorize(
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Document,
                EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                RecordId = request.DocumentId,
                Operation = "finance.confirm",
                Source = "body.documentId",
            },
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
                RecordId = request.DocumentId,
                Operation = "finance.link_invoice",
                Source = "body.documentId",
            },
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.MatterEntitySet,
                RecordId = request.MatterId,
                Operation = "finance.attach_invoice",
                Source = "body.matterId",
            },
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = request.VendorOrgId,
                Operation = "finance.attach_invoice",
                Source = "body.vendorOrgId",
            },
            FinanceAuthorizationCheck.CallerPrivilege(
                FinanceAuthorizationFilter.CreateInvoicePrivilege,
                FinanceAuthorizationFilter.InvoiceEntitySet,
                source: "privilege.createInvoice"));
    }

    /// <summary>Reject: Write on the body DocumentId (its review status is updated) via the document path.</summary>
    internal static FinanceAuthorizationTargets ResolveRejectTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<InvoiceReviewRejectRequest>().FirstOrDefault();
        if (request is null)
        {
            return FinanceAuthorizationTargets.Reject(RequestBodyRequired());
        }

        var errors = ValidateRejectRequest(request);
        if (errors.Count > 0)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(errors));
        }

        return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Document,
            EntitySetName = FinanceAuthorizationFilter.DocumentEntitySet,
            RecordId = request.DocumentId,
            Operation = "finance.confirm",
            Source = "body.documentId",
        });
    }

    /// <summary>
    /// Search: <c>matterId</c> is REQUIRED — an unscoped search would be tenant-wide
    /// (<c>InvoiceSearchService</c> builds a tenant-only filter when it is null). Exactly one value is
    /// accepted, so the filter and the handler's binding cannot read different ones. <c>?documentId=</c> /
    /// <c>?invoiceId=</c> authorize nothing.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveSearchTargets(EndpointFilterInvocationContext context)
    {
        var raw = context.HttpContext.Request.Query["matterId"];
        if (raw.Count != 1 || !Guid.TryParse(raw[0], out var matterId) || matterId == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Reject(MatterIdRequired());
        }

        return FinanceAuthorizationTargets.Authorize(new FinanceAuthorizationCheck
        {
            Path = FinanceCheckPath.Record,
            EntitySetName = FinanceAuthorizationFilter.MatterEntitySet,
            RecordId = matterId,
            Operation = "finance.read",
            Source = "query.matterId",
        });
    }

    private static IResult RequestBodyRequired() =>
        ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
        {
            ["body"] = ["A JSON request body is required."]
        });

    private static IResult MatterIdRequired() =>
        ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
        {
            ["matterId"] = ["matterId is required and must be a single non-empty GUID; invoice search is scoped to one matter."]
        });

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
            // Task 146 c1-r1 (owner round 13 item 9): the signed-in reviewer asked for the invoice the app creates.
            var result = await invoiceReviewService.ConfirmInvoiceAsync(
                request with { RequestedBy = Sprk.Bff.Api.Services.Dataverse.RecordRequester.OfCaller(httpContext.User) },
                correlationId,
                cancellationToken);

            logger.LogInformation(
                "Invoice review confirmed. InvoiceId={InvoiceId}, JobId={JobId}, CorrelationId={CorrelationId}",
                result.InvoiceId, result.JobId, correlationId);

            return Results.Accepted(result.StatusUrl, result);
        }
        catch (InvoiceReviewException ex)
        {
            logger.LogError(ex,
                "Invoice review confirmation refused or incomplete ({Failure}). DocumentId={DocumentId}, " +
                "InvoiceId={InvoiceId}, CorrelationId={CorrelationId}",
                ex.Failure, request.DocumentId, ex.InvoiceId, correlationId);

            return ConfirmFailure(ex, correlationId);
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
        catch (KeyNotFoundException)
        {
            // The document was deleted after the authorization check; the update-only write created nothing.
            return Results.Problem(
                title: "Document Not Found",
                detail: "The document no longer exists.",
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    ["reasonCode"] = InvoiceReviewException.DocumentNotFoundReasonCode,
                    ["correlationId"] = correlationId,
                });
        }
        catch (InvoiceReviewException ex)
        {
            // 409: the document is linked to an invoice (confirmed), or kept changing. Nothing was written.
            logger.LogWarning(ex,
                "Invoice review rejection refused ({Failure}). DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                ex.Failure, request.DocumentId, correlationId);

            return ConfirmFailure(ex, correlationId);
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
    /// Renders an <see cref="InvoiceReviewException"/> from confirm — and reject's refusals (linked document, kept
    /// changing), which carry no invoice id. Every message states what was and was not saved, and any
    /// failure AFTER the invoice row exists names that row's id — the caller (and support) must be able to find it.
    /// A retry is safe in every case: confirm resumes from the invoice the document is already linked to.
    /// </summary>
    internal static IResult ConfirmFailure(InvoiceReviewException ex, string correlationId)
    {
        var (status, title) = ex.Failure switch
        {
            InvoiceReviewFailure.DocumentNotFound => (StatusCodes.Status404NotFound, "Document Not Found"),
            InvoiceReviewFailure.DocumentLinkedToAnotherInvoice => (StatusCodes.Status409Conflict, "Document Already Linked"),
            InvoiceReviewFailure.DocumentChangedConcurrently => (StatusCodes.Status409Conflict, "Document Changed"),
            InvoiceReviewFailure.ReviewDecisionChanged => (StatusCodes.Status409Conflict, "Review Decision Changed"),
            InvoiceReviewFailure.DocumentLinkedToInvoice => (StatusCodes.Status409Conflict, "Document Already Confirmed"),
            InvoiceReviewFailure.OwnerTeamUnresolved => (StatusCodes.Status403Forbidden, "Invoice Not Created"),
            _ => (StatusCodes.Status500InternalServerError, "Invoice Review Incomplete"),
        };

        var extensions = new Dictionary<string, object?>
        {
            ["reasonCode"] = ex.ReasonCode,
            ["correlationId"] = correlationId,
        };

        // The invoice another matter's document link points at was never authorized for this caller: its id is
        // logged server-side only, never rendered — not in the detail, not as an extension (task 130 item 5).
        if (ex.Failure == InvoiceReviewFailure.DocumentLinkedToAnotherInvoice)
        {
            return Results.Problem(
                title: title,
                detail: "The document is already linked to an invoice for a different matter or vendor. Nothing was saved.",
                statusCode: status,
                extensions: extensions);
        }

        if (ex.InvoiceId is { } invoiceId)
        {
            extensions["invoiceId"] = invoiceId;
        }

        return Results.Problem(title: title, detail: ex.Message, statusCode: status, extensions: extensions);
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
    /// GET /api/finance/invoices/search?query={text}&amp;matterId={guid (required)}&amp;top={int}
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

        // matterId is required (task 130). The route's authorization filter already rejects a missing one
        // before this handler runs; this is the handler's own refusal to run an unscoped, tenant-wide search
        // should the filter ever be detached.
        if (matterId is null || matterId == Guid.Empty)
        {
            return MatterIdRequired();
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
        catch (FeatureDisabledException ex)
        {
            // Task 011 Phase 1b Tier 2 (D-09 §2 L2): NullInvoiceSearchService surfaced.
            // Convert to 503 ProblemDetails per ADR-018 + ADR-019.
            logger.LogDebug(
                "Invoice search called while AI feature disabled. ErrorCode={ErrorCode}, CorrelationId={CorrelationId}",
                ex.ErrorCode, correlationId);
            return ex.AsFeatureDisabled503();
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
