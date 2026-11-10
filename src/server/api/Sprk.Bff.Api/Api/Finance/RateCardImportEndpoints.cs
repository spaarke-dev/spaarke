using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Files;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Rate card and timekeeper import and publish (ebilling-r1 task 078, FR-29 / FR-08), ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Admin-only, by Dataverse table privilege (D-76).</b> The "billing admin" is whoever holds the table privileges for what the
/// route does, asked of Dataverse AS THE CALLER (<see cref="FinanceAuthorizationCheck.CallerPrivilege"/>, fail closed): the template needs
/// Create on <c>sprk_ratecard</c>; the import (which the app then writes on the caller's behalf, owner decision G5) needs Create on the
/// card, its rates and the timekeeper, and the caller's own Write on the Procedure the version belongs to (<see cref="RequireWriteOnTheVersionsProcedure"/>);
/// the publish needs Write on the card and on the timekeeper, and the caller's own Write on the card's Procedure (<see cref="RequireWriteOnTheCardsProcedure"/>). No role-name check, no app role. Each route carries its OWN authorization filter, then the eBilling flag filter
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization.</para>
/// <para><b>Import inputs (085, EB-D-148).</b> The workbook is the multipart <c>file</c> part. The card name and the Procedure Version are QUERY
/// parameters (<c>name</c>, <c>procedureVersionId</c>), so the authorization filter and the handler read the same source (the Finance doctrine,
/// task 130 defect C8). The card is created under that version, which must be a Draft of a Rate card Procedure; the firm comes from the Procedure
/// (<c>firmId</c> is optional and, when given, must be that firm). <c>matterId</c> / <c>projectId</c> / <c>workAssignmentId</c> are no longer
/// accepted: a card belongs to a Procedure Version, not to a core record (D-120).</para>
/// </remarks>
public static class RateCardImportEndpoints
{
    internal const string Route = "/api/finance/ratecards";

    // Live privilege names (spaarkedev1 `privileges`, 2026-10-16). sprk_ratecard / sprk_ratecardrate are lower case; sprk_Timekeeper is not.
    internal const string CreateRateCardPrivilege = "prvCreatesprk_ratecard";
    internal const string WriteRateCardPrivilege = "prvWritesprk_ratecard";
    internal const string CreateRatePrivilege = "prvCreatesprk_ratecardrate";
    internal const string CreateTimekeeperPrivilege = "prvCreatesprk_Timekeeper";
    internal const string WriteTimekeeperPrivilege = "prvWritesprk_Timekeeper";

    internal const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    internal const string CodeFileTooLarge = "ratecard.file_too_large";

    /// <summary>The query keys of the retired core-record scope; a request that still names one is refused, not ignored.</summary>
    private static readonly string[] RetiredCoreKeys = ["matterId", "projectId", "workAssignmentId"];

    public static IEndpointRouteBuilder MapRateCardImportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099)
            .WithTags("Finance");

        // GET /api/finance/ratecards/import-template — the workbook template
        group.MapGet("/import-template", GetTemplate)
            .AddFinanceAuthorizationFilter(_ => FinanceAuthorizationTargets.Authorize(Privilege(CreateRateCardPrivilege, "sprk_ratecards")))
            .RequireEbillingEnabled()
            .WithName("GetRateCardImportTemplate")
            .WithSummary("Download the rate card and timekeeper import template (.xlsx)")
            .Produces(StatusCodes.Status200OK, contentType: XlsxContentType)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/ratecards/import — validate, store the workbook, create the Draft card
        group.MapPost("/import", ImportRateCard)
            .AddFinanceAuthorizationFilter(ResolveImportTargets)
            .RequireEbillingEnabled()
            .RequireWriteOnTheVersionsProcedure() // 085 (I-P5): and Write on the Procedure the version belongs to
            .DisableAntiforgery() // multipart upload; auth is the JWT + the caller's table privileges, not a form token
            .Accepts<IFormFile>("multipart/form-data")
            // 116 fix round 1: the body cap of the submission routes (the configurable ceiling plus the envelope), above Kestrel's 30 MB default.
            .WithMetadata(new RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes))
            .WithName("ImportRateCard")
            .WithSummary("Import a rate card and timekeepers from a workbook as a Draft")
            .WithDescription(
                "Validates every row and returns all invalid rows (sheet, row, column, message) with nothing written; otherwise creates a " +
                "Draft rate card with its rates and Pending timekeepers and stores the workbook as the Procedure Version's source file. " +
                "A refused file write removes the rows and answers as the version file route does (403, 409, 503).")
            .Produces<RateCardImportResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/ratecards/{rateCardId}/publish — Draft to Published
        group.MapPost("/{rateCardId:guid}/publish", PublishRateCard)
            .AddFinanceAuthorizationFilter(_ => FinanceAuthorizationTargets.Authorize(
                Privilege(WriteRateCardPrivilege, "sprk_ratecards"),
                Privilege(WriteTimekeeperPrivilege, "sprk_timekeepers")))
            .RequireEbillingEnabled()
            .RequireWriteOnTheCardsProcedure() // publish also asks Write on the card's Procedure
            .WithName("PublishRateCard")
            .WithSummary("Publish a Draft rate card")
            .WithDescription(
                "Re-checks that no effective-date ranges overlap, approves the card's Pending timekeepers and moves the card to Published. " +
                "Also asks the caller's Write on the Procedure the card's version belongs to.")
            .Produces<RateCardPublishResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static FinanceAuthorizationCheck Privilege(string privilege, string entitySet) =>
        FinanceAuthorizationCheck.CallerPrivilege(privilege, entitySet, "rate card admin");

    // ═══════════════════════════════════════════════════════════════════════════
    // Authorization declaration — reads the SAME query values the handler binds
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The table privileges the import needs. The caller's Write on the Procedure is asked next, by <see cref="RequireWriteOnTheVersionsProcedure"/>
    /// (the Procedure is the version's, read server-side). A malformed value adds no check (the handler returns the 400), so a caller without the
    /// privileges is always answered 403 first and never learns what is valid.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveImportTargets(EndpointFilterInvocationContext context) =>
        FinanceAuthorizationTargets.Authorize(
            Privilege(CreateRateCardPrivilege, "sprk_ratecards"),
            Privilege(CreateRatePrivilege, "sprk_ratecardrates"),
            Privilege(CreateTimekeeperPrivilege, "sprk_timekeepers"));

    /// <summary>
    /// 085 (I-P5): the import also needs the caller's own Write on the Procedure the named version belongs to: the Record-path "write" check on
    /// <c>sprk_procedures</c> the Procedure routes declare, asked of Dataverse as the caller (<see cref="CallerReadCheck"/>, which keeps "denied" apart
    /// from "no answer"). The Procedure is read server-side from the stored version. Added AFTER the route's <c>AddFinanceAuthorizationFilter</c>
    /// (step 0 and the table privileges run first). An unreadable or unknown version is passed on: the service answers 400
    /// <c>ratecard.procedure_version_invalid</c>. A fault reading the version is a 403 <c>system_failure</c> (fail closed).
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — <c>BillingAdminAuthorization.RequireWriteOnTheRuleSetsProcedure</c> asks this question from a rule set's id.
    /// <i>Extension</i> — it is keyed to the <c>ruleSetId</c> route value and the rule set service; this route carries a version id in the query, so
    /// the same body is repeated here for that source (no new service). <i>Cost of doing nothing</i> — a rate card admin without Write on a Procedure
    /// could attach a card to it, which sets the rate its firm's invoices are judged against.</remarks>
    internal static TBuilder RequireWriteOnTheVersionsProcedure<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            if (!Guid.TryParse(http.Request.Query["procedureVersionId"].ToString(), out var versionId) || versionId == Guid.Empty)
            {
                return await next(context); // the handler answers the 400
            }

            var read = await ReadVersionsProcedureAsync(http, versionId);
            if (read.Faulted)
            {
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (read.ProcedureId is not { } id || id == Guid.Empty)
            {
                return await next(context);
            }

            return await CheckCallerWriteOnProcedureAsync(http, id, $"Rate card import: caller write on the Procedure of version {versionId}")
                ?? await next(context);
        });

    /// <summary>The <see cref="HttpContext.Items"/> key under which <see cref="RequireWriteOnTheCardsProcedure"/> leaves the version id it checked.</summary>
    internal const string CheckedVersionItem = "ratecard.checkedProcedureVersionId";

    /// <summary>
    /// The publish also asks the caller's own Write on the Procedure the card's version belongs to (the same question as
    /// <see cref="RequireWriteOnTheVersionsProcedure"/>, through the same <see cref="CheckCallerWriteOnProcedureAsync"/>). The card, its version and
    /// the Procedure are read app-only. A fault, an unknown card, a card with no version and a version with no Procedure all end in a 403, so the
    /// answer does not say whether a card exists. The version id checked is left in <see cref="HttpContext.Items"/>; the publish passes it to the
    /// service, which refuses 409 <c>ratecard.version_changed</c> if the card then names a different version. Added AFTER the route's
    /// <c>AddFinanceAuthorizationFilter</c> and <c>RequireEbillingEnabled</c>, like the import's.
    /// </summary>
    /// <remarks>§11: <i>Existing</i> — the import's <see cref="RequireWriteOnTheVersionsProcedure"/>. <i>Extension</i> — it is keyed to a query value
    /// and passes an unknown version on; here the key is the card (a route value), the version must be found, and the version checked is carried
    /// to the write, so the body is shared and only the lookup differs. <i>Cost of doing nothing</i> — a caller with the table privileges but not
    /// Write on the Procedure could publish a card, which puts its rates in force for the firm's invoices.</remarks>
    internal static TBuilder RequireWriteOnTheCardsProcedure<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var http = context.HttpContext;
            var denied = ProblemDetailsHelper.Forbidden(
                FinanceAuthorizationFilter.InsufficientRightsReasonCode, detail: "You need Write on this Procedure.", traceId: http.TraceIdentifier);
            var failed = ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);

            if (!Guid.TryParse(http.Request.RouteValues["rateCardId"]?.ToString(), out var cardId) || cardId == Guid.Empty)
            {
                return denied;
            }

            Guid? versionId;
            try
            {
                var cards = await http.RequestServices.GetRequiredService<IGenericEntityService>().RetrieveMultipleAsync(new QueryExpression(RateCardImportService.RateCardEntity)
                {
                    ColumnSet = new ColumnSet("sprk_procedureversion"),
                    Criteria = { Conditions = { new ConditionExpression(RateCardImportService.RateCardEntity + "id", ConditionOperator.Equal, cardId) } },
                    TopCount = 1,
                }, http.RequestAborted);
                versionId = cards.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>("sprk_procedureversion")?.Id;
            }
            catch (Exception ex)
            {
                http.RequestServices.GetService<ILogger<FinanceAuthorizationFilter>>()?.LogError(ex,
                    "Rate card {RateCardId}: its Procedure Version could not be read for the Procedure write check; denying (fail closed)", cardId);
                return failed;
            }

            if (versionId is not { } checkedVersion || checkedVersion == Guid.Empty)
            {
                return denied; // unknown card, or a card with no version
            }

            var read = await ReadVersionsProcedureAsync(http, checkedVersion);
            if (read.Faulted)
            {
                return failed;
            }

            if (read.ProcedureId is not { } procedureId || procedureId == Guid.Empty)
            {
                return denied; // an unknown version, or a version with no Procedure
            }

            var refusal = await CheckCallerWriteOnProcedureAsync(http, procedureId, $"Rate card publish: caller write on the Procedure of card {cardId}");
            if (refusal is not null)
            {
                return refusal;
            }

            http.Items[CheckedVersionItem] = checkedVersion;
            return await next(context);
        });

    /// <summary>Reads a Procedure Version's Procedure app-only. <c>Faulted</c> is true when the read threw (logged; the caller fails closed).</summary>
    private static async Task<(Guid? ProcedureId, bool Faulted)> ReadVersionsProcedureAsync(HttpContext http, Guid versionId)
    {
        var services = http.RequestServices;
        try
        {
            var rows = await services.GetRequiredService<IGenericEntityService>().RetrieveMultipleAsync(new QueryExpression(ProcedureEntity.VersionEntity)
            {
                ColumnSet = new ColumnSet(ProcedureEntity.VersionProcedureColumn),
                Criteria = { Conditions = { new ConditionExpression(ProcedureEntity.VersionEntity + "id", ConditionOperator.Equal, versionId) } },
                TopCount = 1,
            }, http.RequestAborted);
            return (rows.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id, false);
        }
        catch (Exception ex)
        {
            services.GetService<ILogger<FinanceAuthorizationFilter>>()?.LogError(ex,
                "Procedure Version {VersionId}: its Procedure could not be read for the Procedure write check; denying (fail closed)", versionId);
            return (null, true);
        }
    }

    /// <summary>
    /// Asks the caller's own Write on a Procedure (<see cref="CallerReadCheck.CheckAsync"/>). Null when the caller holds it; otherwise the 403:
    /// <c>insufficient_rights</c> when Dataverse said no, <c>system_failure</c> when there was no answer.
    /// </summary>
    private static async Task<IResult?> CheckCallerWriteOnProcedureAsync(HttpContext http, Guid procedureId, string logContext)
    {
        var services = http.RequestServices;
        var caller = new InvoiceContextService.InvoiceCaller(
            CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
        var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
        var outcome = await CallerReadCheck.CheckAsync(
            services.GetRequiredService<AuthorizationService>(), caller, BillingAdminAuthorization.ProcedureEntitySet, "write", procedureId, logger,
            logContext, http.RequestAborted);
        if (outcome == CallerReadOutcome.Readable)
        {
            return null;
        }

        return outcome == CallerReadOutcome.Refused
            ? ProblemDetailsHelper.Forbidden(
                FinanceAuthorizationFilter.InsufficientRightsReasonCode,
                detail: "You need Write on this Procedure.",
                traceId: http.TraceIdentifier)
            : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static IResult GetTemplate(RateCardImportService service) =>
        Results.File(service.BuildTemplate(), XlsxContentType, "rate-card-import-template.xlsx");

    private static async Task<IResult> ImportRateCard(
        HttpContext http,
        RateCardImportService service,
        IOptionsMonitor<EbillingOptions> ebilling,
        CancellationToken ct)
    {
        var request = http.Request;
        var query = request.Query;

        var problems = new List<RateCardImportError>();
        if (!Guid.TryParse(query["procedureVersionId"].ToString(), out var procedureVersionId) || procedureVersionId == Guid.Empty)
        {
            problems.Add(new RateCardImportError(null, null, "procedureVersionId", "procedureVersionId is required and must be a GUID."));
        }

        // firmId is optional (the Procedure names the firm); a value that is present must be a GUID, and the service refuses a different firm.
        Guid? firmId = null;
        var rawFirm = query["firmId"].ToString();
        if (rawFirm.Length > 0)
        {
            if (Guid.TryParse(rawFirm, out var parsedFirm) && parsedFirm != Guid.Empty)
            {
                firmId = parsedFirm;
            }
            else
            {
                problems.Add(new RateCardImportError(null, null, "firmId", "firmId must be a GUID."));
            }
        }

        var name = query["name"].ToString().Trim();
        if (name.Length == 0 || name.Length > 200)
        {
            problems.Add(new RateCardImportError(null, null, "name", "name is required and may be at most 200 characters."));
        }

        foreach (var key in RetiredCoreKeys)
        {
            if (query[key].ToString().Length > 0)
            {
                problems.Add(new RateCardImportError(null, null, key, $"{key} is no longer accepted: a rate card belongs to a Procedure Version (procedureVersionId)."));
            }
        }

        if (problems.Count > 0)
        {
            return InvalidProblem(problems);
        }

        var maxBytes = (long)ebilling.CurrentValue.MaxSubmissionFileSizeMb * 1024 * 1024;
        if (request.ContentLength > maxBytes + (1024 * 1024)) // the multipart envelope adds a little; the file itself is checked below
        {
            return FileTooLarge(ebilling.CurrentValue.MaxSubmissionFileSizeMb);
        }

        // 116 fix round 1: a chunked body has no Content-Length; the server stops reading it at the configured limit (413 below).
        if (http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } cap)
        {
            cap.MaxRequestBodySize = maxBytes + (1024 * 1024);
        }

        // A malformed multipart body (e.g. a section without Content-Disposition) is a CLIENT error: 400, not the parser's exception as a
        // 500 (as ChatDocumentEndpoints answers). The parser's message can echo request content, so it is not returned. Cancellation is
        // deliberately not caught here.
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // The server's body cap (a chunked body has no Content-Length to check above).
            return FileTooLarge(ebilling.CurrentValue.MaxSubmissionFileSizeMb);
        }
        catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
        {
            return InvalidProblem(new[] { new RateCardImportError(null, null, "file", "The request body is not valid multipart/form-data.") });
        }

        var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        if (file is null || file.Length == 0)
        {
            return InvalidProblem(new[] { new RateCardImportError(null, null, "file", "The workbook is missing or empty.") });
        }

        if (file.Length > maxBytes)
        {
            return FileTooLarge(ebilling.CurrentValue.MaxSubmissionFileSizeMb);
        }

        byte[] bytes;
        await using (var stream = file.OpenReadStream())
        using (var buffer = new MemoryStream())
        {
            await stream.CopyToAsync(buffer, ct);
            bytes = buffer.ToArray();
        }

        // The oid claim; Guid.TryParse yields null for a caller the filter would already have refused.
        Guid? callerOid = Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;

        var outcome = await service.ImportAsync(
            new RateCardImportRequest(firmId, name, procedureVersionId, callerOid, http.User, TokenHelper.ExtractBearerTokenOrNull(http)), bytes, ct);

        return outcome.Kind switch
        {
            RateCardOutcomeKind.Done => Results.Created($"{Route}/{outcome.Value!.RateCardId}", outcome.Value),
            RateCardOutcomeKind.Invalid => InvalidProblem(outcome.Errors),
            RateCardOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "rate card", http.TraceIdentifier),
            RateCardOutcomeKind.FileRefused => FileRefused(outcome.FileRefusal!, http),
            _ => Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail!),
        };
    }

    /// <summary>
    /// Task 116: the file store refused the workbook (the rows were removed). Mapped as <c>ProcedureEndpoints.PutVersionFile</c> maps a refused
    /// caller write: step 0 is a 403 <c>system_failure</c>, no Write on the version a 403 <c>insufficient_rights</c>, no owner the 409 owner code,
    /// a store that is not configured 503, a request the store does not accept 400, anything else 500 <c>ratecard.import_failed</c>.
    /// </summary>
    private static IResult FileRefused(StoredFileCreateResult refusal, HttpContext http) => refusal.RefusalCode switch
    {
        StoredFileRefusal.CallerNotInternal => ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier),
        StoredFileRefusal.NoWriteOnParent => ProblemDetailsHelper.Forbidden(
            FinanceAuthorizationFilter.InsufficientRightsReasonCode, detail: "You need Write on this Procedure Version.", traceId: http.TraceIdentifier),
        StoredFileRefusal.OwnerUnresolved => Results.Problem(
            statusCode: StatusCodes.Status409Conflict, detail: "The workbook could not be given an owner; nothing was imported.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = refusal.OwnerRefusalCode ?? StoredFileRefusal.OwnerUnresolved }),
        StoredFileRefusal.NotConfigured => Refused(
            StatusCodes.Status503ServiceUnavailable, StoredFileRefusal.NotConfigured, "The file store is not configured in this environment."),
        StoredFileRefusal.InvalidRequest => InvalidProblem(new[] { new RateCardImportError(null, null, "file", "The workbook was not accepted by the file store.") }),
        StoredFileRefusal.ParentNoLongerAccepts => Refused(
            StatusCodes.Status409Conflict, RuleSetService.CodeVersionNotDraft, "The Procedure Version is no longer a Draft; nothing was imported."),
        _ => Refused(
            StatusCodes.Status500InternalServerError, RateCardImportService.CodeImportFailed,
            "The rate card could not be imported and nothing was saved. Try again; if it keeps failing, contact support."),
    };

    internal static async Task<IResult> PublishRateCard(Guid rateCardId, HttpContext http, RateCardImportService service, CancellationToken ct)
    {
        // The version the filter checked; the service refuses 409 if the card names a different one now. No record of the check is a fault.
        if (!http.Items.TryGetValue(CheckedVersionItem, out var checkedItem) || checkedItem is not Guid checkedVersion)
        {
            return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        }

        var outcome = await service.PublishAsync(rateCardId, checkedVersion, ct);
        return outcome.Kind switch
        {
            RateCardOutcomeKind.Done => Results.Ok(outcome.Value),
            RateCardOutcomeKind.Invalid => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Rate card cannot be published",
                detail: "The rate card's rates are not valid; nothing was published.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = RateCardImportService.CodeOverlap,
                    ["errors"] = ToWire(outcome.Errors),
                }),
            _ => Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail!),
        };
    }

    // ── Responses (ADR-019: ProblemDetails with a stable errorCode) ──

    private static IResult InvalidProblem(IReadOnlyList<RateCardImportError> errors) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Rate card import is not valid",
            detail: $"{errors.Count} problem(s) found; nothing was imported. Fix them and upload again.",
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = RateCardImportService.CodeInvalid,
                ["errors"] = ToWire(errors),
            });

    private static IResult FileTooLarge(int maxMb) =>
        Results.Problem(
            statusCode: StatusCodes.Status413PayloadTooLarge, title: "File too large",
            detail: $"The workbook may be at most {maxMb} MB.",
            extensions: new Dictionary<string, object?> { ["errorCode"] = CodeFileTooLarge });

    private static IResult Refused(int statusCode, string errorCode, string detail) =>
        Results.Problem(
            statusCode: statusCode, detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = errorCode });

    private static object[] ToWire(IReadOnlyList<RateCardImportError> errors) =>
        errors.Select(e => (object)new { sheet = e.Sheet, row = e.Row, column = e.Column, message = e.Message }).ToArray();
}
