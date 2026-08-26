using System.Security.Claims;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Models.Ai.SemanticSearch;

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
public class SemanticSearchAuthorizationFilter : IEndpointFilter
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
    private static readonly IReadOnlyDictionary<string, string> AuthorizableParentEntitySets =
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

        // The support handle stamped on every denial below. Same value AuditEnrichmentMiddleware
        // logs as `correlationId`, so a code a user reads off a client maps to a server-side trace.
        var correlationId = httpContext.TraceIdentifier;

        // Step 1: tenant membership.
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Semantic search denied: no tenant claim in token");
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token",
                extensions: ProblemExtensions(SearchErrorCodes.MissingTenantIdentity, correlationId));
        }

        // Step 2: the request body.
        //
        // A missing body is DENIED here rather than deferred to the endpoint's model validation. The
        // previous implementation called next(context) in this case, which meant "no parseable request"
        // was a path that skipped authorization entirely — the endpoint would then have applied only
        // shape validation. Denying costs a caller with a malformed body a 400 instead of a 403; the
        // alternative costs an unauthorized read.
        var request = ExtractSearchRequest(context);
        if (request is null)
        {
            _logger?.LogWarning(
                "Semantic search denied for tenant {TenantId}: no search request found on the invocation",
                userTenantId);
            return Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "A search request body is required.",
                extensions: ProblemExtensions(SearchErrorCodes.RequestBodyRequired, correlationId));
        }

        // Step 3: the caller. Both the identity AND the bearer token are required — the token is what
        // makes the downstream Dataverse evaluation run AS THE CALLER instead of as the application.
        // Without it, AuthorizationService fails closed anyway; checking here produces a clearer 401.
        var callerObjectId = httpContext.User.FindFirst(ObjectIdClaimType)?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(callerObjectId))
        {
            _logger?.LogWarning(
                "Semantic search denied for tenant {TenantId}: no caller object id in token", userTenantId);
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Caller identity not found in authentication token",
                extensions: ProblemExtensions(SearchErrorCodes.MissingCallerIdentity, correlationId));
        }

        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (string.IsNullOrEmpty(callerToken))
        {
            _logger?.LogWarning(
                "Semantic search denied for caller {CallerId}: no bearer token available, so access " +
                "cannot be evaluated as the caller. Refusing rather than evaluating app-only.",
                callerObjectId);
            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "A caller bearer token is required to evaluate access.",
                extensions: ProblemExtensions(SearchErrorCodes.MissingCallerToken, correlationId));
        }

        // Step 4: authorize the request against real records.
        var (authorization, denial) = await AuthorizeScopeAsync(
            request, callerObjectId, callerToken, correlationId, httpContext.RequestAborted);

        if (denial is not null)
        {
            return denial;
        }

        // Publish the decision so the endpoint can enforce it at result level.
        httpContext.Items[SemanticSearchAuthorization.HttpContextItemsKey] = authorization;

        return await next(context);
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
    /// Authorizes the requested scope. Returns the decision to carry forward, or an <see cref="IResult"/>
    /// denial. Exactly one of the two is non-null.
    /// </summary>
    private async Task<(SemanticSearchAuthorization? Authorization, IResult? Denial)> AuthorizeScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        string correlationId,
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
                    request, callerObjectId, callerToken, correlationId, ct);

            case not null when Matches(scope, SearchScope.DocumentIds):
                return await AuthorizeDocumentIdsScopeAsync(
                    request, callerObjectId, callerToken, correlationId, ct);

            case not null when Matches(scope, SearchScope.All):
                // REFUSED, not reduced to the caller's accessible set.
                //
                // Reducing would be kinder to a UI, but there is no caller that needs it: the flagship
                // consumers are parent-scoped (a Matter form's document list), and a tenant-wide
                // document search is not a capability this product has decided to offer. Refusing is
                // also the only option that cannot be subtly wrong — a reduction is one filter bug away
                // from being the disclosure again, and that bug would be invisible because the response
                // would still look plausible.
                _logger?.LogWarning(
                    "Semantic search REFUSED for caller {CallerId}: scope=all is not permitted.",
                    callerObjectId);

                return (null, Results.Problem(
                    statusCode: 403,
                    title: "Forbidden",
                    detail: "scope=all is not permitted. Search within a specific parent record "
                            + "(scope=entity) or a specific set of documents (scope=documentIds).",
                    extensions: ProblemExtensions(
                        SearchErrorCodes.ScopeAllNotPermitted, correlationId)));

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
                // contract a client sees for a bad scope is unchanged by the filter now answering
                // first. Note the advertised list omits `all` — it is a valid scope VALUE that is
                // refused, so offering it here would send callers straight into a 403.
                return (null, Results.Problem(
                    statusCode: 400,
                    title: "Invalid Scope",
                    detail: $"Invalid scope value '{request.Scope}'. Valid values: entity, documentIds.",
                    extensions: ProblemExtensions(SearchErrorCodes.InvalidScope, correlationId)));
        }
    }

    /// <summary>Scope comparison. Case-insensitive — see the note in <see cref="AuthorizeScopeAsync"/>.</summary>
    private static bool Matches(string scope, string known) =>
        string.Equals(scope, known, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>scope=entity</c>: authorize the caller's Read on the PARENT record. One round trip.
    /// </summary>
    private async Task<(SemanticSearchAuthorization?, IResult?)> AuthorizeEntityScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        string correlationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EntityType) || string.IsNullOrWhiteSpace(request.EntityId))
        {
            // One message for both, as before. The CODE names which field is missing — that costs an
            // unauthenticated caller nothing, since they supplied the request being described.
            return (null, Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "entityType and entityId are required when scope=entity.",
                extensions: ProblemExtensions(
                    string.IsNullOrWhiteSpace(request.EntityType)
                        ? SearchErrorCodes.EntityTypeRequired
                        : SearchErrorCodes.EntityIdRequired,
                    correlationId)));
        }

        if (!AuthorizableParentEntitySets.TryGetValue(request.EntityType, out var entitySetName))
        {
            _logger?.LogWarning(
                "Semantic search DENIED for caller {CallerId}: entityType '{EntityType}' is not an "
                + "authorizable parent type.",
                callerObjectId, request.EntityType);

            return (null, Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: $"entityType '{request.EntityType}' is not supported for scoped search.",
                extensions: ProblemExtensions(
                    SearchErrorCodes.EntityTypeNotAuthorizable, correlationId)));
        }

        if (!Guid.TryParse(request.EntityId, out var parentId) || parentId == Guid.Empty)
        {
            return (null, Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "entityId must be a non-empty GUID.",
                extensions: ProblemExtensions(SearchErrorCodes.InvalidEntityId, correlationId)));
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

            return (null, Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have access to this record.",
                extensions: ProblemExtensions(SearchErrorCodes.EntityAccessDenied, correlationId)));
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
    /// <c>scope=documentIds</c>: authorize each named document through the document path, and carry the
    /// permitted subset forward.
    /// </summary>
    private async Task<(SemanticSearchAuthorization?, IResult?)> AuthorizeDocumentIdsScopeAsync(
        SemanticSearchRequest request,
        string callerObjectId,
        string callerToken,
        string correlationId,
        CancellationToken ct)
    {
        var requested = request.DocumentIds;
        if (requested is null || requested.Count == 0)
        {
            return (null, Results.Problem(
                statusCode: 400,
                title: "Bad Request",
                detail: "documentIds is required and must not be empty when scope=documentIds.",
                extensions: ProblemExtensions(SearchErrorCodes.DocumentIdsRequired, correlationId)));
        }

        if (requested.Count > MaxAuthorizableDocumentIds)
        {
            return (null, Results.Problem(
                statusCode: 400,
                title: "Too Many Document Ids",
                detail: $"documentIds is limited to {MaxAuthorizableDocumentIds} entries per request.",
                extensions: ProblemExtensions(SearchErrorCodes.TooManyDocumentIds, correlationId)));
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
            return (null, Results.Problem(
                statusCode: 400,
                title: "Invalid Document Ids",
                detail: $"documentIds must be GUIDs. Invalid: {string.Join(", ", malformed.Take(5))}"
                        + (malformed.Count > 5 ? $" (+{malformed.Count - 5} more)" : string.Empty),
                extensions: ProblemExtensions(SearchErrorCodes.InvalidDocumentIds, correlationId)));
        }

        var candidates = requested
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var authorized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // SEQUENTIAL, deliberately — an earlier draft ran these 8-at-a-time and that was wrong.
        //
        // `GetCallerAccessAsync` reaches DataverseAccessDataSource.GetUserAccessAsync, which assigns
        // `_httpClient.DefaultRequestHeaders.Authorization` before issuing its request. That instance is
        // scoped per HTTP request, so concurrent CALLERS are safely isolated — but running this loop in
        // parallel put several tasks inside ONE scope mutating that header while siblings were mid-send.
        // `HttpHeaders` is not thread-safe. Since every iteration writes the same caller's token there is
        // no identity bleed, but the collection can throw, the catch-all upstream would swallow it, and
        // the caller would see an intermittent phantom denial. A flaky authorization result is worse than
        // a slower one — it is the kind of failure that gets a gate disabled rather than debugged.
        //
        // Cost is bounded: repeat lookups are absorbed by CachedAccessDataSource, and real documentIds
        // lists are short. Restoring parallelism requires giving this path the same explicit
        // per-request-token shape GetRecordAccessAsync uses; it is not a matter of raising a number.
        foreach (var documentId in candidates)
        {
            var snapshot = await _authorizationService.GetCallerAccessAsync(
                callerObjectId, documentId, callerToken, ct);

            if (snapshot.AccessRights.HasFlag(AccessRights.Read))
            {
                authorized.Add(documentId);
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

            return (null, Results.Problem(
                statusCode: 403,
                title: "Forbidden",
                detail: "You do not have access to any of the requested documents.",
                extensions: ProblemExtensions(SearchErrorCodes.NoReadableDocuments, correlationId)));
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
}
