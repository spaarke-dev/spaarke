using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/invite
///
/// Admin-initiated onboarding of an external user to the Secure Project Workspace (ADR-028
/// Amendment A1 — broker-only). Replaces the former Entra B2B guest invitation with Entra External
/// ID (CIAM) account creation:
///   1. Create or resolve the Contact in Dataverse by email (reads its sprk_externalobjectid).
///   2. Idempotency gate: if the Contact already has an oid bound, SKIP account creation.
///   3. Otherwise create a CIAM local email account (task 022 cross-tenant Graph client),
///      persist the returned oid to Contact.sprk_externalobjectid, and send the onboarding email.
///
/// The caller then calls POST /grant to create the sprk_externalrecordaccess record.
///
/// NO workforce Entra B2B guest is created, and the external user's token is never exchanged
/// downstream (broker-only). The temporary password is delivered via SSPR "Forgot password" — it
/// is never returned to the caller.
///
/// ADR-001: Minimal API — no controllers.
/// ADR-008: Endpoint filter for internal caller check (RequireAuthorization).
/// ADR-010: Concrete DI injections.
/// </summary>
public static class InviteExternalUserEndpoint
{
    private const string ContactEntitySet = "contacts";

    /// <summary>
    /// Registers the invite endpoint on the external-access group.
    /// </summary>
    public static RouteGroupBuilder MapInviteExternalUserEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/invite", InviteExternalUserAsync)
            .WithName("InviteExternalUser")
            .WithSummary("Onboard an external user to a Secure Project via Entra External ID (CIAM)")
            .WithDescription(
                "Creates or resolves a Dataverse Contact by email, then — if not already provisioned — " +
                "creates a CIAM local account, persists the oid to sprk_externalobjectid, and sends the " +
                "onboarding email. Idempotent: re-invoking a Contact that already has an oid creates no " +
                "second account. Call POST /grant separately to create the access record.")
            .Produces<InviteExternalUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // 422: the named record is Restricted, so no contact may be invited to it (task 138).
            // 503: the record's access settings could not be read. Both before any Contact or CIAM write.
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    // =========================================================================
    // Handler
    // =========================================================================

    private static async Task<IResult> InviteExternalUserAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

        // Note (task 070): /invite only onboards (resolve-or-create Contact + CIAM account); it writes NO grant.
        // The grant is created separately by /grant or /invite-and-grant.
        //
        // Task 138: the request still names a record — DelegationRuleFilter refuses one that does not, before
        // this handler runs — and inviting someone TO a Restricted record provisions an identity and emails a
        // person who can never get access to it. So the record's access policy is checked here, before the
        // Contact or the CIAM account is touched, with the same decision function the grant routes use. The
        // invitee is a named contact, so only Restricted (or an unreadable policy) refuses; Secure and Limited
        // admit named contacts.
        var root = GrantExternalAccessEndpoint.ResolveGrantRoot(new GrantAccessRequest(
            ContactId: Guid.Empty,      // irrelevant to root resolution
            ProjectId: request.ProjectId,
            AccessLevel: default,
            ExpiryDate: null,
            OrganizationId: null,
            RecordType: request.RecordType,
            RecordId: request.RecordId));
        if (!root.Ok)
            return ProblemDetailsHelper.ValidationError(root.Error!);

        var policy = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            participations, root.Type, root.Id, GrantGranteeKind.Contact, logger, ct);
        if (!policy.IsAllowed)
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(policy, httpContext);

        logger.LogInformation("[EXT-INVITE] Onboarding external user {Email}", request.Email);

        var portalUrl = configuration["ExternalAccess:PortalUrl"]
            ?? throw new InvalidOperationException("ExternalAccess:PortalUrl is not configured.");

        try
        {
            var (contactId, status) = await ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, portalUrl, logger, ct);
            return TypedResults.Ok(new InviteExternalUserResponse(contactId, portalUrl, status));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INVITE] Failed to onboard external user {Email}", request.Email);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to onboard the external user.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }
    }

    // =========================================================================
    // Reusable core (shared with the invite-and-grant orchestration, task 029)
    // =========================================================================

    /// <summary>
    /// Resolves-or-creates the Dataverse Contact by email and — if not already provisioned — creates a
    /// CIAM local account, persists the returned oid to <c>Contact.sprk_externalobjectid</c>, and sends
    /// the onboarding email. <b>Idempotent</b>: when the Contact already has an oid bound, NO second
    /// account is created (returns "AlreadyProvisioned"). Throws on a hard failure (Contact resolve or
    /// CIAM create). Shared by <c>/invite</c> and <c>/invite-and-grant</c> (task 029).
    /// </summary>
    /// <returns>The resolved Contact id and a status ("Provisioned" | "AlreadyProvisioned").</returns>
    internal static async Task<(Guid ContactId, string Status)> ProvisionAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        string portalUrl,
        ILogger logger,
        CancellationToken ct)
    {
        var (contactId, existingOid) = await ResolveOrCreateContactAsync(dataverseClient, request, logger, ct);
        if (contactId == Guid.Empty)
        {
            throw new InvalidOperationException($"Failed to create or resolve Contact for '{request.Email}'.");
        }

        // Idempotency gate: an existing oid means the CIAM account already exists — do NOT create a
        // second account (and do not re-send the onboarding email).
        if (!string.IsNullOrWhiteSpace(existingOid))
        {
            logger.LogInformation(
                "[EXT-INVITE] Contact {ContactId} ({Email}) already has an oid bound — skipping CIAM account creation (idempotent).",
                contactId, request.Email);
            return (contactId, "AlreadyProvisioned");
        }

        // Create the CIAM local account (broker-only; no B2B guest).
        var oid = await ciamProvisioner.CreateCiamUserAsync(request.Email, request.FirstName, request.LastName, ct);

        // Persist the oid to Contact.sprk_externalobjectid.
        await dataverseClient.UpdateAsync(
            ContactEntitySet,
            contactId,
            new Dictionary<string, object?> { ["sprk_externalobjectid"] = oid },
            ct);

        // Send the onboarding email (task 024). portalUrl is inserted un-encoded — trusted server config.
        // Non-fatal: the account + oid are persisted; admin can re-send out-of-band on failure.
        try
        {
            await emailService.SendCiamOnboardingEmailAsync(request.Email, request.FirstName ?? string.Empty, portalUrl, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[EXT-INVITE] CIAM account created for {Email} but onboarding email failed to send.", request.Email);
        }

        logger.LogInformation(
            "[EXT-INVITE] Provisioned CIAM user {Oid} for {Email} — Contact: {ContactId}",
            oid, request.Email, contactId);

        return (contactId, "Provisioned");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Resolves the Contact by email (reading its current oid binding) or creates a new one.
    /// Returns (Guid.Empty, null) on failure.
    /// </summary>
    private static async Task<(Guid ContactId, string? ExistingOid)> ResolveOrCreateContactAsync(
        DataverseWebApiClient dataverseClient,
        InviteExternalUserRequest request,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            // Check if Contact already exists by email (and read any existing oid binding).
            var existing = await FindContactByEmailAsync(dataverseClient, request.Email, ct);

            if (existing is { } found)
            {
                logger.LogDebug("[EXT-INVITE] Found existing Contact {ContactId} for email {Email} (oid bound: {Bound})",
                    found.ContactId, request.Email, !string.IsNullOrWhiteSpace(found.ExistingOid));
                return found;
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

            return (newContactId, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INVITE] Failed to resolve or create Contact for email {Email}", request.Email);
            return (Guid.Empty, null);
        }
    }

    /// <summary>
    /// The EXISTING Contact the onboarding seam would resolve for <paramref name="email"/>, READ-ONLY — or <c>null</c>
    /// when none exists. The one email match: <see cref="ProvisionAsync"/> resolves through it, and
    /// <c>/invite-and-grant</c> calls it BEFORE onboarding (task 139) so its never-lower and No Access checks judge
    /// the same contact onboarding would use, without creating one. Exceptions propagate — a failed lookup must
    /// never read as "no such contact".
    /// </summary>
    internal static async Task<(Guid ContactId, string? ExistingOid)?> FindContactByEmailAsync(
        DataverseWebApiClient dataverseClient, string email, CancellationToken ct)
    {
        var existing = await dataverseClient.QueryAsync<ContactRow>(
            ContactEntitySet,
            filter: $"emailaddress1 eq '{email.Replace("'", "''")}'",
            select: "contactid,sprk_externalobjectid",
            top: 1,
            cancellationToken: ct);

        return existing.Count > 0
            ? (existing[0].contactid, existing[0].sprk_externalobjectid)
            : null;
    }

    // ── Dataverse row DTOs ───────────────────────────────────────────────────

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid contactid { get; set; }

        [JsonPropertyName("sprk_externalobjectid")]
        public string? sprk_externalobjectid { get; set; }
    }
}
