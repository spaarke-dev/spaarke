// R3 Part 1 — User-Record Membership Resolution (user-facing endpoint)
// Task 035 (2026-06-21): GET /api/users/me/memberships/{entityType} — the public
// surface defined by spec.md FR-1A.9. Resolves the calling user's
// systemuser → 6-path PersonIdentity (task 031) → membership rows on the target
// entity (task 033), grouped by role and paginated.
//
// Auth: standard Spaarke Auth v2 OBO (ADR-028). Any authenticated user can query
// their OWN memberships — no specific role gate. `RequireAuthorization()` (default
// JWT policy) on the MapGroup.
//
// Current-user systemuserid lookup mechanism:
//   The Entra ID token's `oid` claim is the AAD object id, NOT the Dataverse
//   systemuserid. Per ADR-028, we cross-reference via the systemuser entity's
//   `azureactivedirectoryobjectid` column. The lookup is performed by this file
//   (an inline helper) rather than a dedicated `ICurrentUserService` because:
//     (a) it's the first BFF surface needing the AAD-oid → systemuserid direction
//         (the existing IdentityNormalizationService goes the OTHER way:
//          systemuserid → AAD oid + 5 other paths)
//     (b) the helper is small (~30 lines), uses cached IDataverseService +
//         IDistributedCache, and stays close to its single consumer
//     (c) introducing an ICurrentUserService now would create a one-use
//         abstraction that would have to evolve when the next endpoint surface
//         needs caller resolution (PrecedentAdminEndpoints already calls out
//         the same need but settled for "use the oid directly" pending a
//         per-request mapping service)
//   Future task: if a SECOND endpoint needs the same AAD oid → systemuserid
//   lookup, extract this helper into a dedicated service in
//   Services/Ai/Membership/CurrentUserResolver.cs and update both callers.
//
// Query-parameter contract per design.md Part 1 § Endpoint contract:
//   ?roles=owner,assignedAttorney      (CSV → IReadOnlyList<string>; null/empty = all)
//   ?identityTypes=SystemUser,Contact  (CSV → IReadOnlyList<string>; null/empty = all)
//   ?includeRelated=documents,events   (CSV; ACCEPTED-BUT-IGNORED — task 054 implements)
//   ?limit=500                         (int; default 500; hard-capped at MaxLimit)
//   ?continuationToken={token}         (opaque cursor from prior response)
//
// Response: 200 OK + MembershipResponse (camelCase JSON locked at type level
// per task 034). 401 if unauthenticated. 400 on malformed query params.
//
// References:
//   - projects/spaarke-platform-foundations-r3/spec.md FR-1A.9 + AC-1A.3 + AC-1A.5
//   - projects/spaarke-platform-foundations-r3/design.md Part 1 § Endpoint contract
//   - ADR-028 (Spaarke Auth v2 OBO — AAD oid is canonical identifier)
//   - ADR-008 (endpoint-filter authorization, no global middleware)
//   - ADR-009 (Redis cache TTL — 10-min for AAD-oid → systemuserid lookup, matching
//     IdentityNormalizationService's 10-min identity cache TTL)
//   - ADR-010 (DI minimalism — no new interface introduced for the one-use helper)
//   - bff-extensions.md §A (BFF pre-merge checklist), §F.1 (unconditional registration
//     — service AddMembership() + endpoint MapMembershipEndpoints() both unconditional)

using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Api.Membership;

/// <summary>
/// Endpoints under <c>/api/users/me/memberships/*</c> implementing the user-facing
/// surface of R3 Part 1 (User-Record Membership Resolution). Currently maps the
/// single FR-1A.9 endpoint; reserved comment blocks below mark future Phase 1B/1D
/// expansion sites.
/// </summary>
public static class MembershipEndpoints
{
    /// <summary>Cache key prefix for AAD-oid → systemuserid lookups. Sibling
    /// namespace to <see cref="IdentityNormalizationService"/>'s
    /// <c>membership:identity:*</c> namespace so future invalidation via
    /// <c>membership:*</c> wipes both.</summary>
    internal const string CurrentUserCacheKeyPrefix = "membership:currentuser:";

    /// <summary>TTL for AAD-oid → systemuserid cache entries. Matches the
    /// identity-normalization cache TTL (10 min) — a freshly disabled user
    /// continues to look like an authenticated systemuser for at most 10 min,
    /// at which point the next request will re-resolve and surface the row's
    /// absence as 401.</summary>
    private static readonly TimeSpan CurrentUserCacheTtl = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Registers the membership endpoint group on the supplied
    /// <see cref="IEndpointRouteBuilder"/>. Called from
    /// <c>EndpointMappingExtensions.MapDomainEndpoints</c> via
    /// <see cref="MembershipApiExtensions.MapMembershipApi"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapMembershipEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/users/me/memberships")
            .RequireAuthorization()
            .WithTags("Membership");

        // ============================================================================
        // ===== Task 035 — GET /api/users/me/memberships/{entityType} ===============
        // ============================================================================
        // Resolves the calling user's memberships on the target entity type. Standard
        // Spaarke Auth v2 OBO (ADR-028). Query params: roles, identityTypes,
        // includeRelated (Phase 1D — ignored Phase 1A), limit, continuationToken.
        group.MapGet("/{entityType}", GetMyMembershipsAsync)
            .WithName("GetMyMemberships")
            .WithSummary("Get the calling user's memberships on the target entity type")
            .WithDescription(
                "Resolves the authenticated user via AAD oid → systemuserid cross-reference " +
                "(per ADR-028) and returns the rows of {entityType} the user is a member of, " +
                "grouped by role. Supports filtering by roles + identityTypes and pagination " +
                "via continuationToken. The includeRelated query parameter is accepted-but-ignored " +
                "in Phase 1A; task 054 implements transitive expansion.")
            .Produces<MembershipResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // ============================================================================
        // Future expansion slots (DO NOT remove these comment headers — they reserve
        // demarcated areas for downstream R3 / R4 tasks):
        //
        //   ===== Phase 1D — transitive memberships richer response shape (task 054)
        //   ===== Phase 2  — junction-table-backed read path (task 086) - same contract,
        //                    different implementation behind the resolver service
        // ============================================================================

        return app;
    }

    // ============================================================================
    // ===== Task 035 handler =====================================================
    // ============================================================================

    /// <summary>
    /// Handler for <c>GET /api/users/me/memberships/{entityType}</c>. Resolves the
    /// caller's systemuserid via the AAD <c>oid</c> claim (per ADR-028), parses
    /// query params into <see cref="MembershipResolveOptions"/>, and delegates
    /// to <see cref="IMembershipResolverService"/>.
    /// </summary>
    private static async Task<IResult> GetMyMembershipsAsync(
        string entityType,
        HttpContext httpContext,
        IMembershipResolverService resolver,
        IDataverseService dataverse,
        IDistributedCache cache,
        ILoggerFactory loggerFactory,
        CancellationToken ct,
        [FromQuery] string? roles = null,
        [FromQuery] string? identityTypes = null,
        [FromQuery] string? includeRelated = null,
        [FromQuery] int? limit = null,
        [FromQuery] string? continuationToken = null)
    {
        var logger = loggerFactory.CreateLogger("Sprk.Bff.Api.Api.Membership.MembershipEndpoints");

        // ---------------------------------------------------------------
        // Validation — RFC 7807 ProblemDetails for every malformed input
        // ---------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: "entityType route parameter is required and cannot be empty.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }

        var effectiveLimit = MembershipResolveOptions.DefaultLimit;
        if (limit.HasValue)
        {
            if (limit.Value <= 0)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Bad Request",
                    detail: "limit must be a positive integer.",
                    type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
            }
            // Hard server-side cap (per MembershipResolveOptions.MaxLimit). Per design
            // a value above the cap is silently clamped (not 400'd) so clients can
            // pass "big" without knowing the exact ceiling — matches the established
            // OData top-N clamp convention used elsewhere in the BFF.
            effectiveLimit = Math.Min(limit.Value, MembershipResolveOptions.MaxLimit);
        }

        // ---------------------------------------------------------------
        // Identity — resolve caller's systemuserid via AAD oid (ADR-028)
        // ---------------------------------------------------------------
        var callerOid = ExtractAadObjectId(httpContext.User);
        if (callerOid is null)
        {
            // RequireAuthorization() should already 401 unauthenticated calls; we
            // defensively re-check so a test or filter override that bypasses the
            // policy still gets a clean 401 instead of an unexplained 500.
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                detail: "User identity (oid claim) not found in authentication token.",
                type: "https://tools.ietf.org/html/rfc7235#section-3.1");
        }

        Guid systemUserId;
        try
        {
            var resolved = await ResolveSystemUserIdAsync(callerOid.Value, dataverse, cache, logger, ct)
                .ConfigureAwait(false);
            if (resolved is null)
            {
                // Authenticated but no matching systemuser row — the caller is a
                // valid Entra ID principal but is not provisioned in Dataverse for
                // this org. 401 (not 403) because Dataverse-side provisioning is the
                // gating condition; we treat the token as authenticating an unknown
                // identity rather than denying a known one.
                logger.LogWarning(
                    "Membership endpoint: caller oid={CallerOid} has no matching systemuser " +
                    "row in Dataverse (azureactivedirectoryobjectid lookup returned 0 rows)",
                    callerOid);
                return Results.Problem(
                    statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized",
                    detail: "Authenticated principal is not provisioned as a systemuser in Dataverse.",
                    type: "https://tools.ietf.org/html/rfc7235#section-3.1");
            }
            systemUserId = resolved.Value;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Membership endpoint: failed to resolve systemuserid for caller oid={CallerOid}",
                callerOid);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to resolve caller identity. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }

        // ---------------------------------------------------------------
        // Build MembershipResolveOptions from query params
        // ---------------------------------------------------------------
        var options = new MembershipResolveOptions(
            Roles: ParseCsv(roles),
            IdentityTypes: ParseCsv(identityTypes),
            IncludeRelated: ParseCsv(includeRelated),
            Limit: effectiveLimit,
            ContinuationToken: string.IsNullOrWhiteSpace(continuationToken) ? null : continuationToken);

        // ---------------------------------------------------------------
        // Delegate to IMembershipResolverService
        // ---------------------------------------------------------------
        try
        {
            var response = await resolver
                .ResolveAsync(systemUserId, entityType, options, ct)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Membership endpoint: systemUserId={SystemUserId} entity={EntityType} " +
                "count={Count} roles={Roles} identityTypes={IdentityTypes} limit={Limit}",
                systemUserId,
                entityType,
                response.Count,
                options.Roles is null ? "(all)" : string.Join(",", options.Roles),
                options.IdentityTypes is null ? "(all)" : string.Join(",", options.IdentityTypes),
                effectiveLimit);

            return Results.Ok(response);
        }
        catch (ArgumentException ex)
        {
            // IMembershipResolverService throws ArgumentException for empty/whitespace
            // entityType or Guid.Empty systemUserId. We already validated entityType
            // above and systemUserId comes from a verified non-empty path, so this
            // SHOULD be unreachable — surface 400 with the message rather than 500
            // so any unforeseen guard violations are debuggable.
            logger.LogWarning(ex,
                "Membership endpoint: resolver rejected arguments for caller oid={CallerOid}",
                callerOid);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1");
        }
        catch (MembershipDepthExceededException ex)
        {
            // R3 Part 1D / FR-1D.2 / Q3 (owner 2026-06-20): includeRelated is 1-hop
            // max. The resolver surfaces depth violations as MembershipDepthExceededException
            // so we can return a structured 400 with the specific offending entry,
            // rather than burying it in a generic 500. The extensions block carries
            // the offendingEntry + reasonTag so SDK/UI callers can present targeted
            // remediation without parsing the human-readable detail string.
            logger.LogInformation(
                "Membership endpoint: 1-hop depth violation — offendingEntry='{Entry}' reason={Reason} caller={CallerOid}",
                ex.OffendingEntry, ex.ReasonTag, callerOid);
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Bad Request",
                detail: ex.Message,
                type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                extensions: new Dictionary<string, object?>
                {
                    ["offendingEntry"] = ex.OffendingEntry,
                    ["reasonTag"] = ex.ReasonTag,
                    ["maxHops"] = 1,
                });
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Membership endpoint: resolver failed for systemUserId={SystemUserId} entity={EntityType}",
                systemUserId, entityType);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to resolve memberships. See server logs for details.",
                type: "https://tools.ietf.org/html/rfc7231#section-6.6.1");
        }
    }

    // ============================================================================
    // ===== Helpers ==============================================================
    // ============================================================================

    /// <summary>
    /// Extracts the Entra ID <c>oid</c> claim (AAD object id) from the supplied
    /// <see cref="ClaimsPrincipal"/>. Accepts the short-form <c>oid</c> claim,
    /// the long-form Microsoft schema URI, and (as a defensive fallback)
    /// <see cref="ClaimTypes.NameIdentifier"/>. Returns <c>null</c> when no
    /// usable identifier is present or the claim does not parse as a Guid.
    /// </summary>
    /// <remarks>
    /// Mirrors the chain used by other authorization filters in this codebase
    /// (e.g., <c>PrecedentAdminEndpoints.CreatePrecedent</c>). The Entra ID
    /// <c>oid</c> claim is the canonical principal identifier per ADR-028
    /// (more stable than <c>sub</c> across token issuance / app re-consent flows).
    /// </remarks>
    internal static Guid? ExtractAadObjectId(ClaimsPrincipal user)
    {
        var oidString = user.FindFirst("oid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(oidString))
        {
            return null;
        }

        return Guid.TryParse(oidString, out var oid) && oid != Guid.Empty
            ? oid
            : null;
    }

    /// <summary>
    /// Resolves an Entra ID <c>oid</c> (AAD object id) to a Dataverse
    /// <c>systemuserid</c> by querying the <c>systemuser</c> entity's
    /// <c>azureactivedirectoryobjectid</c> column (per ADR-028). Result is
    /// cached in <see cref="IDistributedCache"/> for 10 min per
    /// <see cref="CurrentUserCacheTtl"/>.
    /// </summary>
    /// <returns>
    /// The resolved <c>systemuserid</c>, or <c>null</c> when the AAD principal
    /// has no matching Dataverse systemuser row.
    /// </returns>
    /// <remarks>
    /// Cache failures (read or write) are NON-FATAL: read failure falls through
    /// to a live Dataverse query; write failure logs Warning + returns the
    /// freshly-resolved value. This matches the failure-isolation pattern in
    /// <see cref="IdentityNormalizationService"/>.
    /// </remarks>
    internal static async Task<Guid?> ResolveSystemUserIdAsync(
        Guid aadObjectId,
        IDataverseService dataverse,
        IDistributedCache cache,
        ILogger logger,
        CancellationToken ct)
    {
        var cacheKey = CurrentUserCacheKeyPrefix + aadObjectId.ToString("D");

        // ── Cache lookup ────────────────────────────────────────────────────
        try
        {
            var cached = await cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is not null && cached.Length == 16)
            {
                var cachedGuid = new Guid(cached);
                if (cachedGuid != Guid.Empty)
                {
                    return cachedGuid;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Membership endpoint: cache read failed for {CacheKey}; falling through to live resolve",
                cacheKey);
        }

        // ── Live cross-ref via systemuser.azureactivedirectoryobjectid ──────
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition(
            "azureactivedirectoryobjectid",
            ConditionOperator.Equal,
            aadObjectId);
        // Exclude disabled users from the lookup — a disabled systemuser row would
        // resolve to an id whose subsequent membership query returns nothing, but
        // it also means the caller is no longer a valid principal. Returning null
        // (which the caller surfaces as 401) is the correct outcome.
        query.Criteria.AddCondition(
            "isdisabled",
            ConditionOperator.Equal,
            false);

        var results = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (results.Entities.Count == 0)
        {
            return null;
        }

        var systemUserId = results.Entities[0].Id;
        if (systemUserId == Guid.Empty)
        {
            return null;
        }

        // ── Cache write (fire-and-forget on failure) ────────────────────────
        try
        {
            await cache.SetAsync(
                cacheKey,
                systemUserId.ToByteArray(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CurrentUserCacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Membership endpoint: cache write failed for {CacheKey}; next call will re-resolve",
                cacheKey);
        }

        return systemUserId;
    }

    /// <summary>
    /// Parses a comma-separated query parameter value into a trimmed, non-empty,
    /// duplicate-preserving list. Returns <c>null</c> when the input is
    /// null/whitespace OR yields no non-empty tokens (so the resolver treats it
    /// as "use all" per <see cref="MembershipResolveOptions"/> semantics).
    /// </summary>
    internal static IReadOnlyList<string>? ParseCsv(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? null : parts;
    }
}

/// <summary>
/// DI registration extensions for the user-facing membership API surface
/// (R3 Part 1 / FR-1A.9). Pairs the service-side <c>AddMembershipApi()</c>
/// (currently no-op — the resolver service is registered by
/// <see cref="Sprk.Bff.Api.Infrastructure.DI.MembershipModule"/>) with the
/// endpoint-side <c>MapMembershipApi()</c> so both sides of the contract are
/// invoked from <c>Program.cs</c> / <c>EndpointMappingExtensions</c> via a
/// single import.
/// </summary>
public static class MembershipApiExtensions
{
    /// <summary>
    /// Registers any future service-side dependencies of the user-facing
    /// membership endpoints. Currently a no-op because the resolver +
    /// dependencies are registered by
    /// <see cref="Sprk.Bff.Api.Infrastructure.DI.MembershipModule.AddMembership"/>;
    /// kept as the canonical extension site so future endpoint-specific services
    /// (e.g., a CurrentUserResolver extracted from this file) can be added here
    /// without changing the Program.cs hook.
    /// </summary>
    public static IServiceCollection AddMembershipApi(this IServiceCollection services) => services;

    /// <summary>
    /// Maps the user-facing membership endpoint group on the supplied
    /// <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapMembershipApi(this IEndpointRouteBuilder app)
        => app.MapMembershipEndpoints();
}
