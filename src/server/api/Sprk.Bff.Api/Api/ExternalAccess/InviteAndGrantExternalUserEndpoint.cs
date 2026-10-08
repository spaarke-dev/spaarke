using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Errors;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
            // (task 141, sdap.access.invite.contact_lookup_failed). Every 409/422/503 above is decided BEFORE
            // onboarding, so it leaves no Contact, CIAM account or email behind.
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    /// <summary>Handles <c>POST /api/v1/external-access/invite-and-grant</c>. Internal so the auth tests can drive it directly.</summary>
    internal static async Task<IResult> InviteAndGrantAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        ExternalParticipationService participations,
        IAccessibleRecordSetService accessibleRecords,
        CallerRecordAccessProbe callerAccessProbe,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ContactIdentityBinder binder,
        IConfiguration configuration,
        Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer assignedAccess,
        HttpContext httpContext,
        ILogger<Program> logger,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

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
                "[EXT-INVITE-GRANT] Could not check whether {Email} already has a Contact. Nothing was onboarded or granted.",
                request.Email);
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
                    "[EXT-INVITE-GRANT] The invite decision for {Email} ended in an unexpected state ({Action}). Nothing " +
                    "was onboarded or granted.", request.Email, invitee.Action);
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
                dataverseClient, participations, accessibleRecords, logger, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] The pre-onboarding grant check failed for {Email} on {RootType} {RootId}. Nothing " +
                "was onboarded or granted.", request.Email, grantRoot.Type, grantRoot.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "The existing access on this record could not be read, so nothing was onboarded or granted. Try again.",
                extensions: new Dictionary<string, object?> { ["traceId"] = httpContext.TraceIdentifier });
        }

        if (preCheck.Refusal is { } preRefusal)
        {
            logger.LogWarning(
                "[EXT-INVITE-GRANT] Refused {Email} on {RootType} {RootId} before onboarding: {ReasonCode}.",
                request.Email, grantRoot.Type, grantRoot.Id, preRefusal.ReasonCode);
            return GrantExternalAccessEndpoint.PolicyRefusalProblem(preRefusal, httpContext);
        }

        var portalUrl = configuration["ExternalAccess:PortalUrl"]
            ?? throw new InvalidOperationException("ExternalAccess:PortalUrl is not configured.");

        logger.LogInformation(
            "[EXT-INVITE-GRANT] Core user onboarding+granting {Email} to {RootType} {RootId} at level {AccessLevel}",
            request.Email, grantRoot.Type, grantRoot.Id, request.AccessLevel);

        // ── Step 1: Onboard (idempotent CIAM provision) — reuse the invite core (task 025) ──
        Guid contactId;
        string onboardStatus;
        try
        {
            // The resolution the checks above judged — onboarding does not look the email up again.
            var outcome = await InviteExternalUserEndpoint.ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, binder, portalUrl, logger, invitee, ct);

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

            (contactId, onboardStatus) = (outcome.ContactId, outcome.Status);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[EXT-INVITE-GRANT] Onboarding failed for {Email}", request.Email);
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
                dataverseClient, participations, accessibleRecords, logger, ct, reAddRestoresLapsed: true);

            // Task 138: the core's own policy check refused (the record changed after the pre-check above) — or, since
            // task 139, its ceiling, never-lower or No Access check did. The Contact was onboarded; the refusal is
            // reported as itself, with the contact id, never as a 500.
            if (grantOutcome.Refusal is { } refusal)
            {
                logger.LogWarning(
                    "[EXT-INVITE-GRANT] Onboarded Contact {ContactId} but the grant was refused by the record's " +
                    "access policy: {ReasonCode}.", contactId, refusal.ReasonCode);
                return GrantExternalAccessEndpoint.PolicyRefusalProblem(
                    refusal, httpContext, new Dictionary<string, object?> { ["contactId"] = contactId });
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
                    extensions: new Dictionary<string, object?>
                    {
                        ["traceId"] = httpContext.TraceIdentifier,
                        ["reasonCode"] = "sdap.grant.expired_not_restored",
                        ["accessRecordId"] = accessRecordId,
                        ["contactId"] = contactId,
                    });
            }
        }
        catch (Exception ex)
        {
            // Onboarding succeeded (Contact {ContactId} provisioned) but the grant failed. Re-running the
            // action is safe — onboarding is idempotent and re-issues only the grant.
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] Onboarded Contact {ContactId} ({Email}) but the grant failed for {RootType} {RootId}",
                contactId, request.Email, grantRoot.Type, grantRoot.Id);
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "Internal Server Error",
                detail: "The external user was onboarded but granting project access failed. Re-run the action to retry the grant.",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["contactId"] = contactId
                });
        }

        logger.LogInformation(
            "[EXT-INVITE-GRANT] Onboarded ({Status}) + granted Contact {ContactId} to {RootType} {RootId} — access record {AccessRecordId}",
            onboardStatus, contactId, grantRoot.Type, grantRoot.Id, accessRecordId);

        // Task 142: a manual grant onto a contact the Assigned-To ledger holds becomes ADOPTED (as /grant). Never thrown.
        await assignedAccess.MarkGrantAdoptedAsync(
            grantRoot.Type, grantRoot.Id, contactId, null, accessRecordId, CancellationToken.None);

        return TypedResults.Ok(new InviteAndGrantResponse(
            contactId, onboardStatus, accessRecordId, portalUrl,
            GrantedAccessLevel: grantOutcome.GrantedLevel,
            Narrowed: grantOutcome.Narrowed));
    }
}
