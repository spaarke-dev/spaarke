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
/// the publish needs Write on the card and on the timekeeper. No role-name check, no app role. Each route carries its OWN authorization filter, then the eBilling flag filter
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
            .WithName("ImportRateCard")
            .WithSummary("Import a rate card and timekeepers from a workbook as a Draft")
            .WithDescription(
                "Validates every row and returns all invalid rows (sheet, row, column, message) with nothing written; otherwise stores " +
                "the workbook in the Procedure Library and creates a Draft rate card with its rates and Pending timekeepers.")
            .Produces<RateCardImportResult>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // POST /api/finance/ratecards/{rateCardId}/publish — Draft to Published
        group.MapPost("/{rateCardId:guid}/publish", PublishRateCard)
            .AddFinanceAuthorizationFilter(_ => FinanceAuthorizationTargets.Authorize(
                Privilege(WriteRateCardPrivilege, "sprk_ratecards"),
                Privilege(WriteTimekeeperPrivilege, "sprk_timekeepers")))
            .RequireEbillingEnabled()
            .WithName("PublishRateCard")
            .WithSummary("Publish a Draft rate card")
            .WithDescription(
                "Re-checks that no effective-date ranges overlap, approves the card's Pending timekeepers and moves the card to Published.")
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
            var services = http.RequestServices;
            if (!Guid.TryParse(http.Request.Query["procedureVersionId"].ToString(), out var versionId) || versionId == Guid.Empty)
            {
                return await next(context); // the handler answers the 400
            }

            Guid? procedureId;
            try
            {
                var rows = await services.GetRequiredService<IGenericEntityService>().RetrieveMultipleAsync(new QueryExpression(ProcedureEntity.VersionEntity)
                {
                    ColumnSet = new ColumnSet(ProcedureEntity.VersionProcedureColumn),
                    Criteria = { Conditions = { new ConditionExpression(ProcedureEntity.VersionEntity + "id", ConditionOperator.Equal, versionId) } },
                    TopCount = 1,
                }, http.RequestAborted);
                procedureId = rows.Entities.FirstOrDefault()?.GetAttributeValue<EntityReference>(ProcedureEntity.VersionProcedureColumn)?.Id;
            }
            catch (Exception ex)
            {
                services.GetService<ILogger<FinanceAuthorizationFilter>>()?.LogError(ex,
                    "Procedure Version {VersionId}: its Procedure could not be read for the Procedure write check; denying (fail closed)", versionId);
                return ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
            }

            if (procedureId is not { } id || id == Guid.Empty)
            {
                return await next(context);
            }

            var caller = new InvoiceContextService.InvoiceCaller(
                CallerResolution.ResolveObjectId(http.User) ?? string.Empty, TokenHelper.ExtractBearerTokenOrNull(http));
            var logger = services.GetService<ILogger<FinanceAuthorizationFilter>>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FinanceAuthorizationFilter>.Instance;
            var outcome = await CallerReadCheck.CheckAsync(
                services.GetRequiredService<AuthorizationService>(), caller, BillingAdminAuthorization.ProcedureEntitySet, "write", id, logger,
                $"Rate card import: caller write on the Procedure of version {versionId}", http.RequestAborted);
            if (outcome == CallerReadOutcome.Readable)
            {
                return await next(context);
            }

            return outcome == CallerReadOutcome.Refused
                ? ProblemDetailsHelper.Forbidden(
                    FinanceAuthorizationFilter.InsufficientRightsReasonCode,
                    detail: "You need Write on this Procedure.",
                    traceId: http.TraceIdentifier)
                : ProblemDetailsHelper.Forbidden(FinanceAuthorizationFilter.SystemFailureReasonCode, traceId: http.TraceIdentifier);
        });

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

        // A malformed multipart body (e.g. a section without Content-Disposition) is a CLIENT error: 400, not the parser's exception as a
        // 500 (as ChatDocumentEndpoints answers). The parser's message can echo request content, so it is not returned. Cancellation is
        // deliberately not caught here.
        IFormCollection form;
        try
        {
            form = await request.ReadFormAsync(ct);
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
            new RateCardImportRequest(firmId, name, procedureVersionId, callerOid), bytes, ct);

        return outcome.Kind switch
        {
            RateCardOutcomeKind.Done => Results.Created($"{Route}/{outcome.Value!.RateCardId}", outcome.Value),
            RateCardOutcomeKind.Invalid => InvalidProblem(outcome.Errors),
            RateCardOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "rate card", http.TraceIdentifier),
            _ => Refused(outcome.StatusCode, outcome.ErrorCode!, outcome.Detail!),
        };
    }

    private static async Task<IResult> PublishRateCard(Guid rateCardId, RateCardImportService service, CancellationToken ct)
    {
        var outcome = await service.PublishAsync(rateCardId, ct);
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
