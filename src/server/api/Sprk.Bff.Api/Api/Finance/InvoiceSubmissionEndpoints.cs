using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// The internal invoice submission routes (ebilling-r1 task 044, FR-17 internal channel, D-24), ADR-001 (Minimal API) and ADR-008
/// (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Authorization.</b> Each route carries its own <see cref="FinanceAuthorizationFilter"/> AS THE CALLER, then the eBilling flag
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization. A submit needs Read and AppendTo on the core record
/// the route names (<c>finance.submit_invoice</c>) and AppendTo on the firm the query names (<c>finance.attach_invoice</c>, the
/// sprk_vendororg reasoning of the invoice confirm route): the application then writes rows whose lookups point at both. The status
/// route needs Read on the submission itself, and answers an unreadable and an absent row with the same 404.</para>
/// <para><b>Inputs.</b> The firm is the QUERY parameter <c>firmId</c> (so the filter and the handler read the same source, the Finance
/// doctrine); the PDF is the multipart <c>file</c> part; <c>firmInvoiceNumber</c> (required, D-91) and <c>poNumber</c> are form
/// fields. <see cref="InvoiceSubmissionHttp.ReadSubmissionFormAsync"/> is shared with the external route, so both channels parse identically.</para>
/// <para><b>Response contract (tasks 049 / 051 / 052).</b> 202 with <c>Location</c> = <c>statusUrl</c> and the body
/// <see cref="InvoiceSubmissionAccepted"/>; refusals are ProblemDetails with <c>errorCode</c> (<see cref="InvoiceSubmissionErrors"/>).</para>
/// <para><b>Submit Invoice from a source document (ebilling-r1 task 094; procedure-and-file-model 9.1 step 1).</b>
/// <c>POST /api/finance/invoice-submissions/from-document</c>, keyed on (communication, document): the filter checks Read and AppendTo on
/// the body's core record and AppendTo on its firm, as the upload routes do; then <see cref="PrepareSourceAsync"/> (shared with the
/// confirm route) checks the caller's own Read on the document and its communication (gate variant), read-only; the intake's
/// existing-document entry decides the rest (dedupe by document, D-140, isolation, the firm, the link, the owner) and only then runs
/// step A (<see cref="SourceFiling"/>), which files a communication the D-140 chain leaves unfiled to the core record AS THE CALLER
/// (the communication re-file core). The document is never moved: its bytes are copied by the job.</para>
/// </remarks>
public static class InvoiceSubmissionEndpoints
{
    internal const string Route = "/api/finance";

    /// <summary>The status route of a submission on the internal surface.</summary>
    internal static string StatusUrl(Guid submissionId) => $"{Route}/invoice-submissions/{submissionId}";

    public static IEndpointRouteBuilder MapInvoiceSubmissionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // POST /api/finance/{matters|projects|workassignments}/{id}/invoice-submissions. Three literal chains (not a loop): the
        // RouteAuthorizationGuard scanner reads each route's path and attachments from its own Map{Verb} chain.
        group.MapPost("/matters/{id:guid}/invoice-submissions", SubmitForMatterAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.MatterEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's own record rights, not a form token
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForMatter")
            .WithSubmitContract("matter");

        group.MapPost("/projects/{id:guid}/invoice-submissions", SubmitForProjectAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.ProjectEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForProject")
            .WithSubmitContract("project");

        group.MapPost("/workassignments/{id:guid}/invoice-submissions", SubmitForWorkAssignmentAsync)
            .AddFinanceAuthorizationFilter(context => ResolveSubmitTargets(context, FinanceAuthorizationFilter.WorkAssignmentEntitySet))
            .RequireEbillingEnabled()
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("SubmitInvoiceForWorkAssignment")
            .WithSubmitContract("work assignment");

        // POST /api/finance/invoice-submissions/from-document (task 094): Submit Invoice from a source document.
        group.MapPost("/invoice-submissions/from-document", SubmitFromDocumentAsync)
            .AddFinanceAuthorizationFilter(ResolveFromDocumentTargets)
            .RequireEbillingEnabled()
            .WithName("SubmitInvoiceFromDocument")
            .WithSummary("Submit an email attachment (a document of a communication) as an invoice")
            .WithDescription(
                "Submits the source document, a document of the communication named (communicationId is required), as an internal invoice " +
                "submission; the processing job copies its bytes into the invoice's file (the document is never moved). Body: " +
                "{communicationId, documentId, coreRecord: {kind: matter|project|workassignment, id}, firmId, firmInvoiceNumber, poNumber?, " +
                "mode?: New|Correction, invoiceId? (required for Correction: the In Review invoice it replaces, same firm, record and firm " +
                "invoice number), supportingDocumentIds? (documents of the same communication kept with the invoice as Supporting files; " +
                "repeats ignored; the invoice document or more than 50 files in all is 400; one the caller cannot Read, an unknown one or " +
                "one of another communication is the uniform 404)}. " +
                "The caller needs Read on the document and its communication, Read and AppendTo on the core record and AppendTo on the firm " +
                "(and Write on the invoice a Correction replaces); " +
                "after every read-only check passes, a communication the D-140 chain reads as unfiled is filed " +
                "to the core record first, as the caller (Write on it). 202 {submissionId, jobId, trackingNumber, statusUrl}; a retry for " +
                "the same document returns the same submission. Refusals are ProblemDetails with errorCode: 400 validation; 403 " +
                "source_forbidden, COMMUNICATION_REFILE_FORBIDDEN, " +
                "the core record and firm checks; 404 document_not_found, or (reasonCode record_unavailable) the invoice a Correction " +
                "names is not one the caller can Write; 409 document_core_record_mismatch, document_already_invoiced, " +
                "document_rejected, document_already_submitted, submission_already_ended, correction_not_allowed (reason); " +
                "413 too_large (a supporting document over the file limit, or the supporting documents together over the total the " +
                "upload route allows); 422 source_communication_mismatch, " +
                "document_has_no_file, document_unavailable (a supporting document with no file that can be copied, or no longer of this " +
                "communication), storage_not_configured (invoice file storage is not set up, so supporting documents cannot be stored); " +
                "503 record_unavailable.")
            .Accepts<SubmitInvoiceFromDocumentRequest>("application/json")
            .Produces<InvoiceSubmissionAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/invoice-submissions/from-email (ebilling-r1 task 095, model §9.1, contract notes/095-from-email-contract.md).
        // Filters in the constrained order: step 0 and the body-shape 400 and the record and firm rights (the Finance filter, as the
        // caller: Read + AppendTo on the core record, AppendTo on the firm), the Office caller, EntityAccessFilter on the same core record
        // (the save files the .eml there), the Office Save rate limit, the version-save gate (a no-op for this body), the eBilling kill
        // switch, then the X-Idempotency-Key response cache bound to K. The handler is InvoiceSubmissionFromEmailHttp.
        group.MapPost("/invoice-submissions/from-email", InvoiceSubmissionFromEmailHttp.SubmitFromEmailAsync)
            .AddFinanceAuthorizationFilter(InvoiceSubmissionFromEmailHttp.ResolveFromEmailTargets)
            .AddOfficeAuthFilter()
            .AddEntityAccessFilter()
            .AddOfficeRateLimitFilter(OfficeRateLimitCategory.Save)
            .AddOfficeVersionSaveAuthorizationFilter()
            .RequireEbillingEnabled()
            .AddIdempotencyFilter(
                bindClientKeyTo: InvoiceSubmissionFromEmailHttp.IdempotencyBinding,
                mayReplayResponse: InvoiceSubmissionFromEmailHttp.MayReplayResponse)
            .Accepts<FromEmailSubmissionRequest>("application/json")
            .WithName("SubmitInvoiceFromEmail")
            .WithSummary("Submit an invoice from an Outlook email: save the email alone and submit its marked attachment")
            .Produces<InvoiceSubmissionFromEmailAccepted>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound) // task 097b: a Correction of an invoice the caller cannot Write
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/core-records/{entityType}/{entityId}/firms (task 095, contract section 8): the firms the submit accepts.
        group.MapGet("/core-records/{entityType}/{entityId:guid}/firms", InvoiceSubmissionFromEmailHttp.GetFirmsAsync)
            .AddFinanceAuthorizationFilter(InvoiceSubmissionFromEmailHttp.ResolveFirmsTargets, FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetCoreRecordFirms")
            .WithSummary("The active firms linked to a matter, project or work assignment")
            .Produces<IReadOnlyList<CoreRecordFirm>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/invoices/{invoiceId}/correction-prefill (task 097e, contract section 10): what the add-in prefills a correction from.
        // Write on the route's invoice as the caller ("ebilling.invoice.decide", the key the invoice-level writes share), the same Write the
        // Correction intake asks first (PrepareCorrectionAsync); uniform 404 for no Write, an unknown invoice, step 0 and a rights fault.
        group.MapGet("/invoices/{invoiceId:guid}/correction-prefill", InvoiceSubmissionFromEmailHttp.GetCorrectionPrefillAsync)
            .AddFinanceAuthorizationFilter("ebilling.invoice.decide", FinanceAuthorizationFilter.InvoiceEntitySet, "invoiceId", FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetInvoiceCorrectionPrefill")
            .WithSummary("What the add-in needs to prefill a correction of an invoice, and whether a correction is allowed now")
            .Produces<CorrectionPrefillResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/invoice-submissions?internetMessageId=... (task 095a, contract section 11): the latest attempt of an email the caller
        // may read, for the add-in's "already submitted" banner. The filter is step 0, the shape check of the Message-ID (the uniform 404) and
        // the caller's table-level Read on submissions; the per-record Read on the .eml and on each candidate submission is the handler's,
        // through HoldsReadAsync (the same finance.read check, on the rights snapshot so that Faulted is seen), because a skipped candidate must not deny the whole route and a rights fault must
        // be a 503 here, never "not submitted". The filter runs in the fail-closed mode (review F1, uac-r2 binding): a genuine denial (no table Read, a
        // step-0 refusal of an external caller) is the uniform 404, but a step-0 read fault or a table-privilege read with no answer is the same 503.
        group.MapGet("/invoice-submissions", GetByEmailAsync)
            .AddFinanceAuthorizationFilter(ResolveByEmailTargets, FinanceDenial.UniformNotFoundFailClosed)
            .RequireEbillingEnabled()
            .WithName("GetInvoiceSubmissionByEmail")
            .WithSummary("The latest invoice submission of an email (by its Message-ID) that the caller can read")
            .Produces<InvoiceSubmissionByEmailView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // GET /api/finance/invoice-submissions/{id}
        group.MapGet("/invoice-submissions/{id:guid}", GetAsync)
            .AddFinanceAuthorizationFilter("finance.read", FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "id", FinanceDenial.UniformNotFound)
            .RequireEbillingEnabled()
            .WithName("GetInvoiceSubmission")
            .WithSummary("Read one invoice submission's status")
            .Produces<InvoiceSubmissionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declaration — reads the SAME route and query values the handler binds
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read + AppendTo on the route's core record, and AppendTo on the query's firm. An unreadable route id yields no record check, which
    /// the filter denies; a malformed firmId adds no firm check (the handler answers 400 <see cref="InvoiceSubmissionErrors.FirmInvalid"/>),
    /// so a caller without rights on the record is always answered 403 first.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveSubmitTargets(EndpointFilterInvocationContext context, string entitySet)
    {
        var request = context.HttpContext.Request;
        if (!Guid.TryParse(request.RouteValues["id"]?.ToString(), out var id) || id == Guid.Empty)
        {
            return FinanceAuthorizationTargets.Authorize();
        }

        var checks = new List<FinanceAuthorizationCheck>
        {
            new()
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = entitySet,
                RecordId = id,
                Operation = "finance.submit_invoice",
                Source = "route.id",
            },
        };

        if (Guid.TryParse(request.Query["firmId"].ToString(), out var firmId) && firmId != Guid.Empty)
        {
            checks.Add(new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = firmId,
                Operation = "finance.attach_invoice",
                Source = "query.firmId",
            });
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
    }

    /// <summary>
    /// Task 094: the from-document route's filter targets, from the BOUND body (the instance the handler receives): Read and AppendTo on
    /// the core record (<c>finance.submit_invoice</c>) and AppendTo on the firm (<c>finance.attach_invoice</c>), the upload routes'
    /// checks. A body that does not validate is the field-level 400 before any rights query. The document and its communication are
    /// checked by the handler, as the caller, gate variant (<see cref="PrepareSourceAsync"/>).
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveFromDocumentTargets(EndpointFilterInvocationContext context)
    {
        var request = context.Arguments.OfType<SubmitInvoiceFromDocumentRequest>().FirstOrDefault();
        if (request is null)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(new Dictionary<string, string[]>
            {
                ["body"] = ["A JSON request body is required."],
            }));
        }

        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return FinanceAuthorizationTargets.Reject(ProblemDetailsHelper.ValidationProblem(errors));
        }

        var core = request.ToCoreRecord()!;
        return FinanceAuthorizationTargets.Authorize(
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = CoreEntitySet(core.Kind),
                RecordId = core.RecordId,
                Operation = "finance.submit_invoice",
                Source = "body.coreRecord",
            },
            new FinanceAuthorizationCheck
            {
                Path = FinanceCheckPath.Record,
                EntitySetName = FinanceAuthorizationFilter.VendorOrganizationEntitySet,
                RecordId = request.FirmId,
                Operation = "finance.attach_invoice",
                Source = "body.firmId",
            });
    }

    /// <summary>The entity set of a core record kind (the filter's and the re-file's binding).</summary>
    internal static string CoreEntitySet(InvoiceCoreRecordKind kind) => kind switch
    {
        InvoiceCoreRecordKind.Matter => FinanceAuthorizationFilter.MatterEntitySet,
        InvoiceCoreRecordKind.Project => FinanceAuthorizationFilter.ProjectEntitySet,
        InvoiceCoreRecordKind.WorkAssignment => FinanceAuthorizationFilter.WorkAssignmentEntitySet,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Not a core record kind."),
    };

    // ═══════════════════════════════════════════════════════════════════════════
    // Submit Invoice step 1 (task 094): shared by the from-document route and the confirm route
    // ═══════════════════════════════════════════════════════════════════════════

    private const string CommunicationEntity = "sprk_communication";
    private const string CommunicationEntitySet = "sprk_communications";
    private const string DocumentRelatedCommunication = "sprk_relatedcommunication";

    /// <summary>
    /// Task 094: what Submit Invoice step 1 (<see cref="PrepareSourceAsync"/>) uses, from the request scope: the caller's rights probe and
    /// the app-only reader. The communication re-file core's own dependencies (step A) are resolved only when A runs.
    /// </summary>
    internal sealed record SourceServices(CallerRecordAccessProbe Probe, IGenericEntityService Entities, ILogger Logger)
    {
        internal static SourceServices From(HttpContext http) => new(
            http.RequestServices.GetRequiredService<CallerRecordAccessProbe>(),
            http.RequestServices.GetRequiredService<IGenericEntityService>(),
            http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Sprk.Bff.Api.Api.Finance.SubmitInvoice"));
    }

    /// <summary>
    /// Procedure-and-file-model 9.1 step 1, before any submission write. READ-ONLY (fix round F1-b): (1) the caller's own Read on the source
    /// document, gate variant (skipped only when <paramref name="documentReadChecked"/>: a route whose filter already required Read on the
    /// document) (an unknown document and one the caller cannot see get the same 403 <see cref="InvoiceSubmissionErrors.SourceForbidden"/>
    /// on the same schedule); (2) the document's communication, read app-only: the one <paramref name="communicationId"/> names must be it
    /// (422 <see cref="InvoiceSubmissionErrors.SourceCommunicationMismatch"/>; the confirm route names none and takes the document's own, if
    /// any); (3) the caller's own Read on that communication (403 <see cref="InvoiceSubmissionErrors.SourceForbidden"/>). A read fault is
    /// 503. The answer's <see cref="SourcePreparation.Filing"/> is step A (<see cref="SourceFiling"/>), which the route hands to the intake
    /// (<see cref="InvoiceSubmissionRequest.FileSourceBeforeWrite"/>): the intake runs it only after its own read-only refusals, so a refused
    /// submission never leaves a filing behind.
    /// </summary>
    internal static async Task<SourcePreparation> PrepareSourceAsync(
        Guid documentId, Guid? communicationId, InvoiceCoreRecord core, HttpContext http, bool documentReadChecked, CancellationToken ct)
    {
        // Resolved here, after the route's filters, not bound as parameters: a route that refuses never needs them.
        var services = SourceServices.From(http);
        var token = TokenHelper.ExtractBearerTokenOrNull(http);
        if (!documentReadChecked)
        {
            var documentRights = await services.Probe.GetCallerRightsForAuthorizationGateAsync(token, FinanceAuthorizationFilter.DocumentEntitySet, documentId, ct);
            if (!documentRights.HasFlag(AccessRights.Read))
            {
                return SourcePreparation.Refused(SourceForbidden());
            }
        }

        Entity document;
        try
        {
            document = await services.Entities.RetrieveAsync(InvoiceSubmissionPersister.DocumentEntity, documentId, [DocumentRelatedCommunication], ct);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return SourcePreparation.Refused(InvoiceSubmissionHttp.Refused(StatusCodes.Status404NotFound, InvoiceSubmissionErrors.DocumentNotFound,
                "The document no longer exists. Nothing was saved."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            services.Logger.LogWarning("Submit Invoice: document {DocumentId} could not be read ({Fault})", documentId, ex.GetType().Name);
            return SourcePreparation.Refused(Unavailable());
        }

        var linked = document.GetAttributeValue<EntityReference>(DocumentRelatedCommunication)?.Id is { } related && related != Guid.Empty
            ? related
            : (Guid?)null;
        if (communicationId is { } named && named != linked)
        {
            return SourcePreparation.Refused(InvoiceSubmissionHttp.Refused(StatusCodes.Status422UnprocessableEntity, InvoiceSubmissionErrors.SourceCommunicationMismatch,
                "The document does not belong to the communication named, so it cannot be submitted from it. Nothing was saved."));
        }

        if (linked is not { } communication)
        {
            return SourcePreparation.Proceed(null); // a document with no communication (the confirm route): nothing to file
        }

        var communicationRights = await services.Probe.GetCallerRightsForAuthorizationGateAsync(token, CommunicationEntitySet, communication, ct);
        if (!communicationRights.HasFlag(AccessRights.Read))
        {
            return SourcePreparation.Refused(SourceForbidden());
        }

        return SourcePreparation.Proceed(new SourceFiling(communication, communicationRights, core, http, services));
    }

    /// <summary>
    /// Task 118b (POML constraints "contract", "access"): the from-document route's supporting documents, READ-ONLY, after step 1 passed for
    /// the invoice document and before the intake (so before step A and before anything is staged). For each id, in order: the caller's own
    /// Read, through the check the invoice document uses (<see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>,
    /// gate variant); then the row, read app-only, must link <paramref name="communicationId"/> (<c>sprk_relatedcommunication</c>). A document
    /// the caller cannot Read, an unknown one and one of another communication (or none) all get the uniform not-found answer
    /// (<see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/>, 404 <c>sdap.access.deny.record_unavailable</c>): the same bytes, and
    /// the row of an unreadable one is never read. A read fault is 503 <see cref="InvoiceSubmissionErrors.CoreRecordUnavailable"/>. Null: every
    /// one passed (or there are none).
    /// </summary>
    internal static async Task<IResult?> CheckSupportingDocumentsAsync(
        IReadOnlyList<Guid> supportingDocumentIds, Guid communicationId, HttpContext http, CancellationToken ct)
    {
        if (supportingDocumentIds.Count == 0)
        {
            return null;
        }

        var services = SourceServices.From(http);
        var token = TokenHelper.ExtractBearerTokenOrNull(http);
        foreach (var documentId in supportingDocumentIds)
        {
            var rights = await services.Probe.GetCallerRightsForAuthorizationGateAsync(token, FinanceAuthorizationFilter.DocumentEntitySet, documentId, ct);
            if (!rights.HasFlag(AccessRights.Read))
            {
                services.Logger.LogInformation("Submit Invoice: a supporting document the caller cannot Read was refused (uniform not-found)");
                return FinanceAuthorizationFilter.UniformRecordNotFound(http);
            }

            Entity? document;
            try
            {
                document = await services.Entities.RetrieveAsync(InvoiceSubmissionPersister.DocumentEntity, documentId, [DocumentRelatedCommunication], ct);
            }
            catch (Exception ex) when (IsNotFound(ex))
            {
                return FinanceAuthorizationFilter.UniformRecordNotFound(http);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                services.Logger.LogWarning("Submit Invoice: supporting document {DocumentId} could not be read ({Fault})", documentId, ex.GetType().Name);
                return Unavailable();
            }

            if (document?.GetAttributeValue<EntityReference>(DocumentRelatedCommunication)?.Id != communicationId)
            {
                services.Logger.LogInformation("Submit Invoice: supporting document {DocumentId} is not a document of the communication named (uniform not-found)", documentId);
                return FinanceAuthorizationFilter.UniformRecordNotFound(http);
            }
        }

        return null;
    }

    /// <summary>What step 1 decided: a refusal (the route's answer, nothing written), or proceed with step A (null: nothing to file).</summary>
    internal sealed record SourcePreparation(IResult? Refusal, SourceFiling? Filing)
    {
        internal static SourcePreparation Refused(IResult refusal) => new(refusal, null);

        internal static SourcePreparation Proceed(SourceFiling? filing) => new(null, filing);

        /// <summary>The intake's hook (<see cref="InvoiceSubmissionRequest.FileSourceBeforeWrite"/>), or null when there is nothing to file.</summary>
        internal Func<CancellationToken, Task<bool>>? Hook => Filing is { } filing ? filing.RunAsync : null;
    }

    /// <summary>
    /// Task 097b (model 9.1 "Existing invoice", 097 constraint 1): the first check of a Correction, on both Submit Invoice routes, READ-ONLY.
    /// The caller's OWN Write on <paramref name="invoiceId"/>, gate variant (<see cref="CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync"/>):
    /// without it, or for an invoice that does not exist, the answer is the uniform not-found answer
    /// (<see cref="FinanceAuthorizationFilter.UniformRecordNotFound"/>, 404 <c>sdap.access.deny.record_unavailable</c>), the same bytes and
    /// schedule for both, so nothing about the invoice is revealed. Only then is the invoice read app-only with
    /// <see cref="InvoiceSubmissionService.CorrectionInvoiceColumns"/>, for the intake's own checks
    /// (<see cref="InvoiceSubmissionService.CheckCorrectionAsync"/>); a row gone since is the same uniform answer, and a read fault is 503
    /// <see cref="InvoiceSubmissionErrors.CoreRecordUnavailable"/>.
    /// </summary>
    internal static async Task<(IResult? Refusal, Entity? Invoice)> PrepareCorrectionAsync(Guid invoiceId, HttpContext http, CancellationToken ct)
    {
        var services = SourceServices.From(http);
        var rights = await services.Probe.GetCallerRightsForAuthorizationGateAsync(
            TokenHelper.ExtractBearerTokenOrNull(http), FinanceAuthorizationFilter.InvoiceEntitySet, invoiceId, ct);
        if (!rights.HasFlag(AccessRights.Write))
        {
            services.Logger.LogInformation("Submit Invoice: a Correction of an invoice the caller cannot Write was refused (uniform not-found)");
            return (FinanceAuthorizationFilter.UniformRecordNotFound(http), null);
        }

        try
        {
            var invoice = await services.Entities.RetrieveAsync(
                Services.Finance.FinanceBaseValues.InvoiceEntity, invoiceId, InvoiceSubmissionService.CorrectionInvoiceColumns, ct);
            return invoice is null ? (FinanceAuthorizationFilter.UniformRecordNotFound(http), null) : (null, invoice);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return (FinanceAuthorizationFilter.UniformRecordNotFound(http), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            services.Logger.LogWarning("Submit Invoice: invoice {InvoiceId} to correct could not be read ({Fault})", invoiceId, ex.GetType().Name);
            return (InvoiceSubmissionHttp.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
                "The invoice to correct could not be checked right now. Nothing was saved; try again shortly."), null);
        }
    }

    /// <summary>
    /// Step A of Submit Invoice (procedure-and-file-model 9.1), run by the intake after its read-only refusals (fix round F1-b). It decides
    /// "unfiled" from the D-140 chain (<see cref="InvoiceSubmissionService.IsCommunicationFiledAsync"/>: the communication's own filing
    /// columns and every parent its record thread files it under) and files only a communication the chain leaves empty: a filed one is left
    /// as it is (the intake has already judged the filing the chain gives). Before it files, the communication must be under no secure root
    /// and not owned by the Secure Record owner team (<see cref="ExistingDocumentIsolationCheck.CheckCommunicationForFilingAsync"/>: else 409
    /// <see cref="InvoiceSubmissionErrors.DocumentCoreRecordMismatch"/>, a fault 503), and the caller must have Write on it (else the
    /// communication re-file route's own 403 <c>COMMUNICATION_REFILE_FORBIDDEN</c>, as <see cref="RefileForbidden"/>). Then the communication re-file core
    /// (<see cref="CommunicationEndpoints.RefileCommunicationAsync"/>) files it under the core record AS THE CALLER, with its own checks
    /// (AppendTo on the record, the owner, the thread following the filing); any answer but 204 is the route's answer. Every refusal throws
    /// <see cref="SourceFilingRefusedException"/>, so the intake writes nothing after it.
    /// The filing it makes is NOT undone by a later refusal or by the job's undo (the answer and the administrator note say so).
    /// </summary>
    internal sealed class SourceFiling
    {
        private readonly Guid _communication;
        private readonly AccessRights _rights;
        private readonly InvoiceCoreRecord _core;
        private readonly HttpContext _http;
        private readonly SourceServices _services;

        internal SourceFiling(Guid communication, AccessRights rights, InvoiceCoreRecord core, HttpContext http, SourceServices services)
        {
            _communication = communication;
            _rights = rights;
            _core = core;
            _http = http;
            _services = services;
        }

        /// <summary>True when it filed the communication; false when it was already filed (nothing written). Throws to refuse.</summary>
        internal async Task<bool> RunAsync(CancellationToken ct)
        {
            bool filed;
            try
            {
                filed = await InvoiceSubmissionService.IsCommunicationFiledAsync(_services.Entities, _communication, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _services.Logger.LogWarning("Submit Invoice: the filing of communication {CommunicationId} could not be read ({Fault})", _communication, ex.GetType().Name);
                throw new SourceFilingRefusedException(Unavailable());
            }

            if (filed)
            {
                return false;
            }

            var scope = _http.RequestServices;
            var isolation = await scope.GetRequiredService<ExistingDocumentIsolationCheck>().CheckCommunicationForFilingAsync(_communication, ct);
            switch (isolation.Verdict)
            {
                case ExistingDocumentIsolationVerdict.Allowed:
                    break;
                case ExistingDocumentIsolationVerdict.Unavailable:
                    throw new SourceFilingRefusedException(Unavailable());
                default:
                    _services.Logger.LogInformation("Submit Invoice: communication {CommunicationId} is isolated ({Verdict}); it is not filed and nothing is submitted",
                        _communication, isolation.Verdict);
                    throw new SourceFilingRefusedException(InvoiceSubmissionHttp.Refused(StatusCodes.Status409Conflict, InvoiceSubmissionErrors.DocumentCoreRecordMismatch,
                        "The document's communication belongs to a restricted record, so it cannot be filed or submitted for this record. Nothing was saved."));
            }

            if (!_rights.HasFlag(AccessRights.Write))
            {
                throw new SourceFilingRefusedException(RefileForbidden());
            }

            // The re-file core's own dependencies come from the request scope only when A runs (most submissions never file anything).
            var body = await RefileBodyAsync(_core, _services, scope.GetRequiredService<ICommunicationDataverseService>(), ct);
            var refiled = await CommunicationEndpoints.RefileCommunicationAsync(
                _communication, body, scope.GetRequiredService<IDataverseUserClient>(), scope.GetRequiredService<IRecordOwnershipResolver>(),
                scope.GetRequiredService<CoreAncestorRestamper>(), scope.GetRequiredService<Services.Access.SecureChildReconciler>(), _services.Entities,
                scope.GetService<Services.Signals.SignalEvaluationTrigger>(), _http, _services.Logger, ct);
            if (refiled is not Microsoft.AspNetCore.Http.HttpResults.NoContent)
            {
                _services.Logger.LogInformation("Submit Invoice: filing communication {CommunicationId} under {Entity} {RecordId} was refused; nothing submitted",
                    _communication, _core.EntityLogicalName, _core.RecordId);
                throw new SourceFilingRefusedException(refiled);
            }

            _services.Logger.LogInformation("Submit Invoice: communication {CommunicationId} filed under {Entity} {RecordId} before the submission (step A)",
                _communication, _core.EntityLogicalName, _core.RecordId);
            return true;
        }
    }

    /// <summary>
    /// Step A's 403 as the submission routes render a refusal (changed 2026-10-29, 095 contract fix): status 403 and a TOP-LEVEL
    /// <c>errorCode</c> <c>COMMUNICATION_REFILE_FORBIDDEN</c> (<see cref="InvoiceSubmissionHttp.Refused"/>), so a client reading
    /// <c>errorCode ?? reasonCode</c> finds it. It was an <see cref="SdapProblemException"/>, which the problem middleware renders with
    /// the code only under a nested <c>extensions.code</c>. The words are the re-file route's own (no existence oracle), and nothing of the
    /// submission is written. Also the from-email operation's answer to a 403 of the re-file core (review round 5), so that route sends
    /// only contract section 6's code. The communication re-file route's own response is unchanged.
    /// </summary>
    internal static IResult RefileForbidden() => InvoiceSubmissionHttp.Refused(
        StatusCodes.Status403Forbidden, CommunicationRecordAuthorizationFilter.RefileDenyCode, RefileForbiddenDetail);

    /// <summary>The words of <see cref="RefileForbidden"/>.</summary>
    internal const string RefileForbiddenDetail = "The communication does not exist or is not visible to the caller, or the caller may not change it.";

    /// <summary>Step A's refusal, carrying the route's answer (the intake writes nothing after it; the route returns <see cref="Answer"/>).</summary>
    internal sealed class SourceFilingRefusedException(IResult answer) : Exception("Submit Invoice step A refused the filing of the source communication.")
    {
        internal IResult Answer { get; } = answer;
    }

    /// <summary>
    /// Step A's re-file body: the core record's regarding lookup and, when they can be derived, its ADR-024 resolver fields (the shape the
    /// Communication form's Connections writer sends). Without the resolver fields the lookup alone is sent (the pair is optional).
    /// </summary>
    private static async Task<JsonElement> RefileBodyAsync(
        InvoiceCoreRecord core, SourceServices services, ICommunicationDataverseService recordTypes, CancellationToken ct)
    {
        var navigation = core.Kind switch
        {
            InvoiceCoreRecordKind.Matter => "sprk_RegardingMatter",
            InvoiceCoreRecordKind.Project => "sprk_RegardingProject",
            _ => "sprk_RegardingWorkAssignment",
        };
        var body = new Dictionary<string, object?>
        {
            [navigation + "@odata.bind"] = $"/{CoreEntitySet(core.Kind)}({core.RecordId:D})",
        };

        try
        {
            // The invoice-family rule read as for an invoice that regards the core record (no row is built: nothing is created here).
            // Read app-only on purpose (uac-r2, 2026-11-13). The submitter already reads this core record: the invoice follows its
            // one core record (D-28), and the re-file runs as them. Only the type, id, name and URL are written, NO number.
            // A change that writes the number, or that reaches this code without that read, must read as the caller and clear
            // the name and number on 403/404, as task 045d's create does (OwnedChildWrite.CallerDisplayRecords).
            var regarding = new EntityReference(core.EntityLogicalName, core.RecordId);
            var decided = await InvoiceFamilyCoreRecordGuard.DeriveResolverFieldsAsync(
                InvoiceFamilyCoreRecordGuard.Check(Services.Finance.FinanceBaseValues.InvoiceEntity,
                    column => string.Equals(column, core.EntityLogicalName, StringComparison.Ordinal) ? regarding : null),
                services.Entities, recordTypes.QueryRecordTypeRefAsync, ct);
            if (decided.ResolverFields is { } fields)
            {
                body["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({fields.RecordTypeId:D})";
                body["sprk_regardingrecordid"] = fields.RecordId;
                body["sprk_regardingrecordname"] = fields.RecordName;
                body["sprk_regardingrecordurl"] = fields.RecordUrl;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            services.Logger.LogInformation("Submit Invoice: the resolver fields of {Entity} {RecordId} could not be derived ({Fault}); filing by the lookup",
                core.EntityLogicalName, core.RecordId, ex.GetType().Name);
        }

        return JsonSerializer.SerializeToElement(body);
    }

    internal static IResult SourceForbidden() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.SourceForbidden,
            "The document or its communication does not exist or is not visible to the caller. Nothing was saved.");

    private static IResult Unavailable() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
            "The document could not be checked right now. Nothing was saved; try again shortly.");

    internal static bool IsNotFound(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (RecordContainerResolver.IsRecordNotFound(e))
            {
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> SubmitFromDocumentAsync(
        SubmitInvoiceFromDocumentRequest request,
        HttpContext http,
        InvoiceSubmissionService service,
        CancellationToken ct)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return ProblemDetailsHelper.ValidationProblem(errors);
        }

        var core = request.ToCoreRecord()!;

        // Task 097b: a Correction first needs the caller's own Write on the invoice it replaces (else the uniform not-found answer).
        Entity? correctedInvoice = null;
        if (request.CorrectedInvoiceId is { } invoiceId)
        {
            var correction = await PrepareCorrectionAsync(invoiceId, http, ct);
            if (correction.Refusal is { } correctionRefused)
            {
                return correctionRefused;
            }

            correctedInvoice = correction.Invoice;
        }

        var prepared = await PrepareSourceAsync(request.DocumentId, request.CommunicationId, core, http, documentReadChecked: false, ct);
        if (prepared.Refusal is { } refused)
        {
            return refused;
        }

        // Task 118b: the supporting documents, read-only, as the caller, before the intake (nothing is filed or staged before this passes).
        var supporting = request.SupportingDocuments;
        if (await CheckSupportingDocumentsAsync(supporting, request.CommunicationId, http, ct) is { } supportingRefused)
        {
            return supportingRefused;
        }

        Guid? callerOid = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
        InvoiceSubmissionOutcome outcome;
        try
        {
            outcome = await service.SubmitExistingDocumentAsync(new InvoiceSubmissionRequest
            {
                FirmId = request.FirmId,
                CoreRecord = core,
                Channel = InvoiceSubmissionChannel.Internal,
                FirmInvoiceNumber = request.FirmInvoiceNumber,
                PoNumber = request.PoNumber,
                ExistingDocumentId = request.DocumentId,
                SourceCommunicationId = request.CommunicationId,
                CallerObjectId = callerOid,
                CorrelationId = http.TraceIdentifier,
                FileSourceBeforeWrite = prepared.Hook, // step A: the intake runs it after its read-only refusals (fix round F1-b)
                CorrectedInvoice = correctedInvoice, // task 097b: judged by the intake after the retry check, before step A
                SupportingDocumentIds = supporting, // task 118b: compared with an earlier attempt's set (never part of the submission id)
                // Task 118b-2: the ONE shared document read (the job's), bound here to the route's SharePoint Embedded and container seams;
                // resolved only when a new attempt reads a supporting document.
                ReadSupportingDocument = supporting.Count == 0
                    ? null
                    : (id, maxBytes, readCt) => SourceDocumentFileRead.ReadAsync(
                        http.RequestServices.GetRequiredService<IGenericEntityService>(),
                        http.RequestServices.GetRequiredService<ISpeFileOperations>(),
                        http.RequestServices.GetRequiredService<RecordContainerResolver>(),
                        id,
                        readCt,
                        maxBytes),
            }, ct);
        }
        catch (SourceFilingRefusedException ex)
        {
            return ex.Answer;
        }

        return InvoiceSubmissionHttp.ToResult(outcome, StatusUrl, http.TraceIdentifier);
    }

    private static Task<IResult> SubmitForMatterAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, id), http, service, ct);

    private static Task<IResult> SubmitForProjectAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.Project, id), http, service, ct);

    private static Task<IResult> SubmitForWorkAssignmentAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct) =>
        SubmitAsync(new InvoiceCoreRecord(InvoiceCoreRecordKind.WorkAssignment, id), http, service, ct);

    private static async Task<IResult> SubmitAsync(InvoiceCoreRecord core, HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        if (!Guid.TryParse(http.Request.Query["firmId"].ToString(), out var firmId) || firmId == Guid.Empty)
        {
            return InvoiceSubmissionHttp.Refused(StatusCodes.Status400BadRequest, InvoiceSubmissionErrors.FirmInvalid, "firmId is required and must be a GUID.");
        }

        var form = await InvoiceSubmissionHttp.ReadSubmissionFormAsync(http.Request, service, StatusUrl, ct);
        if (form.Refusal is { } refusal)
        {
            return refusal;
        }

        Guid? callerOid = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
        var outcome = await service.SubmitAsync(new InvoiceSubmissionRequest
        {
            FirmId = firmId,
            CoreRecord = core,
            Channel = InvoiceSubmissionChannel.Internal,
            FirmInvoiceNumber = form.FirmInvoiceNumber,
            PoNumber = form.PoNumber,
            File = form.File,
            CallerObjectId = callerOid,
            CorrelationId = http.TraceIdentifier,
        }, ct);

        return InvoiceSubmissionHttp.ToResult(outcome, StatusUrl, http.TraceIdentifier);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // GET /api/finance/invoice-submissions?internetMessageId=... (task 095a, contract section 11)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>The table-level Read the by-email route's filter asks first (every caller who can read any submission holds it).</summary>
    internal const string ReadSubmissionPrivilege = "prvReadsprk_invoicesubmission";

    /// <summary>The most .eml Documents of one Message-ID the route will decide over; more is "cannot decide" (503), never a partial answer.</summary>
    internal const int MaxEmlCandidates = 50;

    /// <summary>The most attempts of one email the route reads; the newest readable one is returned, and when none of this many is readable
    /// while more exist, the answer is "cannot decide" (503).</summary>
    internal const int MaxAttemptCandidates = 50;

    private const string DocumentEmailMessageIdColumn = "sprk_emailmessageid";
    private const string DocumentIsEmailArchiveColumn = "sprk_isemailarchive";
    private const string SystemUserEntity = "systemuser";
    private const string SystemUserFullName = "fullname";

    /// <summary>
    /// The by-email route's filter declaration: a Message-ID that is empty, repeated, over RFC length or not RFC-shaped (the intake's own test,
    /// <c>OfficeEmailEnricher.IsRfcMessageId</c>) is the uniform 404 (nothing to look up, and nothing is revealed); otherwise the caller's
    /// table-level Read on submissions. The per-record rights are the handler's (<see cref="GetByEmailAsync"/>).
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveByEmailTargets(EndpointFilterInvocationContext context)
    {
        var http = context.HttpContext;
        var values = http.Request.Query["internetMessageId"];
        if (values.Count != 1 || MessageIdKeys(values[0]) is null)
        {
            return FinanceAuthorizationTargets.Reject(FinanceAuthorizationFilter.UniformRecordNotFound(http));
        }

        return FinanceAuthorizationTargets.Authorize(FinanceAuthorizationCheck.CallerPrivilege(
            ReadSubmissionPrivilege, FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, "invoice submissions"));
    }

    /// <summary>
    /// The keys <c>sprk_document.sprk_emailmessageid</c> is matched on for <paramref name="raw"/>, or null when it is not a usable Message-ID.
    /// The intake stores and matches the Message-ID exactly as the add-in sent it (<c>OfficeEmailEnricher.ResolveRfcMessageId</c>; Outlook's
    /// carries angle brackets, an older sender's may not), so both spellings are asked, with and without the brackets; case is the column's
    /// own (Dataverse string equality ignores case), as it is for the intake.
    /// </summary>
    internal static IReadOnlyList<string>? MessageIdKeys(string? raw)
    {
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > 998 || !Services.Office.OfficeEmailEnricher.IsRfcMessageId(value))
        {
            return null;
        }

        var bracketed = value.Length > 2 && value[0] == '<' && value[^1] == '>';
        var inner = bracketed ? value[1..^1].Trim() : value;
        if (inner.Length == 0 || inner.Contains('<') || inner.Contains('>'))
        {
            return null;
        }

        return [inner, "<" + inner + ">"];
    }

    /// <summary>
    /// The latest attempt, of the email the Message-ID names, that the caller may read. The .eml Documents of the Message-ID and the attempts
    /// that name them (<c>sprk_sourceemaildocument</c>) are read app-only; EVERY .eml and EVERY candidate attempt is then put to the caller's
    /// own Read (<see cref="HoldsReadAsync"/>, the filter's Record check, <c>finance.read</c>) before
    /// anything is answered, newest attempt first. An unreadable .eml or attempt is skipped and is indistinguishable from no match (the same
    /// uniform 404). ANY read fault, a failed rights read included, is a 503: the route never answers "not submitted" when it could not decide.
    /// </summary>
    internal static async Task<IResult> GetByEmailAsync(
        [FromQuery] string? internetMessageId,
        HttpContext http,
        InvoiceSubmissionService service,
        IGenericEntityService entities,
        Spaarke.Core.Auth.AuthorizationService authorization,
        ILogger<FromEmailSubmissionService> logger,
        CancellationToken ct)
    {
        if (MessageIdKeys(internetMessageId) is not { } keys || CallerResolution.ResolveObjectId(http.User) is not { Length: > 0 } userId)
        {
            return FinanceAuthorizationFilter.UniformRecordNotFound(http);
        }

        var token = TokenHelper.ExtractBearerTokenOrNull(http);
        try
        {
            // 1. The .eml Documents of this Message-ID (app-only), then the caller's own Read on each.
            var emlQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression(InvoiceSubmissionService.DocumentEntity)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet(false),
                TopCount = MaxEmlCandidates + 1,
            };
            emlQuery.Criteria.AddCondition(DocumentEmailMessageIdColumn, Microsoft.Xrm.Sdk.Query.ConditionOperator.In, keys.Cast<object>().ToArray());
            emlQuery.Criteria.AddCondition(DocumentIsEmailArchiveColumn, Microsoft.Xrm.Sdk.Query.ConditionOperator.Equal, true);
            var emls = (await ReadOrUnavailableAsync(() => entities.RetrieveMultipleAsync(emlQuery, ct), logger, "the email's documents", ct)).Entities;
            if (emls.Count > MaxEmlCandidates)
            {
                logger.LogWarning("Submission by email: more than {Max} .eml documents carry one Message-ID; it cannot be decided", MaxEmlCandidates);
                throw InvoiceSubmissionService.ReadUnavailable();
            }

            var readableEmls = new List<object>();
            foreach (var eml in emls)
            {
                if (await HoldsReadAsync(authorization, userId, FinanceAuthorizationFilter.DocumentEntitySet, eml.Id, token, logger, ct))
                {
                    readableEmls.Add(eml.Id);
                }
            }

            if (readableEmls.Count == 0)
            {
                return FinanceAuthorizationFilter.UniformRecordNotFound(http);
            }

            // 2. The attempts that name those .eml Documents, newest first, each put to the caller's own Read; the first readable one answers.
            var attemptQuery = new Microsoft.Xrm.Sdk.Query.QueryExpression(InvoiceSubmissionService.SubmissionEntity)
            {
                ColumnSet = new Microsoft.Xrm.Sdk.Query.ColumnSet([.. InvoiceSubmissionService.ViewColumns, RecordCreatorPerson.Column]),
                TopCount = MaxAttemptCandidates + 1,
            };
            attemptQuery.Criteria.AddCondition(
                InvoiceSubmissionService.SourceEmailDocumentColumn, Microsoft.Xrm.Sdk.Query.ConditionOperator.In, readableEmls.ToArray());
            attemptQuery.AddOrder("createdon", Microsoft.Xrm.Sdk.Query.OrderType.Descending);
            // A tie-break (two attempts created in the same instant): the order, and so the answer, is the same on every read.
            attemptQuery.AddOrder(TrackingNumberAllocator.SubmissionIdColumn, Microsoft.Xrm.Sdk.Query.OrderType.Descending);
            var attempts = (await ReadOrUnavailableAsync(() => entities.RetrieveMultipleAsync(attemptQuery, ct), logger, "the email's attempts", ct)).Entities;

            foreach (var row in attempts.Take(MaxAttemptCandidates))
            {
                if (!await HoldsReadAsync(authorization, userId, FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, row.Id, token, logger, ct))
                {
                    continue;
                }

                var view = InvoiceSubmissionService.ToView(row);
                var name = await ReadSubmitterNameAsync(row, entities, logger, ct);
                var shown = await service.WithReplacementNoteAsync([view], ReplacementGate(http, authorization), ct);
                return Results.Ok(InvoiceSubmissionByEmailView.From(shown[0], name));
            }

            if (attempts.Count > MaxAttemptCandidates)
            {
                logger.LogWarning("Submission by email: none of the newest {Max} attempts is readable and more exist; it cannot be decided", MaxAttemptCandidates);
                throw InvoiceSubmissionService.ReadUnavailable();
            }

            return FinanceAuthorizationFilter.UniformRecordNotFound(http);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }

    /// <summary>
    /// The caller's own Read on one record: the filter's Record check (<c>finance.read</c> over the caller-scoped snapshot, as
    /// <see cref="FinanceAuthorizationFilter.CallerHoldsRightsAsync"/> decides it) but asked of the snapshot itself, because a snapshot that
    /// carries no Read AND is <see cref="Spaarke.Dataverse.AccessSnapshot.Faulted"/> is "Dataverse gave no answer", not "the caller may not
    /// read it": that is the route's 503, never a "no" (which would be the not-submitted 404). A thrown rights read is the same 503.
    /// </summary>
    private static async Task<bool> HoldsReadAsync(
        Spaarke.Core.Auth.AuthorizationService authorization, string userId, string entitySet, Guid id, string? token, ILogger logger, CancellationToken ct)
    {
        try
        {
            var snapshot = await authorization.GetCallerRecordAccessAsync(userId, entitySet, id, token, ct);
            if (Spaarke.Core.Auth.OperationAccessPolicy.HasRequiredRights(snapshot.AccessRights, "finance.read"))
            {
                return true;
            }

            if (snapshot.Faulted)
            {
                logger.LogWarning("Submission by email: the caller's rights on {EntitySet} were not a complete answer", entitySet);
                throw InvoiceSubmissionService.ReadUnavailable();
            }

            return false;
        }
        catch (SdapProblemException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Submission by email: the caller's rights on {EntitySet} could not be read ({Fault})", entitySet, ex.GetType().Name);
            throw InvoiceSubmissionService.ReadUnavailable();
        }
    }

    /// <summary>One app-only read; a fault is the route's 503 (<see cref="InvoiceSubmissionService.ReadUnavailable"/>).</summary>
    private static async Task<T> ReadOrUnavailableAsync<T>(Func<Task<T>> read, ILogger logger, string what, CancellationToken ct)
    {
        try
        {
            return await read();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Submission by email: {What} could not be read ({Fault})", what, ex.GetType().Name);
            throw InvoiceSubmissionService.ReadUnavailable();
        }
    }

    /// <summary>
    /// The display name of the person who submitted (<c>sprk_createdbyperson</c>, a systemuser; never <c>createdby</c>, the service identity),
    /// or null when no person is recorded or the user has no name. A name only: no id, no address. A read fault is the route's 503.
    /// </summary>
    private static async Task<string?> ReadSubmitterNameAsync(Entity submission, IGenericEntityService entities, ILogger logger, CancellationToken ct)
    {
        if (submission.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column) is not { Id: var personId } || personId == Guid.Empty)
        {
            return null;
        }

        try
        {
            var person = await entities.RetrieveAsync(SystemUserEntity, personId, [SystemUserFullName], ct);
            var name = person?.GetAttributeValue<string>(SystemUserFullName);
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Submission by email: the submitter's name could not be read ({Fault})", ex.GetType().Name);
            throw InvoiceSubmissionService.ReadUnavailable();
        }
    }

    private static async Task<IResult> GetAsync(
        Guid id, HttpContext http, InvoiceSubmissionService service, Spaarke.Core.Auth.AuthorizationService authorization, CancellationToken ct)
    {
        try
        {
            var view = await service.GetAsync(id, ct);
            if (view is null)
            {
                return FinanceAuthorizationFilter.UniformRecordNotFound(http);
            }

            // Task 097f (EB-D-180): an Accepted submission whose invoice a correction Superseded still answers Accepted, with the replaced-by-
            // correction reason and, when the caller may read the correcting submission, its id and tracking number. Derived here; nothing is written.
            var shown = await service.WithReplacementNoteAsync([view], ReplacementGate(http, authorization), ct);
            return Results.Ok(shown[0]);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }

    /// <summary>
    /// Who may be told the correcting submission: the caller's own Read on it, <c>finance.read</c> on the submission, exactly the right this
    /// route's filter asked for the submission itself. A caller without a resolvable identity may read none.
    /// </summary>
    private static ReplacementReadGate ReplacementGate(HttpContext http, Spaarke.Core.Auth.AuthorizationService authorization) =>
        async (replacements, ct) =>
        {
            var readable = new HashSet<Guid>();
            if (CallerResolution.ResolveObjectId(http.User) is not { Length: > 0 } userId)
            {
                return readable;
            }

            var token = TokenHelper.ExtractBearerTokenOrNull(http);
            foreach (var replacement in replacements)
            {
                if (await FinanceAuthorizationFilter.CallerHoldsRightsAsync(
                        authorization, userId, FinanceAuthorizationFilter.InvoiceSubmissionEntitySet, replacement.SubmissionId, "finance.read", token, ct))
                {
                    readable.Add(replacement.SubmissionId);
                }
            }

            return readable;
        };
}

/// <summary>
/// Task 094: the body of <c>POST /api/finance/invoice-submissions/from-document</c>. Keyed on (<see cref="CommunicationId"/>,
/// <see cref="DocumentId"/>); the document must link the communication.
/// </summary>
public sealed record SubmitInvoiceFromDocumentRequest
{
    public Guid CommunicationId { get; init; }

    public Guid DocumentId { get; init; }

    public SubmitInvoiceCoreRecord? CoreRecord { get; init; }

    public Guid FirmId { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    public string? PoNumber { get; init; }

    /// <summary>
    /// Task 097b (EB-D-179 item 2): <c>New</c> or <c>Correction</c>, exact case, as on the from-email route (contract section 2). Optional
    /// and additive: absent is <c>New</c>, so a request without it behaves exactly as before 097b.
    /// </summary>
    public string? Mode { get; init; }

    /// <summary>Task 097b: the invoice a <c>Correction</c> replaces; required for <c>Correction</c>, absent (or null) otherwise.</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>
    /// Task 118b: the documents of the same communication to keep with the invoice as its Supporting Stored Files. Optional and additive:
    /// absent or empty behaves exactly as before 118b. Repeats are ignored (<see cref="SupportingDocuments"/>); the invoice document itself
    /// is a 400, and so is more files in total (the invoice document and these) than <see cref="SubmissionCommitGate.MaxFiles"/>. Each must be
    /// a <c>sprk_document</c> the caller can Read and that links <see cref="CommunicationId"/>; otherwise the uniform not-found answer.
    /// </summary>
    public IReadOnlyList<Guid>? SupportingDocumentIds { get; init; }

    /// <summary>Task 097b: the invoice a valid body corrects, or null for a New submission (call after <see cref="Validate"/>).</summary>
    internal Guid? CorrectedInvoiceId => Mode == FromEmailModes.Correction ? InvoiceId : null;

    /// <summary>
    /// Task 118b: <see cref="SupportingDocumentIds"/> de-duplicated and sorted (the order the intake compares and, in 118b-2, numbers the
    /// parts in); empty when the body names none.
    /// </summary>
    internal IReadOnlyList<Guid> SupportingDocuments => SupportingDocumentIds is { Count: > 0 } ids
        ? ids.Distinct().Order().ToArray()
        : [];

    /// <summary>The core record, or null when <see cref="CoreRecord"/> does not name one.</summary>
    internal InvoiceCoreRecord? ToCoreRecord() =>
        CoreRecord is { Id: var id } && id != Guid.Empty && SubmitInvoiceCoreRecord.KindOf(CoreRecord.Kind) is { } kind
            ? new InvoiceCoreRecord(kind, id)
            : null;

    /// <summary>Field-level errors (ADR-019); empty when the body is well formed. The intake checks the number's content (D-91).</summary>
    internal Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();
        if (CommunicationId == Guid.Empty)
        {
            errors["communicationId"] = ["communicationId is required."];
        }

        if (DocumentId == Guid.Empty)
        {
            errors["documentId"] = ["documentId is required."];
        }

        if (ToCoreRecord() is null)
        {
            errors["coreRecord"] = ["coreRecord {kind: matter | project | workassignment, id} is required."];
        }

        if (FirmId == Guid.Empty)
        {
            errors["firmId"] = ["firmId is required."];
        }

        // Fix round (validation order): D-91's required number is a 400 here, before the filter's rights queries and step 1's reads. The
        // intake checks it again (length, content) and stays the rule's owner.
        if (string.IsNullOrWhiteSpace(FirmInvoiceNumber))
        {
            errors["firmInvoiceNumber"] = ["firmInvoiceNumber is required (D-91)."];
        }

        // Task 097b: the from-email route's mode rules (InvoiceSubmissionFromEmailHttp.Validate), with an absent mode read as New.
        switch (Mode ?? FromEmailModes.New)
        {
            case FromEmailModes.New when InvoiceId is not null:
                errors["invoiceId"] = ["invoiceId is not sent for mode New."];
                break;
            case FromEmailModes.Correction when InvoiceId is not { } invoiceId || invoiceId == Guid.Empty:
                errors["invoiceId"] = ["invoiceId is required for mode Correction."];
                break;
            case FromEmailModes.New or FromEmailModes.Correction:
                break;
            default:
                errors["mode"] = ["mode must be New or Correction."];
                break;
        }

        // Task 118b: the supporting documents' shape, with the body, so a malformed list is a 400 before any rights query or read (and so
        // before anything is staged). Repeats are not an error; the count is of the distinct ids plus the invoice document.
        if (SupportingDocumentIds is { Count: > 0 } supporting)
        {
            if (supporting.Any(id => id == Guid.Empty))
            {
                errors["supportingDocumentIds"] = ["supportingDocumentIds cannot contain an empty id."];
            }
            else if (DocumentId != Guid.Empty && supporting.Contains(DocumentId))
            {
                errors["supportingDocumentIds"] = ["supportingDocumentIds cannot contain the invoice document (documentId)."];
            }
            else if (SupportingDocuments.Count + 1 > SubmissionCommitGate.MaxFiles)
            {
                errors["supportingDocumentIds"] =
                [
                    string.Create(CultureInfo.InvariantCulture,
                        $"An invoice holds at most {SubmissionCommitGate.MaxFiles} files: the invoice document and at most {SubmissionCommitGate.MaxFiles - 1} supporting documents."),
                ];
            }
        }

        return errors;
    }
}

/// <summary>Task 094: the core record a Submit Invoice is for: <c>kind</c> is <c>matter</c>, <c>project</c> or <c>workassignment</c>.</summary>
public sealed record SubmitInvoiceCoreRecord
{
    public string? Kind { get; init; }

    public Guid Id { get; init; }

    internal static InvoiceCoreRecordKind? KindOf(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "matter" => InvoiceCoreRecordKind.Matter,
        "project" => InvoiceCoreRecordKind.Project,
        "workassignment" => InvoiceCoreRecordKind.WorkAssignment,
        _ => null,
    };
}

/// <summary>The 202 body of a received submission (tasks 051 / 052 rely on these names).</summary>
/// <param name="SubmissionId">The <c>sprk_invoicesubmission</c> id; also the processing job's idempotency key.</param>
/// <param name="JobId">The enqueued processing job.</param>
/// <param name="TrackingNumber">The firm-scoped tracking number (D-87); never null (D-91).</param>
/// <param name="StatusUrl">The status route of this submission on the channel it arrived through.</param>
/// <param name="Warning">
/// ebilling-r1 082c-3 (brief 082c section 7, R4 item 4): the PROVISIONAL billing-terms warning of the external (SPA) route's pre-check
/// (<see cref="SubmissionHoldWarning.ForPrecheck"/>): today no billing terms are set up for the record. It is one fixed constant (the status
/// view's gap code and the approved sentence), never a name, a date, a count or a rule code. Null, and then absent from the JSON, on every other
/// answer, so a 202 without it is byte-for-byte what it was; the terminal status view's own warning (or none) replaces it.
/// </param>
public sealed record InvoiceSubmissionAccepted(
    Guid SubmissionId, Guid JobId, string TrackingNumber, string StatusUrl,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    SubmissionHoldWarning? Warning = null);

/// <summary>
/// Task 095a (contract section 11): the 200 of <c>GET /api/finance/invoice-submissions?internetMessageId=...</c>: the fields of
/// <see cref="InvoiceSubmissionView"/> as <c>GET /invoice-submissions/{id}</c> serializes it (same names, same omissions; pinned by a test),
/// plus <c>invoiceId</c> (the status route keeps it server-side; here the pane needs it, and only a caller who can read the submission gets
/// it), <c>submittedOn</c>, <c>submittedBy</c> and <c>isCorrection</c>.
/// </summary>
public sealed record InvoiceSubmissionByEmailView
{
    public Guid SubmissionId { get; init; }

    public string? TrackingNumber { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    public string? PoNumber { get; init; }

    public string? Status { get; init; }

    public string? Channel { get; init; }

    public DateTime? ReceivedOn { get; init; }

    public string? OutcomeReasonCode { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? DocumentId { get; init; }

    public Guid? FirmId { get; init; }

    public InvoiceCoreRecord? CoreRecord { get; init; }

    public DateTime? CreatedOn { get; init; }

    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public SubmissionReplacement? ReplacedBy { get; init; }

    /// <summary>The resulting invoice, when the attempt has one (an Accepted attempt); absent otherwise.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Guid? InvoiceId { get; init; }

    /// <summary>When the attempt was created (<c>createdon</c>), UTC.</summary>
    public DateTime? SubmittedOn { get; init; }

    /// <summary>The person who submitted (display name only); null when no person is recorded. Never the service identity.</summary>
    public SubmittedByPerson? SubmittedBy { get; init; }

    /// <summary>True when the attempt corrects an existing invoice (it names the earlier submission it replaces).</summary>
    public bool IsCorrection { get; init; }

    internal static InvoiceSubmissionByEmailView From(InvoiceSubmissionView view, string? submittedByName) => new()
    {
        SubmissionId = view.SubmissionId,
        TrackingNumber = view.TrackingNumber,
        FirmInvoiceNumber = view.FirmInvoiceNumber,
        PoNumber = view.PoNumber,
        Status = view.Status,
        Channel = view.Channel,
        ReceivedOn = view.ReceivedOn,
        OutcomeReasonCode = view.OutcomeReasonCode,
        DocumentId = view.DocumentId,
        FirmId = view.FirmId,
        CoreRecord = view.CoreRecord,
        CreatedOn = view.CreatedOn,
        ReplacedBy = view.ReplacedBy,
        InvoiceId = view.InvoiceId,
        SubmittedOn = view.CreatedOn is { } created ? DateTime.SpecifyKind(created, DateTimeKind.Utc) : null,
        SubmittedBy = submittedByName is null ? null : new SubmittedByPerson(submittedByName),
        IsCorrection = view.ResubmissionOfId is not null,
    };
}

/// <summary>The submitter of an attempt as the by-email route names them: a display name only (no id, no address).</summary>
public sealed record SubmittedByPerson(string Name);
