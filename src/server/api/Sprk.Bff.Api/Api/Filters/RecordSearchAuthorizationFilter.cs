using System.Security.Claims;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Models.Ai.RecordSearch;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding record search authorization to endpoints.
/// </summary>
public static class RecordSearchAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the record-search authorization filter: validates the caller's identity and the record
    /// types requested, then publishes the obligation to authorize every returned row.
    /// </summary>
    public static TBuilder AddRecordSearchAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices
                .GetService<ILogger<RecordSearchAuthorizationFilter>>();
            var filter = new RecordSearchAuthorizationFilter(logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The authorization decision for a record search, published for the endpoint to enforce at ROW level.
/// Placed in <see cref="HttpContext.Items"/> under <see cref="HttpContextItemsKey"/>.
/// </summary>
/// <remarks>
/// <para>
/// Record search has no scope to authorize up front. Unlike document search — where
/// <c>scope=entity</c> names one parent the caller can be checked against before the query runs —
/// this route's subject IS the result set: the caller asks "which records match this text", and the
/// answer is a list of records whose readability can only be evaluated once they exist.
/// </para>
/// <para>
/// So the filter authorizes what it can (caller identity, an OBO token, and that every requested
/// record type is one whose access this system can actually evaluate) and carries forward the
/// obligation for the rest. A route that receives this decision and does not perform the row check
/// MUST refuse rather than serve the unfiltered answer.
/// </para>
/// </remarks>
public sealed record RecordSearchAuthorization
{
    public static readonly object HttpContextItemsKey = new();

    /// <summary>
    /// Every returned row MUST be authorized against its own record before it may be served.
    /// </summary>
    /// <remarks>
    /// An explicit flag rather than something inferred, for the same reason as the document-search
    /// equivalent: it keeps "permit" a positive assertion, so a decision that carries nothing permits
    /// nothing. There is no branch here that sets it false — record search is *always* row-authorized.
    /// It exists so the endpoint's fail-closed check has something to assert on, and so a future route
    /// adding this filter has to confront the obligation rather than inherit it silently.
    /// </remarks>
    public bool RequiresPerRowRecordAuthorization { get; init; }

    /// <summary>The record types requested, mapped to the Dataverse entity SET each is evaluated in.</summary>
    public required IReadOnlyDictionary<string, string> RequestedEntitySets { get; init; }
}

/// <summary>
/// Authorization filter for record search.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008 (endpoint filters for resource-level authorization), ADR-016 (tenant isolation) and
/// ADR-003 (fail closed).
/// </para>
/// <para>
/// <b>What this replaced, and why (unified-access-control-r2 task 077).</b> This filter previously did
/// exactly three things: read the <c>tid</c> claim, extract the request, and write
/// <c>LogInformation("Record search authorization granted: …")</c> before calling <c>next()</c>. There
/// was no authorization decision anywhere in the file and its only constructor dependency was
/// <see cref="ILogger"/>. Its own doc block listed "Validates record types are known entity types" as
/// an authorization rule — the code never did that, and the endpoint already did. The remarks then
/// stated that "tenant isolation is now enforced at the search index level" and that this "remains as
/// the authentication + audit gate", which was an accurate description of a filter that is not an
/// authorization filter.
/// </para>
/// <para>
/// <b>Why that mattered more than the document-search twin.</b> This route returns RECORDS — matters,
/// projects, invoices — filtered only by <c>tenantId</c>. Any authenticated caller could enumerate
/// record names across the whole tenant, and <see cref="RecordSearchResult"/> also carries
/// <c>Organizations</c>, <c>People</c>, <c>Keywords</c> and <c>ReferenceNumbers</c>. For a secure
/// matter the NAME is frequently the sensitive fact — a matter named for a counterparty discloses the
/// engagement's existence to someone with no access to it — and the extracted-entity fields disclose
/// who is involved.
/// </para>
/// <para>
/// <b>Why it survived four hand enumerations of this surface.</b> A filter WAS attached, so the route
/// looked gated to every prior review and to task 074's first rule ("does this route carry an
/// authorization filter?"). Only 074's Rule B — does the filter actually consult an authorization
/// service? — catches this shape. This route is the reason Rule B exists; see
/// <c>RouteAuthorizationGuardTests</c>.
/// </para>
/// <para>
/// <b>An audit log is not an authorization decision</b>, and neither is a tenant filter. The previous
/// implementation logged the word "granted" on every request, which made the gap read as a decision in
/// any log review.
/// </para>
/// </remarks>
public class RecordSearchAuthorizationFilter : IEndpointFilter
{
    private readonly ILogger<RecordSearchAuthorizationFilter>? _logger;

    // Azure AD claim names
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdClaimType = "oid";

    public RecordSearchAuthorizationFilter(ILogger<RecordSearchAuthorizationFilter>? logger = null)
    {
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var correlationId = httpContext.TraceIdentifier;

        // Step 1: tenant membership.
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Record search denied: no tenant claim in token");
            return Problem(401, "Unauthorized",
                "Tenant identity not found in authentication token",
                SearchErrorCodes.MissingTenantIdentity, correlationId);
        }

        // Step 2: the request body.
        //
        // A missing body was previously a path that called next() — so "no parseable request" skipped
        // authorization entirely and left only the endpoint's shape validation. Denying costs a caller
        // with a malformed body a 400; the alternative costs an unauthorized read.
        var request = ExtractRecordSearchRequest(context);
        if (request is null)
        {
            _logger?.LogWarning(
                "Record search denied for tenant {TenantId}: no search request found on the invocation",
                userTenantId);
            return Problem(400, "Bad Request", "A record search request body is required.",
                SearchErrorCodes.RequestBodyRequired, correlationId);
        }

        // Step 3: the caller. Identity AND the bearer token — the token is what makes the downstream
        // Dataverse evaluation run AS THE CALLER rather than as the application. Without it the row
        // check would fail closed anyway; refusing here produces a clearer 401.
        // ⚠️ FIXED 2026-08-27 during the master merge. This previously read
        //     FindFirst("oid") ?? FindFirst(ClaimTypes.NameIdentifier)
        // which resolved the caller's Entra `sub`, NOT its `oid`. This app runs with inbound claim-type
        // mapping ON (the default), so .NET renames `oid` to the schema URI and `sub` to
        // ClaimTypes.NameIdentifier — meaning FindFirst("oid") is ALWAYS null here and the fallback
        // always fired. `sub` is pairwise per (user, application) and joins to no `systemuser`, so the
        // downstream Dataverse evaluation matched nothing and this filter denied every caller.
        // Fails closed, so it read as "authorization working" rather than as an outage.
        // See Infrastructure/Authentication/CallerResolution and PR #832.
        var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);

        if (string.IsNullOrEmpty(callerObjectId))
        {
            _logger?.LogWarning(
                "Record search denied for tenant {TenantId}: no caller object id in token", userTenantId);
            return Problem(401, "Unauthorized", "Caller identity not found in authentication token",
                SearchErrorCodes.MissingCallerIdentity, correlationId);
        }

        if (string.IsNullOrEmpty(TokenHelper.ExtractBearerTokenOrNull(httpContext)))
        {
            _logger?.LogWarning(
                "Record search denied for caller {CallerId}: no bearer token, so access cannot be "
                + "evaluated as the caller. Refusing rather than evaluating app-only.", callerObjectId);
            return Problem(401, "Unauthorized", "A caller bearer token is required to evaluate access.",
                SearchErrorCodes.MissingCallerToken, correlationId);
        }

        // Step 4: every requested record type must be one whose access we can actually evaluate.
        //
        // This is the check the old doc block CLAIMED to perform. It is not the same as the endpoint's
        // RecordEntityType.IsValid: that asks "is this a known search type", which is a vocabulary
        // question. This asks "can this system evaluate a caller's access to this type" — and a type
        // that is searchable but not authorizable must DENY, because otherwise its rows would reach the
        // row check and be dropped one at a time with no explanation.
        // MALFORMED and UNAUTHORIZABLE are different answers, and conflating them breaks a shipped
        // contract. An earlier draft returned 403 for anything this filter could not map — which turned
        // the endpoint's long-standing 400 INVALID_RECORD_TYPES into a 403 simply because the filter now
        // answers first. That is a caller-visible regression with no security benefit: a typo in
        // `recordTypes` is a bad request, and telling the caller they are "forbidden" sends them looking
        // at permissions instead of at their payload. Task 070 had to fix exactly this shape for
        // `scope`.
        //
        //   - not a known search type            → 400, mirroring the endpoint's own wording and code
        //   - a known type with no access mapping → 403, because this one IS an authorization statement
        //
        // The second set is EMPTY today (RecordEntityType is matter/project/invoice, all three mapped).
        // It is written anyway so that adding a searchable type without an access mapping DENIES rather
        // than silently returning rows nobody authorized.
        var requested = request.RecordTypes ?? [];

        var malformed = requested.Where(t => !RecordEntityType.IsValid(t)).ToList();
        if (malformed.Count > 0)
        {
            return Results.Problem(
                statusCode: 400,
                title: "Invalid Record Types",
                detail: $"Invalid recordTypes value(s): {string.Join(", ", malformed.Select(t => $"'{t}'"))}. "
                        + $"Valid values: {string.Join(", ", RecordEntityType.ValidTypes)}.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = SearchErrorCodes.InvalidRecordTypes,
                    ["code"] = SearchErrorCodes.InvalidRecordTypes,
                    ["invalidValues"] = malformed,
                    ["validValues"] = RecordEntityType.ValidTypes,
                    ["correlationId"] = correlationId
                });
        }

        if (requested.Count == 0)
        {
            // Mirrors the endpoint's wording exactly. Not a pass-through: reaching the search with
            // nothing to authorize against is how "no authorization" becomes a code path.
            return Results.Problem(
                statusCode: 400,
                title: "Record Types Required",
                detail: "recordTypes is required and must contain at least one record type.",
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = SearchErrorCodes.InvalidRecordTypes,
                    ["code"] = SearchErrorCodes.InvalidRecordTypes,
                    ["correlationId"] = correlationId
                });
        }

        var entitySets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unauthorizable = new List<string>();

        foreach (var recordType in requested)
        {
            if (SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(recordType, out var set))
            {
                entitySets[recordType] = set;
            }
            else
            {
                unauthorizable.Add(recordType ?? "(null)");
            }
        }

        if (unauthorizable.Count > 0)
        {
            _logger?.LogWarning(
                "Record search DENIED for caller {CallerId}: record type(s) [{Types}] are searchable but "
                + "not authorizable — no entity-set mapping exists to evaluate access against.",
                callerObjectId, string.Join(", ", unauthorizable));

            return Problem(403, "Forbidden",
                "recordTypes contains value(s) whose access cannot be evaluated: "
                + $"{string.Join(", ", unauthorizable)}.",
                SearchErrorCodes.EntityTypeNotAuthorizable, correlationId);
        }

        httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey] = new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = entitySets
        };

        _logger?.LogInformation(
            "Record search request authorized for caller {CallerId} over types [{Types}]; results will "
            + "be authorized per row.",
            callerObjectId, string.Join(", ", entitySets.Keys));

        return await next(context);
    }

    /// <summary>
    /// Extract RecordSearchRequest from endpoint arguments.
    /// </summary>
    private static RecordSearchRequest? ExtractRecordSearchRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is RecordSearchRequest request)
            {
                return request;
            }
        }
        return null;
    }

    /// <summary>
    /// ProblemDetails with a machine-readable code and a correlation id, per ADR-019. Emits the code
    /// under both <c>errorCode</c> (the ADR's canonical name) and <c>code</c> (what this route group has
    /// always emitted, and what the shipped clients read).
    /// </summary>
    private static IResult Problem(
        int statusCode, string title, string detail, string errorCode, string correlationId) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["errorCode"] = errorCode,
                ["code"] = errorCode,
                ["correlationId"] = correlationId
            });
}
