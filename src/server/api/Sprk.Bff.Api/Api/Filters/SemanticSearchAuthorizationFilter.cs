using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding semantic search authorization to endpoints.
/// </summary>
public static class SemanticSearchAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the semantic-search authorization filter: validates tenant membership, then authorizes the
    /// caller against the records the request asks about.
    /// </summary>
    public static TBuilder AddSemanticSearchAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new SemanticSearchAuthorizationFilter(
                services.GetRequiredService<AuthorizationService>(),
                services.GetService<ILogger<SemanticSearchAuthorizationFilter>>());
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The authorization decision this filter reached, published for the endpoint to enforce at RESULT
/// level. Placed in <see cref="HttpContext.Items"/> under <see cref="HttpContextItemsKey"/>.
/// </summary>
/// <remarks>
/// The filter runs BEFORE the search executes, so it can only authorize the QUESTION (may this caller
/// ask about this parent / these documents?). Authorizing the ANSWER — that every returned row is in
/// fact within the authorized scope — has to happen after results exist. Carrying the decision forward
/// keeps both halves derived from one evaluation instead of two.
/// </remarks>
public sealed record SemanticSearchAuthorization
{
    public static readonly object HttpContextItemsKey = new();

    /// <summary>The scope the filter authorized, lower-cased.</summary>
    public required string Scope { get; init; }

    /// <summary>
    /// For <c>scope=entity</c>: the parent record the caller was authorized against. Every result MUST
    /// belong to this parent.
    /// </summary>
    public Guid? AuthorizedParentId { get; init; }

    /// <summary>
    /// For <c>scope=documentIds</c>: the subset of requested document ids the caller may actually read.
    /// Results outside this set MUST be dropped.
    /// </summary>
    public IReadOnlySet<string>? AuthorizedDocumentIds { get; init; }

    /// <summary>
    /// For <c>scope=all</c>: NO scope constraint was authorized up front. Every returned row MUST be
    /// authorized against its own parent record before it may be served.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An explicit flag rather than an inference from <see cref="Scope"/>. The result-level check fails
    /// closed by default — a decision carrying no parent, no document set and not this flag permits
    /// nothing — and that default is only safe while "permit" requires something to be positively set.
    /// Deriving per-row mode from a string comparison would put the fail-closed default one typo away
    /// from becoming a fail-open one, which is precisely the class of bug task 070 removed.
    /// </para>
    /// <para>
    /// A route that cannot perform per-row enforcement MUST refuse when it sees this flag rather than
    /// serving the unfiltered answer. <c>POST /api/ai/search/count</c> is exactly that case: a count has
    /// no rows to drop, so an unfiltered tenant-wide count would be a (smaller) disclosure of the same
    /// kind. The flag makes that obligation visible to any future route that adds this filter.
    /// </para>
    /// </remarks>
    public bool RequiresPerRowParentAuthorization { get; init; }
}

/// <summary>
/// What a document-search access decision answered (spaarkeai-mcp-bff-api-r2 task 027). One value per distinct response of
/// <c>POST /api/ai/search</c>; each refusal carries the REST code as <see cref="SemanticSearchAccess.ReasonCode"/>.
/// </summary>
/// <remarks><see cref="UserContextRequired"/> is the zero value, so <c>default</c> is never <see cref="Allowed"/>.</remarks>
public enum SemanticSearchAccessOutcome
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

    /// <summary>The scope is absent or unknown (REST: 400).</summary>
    InvalidScope = 5,

    /// <summary><c>scope=entity</c> without <c>entityType</c> (REST: 400).</summary>
    EntityTypeRequired = 6,

    /// <summary><c>scope=entity</c> without <c>entityId</c> (REST: 400).</summary>
    EntityIdRequired = 7,

    /// <summary><c>entityType</c> is not an authorizable parent type (REST: 403).</summary>
    EntityTypeNotAuthorizable = 8,

    /// <summary><c>entityId</c> is not a non-empty GUID (REST: 400).</summary>
    InvalidEntityId = 9,

    /// <summary>The caller has no Read on the parent record, or it does not exist (REST: the uniform 403).</summary>
    EntityAccessDenied = 10,

    /// <summary><c>scope=documentIds</c> without ids (REST: 400).</summary>
    DocumentIdsRequired = 11,

    /// <summary>More document ids than one request may authorize (REST: 400).</summary>
    TooManyDocumentIds = 12,

    /// <summary>A document id is not a GUID (REST: 400).</summary>
    InvalidDocumentIds = 13,

    /// <summary>The caller may read none of the named documents (REST: 403).</summary>
    NoReadableDocuments = 14,

    /// <summary><c>scope=all</c> on the count route, which has no rows to authorize (REST: 403).</summary>
    ScopeAllNotPermitted = 15,

    /// <summary>
    /// The per-row phase found no caller <c>oid</c> or bearer token after the request phase had seen both (REST: the
    /// handler's 500 "Caller context not available.", which carries no code; <see cref="SemanticSearchAccess.ReasonCode"/>
    /// names the missing part).
    /// </summary>
    CallerContextUnavailable = 16,

    /// <summary>Allowed. On the search phase, <see cref="SemanticSearchAccess.Response"/> holds the rows the caller may see.</summary>
    Allowed = 17,
}

/// <summary>
/// The answer of a document-search access decision: an outcome, the machine-readable <c>reasonCode</c> and the text REST
/// sends for a refusal, and, when allowed, the authorized response. It carries no <see cref="IResult"/> and no ProblemDetails,
/// so a non-HTTP host (the MCP document search tool, spaarkeai-mcp-bff-api-r2 task 022) renders it in its own error shape
/// (ADR-019).
/// </summary>
public sealed record SemanticSearchAccess
{
    private SemanticSearchAccess(SemanticSearchAccessOutcome outcome, string? reasonCode, string? detail)
    {
        Outcome = outcome;
        ReasonCode = reasonCode;
        Detail = detail;
    }

    /// <summary>No request context or no signed-in caller (ADR-028 A10): <see cref="DataverseUserClientErrorCodes.UserContextRequired"/>.</summary>
    public static readonly SemanticSearchAccess UserContextRequired = new(
        SemanticSearchAccessOutcome.UserContextRequired,
        DataverseUserClientErrorCodes.UserContextRequired,
        null);

    public SemanticSearchAccessOutcome Outcome { get; }

    /// <summary><c>null</c> when allowed. On a refusal, the <see cref="SearchErrorCodes"/> code REST sends for the same input.</summary>
    public string? ReasonCode { get; }

    /// <summary>On a refusal, the <c>detail</c> text REST sends for the same input; <c>null</c> otherwise.</summary>
    public string? Detail { get; }

    /// <summary>When the search phase is allowed: the rows the caller may see, with recomputed counts and any warning.</summary>
    public SemanticSearchResponse? Response { get; private init; }

    public bool IsAllowed => Outcome == SemanticSearchAccessOutcome.Allowed;

    /// <summary>The request phase's decision, for the REST filter to publish on <see cref="HttpContext.Items"/>.</summary>
    internal SemanticSearchAuthorization? Authorization { get; private init; }

    /// <summary>The request phase's tenant (<c>tid</c>), which the search runs in.</summary>
    internal string? TenantId { get; private init; }

    /// <summary>The count phase's request, narrowed to what was authorized.</summary>
    internal SemanticSearchRequest? CountRequest { get; private init; }

    internal static SemanticSearchAccess Refused(SemanticSearchAccessOutcome outcome, string reasonCode, string detail) =>
        new(outcome, reasonCode, detail);

    internal static SemanticSearchAccess RequestAllowed(SemanticSearchAuthorization authorization, string tenantId) =>
        new(SemanticSearchAccessOutcome.Allowed, null, null) { Authorization = authorization, TenantId = tenantId };

    internal static SemanticSearchAccess SearchAllowed(SemanticSearchResponse response) =>
        new(SemanticSearchAccessOutcome.Allowed, null, null) { Response = response };

    internal static SemanticSearchAccess CountAllowed(SemanticSearchRequest countRequest) =>
        new(SemanticSearchAccessOutcome.Allowed, null, null) { CountRequest = countRequest };

    internal static SemanticSearchAccess CallerContextUnavailable(string reasonCode) =>
        new(SemanticSearchAccessOutcome.CallerContextUnavailable, reasonCode, "Caller context not available.");
}

/// <summary>
/// Authorization filter for semantic search.
/// </summary>
/// <remarks>
/// <para>
/// Follows ADR-008 (endpoint filters for resource-level authorization), ADR-016 (tenant isolation) and
/// ADR-003 (fail closed).
/// </para>
/// <para>
/// <b>What this replaced, and why (unified-access-control-r2 task 070).</b> Every branch of this
/// filter's scope check previously returned <c>new AuthorizationResult(true, null)</c> — entity,
/// documentIds, <c>all</c>, AND <c>default</c>. The only thing actually checked was the <c>tid</c>
/// claim, and the scope was caller-chosen but never caller-authorized. The class remarks listed
/// document-level authorization as a "future enhancement". Because reads on this route are app-only,
/// Dataverse row-level security was inert and this filter was the entire security boundary: any
/// authenticated non-admin could request <c>scope=all</c> and receive every document in the tenant —
/// names, AI summaries, TL;DRs, and SPE pointers. Verified exploitable, then proven end-to-end on
/// 2026-08-25: a non-admin denied Read on all 442 documents by Dataverse listed, opened and downloaded
/// a matter's files through this route on an MDA form.
/// </para>
/// <para>
/// <b>The rule now.</b> Access flows from the parent (SECURE-DOCUMENTS-BUILD-PLAN.md invariant 2):
/// a caller who may read a project/matter/work-assignment may read its documents. So
/// <c>scope=entity</c> authorizes the PARENT — one Dataverse round trip, Dataverse's own answer,
/// evaluated as the caller. <c>scope=documentIds</c> authorizes each named document through the
/// existing document path. <c>scope=all</c> is REFUSED. <c>default</c> DENIES.
/// </para>
/// </remarks>
public sealed class SemanticSearchAuthorizationFilter : IEndpointFilter
{
    private readonly AuthorizationService _authorizationService;
    private readonly ILogger<SemanticSearchAuthorizationFilter>? _logger;

    // Azure AD claim names
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";
    private const string ObjectIdClaimType = "oid";

    /// <summary>
    /// The parent entity types <c>scope=entity</c> may be authorized against, mapped to their Dataverse
    /// entity SET (plural) names.
    /// </summary>
    /// <remarks>
    /// An explicit allow-list, deliberately, rather than pluralizing whatever string the caller sent.
    /// Two reasons. First, an unrecognised <c>entityType</c> must DENY, and a mapping that computes a
    /// set name can always compute one — so it can never deny. Second, the value is interpolated into
    /// the Dataverse request path, so the set of reachable tables should be a fixed list in source, not
    /// a function of request input. Keys cover both the shorthand and the logical name because
    /// <see cref="Sprk.Bff.Api.Services.Ai.SemanticSearch.SemanticSearchService"/>'s associated-only
    /// dispatch accepts both and "both occur in the wild".
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>internal, and deliberately the ONLY such map.</b> The <c>scope=all</c> path added by task 080
    /// authorizes each returned row against its parent, so it needs the same entityType → entity-set
    /// resolution. It consumes THIS dictionary through <see cref="TryResolveAuthorizableEntitySet"/> rather
    /// than declaring its own — a second copy would be a second access policy, and the two would drift
    /// (the project already carries three disagreeing entity-type vocabularies; see the task 080 notes).
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> AuthorizableEntitySets =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter"] = "sprk_matters",
            ["sprk_matter"] = "sprk_matters",
            ["project"] = "sprk_projects",
            ["sprk_project"] = "sprk_projects",
            ["workassignment"] = "sprk_workassignments",
            ["sprk_workassignment"] = "sprk_workassignments",
            ["invoice"] = "sprk_invoices",
            ["sprk_invoice"] = "sprk_invoices",
        };

    /// <summary>
    /// Upper bound on <c>scope=documentIds</c> list length. Deliberately the SAME 100 the
    /// <c>[MaxLength]</c> on <see cref="SemanticSearchRequest.DocumentIds"/> already enforces, so this
    /// filter rejects nothing that was previously valid — a stricter authorization-side cap would break
    /// legitimate callers, and a broken caller gets the endpoint reverted, which reopens the hole.
    /// Over-cap requests are refused rather than truncated: truncation would present a partial list as
    /// though it were the complete set the caller may see.
    /// </summary>
    private const int MaxAuthorizableDocumentIds = 100;

    /// <summary>
    /// A caller-supplied document id must look like a GUID before it costs a Dataverse round trip.
    /// </summary>
    /// <remarks>
    /// The entity path already validates its id (<c>Guid.TryParse</c> on <c>entityId</c>); without the
    /// same check here, 100 arbitrary strings would each buy a <c>RetrievePrincipalAccess</c> call plus
    /// a probe fallback. `scope=documentIds` previously made ZERO Dataverse calls, so this route's load
    /// profile is newly non-trivial and worth bounding at the cheapest point.
    /// </remarks>
    private static bool LooksLikeRecordId(string documentId) => Guid.TryParse(documentId, out _);

    /// <summary>
    /// Resolves a document's <c>parentEntityType</c> to the Dataverse entity SET name its access is
    /// evaluated against. Returns false when the type is not an authorizable parent — callers MUST treat
    /// that as DENY, never as "skip the check".
    /// </summary>
    /// <remarks>
    /// Shared by the request-level <c>scope=entity</c> decision and the row-level <c>scope=all</c>
    /// decision so both answer from one allow-list. Note the map does NOT cover <c>account</c> or
    /// <c>contact</c>, which ARE valid <c>filters.entityTypes</c> values
    /// (<see cref="ValidEntityTypes"/>) — documents parented to those tables therefore fail closed and
    /// are dropped from cross-record results. That is the conservative reading of the build plan's
    /// securable entities (matter / project / work assignment); widening it is a one-line addition and an
    /// owner decision, recorded as O-1 in the task 080 notes.
    /// </remarks>
    internal static bool TryResolveAuthorizableEntitySet(string? parentEntityType, out string entitySetName)
    {
        if (!string.IsNullOrWhiteSpace(parentEntityType)
            && AuthorizableEntitySets.TryGetValue(parentEntityType, out var resolved))
        {
            entitySetName = resolved;
            return true;
        }

        entitySetName = string.Empty;
        return false;
    }

    public SemanticSearchAuthorizationFilter(
        AuthorizationService authorizationService,
        ILogger<SemanticSearchAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService
            ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Binds the request and asks DecideRequestAsync (the ONE owner of the request-level decision, which
        // SearchDocumentsAsCallerAsync also runs); maps its answer to this route's responses. It checks nothing itself.
        var decision = await DecideRequestAsync(
            httpContext, ExtractSearchRequest(context), httpContext.RequestAborted);

        if (decision is { Outcome: SemanticSearchAccessOutcome.Allowed, Authorization: { } authorization })
        {
            // Publish the decision so the endpoint can enforce it at result level.
            httpContext.Items[SemanticSearchAuthorization.HttpContextItemsKey] = authorization;

            return await next(context);
        }

        // The support handle stamped on every denial. Same value AuditEnrichmentMiddleware logs as
        // `correlationId`, so a code a user reads off a client maps to a server-side trace.
        return ToProblem(decision, httpContext.TraceIdentifier);
    }

    /// <summary>
    /// May the caller of <paramref name="httpContext"/> run <paramref name="request"/>, and which rows may they see? THE rule
    /// of <c>POST /api/ai/search</c> for a host that is not that route (the MCP document search tool, spaarkeai-mcp-bff-api-r2
    /// task 022, spec FR-02): the request-level decision this route's filter makes (<see cref="DecideRequestAsync"/>: tenant,
    /// caller, token, scope, parent or document authorization), then the result-level enforcement its handler applies
    /// (<see cref="SearchAndAuthorizeResultsAsync"/>: narrowing, the <c>scope=all</c> candidate pool, the per-row parent check,
    /// SPE pointer stripping, count recomputation, <c>PARTIAL_RESULTS</c>, the readable-document trim), as the caller, with the
    /// same Dataverse questions in the same order. <paramref name="search"/> runs the search itself (request, tenant id,
    /// cancellation); it is called at most once, and only after the request is authorized.
    /// <para>Fails closed (ADR-028 A10): a null context, a principal with no authenticated identity or no request services is
    /// <see cref="SemanticSearchAccess.UserContextRequired"/> before any Dataverse or search call. Every other refusal carries
    /// the code the route sends for the same input. Never falls back to app-only: a principal without a bearer token is
    /// <see cref="SemanticSearchAccessOutcome.MissingCallerToken"/>. A missing <see cref="AuthorizationService"/> or
    /// <see cref="IRetrievalAccessTrim"/> registration throws, as the route's own resolution does.</para>
    /// <para>Not included, because they are input validation rather than access decisions: the handler's
    /// <c>filters.entityTypes</c> check and the <c>[Range(1, 50)]</c> on <c>options.limit</c>. A host validates its own input
    /// before calling; an out-of-range limit is not an access hole (the candidate pool is capped) but a negative one throws.</para>
    /// <para>Pass the request's own context (<c>IHttpContextAccessor.HttpContext</c>) only: the principal and the bearer token
    /// in its <c>Authorization</c> header are read separately and must come from the same validated token. No token is passed
    /// by the caller, and none is logged.</para>
    /// </summary>
    public static async Task<SemanticSearchAccess> SearchDocumentsAsCallerAsync(
        HttpContext? httpContext,
        SemanticSearchRequest? request,
        Func<SemanticSearchRequest, string, CancellationToken, Task<SemanticSearchResponse>> search,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(search);

        if (httpContext?.User is not { } user
            || !user.Identities.Any(i => i.IsAuthenticated)
            || httpContext.RequestServices is not { } services)
        {
            return SemanticSearchAccess.UserContextRequired;
        }

        var logger = services.GetService<ILogger<SemanticSearchAuthorizationFilter>>();
        var authorizationService = services.GetRequiredService<AuthorizationService>();
        var filter = new SemanticSearchAuthorizationFilter(authorizationService, logger);

        var decision = await filter.DecideRequestAsync(httpContext, request, cancellationToken);
        if (decision is not { Outcome: SemanticSearchAccessOutcome.Allowed, Authorization: { } authorization, TenantId: { } tenantId })
        {
            return decision;
        }

        return await SearchAndAuthorizeResultsAsync(
            httpContext,
            request!, // Allowed implies a body: DecideRequestAsync refuses a null one (RequestBodyRequired).
            authorization,
            tenantId,
            search,
            authorizationService,
            services.GetRequiredService<IRetrievalAccessTrim>(),
            (ILogger?)logger ?? NullLogger.Instance,
            cancellationToken);
    }

    /// <summary>
    /// The request-level decision — the ONE implementation, behind both this filter and
    /// <see cref="SearchDocumentsAsCallerAsync"/>. In order: tenant membership, the request body, the caller's <c>oid</c> and
    /// bearer token, then the scope (<see cref="AuthorizeScopeAsync"/>).
    /// </summary>
    private async Task<SemanticSearchAccess> DecideRequestAsync(
        HttpContext httpContext, SemanticSearchRequest? request, CancellationToken ct)
    {
        // Step 1: tenant membership.
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Semantic search denied: no tenant claim in token");
            return SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.MissingTenantIdentity,
                SearchErrorCodes.MissingTenantIdentity,
                "Tenant identity not found in authentication token");
        }

        // Step 2: the request body.
        //
        // A missing body is DENIED here rather than deferred to the endpoint's model validation. The
        // previous implementation called next(context) in this case, which meant "no parseable request"
        // was a path that skipped authorization entirely — the endpoint would then have applied only
        // shape validation. Denying costs a caller with a malformed body a 400 instead of a 403; the
        // alternative costs an unauthorized read.
        if (request is null)
        {
            _logger?.LogWarning(
                "Semantic search denied for tenant {TenantId}: no search request found on the invocation",
                userTenantId);
            return SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.RequestBodyRequired,
                SearchErrorCodes.RequestBodyRequired,
                "A search request body is required.");
        }

        // Step 3: the caller. Both the identity AND the bearer token are required — the token is what
        // makes the downstream Dataverse evaluation run AS THE CALLER instead of as the application.
        // Without it, AuthorizationService fails closed anyway; checking here produces a clearer 401.
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
                "Semantic search denied for tenant {TenantId}: no caller object id in token", userTenantId);
            return SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.MissingCallerIdentity,
                SearchErrorCodes.MissingCallerIdentity,
                "Caller identity not found in authentication token");
        }

        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (string.IsNullOrEmpty(callerToken))
        {
            _logger?.LogWarning(
                "Semantic search denied for caller {CallerId}: no bearer token available, so access " +
                "cannot be evaluated as the caller. Refusing rather than evaluating app-only.",
                callerObjectId);
            return SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.MissingCallerToken,
                SearchErrorCodes.MissingCallerToken,
                "A caller bearer token is required to evaluate access.");
        }

        // Step 4: authorize the request against real records.
        var (authorization, denial) = await AuthorizeScopeAsync(
            request, callerObjectId, callerToken, httpContext, ct);

        return denial ?? SemanticSearchAccess.RequestAllowed(authorization!, userTenantId);
    }

    /// <summary>
    /// Maps a request-level refusal to the response this route has always sent for it (status, title, <c>detail</c>, codes).
    /// </summary>
    private static IResult ToProblem(SemanticSearchAccess decision, string correlationId)
    {
        var (statusCode, title) = decision.Outcome switch
        {
            SemanticSearchAccessOutcome.MissingTenantIdentity => (401, "Unauthorized"),
            SemanticSearchAccessOutcome.RequestBodyRequired => (400, "Bad Request"),
            SemanticSearchAccessOutcome.MissingCallerIdentity => (401, "Unauthorized"),
            SemanticSearchAccessOutcome.MissingCallerToken => (401, "Unauthorized"),
            SemanticSearchAccessOutcome.InvalidScope => (400, "Invalid Scope"),
            SemanticSearchAccessOutcome.EntityTypeRequired => (400, "Bad Request"),
            SemanticSearchAccessOutcome.EntityIdRequired => (400, "Bad Request"),
            SemanticSearchAccessOutcome.EntityTypeNotAuthorizable => (403, "Forbidden"),
            SemanticSearchAccessOutcome.InvalidEntityId => (400, "Bad Request"),
            SemanticSearchAccessOutcome.EntityAccessDenied => (403, "Forbidden"),
            SemanticSearchAccessOutcome.DocumentIdsRequired => (400, "Bad Request"),
            SemanticSearchAccessOutcome.TooManyDocumentIds => (400, "Too Many Document Ids"),
            SemanticSearchAccessOutcome.InvalidDocumentIds => (400, "Invalid Document Ids"),
            SemanticSearchAccessOutcome.NoReadableDocuments => (403, "Forbidden"),
            _ => (0, string.Empty),
        };

        if (statusCode == 0 || decision.ReasonCode is null)
        {
            // Unreachable: the request phase answers Allowed (with a decision) or one of the refusals above. ADR-003: never
            // a pass-through — the same 500 the handler gives a request that reached it with no decision.
            return Results.Problem(
                statusCode: 500,
                title: "Internal Server Error",
                detail: "Authorization context not available.");
        }

        return Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: decision.Detail,
            extensions: ProblemExtensions(decision.ReasonCode, correlationId));
    }

    /// <summary>
    /// Extract SemanticSearchRequest from endpoint arguments.
    /// </summary>
    private static SemanticSearchRequest? ExtractSearchRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is SemanticSearchRequest request)
            {
                return request;
            }
        }
        return null;
    }

    /// <summary>
    /// The extension bag every response from this filter carries: a stable machine-readable code plus
    /// a correlation id, per ADR-019.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why the code appears under two keys.</b> <c>errorCode</c> is ADR-019's canonical name and
    /// what the rest of the BFF emits (~195 sites). <c>code</c> is the key THIS route has always used:
    /// <see cref="Sprk.Bff.Api.Api.Ai.SemanticSearchEndpoints"/> and <c>RecordSearchEndpoints</c> emit
    /// it, this filter's own <c>InvalidScope</c> denial already emitted it, and the
    /// SemanticSearchControl PCF reads <c>errorData.code</c>. Emitting only <c>errorCode</c> would
    /// leave the one shipped client unable to read the codes this change adds; emitting only
    /// <c>code</c> would omit the key the ADR names. Both is a superset, and neither existing
    /// consumer changes.
    /// </para>
    /// <para>
    /// These codes may distinguish cases the <c>detail</c> text deliberately does not. Keeping the
    /// wording uniform is a security property — see the note at the parent-Read denial.
    /// </para>
    /// </remarks>
    private static Dictionary<string, object?> ProblemExtensions(string errorCode, string correlationId) =>
        new()
        {
            ["errorCode"] = errorCode,
            ["code"] = errorCode,
            ["correlationId"] = correlationId
        };

    /// <summary>
    /// Authorizes the requested scope. Returns the decision to carry forward, or a typed refusal
    /// (<see cref="ToProblem"/> renders it for REST). Exactly one of the two is non-null.
    /// </summary>
    private async Task<(SemanticSearchAuthorization? Authorization, SemanticSearchAccess? Denial)> AuthorizeScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        HttpContext httpContext,
        CancellationToken ct)
    {
        // Case-INSENSITIVE comparison, deliberately, instead of the previous
        // `request.Scope?.ToLowerInvariant()` fed into a switch over the SearchScope constants.
        //
        // That was a live bug, and closing the `default:` hole is what exposed it: `SearchScope
        // .DocumentIds` is the camel-cased literal "documentIds", so a lower-cased input could NEVER
        // match that case label. Every scope=documentIds request therefore fell through to `default:`
        // — which returned allow. The bug was invisible precisely because the fall-through was
        // permissive; with `default:` now denying, a broken match becomes a denial rather than an
        // unauthorized read. That is the failure direction this code should have had all along.
        var scope = request.Scope;

        switch (scope)
        {
            case not null when Matches(scope, SearchScope.Entity):
                return await AuthorizeEntityScopeAsync(
                    request, callerObjectId, callerToken, ct);

            case not null when Matches(scope, SearchScope.DocumentIds):
                return await AuthorizeDocumentIdsScopeAsync(
                    request, callerObjectId, callerToken, httpContext, ct);

            case not null when Matches(scope, SearchScope.All):
                // FILTERED, not refused — and not reduced to a pre-enumerated accessible set either.
                //
                // Task 070 refused this scope outright, on the stated premise that "there is no caller
                // that needs it". That premise was FALSE: the SemanticSearch code page emits scope=all
                // for its "All" row, for a blank-label row, and — the highest-traffic path — for every
                // search after the user types a query, because the launch-time entity scope is
                // deliberately dropped at that point. Cross-record search is a capability this product
                // offers (owner decision, 2026-08-26). Refusing was the right stop-gap and the wrong
                // end state.
                //
                // What is NOT done here: enumerating the caller's accessible record set and pre-filtering
                // the query. For a workforce principal that needs Dataverse's real answer, which is task
                // 031's ADR-028 A2 amendment and has not landed; today's AccessibleRecordSetService
                // resolves ADR-034 membership, which is far narrower than what a user can actually read.
                // Gating on it would hide documents from people who can plainly read the parent.
                //
                // Instead the ANSWER is authorized: the endpoint checks each returned row against its own
                // parent record and drops what the caller cannot read. N is a page, not the corpus.
                _logger?.LogInformation(
                    "Semantic search: caller {CallerId} requested scope=all — results will be authorized "
                    + "per row against each document's parent record.",
                    callerObjectId);

                return (new SemanticSearchAuthorization
                {
                    Scope = SearchScope.All,
                    RequiresPerRowParentAuthorization = true
                }, null);

            default:
                // REFUSED. This branch previously returned allow with the comment "let endpoint handle
                // validation" — so an empty or unrecognised scope was an unauthorized read whose only
                // remaining gate was shape validation. An unknown scope cannot be authorized, because
                // there is nothing to authorize it against.
                //
                // 400 rather than 403, deliberately. Only `all`, `entity` and `documentIds` exist, and
                // all three are handled above, so reaching here means the scope was absent or not a
                // scope at all — a malformed request, which is what the endpoint's own validation has
                // always called it. The security property is unchanged either way (next() is not
                // called); the status code is a contract question, and clients already treat a bad
                // scope as 400. Answering 403 would break them to no benefit.
                _logger?.LogWarning(
                    "Semantic search REFUSED for caller {CallerId}: scope '{Scope}' is empty or unknown.",
                    callerObjectId, request.Scope ?? "(none)");

                // Mirrors the wording and error code the endpoint's own ValidateScope produced, so the
                // contract a client sees for a bad scope is unchanged by the filter now answering first.
                // `all` is advertised again as of task 080: it is permitted-and-filtered rather than
                // refused, so omitting it here would now be actively misleading.
                return (null, SemanticSearchAccess.Refused(
                    SemanticSearchAccessOutcome.InvalidScope,
                    SearchErrorCodes.InvalidScope,
                    $"Invalid scope value '{request.Scope}'. Valid values: all, entity, documentIds."));
        }
    }

    /// <summary>Scope comparison. Case-insensitive — see the note in <see cref="AuthorizeScopeAsync"/>.</summary>
    private static bool Matches(string scope, string known) =>
        string.Equals(scope, known, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>scope=entity</c>: authorize the caller's Read on the PARENT record. One round trip.
    /// </summary>
    private async Task<(SemanticSearchAuthorization?, SemanticSearchAccess?)> AuthorizeEntityScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.EntityId))
        {
            // One message for both, as before. The CODE names which field is missing — that costs an
            // unauthenticated caller nothing, since they supplied the request being described.
            const string detail = "entityType and entityId are required when scope=entity.";
            return (null, string.IsNullOrWhiteSpace(request.EntityType)
                ? SemanticSearchAccess.Refused(
                    SemanticSearchAccessOutcome.EntityTypeRequired, SearchErrorCodes.EntityTypeRequired, detail)
                : SemanticSearchAccess.Refused(
                    SemanticSearchAccessOutcome.EntityIdRequired, SearchErrorCodes.EntityIdRequired, detail));
        }

        if (!AuthorizableEntitySets.TryGetValue(request.EntityType, out var entitySetName))
        {
            _logger?.LogWarning(
                "Semantic search DENIED for caller {CallerId}: entityType '{EntityType}' is not an "
                + "authorizable parent type.",
                callerObjectId, request.EntityType);

            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.EntityTypeNotAuthorizable,
                SearchErrorCodes.EntityTypeNotAuthorizable,
                $"entityType '{request.EntityType}' is not supported for scoped search."));
        }

        if (!Guid.TryParse(request.EntityId, out var parentId) || parentId == Guid.Empty)
        {
            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.InvalidEntityId,
                SearchErrorCodes.InvalidEntityId,
                "entityId must be a non-empty GUID."));
        }

        var snapshot = await _authorizationService.GetCallerRecordAccessAsync(
            callerObjectId, entitySetName, parentId, callerToken, ct);

        if (!snapshot.AccessRights.HasFlag(AccessRights.Read))
        {
            // Uniform 403 whether the record is unreadable or absent — distinguishing them would
            // confirm the existence of records the caller cannot see. The error CODE is uniform here
            // for the same reason: one code covers both, because the two cases must stay
            // indistinguishable to the caller in EVERY channel, not just the prose.
            _logger?.LogWarning(
                "Semantic search DENIED: caller {CallerId} has no Read on {EntitySet}({ParentId}) "
                + "(rights={Rights})",
                callerObjectId, entitySetName, parentId, snapshot.AccessRights);

            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.EntityAccessDenied,
                SearchErrorCodes.EntityAccessDenied,
                "You do not have access to this record."));
        }

        _logger?.LogInformation(
            "Semantic search authorized: caller {CallerId} holds {Rights} on {EntitySet}({ParentId})",
            callerObjectId, snapshot.AccessRights, entitySetName, parentId);

        return (new SemanticSearchAuthorization
        {
            Scope = SearchScope.Entity,
            AuthorizedParentId = parentId
        }, null);
    }

    /// <summary>
    /// <c>scope=documentIds</c>: authorize each named document through the document path, then THE external document
    /// narrowing, and carry the permitted subset forward.
    /// </summary>
    /// <remarks>
    /// <b>The narrowing is here, not only downstream (task 188 round 4d, #2199).</b> <c>POST /api/ai/search</c> trims its
    /// rows afterwards (<c>IRetrievalAccessTrim</c>), but <c>POST /api/ai/search/count</c> has no rows to trim and counts
    /// over exactly this set, so a document the narrowing hides answered "does it match the query" through the count.
    /// Narrowing the SET makes both routes see only what the caller may see; refusing documentIds counts for every
    /// caller who is not internal would also close it, but would take a working count away from callers counting
    /// documents they may see. One call over the readable ids (an unreadable id is never sent); a document it hides is
    /// dropped like an unreadable one (all hidden → the same 403); a narrowing fault hides every id; an internal
    /// systemuser makes no call and no read.
    /// </remarks>
    private async Task<(SemanticSearchAuthorization?, SemanticSearchAccess?)> AuthorizeDocumentIdsScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var requested = request.DocumentIds;
        if (requested is null || requested.Count == 0)
        {
            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.DocumentIdsRequired,
                SearchErrorCodes.DocumentIdsRequired,
                "documentIds is required and must not be empty when scope=documentIds."));
        }

        if (requested.Count > MaxAuthorizableDocumentIds)
        {
            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.TooManyDocumentIds,
                SearchErrorCodes.TooManyDocumentIds,
                $"documentIds is limited to {MaxAuthorizableDocumentIds} entries per request."));
        }

        // A malformed id is a BAD REQUEST, not an access failure — and saying so matters.
        //
        // An earlier draft silently dropped non-GUID ids from the candidate list. That produced the
        // wrong answer with a straight face: every id being unparseable left the authorized set empty,
        // which fell through to the 403 below telling the caller "you do not have access to any of the
        // requested documents" when the truth was "those are not document ids". A denial that
        // misattributes its own cause sends the reader looking at permissions instead of at their
        // payload. It also mirrors the entity path, which already rejects a non-GUID entityId with 400.
        var malformed = requested
            .Where(id => !string.IsNullOrWhiteSpace(id) && !LooksLikeRecordId(id))
            .ToList();

        if (malformed.Count > 0)
        {
            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.InvalidDocumentIds,
                SearchErrorCodes.InvalidDocumentIds,
                $"documentIds must be GUIDs. Invalid: {string.Join(", ", malformed.Take(5))}"
                + (malformed.Count > 5 ? $" (+{malformed.Count - 5} more)" : string.Empty)));
        }

        var candidates = requested
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var authorized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // SEQUENTIAL, deliberately — an earlier draft ran these 8-at-a-time.
        //
        // The original reason is gone: `GetCallerAccessAsync` reaches
        // DataverseAccessDataSource.GetUserAccessAsync, which used to assign
        // `_httpClient.DefaultRequestHeaders.Authorization` (not thread-safe) before its reads. Since
        // issue #1576 every read there carries its token on its own HttpRequestMessage, so the
        // header race that made parallelism unsafe no longer exists.
        //
        // It stays sequential because nothing else has been proven safe to run concurrently inside one
        // request scope — the cache decorator and the authorization service above the data source were
        // never reviewed for it — and the cost is bounded: repeat lookups are absorbed by
        // CachedAccessDataSource, and real documentIds lists are short. A flaky authorization result is
        // worse than a slower one. Parallelising this loop is a separate change with its own review.
        foreach (var documentId in candidates)
        {
            var snapshot = await _authorizationService.GetCallerAccessAsync(
                callerObjectId, documentId, callerToken, ct);

            if (snapshot.AccessRights.HasFlag(AccessRights.Read))
            {
                authorized.Add(documentId);
            }
        }

        // Task 188 round 4d (#2199): THE external document narrowing over the readable ids — see the remarks.
        if (authorized.Count > 0)
        {
            var visibility = await InternalPlaneDocumentNarrowing.ForCallerAsync(
                httpContext, authorized.Select(id => (string?)id).ToList(), "semantic-search-document-ids", _logger);
            if (visibility.Narrowed)
            {
                var hidden = authorized.RemoveWhere(id => !visibility.IsShown(id));
                if (hidden > 0)
                {
                    _logger?.LogInformation(
                        "Semantic search: the external document narrowing withheld {Hidden} of {Readable} readable documents",
                        hidden, hidden + authorized.Count);
                }
            }
        }

        // Every requested document was unreadable: refuse rather than return an empty result set. An
        // empty 200 reads as "there is nothing there", which is a claim about content; a 403 is a claim
        // about the caller, which is the true one.
        if (authorized.Count == 0)
        {
            _logger?.LogWarning(
                "Semantic search DENIED: caller {CallerId} has Read on none of the {Count} requested documents",
                callerObjectId, requested.Count);

            return (null, SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.NoReadableDocuments,
                SearchErrorCodes.NoReadableDocuments,
                "You do not have access to any of the requested documents."));
        }

        _logger?.LogInformation(
            "Semantic search authorized: caller {CallerId} may read {Authorized} of {Requested} documents",
            callerObjectId, authorized.Count, requested.Count);

        return (new SemanticSearchAuthorization
        {
            Scope = SearchScope.DocumentIds,
            AuthorizedDocumentIds = authorized
        }, null);
    }
    /// <summary>
    /// How many candidate rows to draw from the index per permitted row the caller asked for, when
    /// <c>scope=all</c> results must be authorized row by row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cross-record results are authorized AFTER the index ranks them, so a caller entitled to few
    /// records can have every one of their matches ranked below the page boundary. Fetching exactly
    /// <c>limit</c> rows and then filtering would return an empty page while matches existed further
    /// down — an over-filtering failure that is indistinguishable from "nothing matched". Drawing a
    /// deeper candidate pool is what makes the acceptance criterion ("entitled to 3 of 50 → gets
    /// exactly 3") reachable at all.
    /// </para>
    /// <para>
    /// Bounded at 3×, not more: every fetched row is enriched from Dataverse by
    /// <c>EnrichResultsWithDataverseMetadataAsync</c> before it reaches here, so the candidate pool is
    /// not free — a large factor buys recall with latency the caller pays on every keystroke-driven
    /// search.
    /// </para>
    /// </remarks>
    private const int CrossRecordOverFetchFactor = 3;

    /// <summary>Absolute ceiling on the candidate pool, whatever the requested limit.</summary>
    private const int CrossRecordOverFetchCap = 150;

    /// <summary>
    /// Upper bound on DISTINCT parent records this endpoint will evaluate access for while assembling
    /// one cross-record page.
    /// </summary>
    /// <remarks>
    /// The cost that matters is distinct parents, not rows: twenty documents from one matter cost ONE
    /// Dataverse round trip, and repeats inside the window are absorbed by <c>CachedAccessDataSource</c>.
    /// The typical page touches one to three parents. This budget exists for the pathological page where
    /// every row has a different parent — 25 sequential checks is roughly two seconds worst case, which
    /// is a slow search; 150 would be a broken one. Exhausting the budget does NOT relax the decision:
    /// unexamined rows are dropped and the response is marked incomplete, so the ceiling can only ever
    /// cost recall, never authorization.
    /// </remarks>
    private const int MaxParentAuthorizationChecks = 25;


    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────
    // Result level (spaarkeai-mcp-bff-api-r2 task 027): moved here from SemanticSearchEndpoints, unchanged, so the request
    // decision and the enforcement of it on the results have one owner. The handler binds and maps; so does
    // SearchDocumentsAsCallerAsync.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the search for an authorized request and enforces the decision on its results — the ONE implementation, behind
    /// both <c>POST /api/ai/search</c>'s handler and <see cref="SearchDocumentsAsCallerAsync"/>. The request is narrowed to
    /// what was authorized (and, for <c>scope=all</c>, widened to the candidate pool); after the search, <c>scope=all</c> rows
    /// are authorized one by one against their parents (<see cref="AuthorizeRowsByParentAsync"/>) and every other scope is
    /// held to its decision (<see cref="AuthorizeResults"/>); then every surviving row is trimmed to documents the caller can
    /// read (<see cref="TrimToReadableDocumentsAsync"/>).
    /// </summary>
    /// <returns>
    /// <see cref="SemanticSearchAccessOutcome.Allowed"/> with the response, or
    /// <see cref="SemanticSearchAccessOutcome.CallerContextUnavailable"/> when a <c>scope=all</c> search finds no caller
    /// <c>oid</c> or bearer token on the context (the request phase saw both; the handler answers 500).
    /// </returns>
    internal static async Task<SemanticSearchAccess> SearchAndAuthorizeResultsAsync(
        HttpContext httpContext,
        SemanticSearchRequest request,
        SemanticSearchAuthorization authorization,
        string tenantId,
        Func<SemanticSearchRequest, string, CancellationToken, Task<SemanticSearchResponse>> search,
        AuthorizationService authorizationService,
        IRetrievalAccessTrim accessTrim,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var callerLimit = request.Options?.Limit ?? 20;

        // scope=all draws a deeper candidate pool so that row-level authorization can still fill a
        // page when the caller's readable records rank below the page boundary. Every other scope
        // was already constrained by the filter and is fetched exactly as asked.
        var searchRequest = NarrowToAuthorized(request, authorization);
        if (authorization.RequiresPerRowParentAuthorization)
        {
            searchRequest = WithCrossRecordCandidatePool(searchRequest, callerLimit);
        }

        var response = await search(searchRequest, tenantId, cancellationToken);

        if (authorization.RequiresPerRowParentAuthorization)
        {
            // The filter proved both of these present before it permitted the request; if either is
            // missing now, the pipeline changed underneath us. Refuse rather than serve the
            // unfiltered cross-record answer — that answer IS the disclosure task 070 closed.
            var callerObjectId = CallerResolution.ResolveObjectId(httpContext.User);
            var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);

            if (string.IsNullOrEmpty(callerObjectId) || string.IsNullOrEmpty(callerToken))
            {
                logger.LogError(
                    "Cross-record search reached the handler without a caller identity or bearer "
                    + "token, which the authorization filter guarantees. Refusing.");

                return SemanticSearchAccess.CallerContextUnavailable(
                    string.IsNullOrEmpty(callerObjectId)
                        ? SearchErrorCodes.MissingCallerIdentity
                        : SearchErrorCodes.MissingCallerToken);
            }

            response = await AuthorizeRowsByParentAsync(
                response, callerLimit, callerObjectId, callerToken,
                authorizationService, logger, cancellationToken);
        }
        else
        {
            response = AuthorizeResults(response, authorization, logger);
        }

        // Task 176 fix round 2 (#1511): the steps above decide by PARENT record (or, for scope=documentIds,
        // by the named documents). A restricted or secure document under a readable parent passes them, so
        // every surviving row is also trimmed to documents the CALLER can read (one caller-scoped read per
        // page). Fail closed: a failed check returns no rows.
        response = await TrimToReadableDocumentsAsync(
            response, accessTrim, CallerResolution.ResolveObjectId(httpContext.User), logger, cancellationToken);

        return SemanticSearchAccess.SearchAllowed(response);
    }

    /// <summary>
    /// The count route's half of the decision (<c>POST /api/ai/search/count</c>): <c>scope=all</c> is refused, every other
    /// authorized request is narrowed to what was authorized (<see cref="SemanticSearchAccess.CountRequest"/>).
    /// </summary>
    /// <remarks>
    /// <para>scope=all is permitted on /search but REFUSED here, and the asymmetry is the point.</para>
    /// <para>/search enforces cross-record access by dropping rows the caller may not read. A COUNT has no
    /// rows to drop: the only thing it can return is a number derived from the unfiltered corpus,
    /// which discloses how many documents exist tenant-wide. That is a smaller leak than the
    /// documents themselves but the same kind, and it is exactly what task 070 closed.</para>
    /// <para>Counting only what the caller may read would mean authorizing the whole matching corpus rather
    /// than a page — unbounded work for a number, and it needs the accessible-record-set enumeration
    /// that task 031 has not delivered. Refusing is honest; a filtered-looking count would not be.</para>
    /// <para>The count has no rows to post-filter, so narrowing the REQUEST is the whole enforcement
    /// here: for scope=documentIds the query is reduced to the ids the caller may read, and for
    /// scope=entity the parent was authorized by the filter and SearchFilterBuilder constrains
    /// the query to it.</para>
    /// </remarks>
    internal static SemanticSearchAccess DecideCount(
        SemanticSearchRequest request, SemanticSearchAuthorization authorization, ILogger logger)
    {
        if (authorization.RequiresPerRowParentAuthorization)
        {
            logger.LogWarning(
                "Semantic search count REFUSED: scope=all cannot be counted without disclosing the "
                + "unfiltered corpus size. Caller should count within a parent record or document set.");

            return SemanticSearchAccess.Refused(
                SemanticSearchAccessOutcome.ScopeAllNotPermitted,
                SearchErrorCodes.ScopeAllNotPermitted,
                "scope=all is not supported for count. Count within a specific parent record "
                + "(scope=entity) or a specific set of documents (scope=documentIds).");
        }

        return SemanticSearchAccess.CountAllowed(NarrowToAuthorized(request, authorization));
    }

    /// <summary>
    /// Trims a parent-authorized page to the documents the caller can read (task 176 fix round 2) and recomputes the
    /// counts: rows the trim removed are subtracted from <c>TotalResults</c>, which keeps each path's paging contract
    /// (a page total for scope=entity, the returned count for scope=all), and <c>ReturnedResults</c> is the row count.
    /// </summary>
    internal static async Task<SemanticSearchResponse> TrimToReadableDocumentsAsync(
        SemanticSearchResponse response,
        Sprk.Bff.Api.Services.Ai.PublicContracts.IRetrievalAccessTrim accessTrim,
        string? callerObjectId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var trim = await accessTrim.TrimAsync(response.Results, r => r.DocumentId, callerObjectId, cancellationToken);
        if (trim.Withheld)
        {
            logger.LogWarning(
                "Semantic search withheld all {Count} row(s): document access could not be checked ({Outcome}); fail closed",
                response.Results.Count, trim.Outcome);
        }

        var removed = response.Results.Count - trim.Rows.Count;
        if (removed == 0)
        {
            return response;
        }

        return response with
        {
            Results = trim.Rows,
            Metadata = response.Metadata with
            {
                TotalResults = Math.Max(0, response.Metadata.TotalResults - removed),
                ReturnedResults = trim.Rows.Count,
            }
        };
    }


    /// <summary>
    /// Reduces the request to what the caller was actually authorized for, before the search runs.
    /// </summary>
    /// <remarks>
    /// For <c>scope=documentIds</c> the caller may name documents they cannot read; the filter worked
    /// out which subset is permitted, and this narrows the query to that subset so unauthorized ids
    /// never reach the index or Dataverse at all. For <c>scope=entity</c> nothing needs narrowing — the
    /// parent itself was authorized, and the existing filter expression already scopes to it.
    /// </remarks>
    private static SemanticSearchRequest NarrowToAuthorized(
        SemanticSearchRequest request, SemanticSearchAuthorization authorization)
    {
        // ADR-044 — canonicalize the ids at the boundary this filter now owns.
        //
        // The filter parses entityId with Guid.TryParse, which happily accepts `{ABC-...}` and uppercase
        // forms, and then discarded the parsed value. The RAW caller string flowed on to
        // SearchFilterBuilder, which emits `parentEntityId eq '<raw>'` — a CASE-SENSITIVE Edm.String
        // comparison. So a braced or upper-cased GUID from Xrm would pass authorization and then match
        // zero index rows: an authorization layer and a query layer disagreeing about identity. Writing
        // back the canonical form the filter already computed removes the disagreement.
        if (authorization.AuthorizedParentId is { } parentId)
        {
            return request with { EntityId = parentId.ToString() };
        }

        if (authorization.AuthorizedDocumentIds is not { } allowed)
        {
            return request;
        }

        return request with
        {
            DocumentIds = request.DocumentIds?
                .Where(allowed.Contains)
                .Select(id => Guid.TryParse(id, out var g) ? g.ToString() : id)
                .ToList() ?? []
        };
    }

    /// <summary>
    /// Enforces the authorization decision on the RESULTS, and strips SPE pointers from the response.
    /// </summary>
    /// <remarks>
    /// <para><b>Why results are re-checked when the query was already scoped.</b> The semantic path
    /// answers from an Azure AI Search index, which is a separate data plane carrying no ACL data and no
    /// guarantee of freshness. A filter expression there is a query predicate, not an authorization
    /// decision: if a document is reparented in Dataverse and the index still holds the old parent, a
    /// parent-scoped query returns a document that no longer belongs to the authorized parent. Checking
    /// each row's parent against the authorized parent closes that, and costs nothing — the value is
    /// already on the row, so there is no extra round trip.</para>
    ///
    /// <para><b>Counts are recomputed.</b> Leaving <c>totalResults</c> at the pre-filter number would
    /// report how many documents exist beyond what was returned — a smaller leak than the documents
    /// themselves, but the same kind.</para>
    ///
    /// <para><b>Pointers are stripped unconditionally.</b> Under the broker-only decision no client needs
    /// <c>driveId</c>/<c>speFileId</c>: file access goes through document-id-keyed BFF routes that carry
    /// the standard gate. Returning pointers invites clients to address SPE directly, which is the
    /// pattern that produced the ungated drive-keyed routes in the first place.</para>
    /// </remarks>
    private static SemanticSearchResponse AuthorizeResults(
        SemanticSearchResponse response,
        SemanticSearchAuthorization authorization,
        ILogger logger)
    {
        var permitted = response.Results.Where(r => IsPermitted(r, authorization)).ToList();

        var dropped = response.Results.Count - permitted.Count;
        if (dropped > 0)
        {
            // Not routine. Reaching here means the query returned rows outside the authorized scope,
            // which for scope=entity means the index disagrees with Dataverse about parentage.
            logger.LogWarning(
                "Semantic search dropped {Dropped} of {Total} results that fell outside the authorized "
                + "scope (scope={Scope}). Expected zero — investigate index/Dataverse divergence.",
                dropped, response.Results.Count, authorization.Scope);
        }

        // Strip SPE pointers from every surviving row.
        var sanitized = permitted
            .Select(r => r with { DriveId = null, SpeFileId = null })
            .ToList();

        return response with
        {
            Results = sanitized,
            Metadata = response.Metadata with
            {
                // Subtract what authorization removed — do NOT clamp to the page size.
                //
                // An earlier draft used Math.Min(TotalResults, sanitized.Count). That is leak-free but
                // arithmetically wrong wherever TotalResults is a CORPUS total rather than a page count:
                // the associated-only path reports the full match count, so a matter with 100 documents
                // and limit=20 would have reported 20 and quietly broken pagination. The AI-Search path
                // reports a page-level count, where both forms agree — which is why the existing tests
                // could not tell the two apart.
                TotalResults = Math.Max(0, response.Metadata.TotalResults - dropped),
                ReturnedResults = sanitized.Count
            }
        };
    }

    private static bool IsPermitted(SearchResult result, SemanticSearchAuthorization authorization)
    {
        if (authorization.AuthorizedDocumentIds is { } allowedDocuments)
        {
            return result.DocumentId is { } id && allowedDocuments.Contains(id);
        }

        if (authorization.AuthorizedParentId is { } parentId)
        {
            // Fail closed on a row with no parent: it cannot be shown to belong to the authorized
            // parent, and "unknown parentage" is exactly the case that must not be served.
            return Guid.TryParse(result.ParentEntityId, out var rowParent) && rowParent == parentId;
        }

        return false;
    }

    /// <summary>
    /// Widens the index fetch for a cross-record search so row-level authorization has candidates to
    /// work with. See <see cref="CrossRecordOverFetchFactor"/> for why this is necessary and bounded.
    /// </summary>
    /// <remarks>
    /// The DTO's <c>[Range(1, 50)]</c> on <see cref="SearchOptions.Limit"/> has already been enforced
    /// against the CALLER's value by model validation; this is a server-computed internal value that
    /// deliberately exceeds it, and it is never echoed back to the caller.
    /// </remarks>
    private static SemanticSearchRequest WithCrossRecordCandidatePool(
        SemanticSearchRequest request, int callerLimit)
    {
        var poolSize = CrossRecordCandidatePoolSize(callerLimit);
        var options = request.Options ?? new SearchOptions();

        return request with { Options = options with { Limit = poolSize } };
    }

    private static int CrossRecordCandidatePoolSize(int callerLimit) =>
        Math.Min(Math.Max(callerLimit, 1) * CrossRecordOverFetchFactor, CrossRecordOverFetchCap);

    /// <summary>
    /// Authorizes a cross-record (<c>scope=all</c>) result set ROW BY ROW against each document's parent
    /// record, and returns at most <paramref name="callerLimit"/> rows the caller may actually read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Grouped by distinct parent, evaluated in relevance order, lazily.</b> The unit of cost is the
    /// distinct parent record, not the row: a page of twenty documents belonging to one matter is ONE
    /// Dataverse round trip. Rows are walked in the order the index ranked them and evaluation stops as
    /// soon as the caller's page is full, so the common case spends one to three checks. Repeat parents
    /// within a request hit the local memo; repeats across requests hit
    /// <c>CachedAccessDataSource</c>'s entity-set-qualified 60 s key.
    /// </para>
    /// <para>
    /// <b>Fail closed per row (ADR-003).</b> A row is dropped unless its parent type resolves to an
    /// authorizable table AND its parent id parses as a non-empty GUID AND Dataverse says the caller
    /// holds Read. "Unknown parentage" is the case that must not be served, so it is never a skip.
    /// </para>
    /// <para>
    /// <b>Sequential, deliberately</b> — the same constraint documented on task 070's documentIds loop. Its
    /// original reason (<c>DataverseAccessDataSource</c> assigning the shared
    /// <c>_httpClient.DefaultRequestHeaders.Authorization</c> mid-request) is gone since issue #1576 — every read
    /// there carries its own token. It stays sequential because nothing above the data source has been reviewed for
    /// concurrent use within one request scope, and a flaky authorization result is worse than a slower one.
    /// </para>
    /// <para>
    /// <b>Why a short page is announced.</b> Because rows are authorized after the index ranks them, a
    /// caller entitled to few records can have their matches ranked below the candidate pool, or below
    /// the check budget. The result is a page shorter than requested — which is indistinguishable, by
    /// inspection, from "there simply are not many matches". That is the failure mode this whole task has
    /// to defeat: it looks like success. A <c>PARTIAL_RESULTS</c> warning is what makes it legible.
    /// </para>
    /// </remarks>
    private static async Task<SemanticSearchResponse> AuthorizeRowsByParentAsync(
        SemanticSearchResponse response,
        int callerLimit,
        string callerObjectId,
        string callerToken,
        AuthorizationService authorizationService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var permitted = new List<SearchResult>(callerLimit);
        var decisions = new Dictionary<(string EntitySet, Guid ParentId), bool>();

        var examined = 0;
        var checksSpent = 0;
        var unresolvableParents = 0;
        var budgetExhausted = false;

        foreach (var row in response.Results)
        {
            if (permitted.Count >= callerLimit)
            {
                break;
            }

            examined++;

            if (!SemanticSearchAuthorizationFilter.TryResolveAuthorizableEntitySet(
                    row.ParentEntityType, out var entitySetName)
                || !Guid.TryParse(row.ParentEntityId, out var parentId)
                || parentId == Guid.Empty)
            {
                unresolvableParents++;
                continue;
            }

            var key = (entitySetName, parentId);
            if (!decisions.TryGetValue(key, out var readable))
            {
                if (checksSpent >= MaxParentAuthorizationChecks)
                {
                    // Stop spending. Everything not yet evaluated stays unserved.
                    budgetExhausted = true;
                    examined--;
                    break;
                }

                checksSpent++;

                var snapshot = await authorizationService.GetCallerRecordAccessAsync(
                    callerObjectId, entitySetName, parentId, callerToken, cancellationToken);

                readable = snapshot.AccessRights.HasFlag(AccessRights.Read);
                decisions[key] = readable;
            }

            if (readable)
            {
                permitted.Add(row);
            }
        }

        // Pointers stripped for the same reason as every other scope: under the broker-only decision no
        // client needs driveId/speFileId, and returning them invites clients to address SPE directly.
        var sanitized = permitted
            .Select(r => r with { DriveId = null, SpeFileId = null })
            .ToList();

        var droppedForAccess = examined - permitted.Count;

        // "This page may be missing documents you are entitled to see." True in exactly two cases:
        //
        //   1. The parent-check budget ran out, so rows were left unevaluated. Always incomplete.
        //   2. The candidate pool came back SATURATED — the index had at least as many rows as we were
        //      willing to draw — and we still could not fill the caller's page. The shortfall might have
        //      been fillable from rows we never fetched.
        //
        // Deliberately NOT `droppedForAccess > 0`, which was the first cut: that fires whenever ANY row
        // was withheld, including the common case where the pool was not saturated and we therefore
        // examined every matching document there was. That warning would be present on nearly every
        // filtered cross-record search, and a warning that always fires is noise — which gets ignored,
        // which restores exactly the blindness it exists to cure.
        //
        // A full page is not incomplete: the caller got what they asked for, and matches ranked below it
        // are a paging question, not an authorization one (see the paging contract, task 080 notes §1).
        var poolWasSaturated = response.Results.Count >= CrossRecordCandidatePoolSize(callerLimit);
        var incomplete = budgetExhausted || (poolWasSaturated && sanitized.Count < callerLimit);

        var warnings = response.Metadata.Warnings is { } existing
            ? new List<SearchWarning>(existing)
            : new List<SearchWarning>();

        if (incomplete)
        {
            warnings.Add(new SearchWarning
            {
                Code = SearchWarningCode.PartialResults,
                Message = "Some matching documents were withheld because you do not have access to their "
                          + "parent records, and further matches may exist beyond the results examined. "
                          + "Search within a specific record for a complete list."
            });
        }

        logger.LogInformation(
            "Cross-record search authorized for caller {CallerId}: {Permitted} of {Examined} examined rows "
            + "permitted ({Checks} distinct parents evaluated, {Unresolvable} rows had unauthorizable "
            + "parentage, budgetExhausted={BudgetExhausted}, incomplete={Incomplete})",
            callerObjectId, sanitized.Count, examined, checksSpent, unresolvableParents,
            budgetExhausted, incomplete);

        return response with
        {
            Results = sanitized,
            Metadata = response.Metadata with
            {
                // The count of rows in THIS response — the paging contract in the task 080 notes §1.
                //
                // Deliberately NOT `TotalResults - dropped`, which is what the request-scoped path above
                // does. That subtraction is right there because the pre-filter total describes the same
                // page being filtered. Here the pre-filter total describes the OVER-FETCHED candidate
                // pool, so subtracting drops from it would report a number drawn from a page the caller
                // never sees. What the caller can actually reach is exactly what is returned.
                TotalResults = sanitized.Count,
                ReturnedResults = sanitized.Count,
                Warnings = warnings.Count > 0 ? warnings : null
            }
        };
    }

}
