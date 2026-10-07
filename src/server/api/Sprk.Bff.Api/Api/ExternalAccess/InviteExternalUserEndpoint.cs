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
///   1. Resolve the Contact by email over ACTIVE contacts (two rows), or create one.
///   2. Refuse — HTTP 409, a reason code, and a durable collision flag — a contact bound on the WORKFORCE
///      plane or one a systemuser links to (task 141): one contact carries one sign-in.
///   3. Idempotency gate: a contact already bound on the CIAM plane skips account creation.
///   4. Otherwise create a CIAM local email account (task 022 cross-tenant Graph client),
///      persist the returned oid (plane External) and send the onboarding email.
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
                "Resolves an ACTIVE Dataverse Contact by email (or creates one), then — if not already provisioned — " +
                "creates a CIAM local account, persists the oid to sprk_externalobjectid, and sends the " +
                "onboarding email. Idempotent: re-invoking a Contact already bound on the CIAM plane creates no " +
                "second account. Refuses (409) an email whose contact belongs to an employee's work identity or " +
                "an internal user, and an email carried by more than one active contact. Call POST /grant " +
                "separately to create the access record.")
            .Produces<InviteExternalUserResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // 409 (task 141): the email's contact belongs to another sign-in (a work identity, an internal user's
            // contact), is ambiguous, or carries an unreadable binding — refused and flagged; no CIAM account.
            .ProducesProblem(StatusCodes.Status409Conflict)
            // 422: the named record is Restricted, so no contact may be invited to it (task 138).
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            // 503: the record's access settings could not be read (task 138, before any Contact or CIAM work), or
            // the contact lookup could not be read (task 141, sdap.access.invite.contact_lookup_failed).
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
        ContactIdentityBinder binder,
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
            var outcome = await ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, logger,
                resolution: null, ct);
            if (outcome.Failure is { } failure)
            {
                return LookupFailureResult(failure, httpContext);
            }

            if (outcome.Refusal is { } refusal)
            {
                return RefusalResult(refusal, httpContext);
            }

            return TypedResults.Ok(new InviteExternalUserResponse(outcome.ContactId, portalUrl, outcome.Status));
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

    /// <summary>A refused invite: HTTP 409 with a stable reason code and a human message.</summary>
    internal sealed record InviteRefusal(string ReasonCode, string Message);

    /// <summary>
    /// An invite whose contact lookup could not be read (the decision's <c>Fail</c>): not a refusal and not a
    /// collision — nothing was decided, nothing was created or flagged. HTTP 503 with the decision's own reason
    /// code and message, so the client can tell "try again" from a refusal and from an unknown fault (task 141,
    /// verifier finding 5: this used to be thrown into the generic 500 and the code was dropped).
    /// </summary>
    internal sealed record InviteLookupFailure(string ReasonCode, string Message);

    /// <summary>The provisioning outcome: a contact and a status, or a refusal, or a lookup failure.</summary>
    internal sealed record ProvisionOutcome(
        Guid ContactId, string Status, InviteRefusal? Refusal = null, InviteLookupFailure? Failure = null);

    /// <summary>
    /// Resolves-or-creates the Dataverse Contact by email and — if not already provisioned — creates a
    /// CIAM local account, persists the returned oid to <c>Contact.sprk_externalobjectid</c> (plane External),
    /// and sends the onboarding email. <b>Idempotent</b>: a Contact already bound on the CIAM plane creates NO
    /// second account ("AlreadyProvisioned"). <b>Refuses</b> (task 141) a contact bound on the WORKFORCE plane,
    /// one an internal user links to, an ambiguous email, or an unreadable binding — BEFORE any CIAM account is
    /// created. Returns a <see cref="InviteLookupFailure"/> (HTTP 503, its own reason code) when the contact
    /// lookup could not be read. Throws on any other hard failure (Contact create, CIAM create, oid bind). Shared
    /// by <c>/invite</c> and <c>/invite-and-grant</c> (task 029).
    /// </summary>
    /// <param name="resolution">
    /// The invite resolution the caller ALREADY made with <see cref="ContactIdentityBinder.ResolveInviteContactAsync"/>,
    /// or <c>null</c> to resolve here. <c>/invite-and-grant</c> resolves once, BEFORE onboarding, judges that contact
    /// with the grant checks (task 139), and passes the same resolution in — so the contact the checks judged IS the
    /// contact onboarding provisions: one email→contact answer, never a second, possibly different, lookup.
    /// </param>
    internal static async Task<ProvisionOutcome> ProvisionAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ContactIdentityBinder binder,
        string portalUrl,
        ILogger logger,
        InviteContactResolution? resolution,
        CancellationToken ct)
    {
        resolution ??= await binder.ResolveInviteContactAsync(request.Email, ct);

        if (NotProvisionable(resolution, request.Email, logger) is { } stopped)
        {
            return stopped;
        }

        Guid contactId;
        string? etag = null;
        switch (resolution.Action)
        {
            case InviteContactAction.AlreadyProvisioned:
                // Idempotency gate: an oid bound on the CIAM plane means the CIAM account already exists — do NOT
                // create a second account (and do not re-send the onboarding email).
                logger.LogInformation(
                    "[EXT-INVITE] Contact {ContactId} ({Email}) already has a CIAM oid bound — skipping account creation (idempotent).",
                    resolution.ContactId, request.Email);
                return new ProvisionOutcome(resolution.ContactId!.Value, "AlreadyProvisioned");

            case InviteContactAction.ProvisionExisting:
                contactId = resolution.ContactId!.Value;
                etag = resolution.ETag;
                break;

            case InviteContactAction.CreateContact:
                contactId = await CreateContactAsync(dataverseClient, request, logger, ct);
                break;

            default:
                // NeedReferenceLookup (or an action this code does not know) cannot reach here: the binder
                // completes the reference lookup itself. Fail closed — nothing is created.
                throw new InvalidOperationException(
                    $"The invite decision for '{request.Email}' ended in an unexpected state ({resolution.Action}).");
        }

        // Create the CIAM local account (broker-only; no B2B guest).
        var rawOid = await ciamProvisioner.CreateCiamUserAsync(request.Email, request.FirstName, request.LastName, ct);
        if (!Guid.TryParse(rawOid, out var ciamOid) || ciamOid == Guid.Empty)
        {
            throw new InvalidOperationException($"CIAM returned an object id that is not a GUID for '{request.Email}'.");
        }

        // Persist the oid (D format) + plane External, conditional on the row version read with it. If the bind
        // does not land, the CIAM first-login email bind repairs it (the one case that path is kept for).
        var bind = await binder.BindInvitedContactAsync(contactId, etag, ciamOid, ct);
        if (bind.Status != StoreWriteStatus.Written)
        {
            throw new InvalidOperationException(
                $"CIAM account {ciamOid:D} was created for '{request.Email}' but its oid could not be bound to contact " +
                $"{contactId:D} ({bind.Status}). The first CIAM sign-in repairs the binding.");
        }

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
            ciamOid, request.Email, contactId);

        return new ProvisionOutcome(contactId, "Provisioned");
    }

    /// <summary>
    /// The outcome of an invite resolution that must provision NOTHING — a refusal (409; the binder has already
    /// flagged the contact) or an unreadable lookup (503) — or <c>null</c> when the resolution names a contact to
    /// provision (or to create). The ONE mapping, used by <see cref="ProvisionAsync"/> and by <c>/invite-and-grant</c>'s
    /// pre-onboarding check, so both answer a refusal and a lookup failure with the same code and message.
    /// </summary>
    internal static ProvisionOutcome? NotProvisionable(InviteContactResolution resolution, string email, ILogger logger)
    {
        switch (resolution.Action)
        {
            case InviteContactAction.Refuse:
                logger.LogWarning(
                    "[EXT-INVITE] Refused invite for {Email} ({ReasonCode}) — no CIAM account created",
                    email, resolution.ReasonCode);
                return new ProvisionOutcome(resolution.ContactId ?? Guid.Empty, "Refused",
                    new InviteRefusal(resolution.ReasonCode!, resolution.Message ?? "The invite was refused."));

            case InviteContactAction.Fail:
                // The email or reference lookup could not be read. Nothing was decided, created or flagged. The
                // decision's own reason code and message reach the client (503) — never folded into the generic
                // "Failed to onboard" 500, and never read as "no such contact".
                var code = resolution.ReasonCode ?? ContactBindingDecision.InviteContactLookupFailed;
                logger.LogWarning(
                    "[EXT-INVITE] Contact lookup for {Email} could not be read ({ReasonCode}) — nothing created",
                    email, code);
                // The last fallback is deliberately NOT the decision's wording, so a test can tell which one reached
                // the client (third fix round, verifier INFO finding).
                return new ProvisionOutcome(resolution.ContactId ?? Guid.Empty, "LookupFailed",
                    Failure: new InviteLookupFailure(code,
                        resolution.Message ?? ContactIdentityBinder.InviteMessage(code)
                        ?? "The invite could not be completed. Nothing was created; try again."));

            default:
                return null;
        }
    }

    /// <summary>The 409 ProblemDetails for a refused invite (both <c>/invite</c> and <c>/invite-and-grant</c>).</summary>
    internal static IResult RefusalResult(InviteRefusal refusal, HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Identity collision",
            detail: refusal.Message,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = refusal.ReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });

    /// <summary>
    /// The 503 ProblemDetails for an invite whose contact lookup could not be read (both <c>/invite</c> and
    /// <c>/invite-and-grant</c>): the decision's reason code and its message. Retrying is safe — nothing was
    /// created, bound, flagged or granted.
    /// </summary>
    internal static IResult LookupFailureResult(InviteLookupFailure failure, HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Contact lookup unavailable",
            detail: failure.Message,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = failure.ReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>Creates a new, unbound Contact for the invite email. Throws on failure.</summary>
    private static async Task<Guid> CreateContactAsync(
        DataverseWebApiClient dataverseClient,
        InviteExternalUserRequest request,
        ILogger logger,
        CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["emailaddress1"] = request.Email,
            ["firstname"] = request.FirstName ?? string.Empty,
            ["lastname"] = request.LastName ?? request.Email.Split('@')[0]
        };

        var newContactId = await dataverseClient.CreateAsync(ContactEntitySet, payload, ct);
        if (newContactId == Guid.Empty)
        {
            throw new InvalidOperationException($"Failed to create a Contact for '{request.Email}'.");
        }

        logger.LogInformation("[EXT-INVITE] Created new Contact {ContactId} for email {Email}",
            newContactId, request.Email);
        return newContactId;
    }
}
