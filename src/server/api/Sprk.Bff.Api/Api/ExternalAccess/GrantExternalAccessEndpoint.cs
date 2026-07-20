using System.Security.Claims;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/grant
///
/// Grants an external Contact access to a Secure Project by:
///   1. Creating a sprk_externalrecordaccess record in Dataverse.
///   2. Invalidating the contact's participation cache in Redis.
///
/// Broker-only (ADR-028 Amendment A1): external users never authenticate to SPE
/// directly — all external SPE access is app-only via the BFF — so no synthetic
/// SPE container permission is written on grant.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after grant (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class GrantExternalAccessEndpoint
{
    private const string EntitySet = "sprk_externalrecordaccesses";
    // Resource identifier for ITenantCache (FR-05). Tenant scope is derived from the caller's
    // 'tid' claim. The cached value is a list of active participations per Contact — not an
    // authorization decision.
    private const string ExternalAccessResource = "external-access-grant";
    private const int CacheVersion = 1;

    /// <summary>
    /// Registers the grant endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapGrantExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/grant", GrantAccessAsync)
            .WithName("GrantExternalAccess")
            .WithSummary("Grant external access to a Contact for a Secure Project")
            .WithDescription(
                "Creates a sprk_externalrecordaccess record and invalidates the contact's Redis " +
                "participation cache after granting. External SPE access is app-only (broker-only).")
            .Produces<GrantAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> GrantAccessAsync(
        GrantAccessRequest request,
        DataverseWebApiClient dataverseClient,
        ITenantCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.ContactId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContactId is required and must be a valid GUID.");

        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        // ── Resolve caller identity for granted-by reference ─────────────────
        var callerSystemUserId = httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        logger.LogInformation(
            "[EXT-GRANT] Granting {AccessLevel} access to Contact {ContactId} for Project {ProjectId}",
            request.AccessLevel, request.ContactId, request.ProjectId);

        // ── Step 1: Create sprk_externalrecordaccess in Dataverse ────────────
        Guid accessRecordId;
        try
        {
            var payload = BuildGrantPayload(request, callerSystemUserId);
            accessRecordId = await dataverseClient.CreateAsync(EntitySet, payload, ct);

            logger.LogInformation(
                "[EXT-GRANT] Created access record {AccessRecordId} for Contact {ContactId} / Project {ProjectId}",
                accessRecordId, request.ContactId, request.ProjectId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-GRANT] Failed to create Dataverse access record for Contact {ContactId} / Project {ProjectId}",
                request.ContactId, request.ProjectId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 2: Invalidate Redis cache ───────────────────────────────────
        try
        {
            var tenantId = ExtractTenantId(httpContext);
            if (!string.IsNullOrEmpty(tenantId))
            {
                await cache.RemoveAsync(
                    tenantId, ExternalAccessResource, request.ContactId.ToString(), CacheVersion,
                    ct: ct);
                logger.LogDebug("[EXT-GRANT] Invalidated cache for Contact {ContactId}", request.ContactId);
            }
            else
            {
                logger.LogWarning(
                    "[EXT-GRANT] No tenant claim found — skipping cache invalidation for Contact {ContactId}",
                    request.ContactId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        // Broker-only: no synthetic SPE container membership is granted on the external path.
        return TypedResults.Ok(new GrantAccessResponse(accessRecordId, SpeContainerMembershipGranted: false));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static object BuildGrantPayload(GrantAccessRequest request, string? callerSystemUserId)
    {
        var payload = new Dictionary<string, object?>
        {
            ["sprk_contactid@odata.bind"] = $"/contacts({request.ContactId})",
            ["sprk_projectid@odata.bind"] = $"/sprk_projects({request.ProjectId})",
            ["sprk_accesslevel"] = (int)request.AccessLevel,
            ["sprk_granteddate"] = DateTime.UtcNow.ToString("o")
        };

        if (!string.IsNullOrEmpty(callerSystemUserId) &&
            Guid.TryParse(callerSystemUserId, out var systemUserId))
        {
            payload["sprk_grantedby@odata.bind"] = $"/systemusers({systemUserId})";
        }

        if (request.ExpiryDate.HasValue)
        {
            payload["sprk_expirydate"] = request.ExpiryDate.Value.ToString("o");
        }

        if (request.AccountId.HasValue)
        {
            payload["sprk_accountid@odata.bind"] = $"/accounts({request.AccountId.Value})";
        }

        return payload;
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the authenticated HttpContext.
    /// Returns null when no claim is present (in which case cache invalidation is skipped).
    /// </summary>
    private static string? ExtractTenantId(HttpContext httpContext)
        => httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
}
