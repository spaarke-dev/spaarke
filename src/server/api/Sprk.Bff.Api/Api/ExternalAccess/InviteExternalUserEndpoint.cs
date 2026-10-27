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
///      succeeds as "ExistingAccount" internally: the contact stays pending (marker, no oid), the "sign in with your
///      existing account" email is sent instead, and the first CIAM sign-in binds the contact (E11's intent gate). Task
///      185 §12 I-10: the response answers "Provisioned", as for a new account; only the operator log names the branch.
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
                "onboarding email. A person who already has a CIAM account gets the existing-account email instead and is " +
                "answered \"Provisioned\", like a new account (the inviter is never told which): the contact stays pending " +
                "and is bound at their first sign-in. Idempotent: re-invoking a Contact already bound on the CIAM plane creates no " +
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
            // no email sent — retry, after the SpaarkeMaster import on a stamp still receiving the column), or the email's
            // organisation could not be looked up (slice B2, sdap.access.invite.tenant_lookup_unavailable; nothing written —
            // retry).
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
        InviteOrganizationResolver organizations,
        IConfiguration configuration,
        Sprk.Bff.Api.Configuration.CustomerIdentity customer,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // C5: the request's own shape is validated first, on every host, before any lookup: a malformed request is a 400
        // whether or not this host has a CIAM configuration.
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

        if (ValidateInvitedTenant(request.InvitedTenantId) is { } tenantProblem)
            return tenantProblem;

        if (ValidateNoInviteKindChoice(request.SendAsPortalAccount) is { } kindProblem)
            return kindProblem;

        // Task 185 slice F + §12 I-11: the CIAM provisioner constructs on a host without Ciam:* and validates at first use.
        // A portal-account invite needs CIAM, so there it answers a 503 ProblemDetails with the same reason code /grant
        // reports (ciam_not_configured), before anything else is read or written. Slice B3: a work-account invite needs no
        // CIAM, so on such a host the organisation is decided first and only a work-account invite goes on.
        InviteOrganizationDecision? organizationWithoutCiam = null;
        if (!ciamProvisioner.IsConfigured)
        {
            var (decided, withoutCiamProblem) = await DecideOrganizationWithoutCiamAsync(organizations, request, httpContext, ct);
            if (withoutCiamProblem is not null)
                return withoutCiamProblem;
            organizationWithoutCiam = decided;
        }

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

        // Slice B2: a work account (the email's organisation is a configured customer tenant) or a portal account, decided
        // here before any contact is read or written. A malformed domain is a 400; a lookup with no definite answer a 503.
        var (organization, organizationProblem) = organizationWithoutCiam is not null
            ? (organizationWithoutCiam, null)
            : await DecideOrganizationAsync(organizations, request, httpContext, ct);
        if (organizationProblem is not null)
            return organizationProblem;

        logger.LogInformation("[EXT-INVITE] Onboarding external user {Email}", request.Email);

        var portalUrl = configuration["ExternalAccess:PortalUrl"]
            ?? throw new InvalidOperationException("ExternalAccess:PortalUrl is not configured.");

        try
        {
            var outcome = await ExternalInviteService.ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, InviteLinkCustomerKey(customer),
                logger, resolution: null, ct, organization!.WorkAccountTenantId);
            if (outcome.Failure is { } failure)
            {
                return LookupFailureResult(failure, httpContext);
            }

            if (outcome.Refusal is { } refusal)
            {
                return RefusalResult(refusal, httpContext);
            }

            // Task 185: the response carries the BASE portal URL. The invite link and its code go only into the email.
            // §12 I-10: an existing CIAM account is answered as a new one (the operator log keeps the difference), and an
            // email that did not send is reported the same way on both branches (adversarial pass 2, F2).
            var (emailSent, emailErrorCode) = InviterVisibleEmail(outcome);
            var status = InviterVisibleStatus(outcome.Status);

            // Slice B3: a work-account stamp landed, so the notice goes to the stamped address. /invite writes no grant, so
            // the notice is the fixed text and names no record. Slice C2b: a notice that did not send retracts the stamp
            // ("email not sent"), so the next invite stamps and sends again. C5: that invite is then answered as not sent.
            if (outcome.Status == WorkAccountInvitedStatus)
            {
                (status, emailSent, emailErrorCode) = await ExternalInviteService.SendWorkAccountNoticeAsync(
                        emailService, request.Email, portalUrl, InviteLinkCustomerKey(customer), record: null, outcome.ContactId,
                        logger, binder, outcome.WrittenInviteStamp)
                    ? (status, true, (string?)null)
                    : (InviteNotSentStatus, false, GrantInviteStatus.EmailNotSentReasonCode);
            }

            // Slice C2b: the person already has an account. No invite email is due, none is sent and nothing was written:
            // emailSent is false (never null) with its own code, and staff are given the sign-in link to pass on.
            if (outcome.Status == AlreadyProvisionedStatus)
            {
                return TypedResults.Ok(AlreadyHasAccountResponse(outcome.ContactId, portalUrl, InviteLinkCustomerKey(customer)));
            }

            return TypedResults.Ok(new InviteExternalUserResponse(
                outcome.ContactId, portalUrl, status, emailSent, emailErrorCode));
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
    /// Slice B2: <see cref="InviteExternalUserRequest.InvitedTenantId"/> is not accepted. Null when absent or blank, else a
    /// 400 in the route's validation shape: the organisation is resolved by the server from the email, never supplied by the
    /// request. Pure. Shared by <c>/invite</c> and <c>/invite-and-grant</c>.
    /// </summary>
    internal static IResult? ValidateInvitedTenant(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? null
            : ProblemDetailsHelper.ValidationError(
                "InvitedTenantId is not accepted: the person's organisation is resolved from the email address. Omit it.");

    /// <summary>
    /// Slice B3: <see cref="InviteExternalUserRequest.SendAsPortalAccount"/> is not accepted. Null when absent, else a 400 in
    /// the route's validation shape, whatever the value (<c>false</c> included): the kind of sign-in an invite is for is
    /// decided by the server from the email's domain, never chosen by the request. Pure. Shared by <c>/invite</c> and
    /// <c>/invite-and-grant</c>.
    /// </summary>
    internal static IResult? ValidateNoInviteKindChoice(bool? sendAsPortalAccount)
        => sendAsPortalAccount is null
            ? null
            : ProblemDetailsHelper.ValidationError(
                "SendAsPortalAccount is not accepted: the kind of sign-in is decided from the email address. Omit it.");

    /// <summary>
    /// Slice B2: which sign-in the invite is for (<see cref="InviteOrganizationResolver"/>), or the problem that stops the
    /// invite before anything is read or written: 400 for an email domain that is not a host name, 503
    /// <see cref="ContactBindingDecision.InviteTenantLookupUnavailable"/> when the lookup gave no definite answer (retry). The
    /// cache is scoped to the caller's tenant. Shared by <c>/invite</c> and
    /// <c>/invite-and-grant</c>.
    /// </summary>
    internal static async Task<(InviteOrganizationDecision? Decision, IResult? Problem)> DecideOrganizationAsync(
        InviteOrganizationResolver organizations, InviteExternalUserRequest request, HttpContext httpContext,
        CancellationToken ct)
    {
        var callerTenant = httpContext.User.FindFirst("tid")?.Value
            ?? httpContext.User.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        var decision = await organizations.DecideAsync(request.Email, callerTenant, ct);
        return decision.Status switch
        {
            InviteOrganizationStatus.Decided => (decision, null),
            InviteOrganizationStatus.MalformedDomain => (null, ProblemDetailsHelper.ValidationError(
                "The email address's domain is not a valid domain name.")),
            _ => (null, LookupFailureResult(new InviteLookupFailure(
                ContactBindingDecision.InviteTenantLookupUnavailable,
                ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteTenantLookupUnavailable)!,
                Title: "Organisation lookup unavailable"), httpContext)),
        };
    }

    /// <summary>
    /// Slice B3: the organisation decision on a host without <c>Ciam:*</c>, where only a work-account invite can be sent. The
    /// decision when it is a work account; otherwise the problem that stops the invite before anything else is read or
    /// written: 503 <see cref="GrantInviteStatus.CiamNotConfiguredReasonCode"/> for a portal-account invite (or a request with
    /// no email), 400 for a domain that is not a host name, 503 tenant_lookup_unavailable when the lookup gave no definite
    /// answer (it may be a work account, so staff retry). Shared by <c>/invite</c> and <c>/invite-and-grant</c>.
    /// </summary>
    internal static async Task<(InviteOrganizationDecision? Decision, IResult? Problem)> DecideOrganizationWithoutCiamAsync(
        InviteOrganizationResolver organizations, InviteExternalUserRequest request, HttpContext httpContext,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
            return (null, CiamNotConfiguredResult(httpContext));

        var (decision, problem) = await DecideOrganizationAsync(organizations, request, httpContext, ct);
        if (problem is not null)
            return (null, problem);

        return decision!.IsWorkAccount ? (decision, null) : (null, CiamNotConfiguredResult(httpContext));
    }

    /// <summary>
    /// Slice B2: the <see cref="ProvisionOutcome.Status"/> of a work-account invite. The contact is stamped for a Workforce
    /// or WorkforceHome sign-in from its organisation's tenant; no CIAM account was created and no invite email was sent.
    /// </summary>
    internal const string WorkAccountInvitedStatus = "WorkAccountInvited";

    /// <summary>
    /// C5: the onboarding status <c>/invite</c> and <c>/invite-and-grant</c> answer for a work-account invite whose notice did
    /// not send and was therefore retracted: an invite is reported as made only when its email went out. It is the value the
    /// grant response's <c>invite</c> field reports for an invite that did not complete (<see cref="GrantInviteStatus.Failed"/>),
    /// always with <c>emailSent: false</c> and <see cref="GrantInviteStatus.EmailNotSentReasonCode"/>. No new value: the
    /// existing one, named for these routes.
    /// </summary>
    internal const string InviteNotSentStatus = GrantInviteStatus.Failed;

    /// <summary>
    /// Slice B2: a work-account invite whose contact already carries a sign-in is refused (409,
    /// <see cref="ContactBindingDecision.InviteContactAlreadyBound"/>; nothing written, no flag): one contact carries one
    /// sign-in. A contact bound on a workforce plane is already refused by the invite decision itself. <c>null</c> when the
    /// resolution may receive a work-account invite.
    /// </summary>
    internal static ProvisionOutcome? WorkAccountNotProvisionable(InviteContactResolution resolution, string email, ILogger logger)
    {
        // A contact carrying the removed marker never reaches here: the invite decision refused it
        // (ContactBindingDecision.InviteContactRemovedAdminReinstate, through NotProvisionable).
        if (resolution.Action != InviteContactAction.AlreadyProvisioned)
        {
            return null;
        }

        logger.LogWarning(
            "[EXT-INVITE] Refused work-account invite ({ReasonCode}): contact {ContactId} already signs in with a portal "
            + "account — nothing written", ContactBindingDecision.InviteContactAlreadyBound, resolution.ContactId);
        return new ProvisionOutcome(resolution.ContactId ?? Guid.Empty, "Refused",
            new InviteRefusal(ContactBindingDecision.InviteContactAlreadyBound,
                ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactAlreadyBound)!));
    }

    /// <summary>A refused invite: HTTP 409 with a stable reason code and a human message.</summary>
    internal sealed record InviteRefusal(string ReasonCode, string Message);

    /// <summary>
    /// An invite whose contact lookup could not be read (the decision's <c>Fail</c>): not a refusal and not a
    /// collision — nothing was decided, nothing was created or flagged. HTTP 503 with the decision's own reason
    /// code and message, so the client can tell "try again" from a refusal and from an unknown fault (task 141,
    /// verifier finding 5: this used to be thrown into the generic 500 and the code was dropped). ISS-005 (#1673): an
    /// invite that cannot resolve the owner of the contact it would create is the same shape with
    /// <paramref name="StatusCode"/> 500 (an operator fix, not a retry).
    /// </summary>
    internal sealed record InviteLookupFailure(
        string ReasonCode, string Message, string? Title = null, int StatusCode = StatusCodes.Status503ServiceUnavailable);

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

    /// <summary>
    /// Task 185 §12 I-10 (owner round 98c): the onboarding status the INVITER sees on <c>/invite</c> and
    /// <c>/invite-and-grant</c>. <see cref="ExistingAccountStatus"/> is answered as <see cref="ProvisionedStatus"/>, the
    /// new-account answer, so the response never tells an inviter that an email already has a CIAM account (a
    /// cross-customer enumeration oracle). The branch stays in the operator log; the person still gets the existing-account
    /// email. Every other status is unchanged.
    /// </summary>
    internal static string InviterVisibleStatus(string status)
        => string.Equals(status, ExistingAccountStatus, StringComparison.Ordinal) ? ProvisionedStatus : status;

    /// <summary>
    /// Task 185 (adversarial pass 2, F2): the invite email's result as <c>/invite</c> and <c>/invite-and-grant</c> report it —
    /// <c>(false, <see cref="GrantInviteStatus.EmailNotSentReasonCode"/>)</c> when the invite email was attempted and did not
    /// send (nobody holds the code: a re-invite sends a new one), <c>(true, null)</c> when it was sent, <c>(null, null)</c> when
    /// no invite email was due. §12 I-10: the new-account and the existing-account branch answer identically.
    /// </summary>
    internal static (bool? EmailSent, string? EmailErrorCode) InviterVisibleEmail(ProvisionOutcome outcome)
    {
        if (outcome.EmailFailed)
            return (false, GrantInviteStatus.EmailNotSentReasonCode);

        return outcome.Status is ProvisionedStatus or ExistingAccountStatus ? (true, null) : (null, null);
    }

    /// <summary>
    /// Slice C2b: <c>/invite</c>'s answer for a contact that already has an account ("AlreadyProvisioned"): no email was
    /// due or sent, so <c>emailSent</c> is <c>false</c> with <see cref="ContactBindingDecision.InviteContactAlreadyHasAccount"/>,
    /// the staff text, and the portal sign-in link the work-account notice carries (no code). Pure.
    /// </summary>
    internal static InviteExternalUserResponse AlreadyHasAccountResponse(Guid contactId, string portalUrl, string? customerKey)
        => new(
            contactId, portalUrl, AlreadyProvisionedStatus, EmailSent: false,
            EmailErrorCode: ContactBindingDecision.InviteContactAlreadyHasAccount,
            StaffMessage: ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactAlreadyHasAccount),
            SignInUrl: BuildInviteLink(portalUrl, customerKey, inviteCode: null));

    /// <summary>The provisioning outcome: a contact and a status, or a refusal, or a lookup failure.</summary>
    /// <param name="EmailFailed">
    /// Task 185 slice E: the invite email (onboarding or existing-account) was attempted and did not send, so nobody holds
    /// the code. The grant flows report it as a failed invite; <c>/invite</c> and <c>/invite-and-grant</c> still answer 200
    /// and report it in <c>emailSent</c> / <c>emailErrorCode</c> (<see cref="InviterVisibleEmail"/>).
    /// </param>
    internal sealed record ProvisionOutcome(
        Guid ContactId, string Status, InviteRefusal? Refusal = null, InviteLookupFailure? Failure = null,
        bool EmailFailed = false)
    {
        /// <summary>
        /// Slice C2b: the invite stamp's email hash this invite wrote on the contact, set on a work-account invite, whose
        /// notice the caller sends: when the notice does not send, the caller retracts the invite with it
        /// (<see cref="ExternalInviteService.SendWorkAccountNoticeAsync"/>). Null otherwise.
        /// </summary>
        internal string? WrittenInviteStamp { get; init; }
    }

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
        CancellationToken ct,
        Guid? workAccountTenantId = null)
        => ExternalInviteService.ProvisionAsync(
            request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, customerKey, logger, resolution, ct,
            workAccountTenantId);

    /// <summary>
    /// Task 185 (design note §1.1, §12 I-12) — the invite link: <c>{PortalUrl}/?customer={key}#invite={code}</c>, the code in
    /// the fragment so no server or proxy log on the way to the SPA sees it. Copies
    /// <see cref="GrantAccessNotifier.BuildPortalLink"/>: built ONLY from the trusted portal URL (an absolute https URL, http
    /// only on loopback, no credentials; its own query and fragment are dropped), this stamp's customer key and the code,
    /// each URL-encoded. Unlike a record link it is still built when the customer key is not resolved (dev before
    /// <c>Customer:Id</c>): <c>{PortalUrl}/#invite={code}</c>. With no code, it is the portal link without <c>invite</c>.
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

        var baseUrl = portal.GetLeftPart(UriPartial.Path).TrimEnd('/');
        var link = string.IsNullOrWhiteSpace(customerKey)
            ? $"{baseUrl}/"
            : $"{baseUrl}/?customer={Uri.EscapeDataString(customerKey.Trim())}";

        // §12 I-12: the code rides in the FRAGMENT, which the browser never sends, so the SPA host, CDN, WAF and front-door
        // access logs never see it. The SPA reads it (falling back to ?invite=) and strips it before MSAL runs.
        return string.IsNullOrEmpty(inviteCode) ? link : $"{link}#invite={Uri.EscapeDataString(inviteCode)}";
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

    /// <summary>
    /// The 409 ProblemDetails for a refused invite (both <c>/invite</c> and <c>/invite-and-grant</c>). C5: titled by reason
    /// code, as the grant core titles its refusals: a person whose external access an administrator removed is "External
    /// access removed", never an identity collision; every other refusal keeps "Identity collision".
    /// </summary>
    internal static IResult RefusalResult(InviteRefusal refusal, HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: refusal.ReasonCode == ContactBindingDecision.InviteContactRemovedAdminReinstate
                ? "External access removed"
                : "Identity collision",
            detail: refusal.Message,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = refusal.ReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });

    /// <summary>
    /// Task 185 §12 I-11: the 503 ProblemDetails of <c>/invite</c> and <c>/invite-and-grant</c> on a host with no
    /// <c>Ciam:*</c> configuration, carrying <see cref="GrantInviteStatus.CiamNotConfiguredReasonCode"/> (the code
    /// <c>/grant</c> reports in its <c>invite</c> field). Answered before anything is read or written.
    /// </summary>
    internal static IResult CiamNotConfiguredResult(HttpContext httpContext)
        => Results.Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "External sign-in is not configured",
            detail: "This environment has no external identity (CIAM) configuration, so no external user can be invited. "
                + "Nothing was created or sent.",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = GrantInviteStatus.CiamNotConfiguredReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });

    /// <summary>
    /// The 503 ProblemDetails for an invite whose contact lookup could not be read (both <c>/invite</c> and
    /// <c>/invite-and-grant</c>): the decision's reason code and its message. Retrying is safe — nothing was
    /// created, bound, flagged or granted.
    /// </summary>
    internal static IResult LookupFailureResult(InviteLookupFailure failure, HttpContext httpContext)
        => Results.Problem(
            statusCode: failure.StatusCode,
            title: failure.Title ?? "Contact lookup unavailable",
            detail: failure.Message,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = failure.ReasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });
}
