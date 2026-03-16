using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Graph.Models;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/revoke
///
/// Revokes an external Contact's access to a Secure Project by:
///   1. Deactivating the sprk_externalrecordaccess record in Dataverse (statecode=1, statuscode=2).
///   2. Removing the Contact from the SPE container permissions.
///   3. If the Contact has no remaining active participations, removing the "Secure Project Participant" web role.
///   4. Invalidating the contact's participation cache in Redis.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-009: Redis cache invalidation after revoke (key: sdap:external:access:{contactId}).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class RevokeExternalAccessEndpoint
{
    private const string AccessEntitySet = "sprk_externalrecordaccesses";
    private const string CacheKeyPrefix = "sdap:external:access:";

    /// <summary>
    /// Registers the revoke endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapRevokeExternalAccessEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/revoke", RevokeAccessAsync)
            .WithName("RevokeExternalAccess")
            .WithSummary("Revoke external access from a Contact for a Secure Project")
            .WithDescription(
                "Deactivates the sprk_externalrecordaccess record, removes the Contact from the SPE container, " +
                "and optionally removes the Power Pages web role if no other active participations remain. " +
                "Invalidates the contact's Redis participation cache after revoking.")
            .Produces<RevokeAccessResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> RevokeAccessAsync(
        RevokeAccessRequest request,
        DataverseWebApiClient dataverseClient,
        IGraphClientFactory graphClientFactory,
        IDistributedCache cache,
        HttpContext httpContext,
        ILogger<Program> logger,
        IConfiguration configuration,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (request.AccessRecordId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("AccessRecordId is required and must be a valid GUID.");

        if (request.ContactId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ContactId is required and must be a valid GUID.");

        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        logger.LogInformation(
            "[EXT-REVOKE] Revoking access record {AccessRecordId} for Contact {ContactId} / Project {ProjectId}",
            request.AccessRecordId, request.ContactId, request.ProjectId);

        // ── Step 1: Deactivate the sprk_externalrecordaccess record ──────────
        try
        {
            var deactivatePayload = new { statecode = 1, statuscode = 2 };
            await dataverseClient.UpdateAsync(AccessEntitySet, request.AccessRecordId, deactivatePayload, ct);

            logger.LogInformation(
                "[EXT-REVOKE] Deactivated access record {AccessRecordId}", request.AccessRecordId);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"Access record '{request.AccessRecordId}' was not found.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-REVOKE] Failed to deactivate access record {AccessRecordId}", request.AccessRecordId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to deactivate external access record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 2: Remove Contact from SPE container ─────────────────────────
        var speRevoked = false;
        if (request.ContainerId.HasValue)
        {
            try
            {
                speRevoked = await RemoveContactFromSpeContainerAsync(
                    graphClientFactory,
                    request.ContainerId.Value.ToString(),
                    request.ContactId,
                    logger,
                    ct);
            }
            catch (Exception ex)
            {
                // Non-fatal: Dataverse record was deactivated, log and continue
                logger.LogError(ex,
                    "[EXT-REVOKE] Failed to remove Contact {ContactId} from SPE container {ContainerId}. " +
                    "Access record {AccessRecordId} was deactivated.",
                    request.ContactId, request.ContainerId, request.AccessRecordId);
            }
        }
        else
        {
            logger.LogInformation(
                "[EXT-REVOKE] No ContainerId provided — skipping SPE container membership removal for Contact {ContactId}",
                request.ContactId);
        }

        // ── Step 3: Check remaining participations and optionally remove web role ──
        var webRoleRemoved = false;
        try
        {
            var remainingParticipations = await dataverseClient.QueryAsync<ActiveParticipationRow>(
                AccessEntitySet,
                filter: $"_sprk_contactid_value eq {request.ContactId} and statecode eq 0",
                select: "sprk_externalrecordaccessid",
                top: 1,
                cancellationToken: ct);

            if (remainingParticipations.Count == 0)
            {
                logger.LogInformation(
                    "[EXT-REVOKE] Contact {ContactId} has no remaining active participations — removing web role",
                    request.ContactId);

                webRoleRemoved = await RemoveSecureProjectWebRoleAsync(
                    dataverseClient, request.ContactId, configuration, logger, ct);
            }
            else
            {
                logger.LogInformation(
                    "[EXT-REVOKE] Contact {ContactId} still has active participations — web role retained",
                    request.ContactId);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal: log and continue
            logger.LogWarning(ex,
                "[EXT-REVOKE] Error checking remaining participations or removing web role for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        // ── Step 4: Invalidate Redis cache ────────────────────────────────────
        try
        {
            var cacheKey = $"{CacheKeyPrefix}{request.ContactId}";
            await cache.RemoveAsync(cacheKey, ct);
            logger.LogDebug("[EXT-REVOKE] Invalidated cache key {CacheKey}", cacheKey);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Failed to invalidate Redis cache for Contact {ContactId}. Non-critical.",
                request.ContactId);
        }

        return TypedResults.Ok(new RevokeAccessResponse(speRevoked, webRoleRemoved));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<bool> RemoveContactFromSpeContainerAsync(
        IGraphClientFactory graphClientFactory,
        string containerId,
        Guid contactId,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var graphClient = graphClientFactory.ForApp();

            // Step 2a: List container permissions to find the Contact's entry
            var permissions = await graphClient.Storage.FileStorage.Containers[containerId].Permissions
                .GetAsync(cancellationToken: ct);

            if (permissions?.Value == null || permissions.Value.Count == 0)
            {
                logger.LogInformation(
                    "[EXT-REVOKE] No permissions found on container {ContainerId}", containerId);
                return false;
            }

            // Step 2b: Find the Contact's permission entry by matching the contact ID in the
            // user's email or userPrincipalName (AdditionalData). The SPE Graph API stores
            // external user identity in GrantedToV2.User.Email or AdditionalData["userPrincipalName"].
            var contactIdStr = contactId.ToString();
            var contactPermission = permissions.Value.FirstOrDefault(p =>
            {
                var user = p.GrantedToV2?.User;
                if (user == null) return false;

                // Graph SDK 5.x Identity type does not expose Email or LoginName directly.
                // Try AdditionalData["userPrincipalName"]
                if (user.AdditionalData?.TryGetValue("userPrincipalName", out var upn) == true &&
                    upn?.ToString()?.Contains(contactIdStr, StringComparison.OrdinalIgnoreCase) == true)
                    return true;

                return false;
            });

            if (contactPermission?.Id == null)
            {
                logger.LogInformation(
                    "[EXT-REVOKE] No permission entry found for Contact {ContactId} on container {ContainerId}",
                    contactId, containerId);
                // Not a failure — the permission may have already been removed
                return true;
            }

            // Step 2c: Delete the permission entry
            await graphClient.Storage.FileStorage.Containers[containerId]
                .Permissions[contactPermission.Id]
                .DeleteAsync(cancellationToken: ct);

            logger.LogInformation(
                "[EXT-REVOKE] Removed Contact {ContactId} (permission {PermissionId}) from SPE container {ContainerId}",
                contactId, contactPermission.Id, containerId);

            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Failed to remove Contact {ContactId} from SPE container {ContainerId}",
                contactId, containerId);
            return false;
        }
    }

    private static async Task<bool> RemoveSecureProjectWebRoleAsync(
        DataverseWebApiClient dataverseClient,
        Guid contactId,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var webRoleIdStr = configuration["PowerPages:SecureProjectParticipantWebRoleId"];
            if (string.IsNullOrEmpty(webRoleIdStr) || !Guid.TryParse(webRoleIdStr, out var webRoleId))
            {
                logger.LogWarning(
                    "[EXT-REVOKE] PowerPages:SecureProjectParticipantWebRoleId not configured — cannot remove web role from Contact {ContactId}",
                    contactId);
                return false;
            }

            // DELETE the N:N association: contacts({contactId})/mspp_contact_mspp_webrole_powerpagecomponent/{webRoleId}/$ref
            var disassociateUrl = $"contacts({contactId})/mspp_contact_mspp_webrole_powerpagecomponent/{webRoleId}/$ref";
            await dataverseClient.DisassociateAsync(disassociateUrl, ct);

            logger.LogInformation(
                "[EXT-REVOKE] Removed web role {WebRoleId} from Contact {ContactId}",
                webRoleId, contactId);

            return true;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // 404 means the association doesn't exist — not an error
            logger.LogInformation(
                "[EXT-REVOKE] Web role was not associated with Contact {ContactId} — no action needed",
                contactId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "[EXT-REVOKE] Failed to remove web role from Contact {ContactId}. Non-critical.",
                contactId);
            return false;
        }
    }

    // ── Dataverse row DTOs ───────────────────────────────────────────────────

    private sealed class ActiveParticipationRow
    {
        [JsonPropertyName("sprk_externalrecordaccessid")]
        public Guid sprk_externalrecordaccessid { get; set; }
    }
}
