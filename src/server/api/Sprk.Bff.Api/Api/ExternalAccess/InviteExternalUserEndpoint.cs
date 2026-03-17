using System.Text.Json.Serialization;
using Microsoft.Graph.Models;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/invite
///
/// Invites an external user to a Secure Project by:
///   1. Creating or resolving the Contact in Dataverse by email.
///   2. Sending an Azure AD B2B guest invitation via Microsoft Graph.
///   3. Returning the Contact ID and invitation redemption URL.
///
/// The caller should then call POST /grant to create the sprk_externalrecordaccess record.
///
/// Prerequisites:
///   - The managed identity must have the "User.Invite.All" Microsoft Graph permission.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class InviteExternalUserEndpoint
{
    private const string ContactEntitySet = "contacts";
    private const string RedirectUrl = "https://myapplications.microsoft.com";

    /// <summary>
    /// Registers the invite endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapInviteExternalUserEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/invite", InviteExternalUserAsync)
            .WithName("InviteExternalUser")
            .WithSummary("Invite an external user to a Secure Project via Azure AD B2B")
            .WithDescription(
                "Creates or resolves a Dataverse Contact by email, then sends an Azure AD B2B guest invitation. " +
                "Returns the Contact ID and invitation redemption URL. " +
                "Call POST /grant separately to create the access record.")
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
        IGraphClientFactory graphClientFactory,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        logger.LogInformation(
            "[EXT-INVITE] Inviting external user {Email} to Project {ProjectId}",
            request.Email, request.ProjectId);

        // ── Step 1: Create or resolve Contact in Dataverse ────────────────────
        var contactId = await ResolveOrCreateContactAsync(dataverseClient, request, logger, ct);
        if (contactId == Guid.Empty)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to create or resolve Contact record in Dataverse.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 2: Send Azure AD B2B invitation via Microsoft Graph ──────────
        // Requires: User.Invite.All application permission on the managed identity.
        var (inviteRedeemUrl, status) = await SendB2BInvitationAsync(
            graphClientFactory, request.Email, request.FirstName, request.LastName, logger, ct);

        logger.LogInformation(
            "[EXT-INVITE] B2B invitation sent to {Email} — Status: {Status}, Contact: {ContactId}",
            request.Email, status, contactId);

        return TypedResults.Ok(new InviteExternalUserResponse(contactId, inviteRedeemUrl, status));
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<Guid> ResolveOrCreateContactAsync(
        DataverseWebApiClient dataverseClient,
        InviteExternalUserRequest request,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // Check if Contact already exists by email
            var existing = await dataverseClient.QueryAsync<ContactRow>(
                ContactEntitySet,
                filter: $"emailaddress1 eq '{Uri.EscapeDataString(request.Email)}'",
                select: "contactid",
                top: 1,
                cancellationToken: ct);

            if (existing.Count > 0)
            {
                logger.LogDebug("[EXT-INVITE] Found existing Contact {ContactId} for email {Email}",
                    existing[0].contactid, request.Email);
                return existing[0].contactid;
            }

            // Create new Contact
            var payload = new Dictionary<string, object?>
            {
                ["emailaddress1"] = request.Email,
                ["firstname"] = request.FirstName ?? string.Empty,
                ["lastname"] = request.LastName ?? request.Email.Split('@')[0]
            };

            var newContactId = await dataverseClient.CreateAsync(ContactEntitySet, payload, ct);
            logger.LogInformation("[EXT-INVITE] Created new Contact {ContactId} for email {Email}",
                newContactId, request.Email);

            return newContactId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INVITE] Failed to resolve or create Contact for email {Email}", request.Email);
            return Guid.Empty;
        }
    }

    private static async Task<(string redeemUrl, string status)> SendB2BInvitationAsync(
        IGraphClientFactory graphClientFactory,
        string email,
        string? firstName,
        string? lastName,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            var graphClient = graphClientFactory.ForApp();
            var displayName = string.Join(" ", new[] { firstName, lastName }.Where(s => !string.IsNullOrEmpty(s)));

            var invitation = await graphClient.Invitations.PostAsync(new Invitation
            {
                InvitedUserEmailAddress = email,
                InviteRedirectUrl = RedirectUrl,
                SendInvitationMessage = true,
                InvitedUserDisplayName = string.IsNullOrEmpty(displayName) ? null : displayName,
                InvitedUserMessageInfo = new InvitedUserMessageInfo
                {
                    CustomizedMessageBody =
                        "You have been granted access to a Secure Project workspace. " +
                        "Please accept this invitation to continue."
                }
            }, cancellationToken: ct);

            return (
                invitation?.InviteRedeemUrl ?? string.Empty,
                invitation?.Status ?? "Unknown"
            );
        }
        catch (Exception ex)
        {
            // Non-fatal: Contact was created, but invitation failed.
            // Admin can resend manually. Return empty redeemUrl with error status.
            logger.LogError(ex, "[EXT-INVITE] Failed to send B2B invitation to {Email}", email);
            return (string.Empty, "Error");
        }
    }

    // ── Dataverse row DTOs ───────────────────────────────────────────────────

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid contactid { get; set; }
    }
}
