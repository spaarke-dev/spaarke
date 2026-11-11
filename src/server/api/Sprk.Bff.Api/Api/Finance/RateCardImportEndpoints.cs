using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>
/// Rate card and timekeeper import and publish (ebilling-r1 task 078, FR-29 / FR-08), ADR-001 (Minimal API) and ADR-008 (endpoint filters).
/// </summary>
/// <remarks>
/// <para><b>Admin-only, by Dataverse table privilege (D-76).</b> The "billing admin" is whoever holds the table privileges for what the
/// route does, asked of Dataverse AS THE CALLER (<see cref="FinanceAuthorizationCheck.CallerPrivilege"/>, fail closed): the template needs
/// Create on <c>sprk_ratecard</c>; the import (which the app then writes on the caller's behalf, owner decision G5) needs Create on the
/// card, its rates and the timekeeper, and Read on the core record when one is named; the publish needs Write on the card and on the
/// timekeeper. No role-name check, no app role. Each route carries its OWN authorization filter, then the eBilling flag filter
/// (<c>RequireEbillingEnabled</c>, ADR-018): the flag never replaces authorization.</para>
/// <para><b>Import inputs.</b> The workbook is the multipart <c>file</c> part. The firm, card name and optional core record are QUERY
/// parameters (<c>firmId</c>, <c>name</c>, at most one of <c>matterId</c> / <c>projectId</c> / <c>workAssignmentId</c>), so the
/// authorization filter and the handler read the same source (the Finance doctrine, task 130 defect C8).</para>
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

    private const string SetMatters = "sprk_matters";
    private const string SetProjects = "sprk_projects";
    private const string SetWorkAssignments = "sprk_workassignments";

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
    /// The table privileges the import needs, then Read on each core record id the query names. Lenient on purpose: a malformed value adds
    /// no check (the handler returns the 400), so a caller without the privileges is always answered 403 first and never learns what is valid.
    /// </summary>
    internal static FinanceAuthorizationTargets ResolveImportTargets(EndpointFilterInvocationContext context)
    {
        var query = context.HttpContext.Request.Query;
        var checks = new List<FinanceAuthorizationCheck>
        {
            Privilege(CreateRateCardPrivilege, "sprk_ratecards"),
            Privilege(CreateRatePrivilege, "sprk_ratecardrates"),
            Privilege(CreateTimekeeperPrivilege, "sprk_timekeepers"),
        };

        foreach (var (key, set) in new[] { ("matterId", SetMatters), ("projectId", SetProjects), ("workAssignmentId", SetWorkAssignments) })
        {
            if (Guid.TryParse(query[key].ToString(), out var id) && id != Guid.Empty)
            {
                checks.Add(new FinanceAuthorizationCheck
                {
                    Path = FinanceCheckPath.Record,
                    EntitySetName = set,
                    RecordId = id,
                    Operation = "finance.read",
                    Source = "query." + key,
                });
            }
        }

        return FinanceAuthorizationTargets.Authorize(checks.ToArray());
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
        if (!Guid.TryParse(query["firmId"].ToString(), out var firmId) || firmId == Guid.Empty)
        {
            problems.Add(new RateCardImportError(null, null, "firmId", "firmId is required and must be a GUID."));
        }

        var name = query["name"].ToString().Trim();
        if (name.Length == 0 || name.Length > 200)
        {
            problems.Add(new RateCardImportError(null, null, "name", "name is required and may be at most 200 characters."));
        }

        var core = new List<RateCardCoreRecord>();
        foreach (var (key, entity) in new[] { ("matterId", "sprk_matter"), ("projectId", "sprk_project"), ("workAssignmentId", "sprk_workassignment") })
        {
            var raw = query[key].ToString();
            if (raw.Length == 0)
            {
                continue;
            }

            if (Guid.TryParse(raw, out var id) && id != Guid.Empty)
            {
                core.Add(new RateCardCoreRecord(entity, id));
            }
            else
            {
                problems.Add(new RateCardImportError(null, null, key, $"{key} must be a GUID."));
            }
        }

        if (core.Count > 1)
        {
            problems.Add(new RateCardImportError(null, null, "coreRecord", "Name at most one of matterId, projectId and workAssignmentId."));
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
            new RateCardImportRequest(firmId, name, core.Count == 1 ? core[0] : null, callerOid), bytes, ct);

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
