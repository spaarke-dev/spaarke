// ebilling-r1 task 135 (FR-49, D-66) - the SPA Billing Guidelines view.
//
// GET /api/v1/external/ebilling/billing-guidelines                      the published rules in force today for the caller's firm(s)
// GET /api/v1/external/ebilling/billing-guidelines?coreRecordId={guid}  the same, for one core record the caller can read
//
// Mounted UNDER the shared /api/v1/external group (ExternalAccessEndpoints), so the route inherits the ExternalCollaboration policy and the
// group-level CallerPrincipalAuthorizationFilter. The route then carries BillingGuidelinesAuthorizationFilter (ADR-008: the service has no caller
// check) and, after it, RequireEbillingEnabled (ADR-018: the flag never replaces authorization). The handler runs only behind the filter.
// Workforce ceiling (uac-r2 task 186): declared exempt, because the route's own filter refuses every workforce principal before any read.

using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Finance;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Finance.Submission;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The external Billing Guidelines route. See the file header.</summary>
public static class BillingGuidelinesEndpoints
{
    /// <summary>Registers the route on the shared external collaboration group (its policy and principal filter are inherited).</summary>
    public static RouteGroupBuilder MapBillingGuidelinesEndpoints(this RouteGroupBuilder externalGroup)
    {
        externalGroup.MapGet("/ebilling/billing-guidelines", GetAsync)
            .AddBillingGuidelinesAuthorizationFilter()
            .ExemptFromWorkforceCeiling("CIAM plane only: the route's own filter refuses every workforce principal (a systemuser or a workforce contact) with its stable 403 before any read.")
            .RequireEbillingEnabled()
            .WithName("GetExternalBillingGuidelines")
            .WithSummary("The published billing rules in force for the caller's firm, grouped by rule set")
            .Produces<BillingGuidelinesResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return externalGroup;
    }

    private static async Task<IResult> GetAsync(
        HttpContext http, BillingGuidelinesService service, IGenericEntityService entities, ILogger<BillingGuidelinesService> logger, CancellationToken ct)
    {
        // The filter decided the caller, its firms and the record; a handler reached without that decision refuses (fail closed).
        if (http.Items[BillingGuidelinesScope.HttpContextItemsKey] is not BillingGuidelinesScope scope)
        {
            return BillingGuidelinesErrors.Forbidden();
        }

        try
        {
            // The ONE firm-name read (/me uses it): only the caller's own ids are asked; a firm that is inactive or unnamed is not listed.
            var named = await InvoiceSubmissionService.ReadActiveFirmNamesAsync(entities, logger, scope.Firms, ct);
            if (named is null)
            {
                return BillingGuidelinesErrors.Unavailable();
            }

            var response = await service.GetAsync(named.Select(n => new CallerFirm(n.FirmId, n.Name)).ToList(), scope.Core, ct);
            http.Response.Headers.CacheControl = "no-store";
            return Results.Ok(response);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Information and above carry the exception type only: a resolver fault message names row and record ids (K2: never shown).
            logger.LogError("[EBILLING-GUIDELINES] The guidelines could not be read ({ExceptionType}); answering 503.", ex.GetType().Name);
            return BillingGuidelinesErrors.Unavailable();
        }
    }
}

/// <summary>What the filter decided, for the handler: the caller's active firms and, for the per-record view, the record it may read.</summary>
internal sealed record BillingGuidelinesScope(IReadOnlyList<Guid> Firms, CoreRecordReference? Core)
{
    internal static readonly object HttpContextItemsKey = new();
}

/// <summary>The stable codes and bodies of the route (ADR-019). A refusal says nothing about WHY.</summary>
internal static class BillingGuidelinesErrors
{
    /// <summary>403: not a CIAM caller with an active firm.</summary>
    public const string ForbiddenCode = "ebilling.billing-guidelines.forbidden";

    /// <summary>404: no such record, or one the caller cannot read (the same answer).</summary>
    public const string RecordNotFoundCode = "ebilling.billing-guidelines.record-not-found";

    /// <summary>503: the firms, the record check or the guidelines could not be read right now.</summary>
    public const string UnavailableCode = "ebilling.billing-guidelines.unavailable";

    public static IResult Forbidden() => Results.Problem(
        statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "You cannot view billing guidelines.",
        extensions: new Dictionary<string, object?> { ["errorCode"] = ForbiddenCode });

    public static IResult RecordNotFound() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound, title: "Not Found", detail: "The record was not found.",
        extensions: new Dictionary<string, object?> { ["errorCode"] = RecordNotFoundCode });

    public static IResult Unavailable() => Results.Problem(
        statusCode: StatusCodes.Status503ServiceUnavailable, title: "Service Unavailable",
        detail: "The billing guidelines could not be read right now. Try again shortly.",
        extensions: new Dictionary<string, object?> { ["errorCode"] = UnavailableCode });
}

/// <summary>Attaches the Billing Guidelines authorization filter to the route.</summary>
public static class BillingGuidelinesAuthorizationFilterExtensions
{
    /// <summary>
    /// The caller, its firms and the optional record, decided before the handler (ebilling-r1 task 135, FR-49, ADR-008). Chain it BEFORE
    /// <c>RequireEbillingEnabled</c>: the flag never replaces authorization (ADR-018).
    /// </summary>
    public static TBuilder AddBillingGuidelinesAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new BillingGuidelinesAuthorizationFilter(
                services.GetRequiredService<ExternalParticipationService>(),
                services.GetRequiredService<IAccessibleRecordSetService>(),
                services.GetRequiredService<ILogger<BillingGuidelinesAuthorizationFilter>>());
            return await filter.InvokeAsync(context, next);
        });
}

/// <summary>
/// The route-level gate of the Billing Guidelines view (ebilling-r1 task 135, FR-49, D-66, ADR-008, ADR-028).
/// </summary>
/// <remarks>
/// <para><b>The rule (closed set).</b> The caller is a CIAM contact (a systemuser and a workforce principal are refused) and has at least one
/// active firm (<see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync"/>, the one definition 044, 051 and 130 use: no
/// second copy). A view for one record also needs <c>coreRecordId</c> to be a record the contact can READ, decided by
/// <see cref="IAccessibleRecordSetService.ComposeForCiamContactAsync"/> and <see cref="AccessibleRecordSet.Contains"/> over the three core
/// tables; a malformed id, a missing record and one the caller cannot read are the same 404.</para>
/// <para><b>Refusals.</b> A workforce principal, no principal and no firm are the same 403 (<see cref="BillingGuidelinesErrors.ForbiddenCode"/>);
/// a firm or record read that cannot be completed is a retryable 503, never "no firm" and never a pass (EB-D-169). A caller of several firms sees
/// each of its firms.</para>
/// </remarks>
internal sealed class BillingGuidelinesAuthorizationFilter : IEndpointFilter
{
    internal const string RecordQuery = "coreRecordId";

    private static readonly (string Table, CoreRecordKind Kind)[] CoreTables =
    [
        ("sprk_matter", CoreRecordKind.Matter),
        ("sprk_project", CoreRecordKind.Project),
        ("sprk_workassignment", CoreRecordKind.WorkAssignment),
    ];

    private readonly ExternalParticipationService _participations;
    private readonly IAccessibleRecordSetService _accessible;
    private readonly ILogger<BillingGuidelinesAuthorizationFilter> _logger;

    public BillingGuidelinesAuthorizationFilter(
        ExternalParticipationService participations, IAccessibleRecordSetService accessible, ILogger<BillingGuidelinesAuthorizationFilter> logger)
    {
        _participations = participations;
        _accessible = accessible;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var ct = http.RequestAborted;

        if (http.Items[CallerPrincipal.HttpContextItemsKey] is not CallerPrincipal principal)
        {
            _logger.LogWarning("[EBILLING-GUIDELINES] No caller principal; denying (fail closed).");
            return BillingGuidelinesErrors.Forbidden();
        }

        // Only a CIAM contact. A systemuser, and a workforce contact (a Teams-host user), are refused before anything is read.
        if (principal.Plane != CallerPrincipalPlane.CiamContact || ExternalInvoiceSubmissionAuthorizationFilter.IsSystemUser(principal))
        {
            _logger.LogWarning("[EBILLING-GUIDELINES] DENIED: a workforce principal called an external billing guidelines route.");
            return BillingGuidelinesErrors.Forbidden();
        }

        IReadOnlyList<Guid> firms;
        try
        {
            var (read, unreadable) = await ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(principal, _participations, _logger, ct);
            if (unreadable || read is null)
            {
                _logger.LogError("[EBILLING-GUIDELINES] SYSTEM FAILURE: the caller's firms are unreadable; answering 503.");
                return BillingGuidelinesErrors.Unavailable();
            }

            firms = read;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("[EBILLING-GUIDELINES] SYSTEM FAILURE: the caller's firms could not be read ({ExceptionType}); answering 503.", ex.GetType().Name);
            return BillingGuidelinesErrors.Unavailable();
        }

        if (firms.Count == 0)
        {
            _logger.LogInformation("[EBILLING-GUIDELINES] Denied: the caller has no active firm.");
            return BillingGuidelinesErrors.Forbidden();
        }

        CoreRecordReference? core = null;
        if (http.Request.Query.ContainsKey(RecordQuery))
        {
            var (record, denial) = await DecideRecordAsync(principal.ContactId, http.Request.Query[RecordQuery].ToString(), ct);
            if (denial is not null)
            {
                return denial;
            }

            core = record;
        }

        http.Items[BillingGuidelinesScope.HttpContextItemsKey] = new BillingGuidelinesScope(firms, core);
        return await next(context);
    }

    /// <summary>The record the contact can read, or the uniform 404 (a malformed id, a missing record and an unreadable one alike), or a 503.</summary>
    private async Task<(CoreRecordReference? Record, IResult? Denial)> DecideRecordAsync(Guid contactId, string raw, CancellationToken ct)
    {
        if (!Guid.TryParse(raw, out var recordId) || recordId == Guid.Empty)
        {
            return (null, BillingGuidelinesErrors.RecordNotFound());
        }

        try
        {
            foreach (var (table, kind) in CoreTables)
            {
                var set = await _accessible.ComposeForCiamContactAsync(contactId, table, ct);
                if (set.Contains(recordId))
                {
                    return (new CoreRecordReference(kind, recordId), null);
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError("[EBILLING-GUIDELINES] SYSTEM FAILURE: the caller's accessible records could not be composed ({ExceptionType}); answering 503.", ex.GetType().Name);
            return (null, BillingGuidelinesErrors.Unavailable());
        }

        _logger.LogInformation("[EBILLING-GUIDELINES] Denied: the record is missing or the caller cannot read it.");
        return (null, BillingGuidelinesErrors.RecordNotFound());
    }
}
