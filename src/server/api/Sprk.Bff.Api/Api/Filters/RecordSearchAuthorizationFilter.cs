using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
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
/// What a record-search access decision answered (spaarkeai-mcp-bff-api-r2 task 027). One value per distinct response of
/// <c>POST /api/ai/search/records</c>; each refusal carries the REST code as <see cref="RecordSearchAccess.ReasonCode"/>.
/// </summary>
/// <remarks><see cref="UserContextRequired"/> is the zero value, so <c>default</c> is never <see cref="Allowed"/>.</remarks>
public enum RecordSearchAccessOutcome
{
    /// <summary>No request context, no authenticated identity or no request services. Nothing was read (REST cannot reach it: the route requires authentication).</summary>
    UserContextRequired = 0,

    /// <summary>No <c>tid</c> claim (REST: 401).</summary>
    MissingTenantIdentity = 1,

    /// <summary>No search request (REST: 400).</summary>
    RequestBodyRequired = 2,

    /// <summary>No Entra <c>oid</c> (REST: 401).</summary>
    MissingCallerIdentity = 3,

    /// <summary>No bearer token, so access cannot be evaluated as the caller (REST: 401).</summary>
    MissingCallerToken = 4,

    /// <summary>A requested record type is not a known search type (REST: 400, with <c>invalidValues</c>).</summary>
    InvalidRecordTypes = 5,

    /// <summary>No record types were requested (REST: 400).</summary>
    RecordTypesRequired = 6,

    /// <summary>A known record type whose access cannot be evaluated (REST: 403).</summary>
    RecordTypeNotAuthorizable = 7,

    /// <summary>
    /// The row phase found no caller <c>oid</c> or bearer token after the request phase had seen both (REST: the
    /// handler's 500 "Caller context not available.", which carries no code; <see cref="RecordSearchAccess.ReasonCode"/>
    /// names the missing part). The search did not run.
    /// </summary>
    CallerContextUnavailable = 8,

    /// <summary>Allowed. On the search phase, <see cref="RecordSearchAccess.Response"/> holds the records the caller may read.</summary>
    Allowed = 9,
}

/// <summary>
/// The answer of a record-search access decision: an outcome, the machine-readable <c>reasonCode</c> and the text REST sends
/// for a refusal, and, when allowed, the authorized response. It carries no <see cref="IResult"/> and no ProblemDetails, so a
/// non-HTTP host (the MCP record search tool, spaarkeai-mcp-bff-api-r2 task 022) renders it in its own error shape (ADR-019).
/// </summary>
public sealed record RecordSearchAccess
{
    private RecordSearchAccess(RecordSearchAccessOutcome outcome, string? reasonCode, string? detail)
    {
        Outcome = outcome;
        ReasonCode = reasonCode;
        Detail = detail;
    }

    /// <summary>No request context or no signed-in caller (ADR-028 A10): <see cref="DataverseUserClientErrorCodes.UserContextRequired"/>.</summary>
    public static readonly RecordSearchAccess UserContextRequired = new(
        RecordSearchAccessOutcome.UserContextRequired, DataverseUserClientErrorCodes.UserContextRequired, null);

    public RecordSearchAccessOutcome Outcome { get; }

    /// <summary><c>null</c> when allowed. On a refusal, the <see cref="SearchErrorCodes"/> code REST sends for the same input.</summary>
    public string? ReasonCode { get; }

    /// <summary>On a refusal, the <c>detail</c> text REST sends for the same input; <c>null</c> otherwise.</summary>
    public string? Detail { get; }

    /// <summary>On <see cref="RecordSearchAccessOutcome.InvalidRecordTypes"/>, the values that are not search types.</summary>
    public IReadOnlyList<string>? InvalidValues { get; private init; }

    /// <summary>When the search phase is allowed: the records the caller may read, with the recomputed count.</summary>
    public RecordSearchResponse? Response { get; private init; }

    public bool IsAllowed => Outcome == RecordSearchAccessOutcome.Allowed;

    /// <summary>The request phase's decision, for the REST filter to publish on <see cref="HttpContext.Items"/>.</summary>
    internal RecordSearchAuthorization? Authorization { get; private init; }

    internal static RecordSearchAccess Refused(RecordSearchAccessOutcome outcome, string reasonCode, string detail) =>
        new(outcome, reasonCode, detail);

    internal static RecordSearchAccess InvalidRecordTypes(IReadOnlyList<string> invalidValues, string detail) =>
        new(RecordSearchAccessOutcome.InvalidRecordTypes, SearchErrorCodes.InvalidRecordTypes, detail)
        {
            InvalidValues = invalidValues
        };

    internal static RecordSearchAccess RequestAllowed(RecordSearchAuthorization authorization) =>
        new(RecordSearchAccessOutcome.Allowed, null, null) { Authorization = authorization };

    internal static RecordSearchAccess SearchAllowed(RecordSearchResponse response) =>
        new(RecordSearchAccessOutcome.Allowed, null, null) { Response = response };

    internal static RecordSearchAccess CallerContextUnavailable(string reasonCode) =>
        new(RecordSearchAccessOutcome.CallerContextUnavailable, reasonCode, "Caller context not available.");
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

        // Binds the request and asks DecideRequest (the ONE owner of the request-level decision, which
        // SearchRecordsAsCallerAsync also runs); maps its answer to this route's responses. It checks nothing itself.
        var decision = DecideRequest(httpContext, ExtractRecordSearchRequest(context));

        if (decision is { Outcome: RecordSearchAccessOutcome.Allowed, Authorization: { } authorization })
        {
            httpContext.Items[RecordSearchAuthorization.HttpContextItemsKey] = authorization;
            return await next(context);
        }

        return ToProblem(decision, httpContext.TraceIdentifier);
    }

    /// <summary>
    /// Which records matching <paramref name="request"/> may the caller of <paramref name="httpContext"/> see? THE rule of
    /// <c>POST /api/ai/search/records</c> for a host that is not that route (the MCP record search tool,
    /// spaarkeai-mcp-bff-api-r2 task 022, spec FR-02): the request-level decision this route's filter makes
    /// (<see cref="DecideRequest"/>: tenant, caller, token, record types), then the row-level authorization its handler
    /// applies (<see cref="SearchAndAuthorizeRowsAsync"/>: every returned record checked as the caller, the count recomputed),
    /// with the same Dataverse questions in the same order. <paramref name="search"/> runs the search itself; it is called at
    /// most once, and only after the request is authorized and the caller's identity and token are present.
    /// <para>Fails closed (ADR-028 A10): a null context, a principal with no authenticated identity or no request services is
    /// <see cref="RecordSearchAccess.UserContextRequired"/> before any Dataverse or search call. Every other refusal carries the
    /// code the route sends for the same input. Never falls back to app-only: a principal without a bearer token is
    /// <see cref="RecordSearchAccessOutcome.MissingCallerToken"/>. A missing <see cref="AuthorizationService"/> registration
    /// throws, as the route's own resolution does.</para>
    /// <para>Not included, because they are input validation rather than access decisions: the handler's query, query-length,
    /// limit and offset checks. A host validates its own input before calling; a limit above the route's 50 is not an access
    /// hole (rows past the check budget are dropped, never served unchecked).</para>
    /// <para>Pass the request's own context (<c>IHttpContextAccessor.HttpContext</c>) only: the principal and the bearer token
    /// in its <c>Authorization</c> header are read separately and must come from the same validated token. No token is passed
    /// by the caller, and none is logged.</para>
    /// </summary>
    public static async Task<RecordSearchAccess> SearchRecordsAsCallerAsync(
        HttpContext? httpContext,
        RecordSearchRequest? request,
        Func<RecordSearchRequest, CancellationToken, Task<RecordSearchResponse>> search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (httpContext?.User is not { } user
            || !user.Identities.Any(i => i.IsAuthenticated)
            || httpContext.RequestServices is not { } services)
        {
            return RecordSearchAccess.UserContextRequired;
        }

        var logger = services.GetService<ILogger<RecordSearchAuthorizationFilter>>();
        var decision = new RecordSearchAuthorizationFilter(logger).DecideRequest(httpContext, request);
        if (decision is not { Outcome: RecordSearchAccessOutcome.Allowed, Authorization: not null })
        {
            return decision;
        }

        return await SearchAndAuthorizeRowsAsync(
            httpContext,
            request!, // Allowed implies a body: DecideRequest refuses a null one (RequestBodyRequired).
            search,
            services.GetRequiredService<AuthorizationService>(),
            (ILogger?)logger ?? NullLogger.Instance,
            cancellationToken);
    }

    /// <summary>
    /// The request-level decision — the ONE implementation, behind both this filter and
    /// <see cref="SearchRecordsAsCallerAsync"/>. In order: tenant membership, the request body, the caller's <c>oid</c> and
    /// bearer token, then the record types. It reads nothing from Dataverse: record search has no scope to authorize up
    /// front, so the decision carries the per-row obligation (<see cref="RecordSearchAuthorization"/>).
    /// </summary>
    private RecordSearchAccess DecideRequest(HttpContext httpContext, RecordSearchRequest? request)
    {
        // Step 1: tenant membership.
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Record search denied: no tenant claim in token");
            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.MissingTenantIdentity,
                SearchErrorCodes.MissingTenantIdentity, "Tenant identity not found in authentication token");
        }

        // Step 2: the request body.
        //
        // A missing body was previously a path that called next() — so "no parseable request" skipped
        // authorization entirely and left only the endpoint's shape validation. Denying costs a caller
        // with a malformed body a 400; the alternative costs an unauthorized read.
        if (request is null)
        {
            _logger?.LogWarning(
                "Record search denied for tenant {TenantId}: no search request found on the invocation",
                userTenantId);
            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.RequestBodyRequired,
                SearchErrorCodes.RequestBodyRequired, "A record search request body is required.");
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
            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.MissingCallerIdentity,
                SearchErrorCodes.MissingCallerIdentity, "Caller identity not found in authentication token");
        }

        if (string.IsNullOrEmpty(TokenHelper.ExtractBearerTokenOrNull(httpContext)))
        {
            _logger?.LogWarning(
                "Record search denied for caller {CallerId}: no bearer token, so access cannot be "
                + "evaluated as the caller. Refusing rather than evaluating app-only.", callerObjectId);
            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.MissingCallerToken,
                SearchErrorCodes.MissingCallerToken, "A caller bearer token is required to evaluate access.");
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
            return RecordSearchAccess.InvalidRecordTypes(
                malformed,
                $"Invalid recordTypes value(s): {string.Join(", ", malformed.Select(t => $"'{t}'"))}. "
                + $"Valid values: {string.Join(", ", RecordEntityType.ValidTypes)}.");
        }

        if (requested.Count == 0)
        {
            // Mirrors the endpoint's wording exactly. Not a pass-through: reaching the search with
            // nothing to authorize against is how "no authorization" becomes a code path.
            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.RecordTypesRequired,
                SearchErrorCodes.InvalidRecordTypes,
                "recordTypes is required and must contain at least one record type.");
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

            return RecordSearchAccess.Refused(RecordSearchAccessOutcome.RecordTypeNotAuthorizable,
                SearchErrorCodes.EntityTypeNotAuthorizable,
                "recordTypes contains value(s) whose access cannot be evaluated: "
                + $"{string.Join(", ", unauthorizable)}.");
        }

        _logger?.LogInformation(
            "Record search request authorized for caller {CallerId} over types [{Types}]; results will "
            + "be authorized per row.",
            callerObjectId, string.Join(", ", entitySets.Keys));

        return RecordSearchAccess.RequestAllowed(new RecordSearchAuthorization
        {
            RequiresPerRowRecordAuthorization = true,
            RequestedEntitySets = entitySets
        });
    }

    /// <summary>
    /// Maps a request-level refusal to the response this route has always sent for it (status, title, <c>detail</c>, codes).
    /// </summary>
    private static IResult ToProblem(RecordSearchAccess decision, string correlationId)
    {
        switch (decision.Outcome)
        {
            case RecordSearchAccessOutcome.MissingTenantIdentity:
            case RecordSearchAccessOutcome.MissingCallerIdentity:
            case RecordSearchAccessOutcome.MissingCallerToken:
                return Problem(401, "Unauthorized", decision.Detail!, decision.ReasonCode!, correlationId);

            case RecordSearchAccessOutcome.RequestBodyRequired:
                return Problem(400, "Bad Request", decision.Detail!, decision.ReasonCode!, correlationId);

            case RecordSearchAccessOutcome.InvalidRecordTypes:
                return Results.Problem(
                    statusCode: 400,
                    title: "Invalid Record Types",
                    detail: decision.Detail,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = SearchErrorCodes.InvalidRecordTypes,
                        ["code"] = SearchErrorCodes.InvalidRecordTypes,
                        ["invalidValues"] = decision.InvalidValues,
                        ["validValues"] = RecordEntityType.ValidTypes,
                        ["correlationId"] = correlationId
                    });

            case RecordSearchAccessOutcome.RecordTypesRequired:
                return Results.Problem(
                    statusCode: 400,
                    title: "Record Types Required",
                    detail: decision.Detail,
                    extensions: new Dictionary<string, object?>
                    {
                        ["errorCode"] = SearchErrorCodes.InvalidRecordTypes,
                        ["code"] = SearchErrorCodes.InvalidRecordTypes,
                        ["correlationId"] = correlationId
                    });

            case RecordSearchAccessOutcome.RecordTypeNotAuthorizable:
                return Problem(403, "Forbidden", decision.Detail!, decision.ReasonCode!, correlationId);

            default:
                // Unreachable: the request phase answers Allowed (with a decision) or one of the refusals above. ADR-003:
                // never a pass-through — the same 500 the handler gives a request that reached it with no decision.
                return Results.Problem(
                    statusCode: 500,
                    title: "Internal Server Error",
                    detail: "Authorization context not available.");
        }
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

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Row level (spaarkeai-mcp-bff-api-r2 task 027): moved here from RecordSearchEndpoints, unchanged, so the request
    // decision and the per-row authorization it obliges have one owner. The handler binds and maps; so does
    // SearchRecordsAsCallerAsync.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the search for an authorized request and authorizes every returned row — the ONE implementation, behind both
    /// <c>POST /api/ai/search/records</c>'s handler and <see cref="SearchRecordsAsCallerAsync"/>. The caller's <c>oid</c> and
    /// bearer token are read from <paramref name="httpContext"/> BEFORE the search runs; then each row is checked as the
    /// caller (<see cref="AuthorizeRowsAsync"/>).
    /// </summary>
    /// <returns>
    /// <see cref="RecordSearchAccessOutcome.Allowed"/> with the response, or
    /// <see cref="RecordSearchAccessOutcome.CallerContextUnavailable"/> (no search ran) when the context has no caller
    /// <c>oid</c> or bearer token (the request phase saw both; the handler answers 500).
    /// </returns>
    internal static async Task<RecordSearchAccess> SearchAndAuthorizeRowsAsync(
        HttpContext httpContext,
        RecordSearchRequest request,
        Func<RecordSearchRequest, CancellationToken, Task<RecordSearchResponse>> search,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Was `FindFirst("oid") ?? FindFirst(ClaimTypes.NameIdentifier)` until 2026-08-27. Inbound claim
        // mapping (ON) renames `oid` to the .../objectidentifier schema URI, so the first term was always
        // null and this resolved `sub` — pairwise per user+app, matching no Dataverse systemuser. It
        // reaches the per-row authorization check below, so this was not a logging concern. Must stay
        // identical to the request phase's resolution; CallerResolution is what keeps the two halves in step.
        var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

        if (string.IsNullOrEmpty(callerObjectId) || string.IsNullOrEmpty(callerToken))
        {
            // The filter proved both present before permitting the request; if either is missing now,
            // the pipeline changed underneath us.
            logger.LogError(
                "Record search reached the handler without a caller identity or bearer token, which the "
                + "authorization filter guarantees. Refusing.");

            return RecordSearchAccess.CallerContextUnavailable(
                string.IsNullOrEmpty(callerObjectId)
                    ? SearchErrorCodes.MissingCallerIdentity
                    : SearchErrorCodes.MissingCallerToken);
        }

        var response = await search(request, cancellationToken);

        response = await AuthorizeRowsAsync(
            response, callerObjectId, callerToken, authorizationService, logger, cancellationToken);

        return RecordSearchAccess.SearchAllowed(response);
    }

    /// <summary>
    /// Upper bound on record-access checks spent assembling one page.
    /// </summary>
    /// <remarks>
    /// Set to the maximum page size the request DTO permits (<c>RecordSearchOptions.Limit</c> is
    /// <c>[Range(1, 50)]</c>), deliberately, so a full page is always fully evaluated. Document search
    /// can afford a budget below its page size because twenty documents often share one parent — one
    /// check covers many rows. Here the row IS the record, so there is no sharing to exploit and a
    /// budget under 50 would silently truncate a legitimate full page. Repeats across requests are
    /// absorbed by <c>CachedAccessDataSource</c>'s entity-set-qualified 60 s key.
    /// </remarks>
    private const int MaxRecordAuthorizationChecks = RecordSearchOptions.MaxLimit;

    /// <summary>
    /// Authorizes every returned row against its own record and drops what the caller cannot read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The row is the subject.</b> Document search authorizes a document's PARENT (access flows from
    /// the parent per the build plan's invariant 2). A record search result IS a matter/project/invoice,
    /// so the check is directly against that record — no parent hop, and no grouping win either: N rows
    /// cost up to N checks, deduplicated by (type, id).
    /// </para>
    /// <para>
    /// <b>Fail closed per row (ADR-003).</b> A row is dropped unless its <c>RecordType</c> resolves
    /// through the single shared allow-list AND its <c>RecordId</c> parses as a non-empty GUID AND
    /// Dataverse says the caller holds Read. The filter already refused unauthorizable types for the
    /// whole request, so an unresolvable type here means the SERVICE returned a row of a type the caller
    /// never asked for — which is a reason to drop it, not to trust it.
    /// </para>
    /// <para>
    /// <b>Sequential</b>, for the reason documented on the document-search equivalent. The original reason —
    /// <c>DataverseAccessDataSource</c> assigning the shared <c>_httpClient.DefaultRequestHeaders.Authorization</c>
    /// before each request — is gone since issue #1576 (every read carries its own token); it stays sequential
    /// because nothing above the data source has been reviewed for concurrent use within one request scope.
    /// </para>
    /// <para>
    /// <b>Known limitation (task 077 follow-up F-4).</b> Rows are authorized after the index ranks them,
    /// so a caller entitled to few records can have their matches ranked below the fetched page and
    /// receive a short page that is indistinguishable from "nothing matched". Document search announces
    /// that with a <c>PARTIAL_RESULTS</c> warning; <see cref="RecordSearchMetadata"/> has no warnings
    /// channel, and adding one is a response-contract change that does not belong inside a security fix.
    /// Recorded rather than silently accepted.
    /// </para>
    /// </remarks>
    private static async Task<RecordSearchResponse> AuthorizeRowsAsync(
        RecordSearchResponse response,
        string callerObjectId,
        string callerToken,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (response.Results is not { Count: > 0 })
        {
            return response;
        }

        var permitted = new List<RecordSearchResult>(response.Results.Count);
        var decisions = new Dictionary<(string EntitySet, Guid RecordId), bool>();

        var checksSpent = 0;
        var unresolvable = 0;
        var budgetExhausted = false;

        foreach (var row in response.Results)
        {
            if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(
                    row.RecordType, out var entitySetName)
                || !Guid.TryParse(row.RecordId, out var recordId)
                || recordId == Guid.Empty)
            {
                unresolvable++;
                continue;
            }

            var key = (entitySetName, recordId);
            if (!decisions.TryGetValue(key, out var readable))
            {
                if (checksSpent >= MaxRecordAuthorizationChecks)
                {
                    budgetExhausted = true;
                    break;
                }

                checksSpent++;

                var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                    callerObjectId, entitySetName, recordId, callerToken, cancellationToken);

                readable = snapshot.AccessRights.HasFlag(AccessRights.Read);
                decisions[key] = readable;
            }

            if (readable)
            {
                permitted.Add(row);
            }
        }

        var dropped = response.Results.Count - permitted.Count;
        if (dropped > 0)
        {
            logger.LogInformation(
                "Record search authorization dropped {Dropped} of {Total} rows the caller cannot read "
                + "({Checks} record checks, {Unresolvable} unresolvable rows, budgetExhausted={Budget})",
                dropped, response.Results.Count, checksSpent, unresolvable, budgetExhausted);
        }

        return response with
        {
            Results = permitted,
            Metadata = response.Metadata with
            {
                // The count of rows in THIS response. Leaving the pre-filter total would report how many
                // records exist beyond what was returned — a smaller leak than the records themselves,
                // but the same kind, and on this route the count alone reveals how many matters match a
                // counterparty's name.
                TotalCount = permitted.Count
            }
        };
    }
}
