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
                "{communicationId, documentId, coreRecord: {kind: matter|project|workassignment, id}, firmId, firmInvoiceNumber, poNumber?}. " +
                "The caller needs Read on the document and its communication, Read and AppendTo on the core record and AppendTo on the firm; " +
                "after every read-only check passes, a communication the D-140 chain reads as unfiled is filed " +
                "to the core record first, as the caller (Write on it). 202 {submissionId, jobId, trackingNumber, statusUrl}; a retry for " +
                "the same document returns the same submission. Refusals are ProblemDetails with errorCode: 400 validation; 403 " +
                "source_forbidden, COMMUNICATION_REFILE_FORBIDDEN, " +
                "the core record and firm checks; 404 document_not_found; 409 document_core_record_mismatch, document_already_invoiced, " +
                "document_rejected, document_already_submitted, submission_already_ended; 422 source_communication_mismatch, " +
                "document_has_no_file; 503 record_unavailable.")
            .Accepts<SubmitInvoiceFromDocumentRequest>("application/json")
            .Produces<InvoiceSubmissionAccepted>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
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

    /// <summary>What step 1 decided: a refusal (the route's answer, nothing written), or proceed with step A (null: nothing to file).</summary>
    internal sealed record SourcePreparation(IResult? Refusal, SourceFiling? Filing)
    {
        internal static SourcePreparation Refused(IResult refusal) => new(refusal, null);

        internal static SourcePreparation Proceed(SourceFiling? filing) => new(null, filing);

        /// <summary>The intake's hook (<see cref="InvoiceSubmissionRequest.FileSourceBeforeWrite"/>), or null when there is nothing to file.</summary>
        internal Func<CancellationToken, Task<bool>>? Hook => Filing is { } filing ? filing.RunAsync : null;
    }

    /// <summary>
    /// Step A of Submit Invoice (procedure-and-file-model 9.1), run by the intake after its read-only refusals (fix round F1-b). It decides
    /// "unfiled" from the D-140 chain (<see cref="InvoiceSubmissionService.IsCommunicationFiledAsync"/>: the communication's own filing
    /// columns and every parent its record thread files it under) and files only a communication the chain leaves empty: a filed one is left
    /// as it is (the intake has already judged the filing the chain gives). Before it files, the communication must be under no secure root
    /// and not owned by the Secure Record owner team (<see cref="ExistingDocumentIsolationCheck.CheckCommunicationForFilingAsync"/>: else 409
    /// <see cref="InvoiceSubmissionErrors.DocumentCoreRecordMismatch"/>, a fault 503), and the caller must have Write on it (else the
    /// communication re-file route's own 403 <c>COMMUNICATION_REFILE_FORBIDDEN</c>). Then the communication re-file core
    /// (<see cref="CommunicationEndpoints.RefileCommunicationAsync"/>) files it under the core record AS THE CALLER, with its own checks
    /// (AppendTo on the record, the owner, the thread following the filing); any answer but 204 is the route's answer. Every refusal throws
    /// <see cref="SourceFilingRefusedException"/> (or the 403's <see cref="SdapProblemException"/>), so the intake writes nothing after it.
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
                // The communication re-file route's own refusal, with its words (no existence oracle).
                throw new SdapProblemException(
                    code: CommunicationRecordAuthorizationFilter.RefileDenyCode,
                    title: "Forbidden",
                    detail: "The communication does not exist or is not visible to the caller, or the caller may not change it.",
                    statusCode: StatusCodes.Status403Forbidden);
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

    private static IResult SourceForbidden() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status403Forbidden, InvoiceSubmissionErrors.SourceForbidden,
            "The document or its communication does not exist or is not visible to the caller. Nothing was saved.");

    private static IResult Unavailable() =>
        InvoiceSubmissionHttp.Refused(StatusCodes.Status503ServiceUnavailable, InvoiceSubmissionErrors.CoreRecordUnavailable,
            "The document could not be checked right now. Nothing was saved; try again shortly.");

    private static bool IsNotFound(Exception ex)
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
        var prepared = await PrepareSourceAsync(request.DocumentId, request.CommunicationId, core, http, documentReadChecked: false, ct);
        if (prepared.Refusal is { } refused)
        {
            return refused;
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

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, InvoiceSubmissionService service, CancellationToken ct)
    {
        try
        {
            var view = await service.GetAsync(id, ct);
            return view is null ? FinanceAuthorizationFilter.UniformRecordNotFound(http) : Results.Ok(view);
        }
        catch (SdapProblemException ex)
        {
            return InvoiceSubmissionHttp.ReadFault(ex);
        }
    }
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
public sealed record InvoiceSubmissionAccepted(Guid SubmissionId, Guid JobId, string TrackingNumber, string StatusUrl);
