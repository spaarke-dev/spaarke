using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Procedures;

namespace Sprk.Bff.Api.Api.Finance;

/// <summary>Create a version. Both dates are optional on a Draft (half-open: [from, to)).</summary>
public sealed record ProcedureVersionCreateBody(DateOnly? EffectiveFrom, DateOnly? EffectiveTo);

/// <summary>Change a version's dates. A null member is left as it is; <see cref="ClearEffectiveTo"/> makes the version open-ended.</summary>
public sealed record ProcedureVersionDatesBody(DateOnly? EffectiveFrom, DateOnly? EffectiveTo, bool ClearEffectiveTo = false);

/// <summary>Publish a version from this date (else the version's own effective-from).</summary>
public sealed record ProcedureVersionPublishBody(DateOnly? EffectiveFrom);

/// <summary>
/// The Procedure and Procedure Version API (ebilling-r1 task 081, EB-D-148; procedure model 3.1, 5.1, 7.2, 8.2 and 15 answers 1-2), ADR-001
/// (Minimal API), ADR-008 (endpoint filters) and ADR-019 (ProblemDetails with a stable <c>errorCode</c>).
/// </summary>
/// <remarks>
/// <para><b>Authorization, in order.</b> Step 0 on the group (task 099): a flagged-external caller, a caller with no systemuser row and a
/// flag-read fault are refused before any read, reads included (Procedures are internal-only). Then each route's own
/// <c>AddFinanceAuthorizationFilter</c> (<see cref="BillingAdminAuthorization"/>, as the caller, fail closed): Create needs Create on
/// <c>sprk_procedure</c>; a read needs the caller's Read on the Procedure; every other route needs the caller's Write on the Procedure AND
/// the billing-admin marker (I-P5), plus the privilege of the policy table it writes. Then the eBilling flag (<c>RequireEbillingEnabled</c>,
/// ADR-018): the flag never replaces authorization. The writes then run as the BFF application user (I-P2 field security).</para>
/// <para><b>Refusal shapes.</b> 401 unauthenticated; 403 step 0 or <c>sdap.access.deny.*</c>; 503 <c>ebilling.disabled</c>; then, from the
/// service: 400 <c>procedure.invalid</c> / <c>procedure.firm_required</c> / <c>procedure.publish_invalid</c> with <c>errors</c>; 404
/// <c>procedure.not_found</c> / <c>procedure.version_not_found</c>; 409 <c>procedure.scope_locked</c>, <c>procedure.version_overlap</c>,
/// <c>procedure.retired</c>, <c>procedure.draft_exists</c>, <c>procedure.version_not_draft</c>, <c>procedure.version_not_editable</c>,
/// <c>procedure.concurrent_edit</c>, <c>procedure.version_conflict</c>, <c>procedure.ruleset_missing</c>, <c>ruleset.no_draft_version</c> or
/// the owner reason code; 422 <c>procedure.too_many_rules</c>; 500 <c>procedure.write_failed</c>.</para>
/// </remarks>
public static class ProcedureEndpoints
{
    internal const string Route = "/api/finance/procedures";

    public static IEndpointRouteBuilder MapProcedureEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Route)
            .RequireAuthorization()
            .RequireInternalFinanceCaller() // step 0 on every /api/finance route (section 5.3, task 099), reads included
            .WithTags("Finance");

        group.MapPost("/", CreateProcedure)
            .AddFinanceAuthorizationFilter(_ => BillingAdminAuthorization.CreateProcedure())
            .RequireEbillingEnabled()
            .WithName("CreateProcedure")
            .WithSummary("Create a Procedure (Billing area, Active) with its one Draft rule set")
            .Produces<ProcedureView>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{procedureId:guid}", GetProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.ReadProcedure)
            .RequireEbillingEnabled()
            .WithName("GetProcedure")
            .WithSummary("A Procedure with its versions")
            .Produces<ProcedureView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/{procedureId:guid}", UpdateProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("UpdateProcedure")
            .WithSummary("Change a Procedure (Applies to and Firm lock once a version has been in force)")
            .Produces<ProcedureView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions", CreateVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.CreateProcedureVersion)
            .RequireEbillingEnabled()
            .WithName("CreateProcedureVersion")
            .WithSummary("Create the next version as a Draft, copying the rules of the version before it")
            .Produces<ProcedureVersionCreated>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/{procedureId:guid}/versions/{versionId:guid}", ChangeVersionDates)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("ChangeProcedureVersionDates")
            .WithSummary("Change a Draft or Active version's dates (versions in force never overlap)")
            .Produces<ProcedureVersionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions/{versionId:guid}/publish", PublishVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.PublishProcedureVersion)
            .RequireEbillingEnabled()
            .WithName("PublishProcedureVersion")
            .WithSummary("Publish a Draft version, superseding the previous one, in one transaction")
            .Produces<ProcedureVersionPublished>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/versions/{versionId:guid}/retire", RetireVersion)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("RetireProcedureVersion")
            .WithSummary("Retire one version (its rules stay enabled; nothing is closed or re-checked)")
            .Produces<ProcedureVersionView>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{procedureId:guid}/retire-preview", PreviewRetire)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.WriteProcedure)
            .RequireEbillingEnabled()
            .WithName("PreviewRetireProcedure")
            .WithSummary("The retire confirmation: the warning and how many open invoices the caller can see are affected (writes nothing)")
            .Produces<ProcedureRetirePreview>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{procedureId:guid}/retire", RetireProcedure)
            .AddFinanceAuthorizationFilter(BillingAdminAuthorization.RetireProcedure)
            .RequireEbillingEnabled()
            .WithName("RetireProcedure")
            .WithSummary("Retire the Procedure: every rule Off and their open findings closed PolicyRetired")
            .Produces<ProcedureRetired>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Handlers
    // ═══════════════════════════════════════════════════════════════════════════

    private static async Task<IResult> CreateProcedure(ProcedureCreateRequest body, ProcedureService service, HttpContext http, CancellationToken ct)
    {
        var outcome = await service.CreateAsync(body, CallerOid(http), ct);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{Route}/{outcome.Value!.Id}", outcome.Value)
            : Map(outcome, http);
    }

    private static async Task<IResult> GetProcedure(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.GetAsync(procedureId, ct), http);

    private static async Task<IResult> UpdateProcedure(
        Guid procedureId, ProcedureUpdateRequest body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.UpdateAsync(procedureId, body, ct), http);

    private static async Task<IResult> CreateVersion(
        Guid procedureId, ProcedureVersionCreateBody? body, ProcedureService service, HttpContext http, CancellationToken ct)
    {
        var outcome = await service.CreateVersionAsync(procedureId, body?.EffectiveFrom, body?.EffectiveTo, CallerOid(http), ct);
        return outcome.Kind == RuleSetOutcomeKind.Done
            ? Results.Created($"{Route}/{procedureId}", outcome.Value)
            : Map(outcome, http);
    }

    private static async Task<IResult> ChangeVersionDates(
        Guid procedureId, Guid versionId, ProcedureVersionDatesBody body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.ChangeDatesAsync(procedureId, versionId, body.EffectiveFrom, body.EffectiveTo, body.ClearEffectiveTo, ct), http);

    private static async Task<IResult> PublishVersion(
        Guid procedureId, Guid versionId, ProcedureVersionPublishBody? body, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.PublishVersionAsync(procedureId, versionId, body?.EffectiveFrom, ct), http);

    private static async Task<IResult> RetireVersion(Guid procedureId, Guid versionId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.RetireVersionAsync(procedureId, versionId, ct), http);

    private static async Task<IResult> PreviewRetire(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.PreviewRetireAsync(procedureId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    private static async Task<IResult> RetireProcedure(Guid procedureId, ProcedureService service, HttpContext http, CancellationToken ct) =>
        Map(await service.RetireAsync(procedureId, TokenHelper.ExtractBearerTokenOrNull(http), ct), http);

    // ═══════════════════════════════════════════════════════════════════════════
    // Responses (ADR-019: ProblemDetails with a stable errorCode)
    // ═══════════════════════════════════════════════════════════════════════════

    private static IResult Map<T>(RuleSetOutcome<T> outcome, HttpContext http) where T : class =>
        outcome.Kind switch
        {
            RuleSetOutcomeKind.Done => Results.Ok(outcome.Value),
            RuleSetOutcomeKind.Invalid => Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: outcome.ErrorCode == ProcedureService.CodePublishInvalid ? "Procedure version cannot be published" : "Procedure request is not valid",
                detail: $"{outcome.Errors.Count} problem(s) found; nothing was written.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = outcome.ErrorCode,
                    ["errors"] = outcome.Errors
                        .Select(e => (object)new { policyId = e.PolicyId, rule = e.RuleName, field = e.Field, message = e.Message, reason = e.Reason })
                        .ToArray(),
                }),
            RuleSetOutcomeKind.OwnerRefused => ProblemDetailsHelper.RecordOwnerRefused(outcome.OwnerRefusal!, "procedure", http.TraceIdentifier),
            _ => Results.Problem(
                statusCode: outcome.StatusCode, detail: outcome.Detail,
                extensions: new Dictionary<string, object?> { ["errorCode"] = outcome.ErrorCode }),
        };

    private static Guid? CallerOid(HttpContext http) =>
        Guid.TryParse(CallerResolution.ResolveObjectId(http.User), out var oid) ? oid : null;
}
