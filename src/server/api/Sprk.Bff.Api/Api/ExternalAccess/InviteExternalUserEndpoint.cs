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
///   4. Otherwise stamp the pending-invite marker (contact.sprk_externalinvitedon, task 184), create a CIAM local
///      email account (task 022 cross-tenant Graph client), persist the returned oid (plane External) and send the
///      onboarding email.
///   5. If the person ALREADY has a CIAM account (Graph 400 ObjectConflict on proxyAddresses — task 184), the invite
///      succeeds as "ExistingAccount": the contact stays pending (marker, no oid), the "sign in with your existing
///      account" email is sent instead, and the first CIAM sign-in binds the contact (E11's intent gate).
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
                "onboarding email. A person who already has a CIAM account is answered \"ExistingAccount\": the contact " +
                "stays pending and is bound at their first sign-in. Idempotent: re-invoking a Contact already bound on the CIAM plane creates no " +
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
            // the contact lookup could not be read (task 141, sdap.access.invite.contact_lookup_failed), or the
            // pending-invite marker a contact needed did not land (task 184, sdap.access.invite.invite_marker_not_written;
            // no email sent — retry, after the SpaarkeMaster import on a stamp still receiving the column).
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
        Sprk.Bff.Api.Configuration.CustomerIdentity customer,
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
            var outcome = await ExternalInviteService.ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, InviteLinkCustomerKey(customer),
                logger, resolution: null, ct);
            if (outcome.Failure is { } failure)
            {
                return LookupFailureResult(failure, httpContext);
            }

            if (outcome.Refusal is { } refusal)
            {
                return RefusalResult(refusal, httpContext);
            }

            // Task 185: the response carries the BASE portal URL. The invite link and its code go only into the email.
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
    internal sealed record InviteLookupFailure(string ReasonCode, string Message, string? Title = null);

    /// <summary>The <see cref="ProvisionOutcome.Status"/> of a contact whose CIAM account already existed (no onboarding email sent).</summary>
    internal const string AlreadyProvisionedStatus = "AlreadyProvisioned";

    /// <summary>
    /// Task 184 — the <see cref="ProvisionOutcome.Status"/> of an invite whose person ALREADY had a CIAM account (Graph
    /// <c>ObjectConflict</c>): no account was created, the contact stays pending (marker set, no oid) and the "sign in with
    /// your existing account" email was sent instead of the onboarding email. The first sign-in binds the contact.
    /// </summary>
    internal const string ExistingAccountStatus = "ExistingAccount";

    /// <summary>The <see cref="ProvisionOutcome.Status"/> of an invite that created the CIAM account and bound its oid.</summary>
    internal const string ProvisionedStatus = "Provisioned";

    /// <summary>The provisioning outcome: a contact and a status, or a refusal, or a lookup failure.</summary>
    /// <param name="EmailFailed">
    /// Task 185 slice E: the invite email (onboarding or existing-account) was attempted and did not send. The routes keep
    /// answering as before (the send is non-fatal there); the grant flows report it as a failed invite, since nobody holds
    /// the code.
    /// </param>
    internal sealed record ProvisionOutcome(
        Guid ContactId, string Status, InviteRefusal? Refusal = null, InviteLookupFailure? Failure = null,
        bool EmailFailed = false);

    /// <summary>
    /// The invite core: resolve-or-create the contact, stamp the pending-invite marker (task 184), create the CIAM account
    /// (or answer ExistingAccount), bind the oid and send the invite email carrying the code (task 185). Task 185 §12 I-5: the
    /// logic lives in ONE service, <see cref="ExternalInviteService.ProvisionAsync"/>, shared with the grant flows; this
    /// forwarder keeps the signature <c>/invite</c>, <c>/invite-and-grant</c> and their tests call.
    /// </summary>
    /// <param name="resolution">
    /// The invite resolution the caller ALREADY made with <see cref="ContactIdentityBinder.ResolveInviteContactAsync"/>,
    /// or <c>null</c> to resolve here (see <see cref="ExternalInviteService.ProvisionAsync"/>).
    /// </param>
    internal static Task<ProvisionOutcome> ProvisionAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ContactIdentityBinder binder,
        string portalUrl,
        string? customerKey,
        ILogger logger,
        InviteContactResolution? resolution,
        CancellationToken ct)
        => ExternalInviteService.ProvisionAsync(
            request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, customerKey, logger, resolution, ct);

    /// <summary>
    /// Task 185 (design note §1.1) — the invite link: <c>{PortalUrl}/?customer={key}&amp;invite={code}</c>. Copies
    /// <see cref="GrantAccessNotifier.BuildPortalLink"/>: built ONLY from the trusted portal URL (an absolute https URL, http
    /// only on loopback, no credentials; its own query and fragment are dropped), this stamp's customer key and the code,
    /// each URL-encoded. Unlike a record link it is still built when the customer key is not resolved (dev before
    /// <c>Customer:Id</c>): <c>{PortalUrl}/?invite={code}</c>. With no code, it is the portal link without <c>invite</c>.
    /// <c>null</c> when the portal URL is unusable. The result carries a secret: never log it, never return it in a response.
    /// </summary>
    internal static string? BuildInviteLink(string? portalUrl, string? customerKey, string? inviteCode)
    {
        if (!Uri.TryCreate(portalUrl?.Trim(), UriKind.Absolute, out var portal)
            || !string.IsNullOrEmpty(portal.UserInfo)
            || !(portal.Scheme == Uri.UriSchemeHttps || (portal.Scheme == Uri.UriSchemeHttp && portal.IsLoopback)))
        {
            return null;
        }

        var query = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(customerKey))
        {
            query.Add($"customer={Uri.EscapeDataString(customerKey.Trim())}");
        }

        if (!string.IsNullOrEmpty(inviteCode))
        {
            query.Add($"invite={Uri.EscapeDataString(inviteCode)}");
        }

        var baseUrl = portal.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return query.Count == 0 ? $"{baseUrl}/" : $"{baseUrl}/?{string.Join('&', query)}";
    }

    /// <summary>This stamp's customer key for the invite link, or <c>null</c> when it is not resolved (dev).</summary>
    internal static string? InviteLinkCustomerKey(Sprk.Bff.Api.Configuration.CustomerIdentity customer)
        => customer.IsResolved ? customer.Id : null;

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
            title: failure.Title ?? "Contact lookup unavailable",
            detail: failure.Message,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = failure.ReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });
}
