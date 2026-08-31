using System.Security.Claims;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Models.Documents;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Api.Filters;

/// <summary>
/// Extension methods for adding bulk-download authorization to endpoints.
/// </summary>
public static class BulkDownloadAuthorizationFilterExtensions
{
    /// <summary>
    /// Adds the bulk-download authorization filter: validates tenant membership, then authorizes the
    /// caller against EVERY requested document and publishes the allowed set for the handler.
    /// </summary>
    public static TBuilder AddBulkDownloadAuthorizationFilter<TBuilder>(
        this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var logger = context.HttpContext.RequestServices.GetService<ILogger<BulkDownloadAuthorizationFilter>>();
            var authorizationService = context.HttpContext.RequestServices.GetRequiredService<AuthorizationService>();
            var filter = new BulkDownloadAuthorizationFilter(authorizationService, logger);
            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// Authorization filter for the bulk-download endpoint (FR-BFF-02).
/// </summary>
/// <remarks>
/// <para><b>Finding C1 (unified-access-control-r2 task 022).</b> This filter previously made NO
/// per-document authorization decision. It read the tenant claim, logged
/// <c>"Bulk download authorization granted"</c>, and called <c>next()</c>. One request naming up to
/// 500 arbitrary GUIDs then streamed every one of them app-only, so a single call both exfiltrated
/// documents the caller could not otherwise reach AND enumerated which GUIDs exist.</para>
///
/// <para><b>The doc comment was the reason it survived review.</b> It asserted — twice — that
/// "per-document access is enforced at Dataverse lookup time via the user's identity (same model as
/// <c>GET /api/documents/{id}/preview-url</c>)". Both halves were false. The lookup is
/// <c>IDocumentDataverseService.GetDocumentAsync</c>, which is app-only and carries no caller
/// identity; and <c>preview-url</c> had no per-document authorization of its own, so the claim
/// derived its authority from a route making the same empty claim. A comment asserting that
/// enforcement happens elsewhere is a claim to verify, not evidence.</para>
///
/// <para><b>What it does now.</b> Authorizes the caller for <c>read</c> against each requested
/// document through the same <see cref="AuthorizationService"/> and the same <c>"read"</c> operation
/// that the single-document <c>GET /api/documents/{id}/download</c> route uses — so bulk and single
/// download reach the same decision for the same caller and document, which is the invariant whose
/// absence WAS the finding. The allowed set is published on
/// <see cref="HttpContext.Items"/> under <see cref="AuthorizedDocumentIdsKey"/>, mirroring how
/// <c>CallerPrincipalAuthorizationFilter</c> hands a resolved principal to its handlers.</para>
///
/// <para><b>Why the decision stays in a filter</b> (ADR-008). The per-resource filter shape assumes
/// ONE resource per request, which is why <see cref="DocumentAuthorizationFilter"/> cannot be reused
/// here. Rather than move authorization into the handler, this filter authorizes all N and passes the
/// verdict forward: the decision remains at the endpoint boundary, and the handler retains only the
/// manifest-building it already owned.</para>
///
/// <para><b>Fail closed</b> (ADR-003). An unparseable id is not authorized. An
/// <see cref="AuthorizationService"/> throw is not authorized. A caller with no
/// <see cref="ClaimTypes.NameIdentifier"/> is rejected outright. And when the request body cannot be
/// located, the filter publishes an EMPTY allowed set rather than skipping publication — an absent
/// key means "the filter did not run", which the handler must treat as a total denial, so silently
/// omitting it here would be indistinguishable from being unmapped.</para>
///
/// <para><b>Cost.</b> One authorization round-trip per requested document, on top of the handler's
/// existing per-document Dataverse lookup and SPE download. The endpoint was already O(N) sequential
/// in two round-trips per document; this makes it three. <see cref="AuthorizationService"/> reads
/// through <c>CachedAccessDataSource</c>, so a repeat request for the same documents is cheap, but a
/// first request for 500 distinct documents pays 500 misses. Bounding this with parallelism is a real
/// improvement and deliberately NOT done here — it would change the endpoint's concurrency behaviour
/// in the same change that closes a disclosure, and those should not be entangled.</para>
/// </remarks>
public sealed class BulkDownloadAuthorizationFilter : IEndpointFilter
{
    /// <summary>
    /// <see cref="HttpContext.Items"/> key carrying the <c>IReadOnlySet&lt;string&gt;</c> of document
    /// ids this caller is authorized to read. The handler MUST treat an absent key as "authorize
    /// nothing" — see the class remarks.
    /// </summary>
    public const string AuthorizedDocumentIdsKey = "sdap.bulk-download.authorized-document-ids";

    /// <summary>The operation authorized per document — the same key the single-document download uses.</summary>
    private const string ReadOperation = "read";

    private readonly AuthorizationService _authorizationService;
    private readonly ILogger<BulkDownloadAuthorizationFilter>? _logger;

    // Azure AD claim names (mirror SemanticSearchAuthorizationFilter)
    private const string TenantIdClaimType = "tid";
    private const string AltTenantIdClaimType = "http://schemas.microsoft.com/identity/claims/tenantid";

    public BulkDownloadAuthorizationFilter(
        AuthorizationService authorizationService,
        ILogger<BulkDownloadAuthorizationFilter>? logger = null)
    {
        _authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        // Step 1: tenant membership.
        var userTenantId = httpContext.User.FindFirst(TenantIdClaimType)?.Value
            ?? httpContext.User.FindFirst(AltTenantIdClaimType)?.Value;

        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger?.LogWarning("Bulk download authorization denied: No tenant claim found in token");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "Tenant identity not found in authentication token");
        }

        // Step 2: caller identity. Required before any per-document decision can be made at all.
        // Entra `oid`, not `sub` — see CallerResolution (UAT 2026-08-26 / D-6 class).
        var callerId = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(callerId))
        {
            _logger?.LogWarning("Bulk download authorization denied: no NameIdentifier claim");

            return Results.Problem(
                statusCode: 401,
                title: "Unauthorized",
                detail: "User identity not found");
        }

        var request = ExtractRequest(context);
        if (request?.DocumentIds is null)
        {
            // Publish an empty set, do NOT skip publication: an absent key is the handler's signal
            // that this filter never ran, and it denies everything on that basis. Letting the body
            // validation 400 come from the handler is intentional, but it must not look unmapped.
            httpContext.Items[AuthorizedDocumentIdsKey] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return await next(context);
        }

        // Step 3: authorize the caller against EVERY requested document.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        var authorized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawId in request.DocumentIds)
        {
            if (string.IsNullOrWhiteSpace(rawId) || !Guid.TryParse(rawId, out _))
            {
                // Malformed ids are never authorized. The handler reports them as shape errors,
                // which is safe to distinguish — it is the caller's own input.
                continue;
            }

            if (authorized.Contains(rawId))
            {
                continue; // duplicate in the request — one decision is enough
            }

            try
            {
                var result = await _authorizationService.AuthorizeAsync(new AuthorizationContext
                {
                    UserId = callerId,
                    ResourceId = rawId,
                    Operation = ReadOperation,
                    CorrelationId = httpContext.TraceIdentifier,
                    UserAccessToken = callerToken
                }, httpContext.RequestAborted);

                if (result.IsAllowed)
                {
                    authorized.Add(rawId);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Fail closed: an errored decision is a denial, never a pass-through.
                //
                // DEFENCE IN DEPTH — currently unreachable, but LIVE AND TESTED, not dead code.
                // The distinction was measured, not assumed (2026-08-24, task 022):
                //
                //   AuthorizeAsync catches Exception around everything except its own
                //   ArgumentNullException.ThrowIfNull, so a failing access data source is denied one
                //   layer down and never propagates here. Inverting this catch to authorize
                //   therefore breaks 0 of 30 tests.
                //
                //   That is a fact about AuthorizationService, NOT about test coverage. A two-factor
                //   experiment proved it: force AuthorizeAsync to throw from outside its own try and
                //   14 of 30 tests fail (everything denies, as it should); do that AND invert this
                //   catch and 17 fail. The 3-test delta IS this catch's coverage. It is load-bearing
                //   the moment it becomes reachable, and the tests that pin it already exist.
                //
                // So: keep it. If AuthorizationService ever narrows its catch or adds validation
                // ahead of its try, this guard activates already-covered. If you are testing
                // fail-closed behaviour TODAY, perturb AuthorizationService's catch — that is the
                // one the tests bite on now (2 of 30).
                _logger?.LogWarning(ex,
                    "Bulk download: authorization check failed for document {DocumentId}; treating as denied",
                    rawId);
            }
        }

        httpContext.Items[AuthorizedDocumentIdsKey] = authorized;

        _logger?.LogInformation(
            "Bulk download authorized: tenant={TenantId}, requestedCount={Count}, authorizedCount={Authorized}",
            userTenantId, request.DocumentIds.Count, authorized.Count);

        return await next(context);
    }

    private static BulkDownloadRequest? ExtractRequest(EndpointFilterInvocationContext context)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is BulkDownloadRequest request)
            {
                return request;
            }
        }
        return null;
    }
}
