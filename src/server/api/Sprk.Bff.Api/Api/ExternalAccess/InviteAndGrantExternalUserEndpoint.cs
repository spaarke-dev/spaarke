using Microsoft.AspNetCore.Mvc;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.Privileges;
using Sprk.Bff.Api.Services.Registration;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// POST /api/v1/external-access/invite-and-grant
///
/// The core-user "Invite to Secure Workspace" action (FR-11 / task 029): in ONE action, a Spaarke
/// core user onboards (idempotent CIAM provision — task 025) AND grants an attorney <b>Contact</b>
/// (person) access to a Project at an access level. Composes the existing invite + grant cores so
/// the two steps are atomic and the invariants are enforced server-side.
///
/// Grantee is the Contact — NEVER <c>sprk_assignedoutsidecounsel</c> (the firm/org lookup). The grant
/// is explicit + audited (<c>sprk_grantedby</c> from the caller) and is only fired by this action,
/// never by a field edit. Onboarding routes through the broker-only CIAM provisioner — no workforce
/// B2B guest is created (ADR-028 Amendment A1).
///
/// Internal management group (workforce default scheme). ADR-001 Minimal API; ADR-010 concrete DI.
/// </summary>
public static class InviteAndGrantExternalUserEndpoint
{
    /// <summary>Registers the invite-and-grant endpoint on the external-access management group.</summary>
    public static RouteGroupBuilder MapInviteAndGrantExternalUserEndpoint(this RouteGroupBuilder group)
    {
        group.MapPost("/invite-and-grant", InviteAndGrantAsync)
            .WithName("InviteAndGrantExternalUser")
            .WithSummary("Onboard + grant an attorney Contact to a Secure Project in one core-user action")
            .WithDescription(
                "Idempotently onboards the attorney (CIAM account via task 025) and grants the Contact " +
                "access to the Project at the requested level (audited via sprk_grantedby) in one action. " +
                "Grantee is the Contact (person), never the firm/org lookup.")
            .Produces<InviteAndGrantResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            // 403: the delegation gate, or (task 139) the caller's own rights allow granting nothing.
            .ProducesProblem(StatusCodes.Status403Forbidden)
            // 409: the request was capped at the caller's level and the EXISTING contact already holds more (task 139);
            // or the email's contact belongs to another sign-in, is ambiguous, or carries an unreadable binding —
            // refused and flagged (task 141).
            .ProducesProblem(StatusCodes.Status409Conflict)
            // 422: the record's access policy refused the grant (task 138), or the grantee is on the record's No
            // Access list (task 139).
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            // 503: the record's access settings could not be read (task 138), or the contact lookup could not be read
            // (task 141, sdap.access.invite.contact_lookup_failed), or (task 184) the pending-invite marker a contact
            // needed did not land (sdap.access.invite.invite_marker_not_written: no grant, no email — retry). Every
            // other 409/422/503 above is decided BEFORE
            // onboarding, so it leaves no Contact, CIAM account or email behind — with two exceptions decided AFTER it,
            // which carry the onboarded contactId: the grant core's own refusal when the record or grant changed while
            // the account was provisioned (the re-run checks, a race), and the expired_not_restored 409 backstop (owner
            // round 80 restores a lapsed grant, so no request is known to reach it). Onboarding is idempotent; a retry
            // re-issues only the grant.
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>
    /// Task 193: <see cref="InviteAndGrantResponse.OnboardStatus"/> when the email's contact represents an internal user and
    /// the record was shared with that user — nobody was onboarded.
    /// </summary>
    internal const string SharedWithInternalUserStatus = "SharedWithInternalUser";

    /// <summary>Handles <c>POST /api/v1/external-access/invite-and-grant</c>. Internal so the auth tests can drive it directly.</summary>
    internal static async Task<IResult> InviteAndGrantAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        IRecordOwnershipResolver ownership,
        IDataversePrivilegeChecker ownerPrivileges,
        CallerRecordAccessProbe callerAccessProbe,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ContactIdentityBinder binder,
        InviteOrganizationResolver organizations,
        IConfiguration configuration,
        Sprk.Bff.Api.Configuration.CustomerIdentity customer,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        GrantAccessNotifier accessNotifier,
        HttpContext httpContext,
        ILogger<Program> logger,
        TimeProvider timeProvider,
        CancellationToken ct,
        [FromServices] ContactInternalUserShare contactShare)
    {
        // ── Validation ───────────────────────────────────────────────────────
        // C5: the request's own shape is validated first, on every host, before any lookup: a malformed request is a 400
        // whether or not this host has a CIAM configuration.
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

        if (InviteExternalUserEndpoint.ValidateInvitedTenant(request.InvitedTenantId) is { } tenantProblem)
            return tenantProblem;

        if (InviteExternalUserEndpoint.ValidateNoInviteKindChoice(request.SendAsPortalAccount) is { } kindProblem)
            return kindProblem;

        // Task 185 slice F + §12 I-11: the CIAM provisioner constructs on a host without Ciam:* and validates at first use.
        // A portal-account invite needs CIAM, so there it answers a 503 ProblemDetails with the same reason code /grant
        // reports (ciam_not_configured), before anything else is read or written: no contact, no account, no grant. Slice
        // B3: a work-account invite needs no CIAM, so on such a host the organisation is decided first and only a
        // work-account invite goes on.
        InviteOrganizationDecision? organizationWithoutCiam = null;
        if (!ciamProvisioner.IsConfigured)
        {
            var (decided, withoutCiamProblem) = await InviteExternalUserEndpoint.DecideOrganizationWithoutCiamAsync(
                organizations, request, httpContext, ct);
            if (withoutCiamProblem is not null)
                return withoutCiamProblem;
            organizationWithoutCiam = decided;
        }

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        // Resolve the polymorphic grant root (project|matter|workassignment) or the legacy ProjectId
        // shorthand up-front, so a missing/unknown root is rejected 400 BEFORE any onboarding side effect.
        var grantRoot = GrantExternalAccessEndpoint.ResolveGrantRoot(new GrantAccessRequest(
            ContactId: Guid.Empty, // not relevant to root resolution — resolved separately below
            ProjectId: request.ProjectId,
            AccessLevel: (ExternalAccessLevel)request.AccessLevel,
            ExpiryDate: request.ExpiryDate,
            OrganizationId: request.OrganizationId,
            RecordType: request.RecordType,
            RecordId: request.RecordId));
        if (!grantRoot.Ok)
            return ProblemDetailsHelper.ValidationError(grantRoot.Error!);

        // FR-33 (task 097): reject a past expiry BEFORE any onboarding side effect — a rejected request must
        // not leave a Contact or a CIAM account behind. An absent expiry is defaulted by the grant core.
        var today = ExternalGrantLifecycle.TodayUtc(timeProvider);
        if (GrantExternalAccessEndpoint.ValidateRequestedExpiry(request.ExpiryDate, today, httpContext) is { } expiryProblem)
            return expiryProblem;

        // Slice B2: a work account (the email's organisation is a configured customer tenant) or a portal account, decided
        // before any contact is read, onboarded or granted. A malformed domain is a 400; a lookup with no definite answer
        // is a 503 with nothing written (retry).
        var (organization, organizationProblem) = organizationWithoutCiam is not null
            ? (organizationWithoutCiam, null)
            : await InviteExternalUserEndpoint.DecideOrganizationAsync(organizations, request, httpContext, ct);
        if (organizationProblem is not null)
            return organizationProblem;
        var workAccountTenantId = organization!.WorkAccountTenantId;

        // ── Task 193 (#2338) step 4: never CIAM-invite an internal user ──
        // An email that names ONE existing contact which represents an internal user takes /grant's decision: exactly one
        // represented user → shared at the requested level (no CIAM account, no email, no grant row); several → 409;
        // unreadable → 503. Decided by ContactUserRepresentation after the organisation decision (which reads no contact),
        // BEFORE the contact policy (a share is not a contact grant) and before anything is onboarded. The binder's link refusal below and the invite core's link guard stay
        // as safety nets, never the decider. No contact, or two (the binder refuses those), goes on unchanged.
        ContactLookup existing;
        try
        {
            existing = await binder.Store.FindActiveContactsByEmailAsync(request.Email, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] The contact lookup for the invite email failed on {RootType} {RootId} (trace {TraceId}). " +
                "Nothing was onboarded or granted.", grantRoot.Type, grantRoot.Id, httpContext.TraceIdentifier);
            existing = ContactLookup.Failed;
        }

        if (existing.Status != LookupStatus.Read)
        {
            // Not "no contact yet": an unread lookup may hide a colleague's contact, so nothing is onboarded.
            return InviteExternalUserEndpoint.LookupFailureResult(new InviteExternalUserEndpoint.InviteLookupFailure(
                ContactBindingDecision.InviteContactLookupFailed,
                ContactIdentityBinder.InviteMessage(ContactBindingDecision.InviteContactLookupFailed)!),
                httpContext);
        }

        var activeContacts = existing.Rows.Where(r => r.IsActive && r.ContactId != Guid.Empty).ToList();
        if (activeContacts.Count == 1)
        {
            var existingContactId = activeContacts[0].ContactId;
            var decision = await contactShare.DecideAsync(
                existingContactId, grantRoot.Type, grantRoot.Id, (ExternalAccessLevel)request.AccessLevel,
                dataverseClient, participations, callerAccessProbe, assignedAccess, accessNotifier, httpContext, logger, ct);

            if (decision.Result is { } answered)
                return answered;

            if (decision is { Shared: { } sharedWith, ShareResponse: { } share })
                return TypedResults.Ok(new InviteAndGrantResponse(
                    existingContactId,
                    SharedWithInternalUserStatus,
                    Guid.Empty,
                    PortalUrl: string.Empty,
                    GrantedAccessLevel: share.AccessLevel,
                    Narrowed: share.Narrowed,
                    NotificationFailed: share.NotificationFailed,
                    EmailSent: null,
                    EmailErrorCode: null,
                    SharedWithUser: sharedWith,
                    ExpiryNotApplied: request.ExpiryDate is not null ? true : null));
        }

        // Task 138: the record's access policy, BEFORE onboarding. The grantee is a named Contact, so this is
        // refused only on a Restricted record (or when the policy cannot be read). The grant core checks again
        // below — that second read is the core's own guarantee for every writer, and catches a record that
        // became Restricted while the account was being provisioned.
        var policy = await ExternalGrantLifecycle.EvaluateGrantPolicyAsync(
            participations, grantRoot.Type, grantRoot.Id, GrantGranteeKind.Contact, logger, ct);
        if (!policy.IsAllowed)
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(policy, httpContext);

        // ── Task 139: the grantor ceiling, BEFORE onboarding ──────────────────
        // The caller's own rights on the record, re-probed as them (not trusted from the delegation filter). A probe
        // that throws is a 500 with nothing onboarded; a probe that answers None becomes a ceiling of "none", which the
        // check below refuses with 403.
        GrantCeiling ceiling;
        try
        {
            ceiling = await GrantExternalAccessEndpoint.ProbeGrantorCeilingAsync(
                callerAccessProbe, httpContext, grantRoot.Type, grantRoot.Id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] Could not establish the caller's own rights on {RootType} {RootId}. Nothing was " +
                "onboarded or granted.", grantRoot.Type, grantRoot.Id);
            return GrantExternalAccessEndpoint.CallerRightsUnreadableProblem(httpContext);
        }

        // ── Tasks 139 + 141: who would receive the grant — resolved ONCE, BEFORE onboarding ──
        // The binder's invite resolution (task 141) — the ONLY email→contact answer: ACTIVE contacts, two rows, the
        // systemuser-reference check. The SAME resolution is handed to onboarding below, so the contact the grant checks
        // judge is, by construction, the contact onboarding provisions (no second lookup that could answer differently).
        // An existing contact is judged by the never-lower and No Access checks exactly as /grant judges it; a person
        // with no contact yet holds no grant, so only the ceiling and the No Access check on the request's firm apply.
        // Creating the contact first to get an id is forbidden: a refused request must leave no Contact and no CIAM
        // account behind. An unreadable lookup is its own answer (503 contact_lookup_failed) and an ambiguous email,
        // a contact another sign-in owns or an unreadable binding is a refusal (409, flagged by the binder) — neither
        // is ever read as "no contact yet" (which would judge a prospective person and then onboard one).
        InviteContactResolution invitee;
        try
        {
            invitee = await binder.ResolveInviteContactAsync(request.Email, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] The invite email's contact lookup failed on {RootType} {RootId} (trace {TraceId}). " +
                "Nothing was onboarded or granted.", grantRoot.Type, grantRoot.Id, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Could not check whether this person already exists, so nothing was onboarded or granted. Try again.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        if (InviteExternalUserEndpoint.NotProvisionable(invitee, request.Email, logger) is { } notProvisionable)
        {
            return notProvisionable.Refusal is { } inviteRefusal
                ? InviteExternalUserEndpoint.RefusalResult(inviteRefusal, httpContext)
                : InviteExternalUserEndpoint.LookupFailureResult(notProvisionable.Failure!, httpContext);
        }

        // Slice B2: a work-account invite to a contact that already carries a sign-in is refused before any grant.
        if (workAccountTenantId is not null
            && InviteExternalUserEndpoint.WorkAccountNotProvisionable(invitee, request.Email, logger) is { Refusal: { } bound })
        {
            return InviteExternalUserEndpoint.RefusalResult(bound, httpContext);
        }

        GrantGrantee preGrantee;
        switch (invitee.Action)
        {
            case InviteContactAction.AlreadyProvisioned or InviteContactAction.ProvisionExisting
                when invitee.ContactId is { } existingId && existingId != Guid.Empty:
                preGrantee = GrantGrantee.ForKey(
                    ExternalGrantKey.ForContact(grantRoot.Type, grantRoot.Id, existingId), request.OrganizationId);
                break;

            case InviteContactAction.CreateContact:
                preGrantee = GrantGrantee.ProspectiveContact(request.OrganizationId);
                break;

            default:
                // An existing-contact outcome without a contact id, or a state this code does not know: fail closed,
                // exactly as onboarding would — nothing is onboarded or granted.
                logger.LogError(
                    "[EXT-INVITE-GRANT] The invite decision on {RootType} {RootId} ended in an unexpected state ({Action}) " +
                    "(trace {TraceId}). Nothing was onboarded or granted.", grantRoot.Type, grantRoot.Id, invitee.Action,
                    httpContext.TraceIdentifier);
                return Results.Problem(
                    statusCode: StatusCodes.Status500InternalServerError,
                    title: "Internal Server Error",
                    detail: "Could not check whether this person already exists, so nothing was onboarded or granted. Try again.",
                    extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        var requestedLevel = (ExternalAccessLevel)request.AccessLevel;

        GrantExternalAccessEndpoint.GrantCheck preCheck;
        try
        {
            preCheck = await GrantExternalAccessEndpoint.CheckGrantAsync(
                preGrantee, requestedLevel, grantRoot.Type, grantRoot.Id, ceiling,
                dataverseClient, participations, accessibleRecords, binder.Store, logger, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] The pre-onboarding grant check failed on {RootType} {RootId} (trace {TraceId}). Nothing " +
                "was onboarded or granted.", grantRoot.Type, grantRoot.Id, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "The existing access on this record could not be read, so nothing was onboarded or granted. Try again.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        if (preCheck.Refusal is { } preRefusal)
        {
            logger.LogWarning(
                "[EXT-INVITE-GRANT] Refused the invitee on {RootType} {RootId} before onboarding: {ReasonCode} (trace {TraceId}).",
                grantRoot.Type, grantRoot.Id, preRefusal.ReasonCode, httpContext.TraceIdentifier);
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(preRefusal, httpContext);
        }

        // ── Task 099b review round 1 (F2): the new grant row's OWNER, BEFORE onboarding ──
        // With no existing row on the key the core will CREATE one, and a create needs its owner team (D-11). It is resolved
        // HERE, so a refusal — 503 owner_unreadable on a read fault, 500 owner_unresolved when no team resolves — leaves no
        // Contact, no CIAM account and no onboarding email behind (the rule above). The team is handed to the core, which
        // does not resolve it a second time; BuildGrantPayload stays the one place that writes ownerid. An existing row is
        // updated in place and never re-owned, so no owner is asked for then (if that row vanishes before the write, the core
        // resolves the owner itself).
        Guid? ownerTeamId = null;
        if (preCheck.ExistingRows.Count == 0)
        {
            var owner = await GrantExternalAccessEndpoint.ResolveGrantOwnerAsync(
                ownership, grantRoot.Type, grantRoot.Id, preGrantee.Key, logger, ct);
            if (owner.Refusal is { } ownerRefusal)
            {
                logger.LogWarning(
                    "[EXT-INVITE-GRANT] Refused the invitee on {RootType} {RootId} before onboarding: the new grant row's " +
                    "owner team could not be resolved ({ReasonCode}; trace {TraceId}). Nothing was onboarded or granted.",
                    grantRoot.Type, grantRoot.Id, ownerRefusal.ReasonCode, httpContext.TraceIdentifier);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(ownerRefusal, httpContext);
            }

            // Review round 2 (F1): the resolved team must also be ALLOWED to own the row. Dataverse refuses a create owned by a
            // team whose roles lack Read on the table (the Secure Record Owner role until it holds the privilege), and that
            // refusal would come only after the Contact, the CIAM account and the email. So it is asked here, read-only,
            // through the ONE owner-privilege helper: 500 owner_privilege_missing, or 503 owner_unreadable when it cannot be
            // read (fail closed) — before any side effect.
            var privilegeRefusal = await GrantExternalAccessEndpoint.CheckGrantOwnerPrivilegeAsync(
                ownerPrivileges, owner.TeamId!.Value, grantRoot.Type, grantRoot.Id, preGrantee.Key, fresh: false, logger, ct);
            if (privilegeRefusal is not null)
            {
                logger.LogWarning(
                    "[EXT-INVITE-GRANT] Refused the invitee on {RootType} {RootId} before onboarding: the new grant row's " +
                    "owner team may not own it ({ReasonCode}; trace {TraceId}). Nothing was onboarded or granted.",
                    grantRoot.Type, grantRoot.Id, privilegeRefusal.ReasonCode, httpContext.TraceIdentifier);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(privilegeRefusal, httpContext);
            }

            ownerTeamId = owner.TeamId;
        }

        var portalUrl = configuration["ExternalAccess:PortalUrl"]
            ?? throw new InvalidOperationException("ExternalAccess:PortalUrl is not configured.");

        logger.LogInformation(
            "[EXT-INVITE-GRANT] Core user onboarding+granting the invitee to {RootType} {RootId} at level {AccessLevel} " +
            "(trace {TraceId})", grantRoot.Type, grantRoot.Id, request.AccessLevel, httpContext.TraceIdentifier);

        // ── Step 1: Onboard (idempotent CIAM provision) — reuse the invite core (task 025) ──
        Guid contactId;
        string onboardStatus;
        string? writtenInviteStamp;
        bool? emailSent;
        string? emailErrorCode;
        try
        {
            // The resolution the checks above judged — onboarding does not look the email up again.
            var outcome = await ExternalInviteService.ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl,
                InviteExternalUserEndpoint.InviteLinkCustomerKey(customer), logger, invitee, ct, workAccountTenantId);

            // Task 141: a refused onboarding writes NO grant. Granting a contact an employee's work identity
            // owns would hand the employee's contact a CIAM grant it can never use — or worse, one it can.
            // (The pre-onboarding check above already answered a refusal or a lookup failure from this same
            // resolution; these two stay as the shared core's contract, not as a second decision.)
            if (outcome.Refusal is { } refusal)
            {
                return InviteExternalUserEndpoint.RefusalResult(refusal, httpContext);
            }

            // Task 141 (verifier finding 5): an unreadable contact lookup is its own answer (503 + reason code),
            // and — like a refusal — writes no grant.
            if (outcome.Failure is { } failure)
            {
                return InviteExternalUserEndpoint.LookupFailureResult(failure, httpContext);
            }

            (contactId, onboardStatus, writtenInviteStamp) = (outcome.ContactId, outcome.Status, outcome.WrittenInviteStamp);

            // Adversarial pass 2, F2: an invite email that did not send is reported (the grant still proceeds), the same
            // way on the new-account and the existing-account branch (§12 I-10).
            (emailSent, emailErrorCode) = InviteExternalUserEndpoint.InviterVisibleEmail(outcome);

            // Slice C2c: a contact that already has an account is due no invite email (the grant email below is its notice,
            // reported as notificationFailed), so emailSent is false, never null, with its own code, as on /invite.
            if (string.Equals(outcome.Status, InviteExternalUserEndpoint.AlreadyProvisionedStatus, StringComparison.Ordinal))
            {
                (emailSent, emailErrorCode) = (false, ContactBindingDecision.InviteContactAlreadyHasAccount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INVITE-GRANT] Onboarding failed on {RootType} {RootId} (trace {TraceId})",
                grantRoot.Type, grantRoot.Id, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "Failed to onboard the external user.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        // ── Step 2: Grant (audited; grantee = the Contact, NOT the firm) — reuse the grant core (task 026) ──
        // The grantee is the resolved Contact (person). request.OrganizationId (optional) is the grantee's
        // firm/org (sprk_organization) for firm-level scoping — it is never the grantee. The grant is bound
        // to the polymorphic root resolved above (project|matter|workassignment).
        var grantRequest = new GrantAccessRequest(
            contactId,
            request.ProjectId,
            (ExternalAccessLevel)request.AccessLevel,
            request.ExpiryDate,
            request.OrganizationId,
            request.RecordType,
            request.RecordId);
        var callerSystemUserId = GrantExternalAccessEndpoint.ResolveCallerSystemUserId(httpContext);

        Guid accessRecordId;
        GrantExternalAccessEndpoint.GrantUpsertOutcome grantOutcome;
        try
        {
            // The SAME ceiling the pre-check used — the core re-runs every check itself (WP-1), so a record or a grant
            // that changed while the account was being provisioned is still judged at write time.
            // Owner round 80: the modal's "+ Contact" re-add sends no date, so a re-add over a LAPSED grant is a SET —
            // restored at the picked (ceiling-capped) level with today + 90, never answered 200 over a write that did
            // not happen.
            grantOutcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
                grantRequest, grantRoot.Type, grantRoot.Id, today, ceiling, callerSystemUserId,
                dataverseClient, participations, accessibleRecords, binder.Store, ownership, ownerPrivileges, logger, ct,
                reAddRestoresLapsed: true, resolvedOwnerTeamId: ownerTeamId);

            // Task 138: the core's own policy check refused (the record changed after the pre-check above) — or, since
            // task 139, its ceiling, never-lower or No Access check did. The Contact was onboarded; the refusal is
            // reported as itself, with the contact id, never as a 500.
            if (grantOutcome.Refusal is { } refusal)
            {
                logger.LogWarning(
                    "[EXT-INVITE-GRANT] Onboarded Contact {ContactId} but the grant core refused the grant: {ReasonCode}.",
                    contactId, refusal.ReasonCode);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(
                    refusal, httpContext,
                    await WithUnsentInviteRetractedAsync(
                        new Dictionary<string, object?> { ["contactId"] = contactId }, binder, contactId, writtenInviteStamp,
                        logger));
            }

            accessRecordId = grantOutcome.AccessRecordId;

            // Task 023 / owner round 80: a grant that confers no access is never answered 200 "granted" — that told the
            // modal a level was written when nothing was, and adopted an Assigned-To ledger entry for it. With the round-80
            // restore a dateless re-add over a lapsed grant IS written (today + 90), and a past date is a 400 above, so no
            // request is known to reach this; if one does, the Contact stays onboarded (idempotent) and the caller gets the
            // same 409 as /grant, with the contact id — and nothing is adopted.
            if (grantOutcome.Warning is { } warning)
            {
                logger.LogWarning(
                    "[INVITE-GRANT] Contact {ContactId} was onboarded, but the grant on row {AccessRecordId} confers no " +
                    "access: {Warning}", grantRequest.ContactId, accessRecordId, warning);
                return Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Grant did not take effect",
                    detail: warning,
                    extensions: await WithUnsentInviteRetractedAsync(
                        new Dictionary<string, object?>
                        {
                            ["traceId"] = httpContext.TraceIdentifier,
                            ["reasonCode"] = "sdap.grant.expired_not_restored",
                            ["accessRecordId"] = accessRecordId,
                            ["contactId"] = contactId,
                        }, binder, contactId, writtenInviteStamp, logger));
            }
        }
        catch (Exception ex)
        {
            // Onboarding succeeded (Contact {ContactId} provisioned) but the grant failed. Re-running the
            // action is safe — onboarding is idempotent and re-issues only the grant.
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] Onboarded Contact {ContactId} but the grant failed for {RootType} {RootId} (trace {TraceId})",
                contactId, grantRoot.Type, grantRoot.Id, httpContext.TraceIdentifier);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "The external user was onboarded but granting project access failed. Re-run the action to retry the grant.",
                extensions: await WithUnsentInviteRetractedAsync(
                    new Dictionary<string, object?>
                    {
                        ["traceId"] = httpContext.TraceIdentifier,
                        ["contactId"] = contactId
                    }, binder, contactId, writtenInviteStamp, logger));
        }

        logger.LogInformation(
            "[EXT-INVITE-GRANT] Onboarded ({Status}) + granted Contact {ContactId} to {RootType} {RootId} — access record {AccessRecordId}",
            onboardStatus, contactId, grantRoot.Type, grantRoot.Id, accessRecordId);

        // Task 142: a manual grant onto a contact the Assigned-To ledger holds becomes ADOPTED (as /grant). Never thrown.
        await assignedAccess.MarkGrantAdoptedAsync(
            grantRoot.Type, grantRoot.Id, contactId, null, accessRecordId, CancellationToken.None);

        // Task 036 (FR-07), review F1: a contact that was ALREADY onboarded gets no CIAM onboarding email on this route, so
        // the grant email is its only notice — sent when the grant GAVE it access (never a same-level re-grant, an
        // expiry-only change or a lower level). A contact onboarded by THIS request is emailed by the CIAM onboarding path
        // already: exactly one email per invite, so no grant email for it. The grantee here is always a Contact (an
        // organization grant is /grant's). Best effort, after the write: a failure is reported, never undoes the grant.
        var notificationFailed = false;
        if (string.Equals(onboardStatus, InviteExternalUserEndpoint.AlreadyProvisionedStatus, StringComparison.Ordinal)
            && grantOutcome.AccessGained)
        {
            notificationFailed = await accessNotifier.NotifyContactGrantAsync(
                grantRoot.Type, grantRoot.Id, contactId, grantOutcome.GrantedLevel, httpContext.User)
                == GrantNotificationOutcome.Failed;
        }

        // Slice B3: a work-account stamp landed (no CIAM email was sent), and the grant is written, so the notice may name
        // the record. Sent only here, after the grant: a refused or failed grant returned above, with no notice.
        if (string.Equals(onboardStatus, InviteExternalUserEndpoint.WorkAccountInvitedStatus, StringComparison.Ordinal))
        {
            var record = new ExternalInviteService.WorkAccountNoticeRecord(
                ExternalGrantRoot.LabelFor(grantRoot.Type),
                await GrantAccessNotifier.ReadRecordNameAsync(dataverseClient, logger, grantRoot.Type, grantRoot.Id));
            (onboardStatus, emailSent, emailErrorCode) = await ExternalInviteService.SendWorkAccountNoticeAsync(
                    emailService, request.Email, portalUrl, InviteExternalUserEndpoint.InviteLinkCustomerKey(customer), record,
                    contactId, logger, binder, writtenInviteStamp)
                ? (onboardStatus, true, (string?)null)
                // C5: the notice did not send, so the stamp was retracted: the invite is answered as not sent, never as made.
                : (InviteExternalUserEndpoint.InviteNotSentStatus, false, GrantInviteStatus.EmailNotSentReasonCode);
        }

        // Task 185 §12 I-10: the inviter is never told that the email already had a CIAM account (the log above keeps it).
        return TypedResults.Ok(new InviteAndGrantResponse(
            contactId, InviteExternalUserEndpoint.InviterVisibleStatus(onboardStatus), accessRecordId, portalUrl,
            GrantedAccessLevel: grantOutcome.GrantedLevel,
            Narrowed: grantOutcome.Narrowed,
            NotificationFailed: notificationFailed,
            EmailSent: emailSent,
            EmailErrorCode: emailErrorCode));
    }

    /// <summary>The problem extension that reports what became of the invite a refused or failed grant left unsent.</summary>
    internal const string PendingInviteExtension = "pendingInvite";

    /// <summary>
    /// Slice C2c: a pending invite exists only if its email went out. The work-account notice is sent only after the grant
    /// is written, so when the grant is refused, confers nothing or fails AFTER this request wrote the stamp, the invite this
    /// request wrote is retracted through <see cref="ExternalInviteService.RetractUnsentInviteAsync"/> (its own row version,
    /// cleared only while the contact still carries this stamp: a newer invite is never cleared), and the next invite writes
    /// and sends a new one. The outcome is reported in <see cref="PendingInviteExtension"/>: <c>retracted</c>,
    /// <c>superseded</c> (a newer invite or a bind replaced it) or <c>retract_failed</c> (logged at Error; it may still be
    /// pending). No stamp written (a portal-account invite, whose email was sent before the grant) adds nothing.
    /// </summary>
    private static async Task<Dictionary<string, object?>> WithUnsentInviteRetractedAsync(
        Dictionary<string, object?> extensions, ContactIdentityBinder binder, Guid contactId, string? writtenInviteStamp,
        ILogger logger)
    {
        var retract = await ExternalInviteService.RetractUnsentInviteAsync(binder, contactId, writtenInviteStamp, logger)
            .ConfigureAwait(false);
        if (retract is { } outcome)
        {
            extensions[PendingInviteExtension] = outcome switch
            {
                InviteRetractOutcome.Retracted => "retracted",
                InviteRetractOutcome.Superseded => "superseded",
                _ => "retract_failed",
            };
        }

        return extensions;
    }
}
