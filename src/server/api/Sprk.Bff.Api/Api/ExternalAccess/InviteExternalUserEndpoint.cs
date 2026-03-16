using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/invite
///
/// Creates a Power Pages portal invitation for an external Contact by:
///   1. Creating an adx_invitation record in Dataverse (type=Single, max 1 redemption).
///   2. Associating the "Secure Project Participant" web role to the invitation (N:N).
///   3. Returning the invitation code (adx_invitationcode) for delivery to the Contact.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class InviteExternalUserEndpoint
{
    private const string InvitationEntitySet = "adx_invitations";
    private const int InvitationTypeSingle = 756150000;
    private const int DefaultExpiryDays = 30;

    /// <summary>
    /// Registers the invite endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapInviteExternalUserEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/invite", InviteExternalUserAsync)
            .WithName("InviteExternalUser")
            .WithSummary("Send a Power Pages portal invitation to an external Contact")
            .WithDescription(
                "Creates an adx_invitation record and associates the 'Secure Project Participant' web role. " +
                "Returns the invitation code for delivery to the Contact via email or other channel.")
            .Produces<InviteExternalUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> InviteExternalUserAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
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

        // Resolve the web role GUID from configuration
        var webRoleIdStr = configuration["PowerPages:SecureProjectParticipantWebRoleId"];
        if (string.IsNullOrEmpty(webRoleIdStr) || !Guid.TryParse(webRoleIdStr, out var webRoleId))
        {
            logger.LogError(
                "[EXT-INVITE] PowerPages:SecureProjectParticipantWebRoleId is not configured or invalid");
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Configuration Error",
                detail: "The 'Secure Project Participant' web role is not configured. Contact your administrator.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        logger.LogInformation(
            "[EXT-INVITE] Creating invitation for Contact {ContactId} / Project {ProjectId}",
            request.ContactId, request.ProjectId);

        // ── Step 1: Determine expiry date ─────────────────────────────────────
        var expiryDate = request.ExpiryDate
            ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(DefaultExpiryDays));

        // ── Step 2: Create adx_invitation record ──────────────────────────────
        Guid invitationId;
        try
        {
            var invitationName = $"Secure Project Access - {request.ProjectId:N} - {DateTime.UtcNow:yyyyMMdd}";
            var payload = BuildInvitationPayload(invitationName, request.ContactId, expiryDate);
            invitationId = await dataverseClient.CreateAsync(InvitationEntitySet, payload, ct);

            logger.LogInformation(
                "[EXT-INVITE] Created invitation {InvitationId} for Contact {ContactId}",
                invitationId, request.ContactId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-INVITE] Failed to create invitation record for Contact {ContactId} / Project {ProjectId}",
                request.ContactId, request.ProjectId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create invitation record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 3: Associate web role to invitation (N:N) ────────────────────
        try
        {
            // POST to the N:N navigation property: adx_invitation_mspp_webrole_powerpagecomponent/$ref
            await dataverseClient.AssociateAsync(
                $"{InvitationEntitySet}({invitationId})/adx_invitation_mspp_webrole_powerpagecomponent/$ref",
                webRoleId,
                "mspp_webroles",
                ct);

            logger.LogInformation(
                "[EXT-INVITE] Associated web role {WebRoleId} to invitation {InvitationId}",
                webRoleId, invitationId);
        }
        catch (Exception ex)
        {
            // Non-fatal: invitation exists but web role association failed
            // Log and continue — the invitation can still be used manually
            logger.LogError(ex,
                "[EXT-INVITE] Failed to associate web role {WebRoleId} to invitation {InvitationId}. " +
                "Invitation exists but web role was not associated.",
                webRoleId, invitationId);
        }

        // ── Step 4: Retrieve the invitation code ──────────────────────────────
        string invitationCode;
        try
        {
            var invitationRow = await dataverseClient.RetrieveAsync<InvitationRow>(
                InvitationEntitySet,
                invitationId,
                select: "adx_invitationid,adx_invitationcode,adx_expirydate",
                cancellationToken: ct);

            invitationCode = invitationRow?.adx_invitationcode
                ?? throw new InvalidOperationException("Invitation code was not returned by Dataverse");
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "[EXT-INVITE] Failed to retrieve invitation code for invitation {InvitationId}",
                invitationId);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Invitation was created but the invitation code could not be retrieved.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        return TypedResults.Ok(new InviteExternalUserResponse(invitationId, invitationCode, expiryDate));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static object BuildInvitationPayload(
        string invitationName,
        Guid contactId,
        DateOnly expiryDate)
    {
        return new Dictionary<string, object?>
        {
            ["adx_name"] = invitationName,
            ["adx_type"] = InvitationTypeSingle,
            ["adx_invitecontact@odata.bind"] = $"/contacts({contactId})",
            ["adx_expirydate"] = expiryDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("o"),
            ["adx_maximumredemptions"] = 1
        };
    }

    // ── Dataverse row DTOs ───────────────────────────────────────────────────

    private sealed class InvitationRow
    {
        [JsonPropertyName("adx_invitationid")]
        public Guid adx_invitationid { get; set; }

        [JsonPropertyName("adx_invitationcode")]
        public string? adx_invitationcode { get; set; }

        [JsonPropertyName("adx_expirydate")]
        public DateTime? adx_expirydate { get; set; }
    }
}
