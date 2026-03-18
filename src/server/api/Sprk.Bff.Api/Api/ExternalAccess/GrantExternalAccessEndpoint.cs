using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Graph.Models;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/grant
///
/// Grants an external Contact access to a Secure Project by:
///   1. Creating a sprk_externalrecordaccess record in Dataverse.
///   2. Adding the Contact to the SPE container as Reader or Writer.
///   3. Invalidating the contact's participation cache in Redis.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after grant (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class GrantExternalAccessEndpoint
{
    private const string EntitySet = "sprk_externalrecordaccesses";
    private const string CacheKeyPrefix = "sdap:external:access:";

    /// <summary>
    /// Registers the grant endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapGrantExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/grant", GrantAccessAsync)
            .WithName("GrantExternalAccess")
            .WithSummary("Grant external access to a Contact for a Secure Project")
            .WithDescription(
                "Creates a sprk_externalrecordaccess record and adds the Contact to the SPE container. " +
                "Invalidates the contact's Redis participation cache after granting.")
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
        IGraphClientFactory graphClientFactory,
        IDistributedCache cache,
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

        // ── Step 2: Add Contact to SPE container ─────────────────────────────
        // Resolve the container ID by looking up the project's SPE container
        var containerMembershipGranted = false;
        try
        {
            var containerId = await ResolveProjectContainerIdAsync(
                dataverseClient, request.ProjectId, ct);

            if (!string.IsNullOrEmpty(containerId))
            {
                var roles = request.AccessLevel == ExternalAccessLevel.ViewOnly
                    ? new[] { "reader" }
                    : new[] { "writer" };

                containerMembershipGranted = await AddContactToSpeContainerAsync(
                    graphClientFactory, containerId, request.ContactId, roles, logger, ct);
            }
            else
            {
                logger.LogWarning(
                    "[EXT-GRANT] No SPE container found for Project {ProjectId} — skipping container membership",
                    request.ProjectId);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: Dataverse record was created, log and continue
            logger.LogError(ex,
                "[EXT-GRANT] Failed to add Contact {ContactId} to SPE container for Project {ProjectId}. " +
                "Access record {AccessRecordId} exists but container membership was not granted.",
                request.ContactId, request.ProjectId, accessRecordId);
        }

        // ── Step 3: Invalidate Redis cache ───────────────────────────────────
        try
        {
            var cacheKey = $"{CacheKeyPrefix}{request.ContactId}";
            await cache.RemoveAsync(cacheKey, ct);
            logger.LogDebug("[EXT-GRANT] Invalidated cache key {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        return TypedResults.Ok(new GrantAccessResponse(accessRecordId, containerMembershipGranted));
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

    private static async Task<string?> ResolveProjectContainerIdAsync(
        DataverseWebApiClient dataverseClient,
        Guid projectId,
        CancellationToken ct)
    {
        try
        {
            var rows = await dataverseClient.QueryAsync<ProjectContainerRow>(
                "sprk_projects",
                filter: $"sprk_projectid eq {projectId}",
                select: "sprk_projectid,sprk_specontainerid",
                top: 1,
                cancellationToken: ct);

            return rows.FirstOrDefault()?.sprk_specontainerid;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<bool> AddContactToSpeContainerAsync(
        IGraphClientFactory graphClientFactory,
        string containerId,
        Guid contactId,
        string[] roles,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var graphClient = graphClientFactory.ForApp();

            // Resolve the Contact's email to grant permission
            // Note: SPE container permissions use email-based identity for external users
            // The permission entry uses a guest/external user reference
            var permission = new Permission
            {
                Roles = roles.ToList(),
                GrantedToV2 = new SharePointIdentitySet
                {
                    User = new SharePointIdentity
                    {
                        // Use Contact ID as the login name for SPE external access
                        // In practice the caller should pass the user's email/UPN
                        LoginName = $"i:0#.f|membership|contact_{contactId}"
                    }
                }
            };

            await graphClient.Storage.FileStorage.Containers[containerId].Permissions
                .PostAsync(permission, cancellationToken: ct);

            logger.LogInformation(
                "[EXT-GRANT] Added Contact {ContactId} to SPE container {ContainerId} with roles [{Roles}]",
                contactId, containerId, string.Join(", ", roles));

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-GRANT] Failed to add Contact {ContactId} to SPE container {ContainerId}",
                contactId, containerId);
            return false;
        }
    }

    // ── Dataverse row DTO ────────────────────────────────────────────────────

    private sealed class ProjectContainerRow
    {
        [JsonPropertyName("sprk_projectid")]
        public Guid sprk_projectid { get; set; }

        [JsonPropertyName("sprk_specontainerid")]
        public string? sprk_specontainerid { get; set; }
    }
}
