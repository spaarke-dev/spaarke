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
            // 409 (task 139): the request was capped at the caller's level and the EXISTING contact already holds more.
            .ProducesProblem(StatusCodes.Status409Conflict)
            // 422/503: the record's access policy refused the grant, or could not be read (task 138); or the grantee
            // is on the record's No Access list (task 139). All checked BEFORE onboarding, so a refusal leaves no
            // Contact, CIAM account or email behind.
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
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
        ITenantCache cache,
        IConfiguration configuration,
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

        // ── Task 139: who would receive the grant — READ-ONLY, BEFORE onboarding ──
        // The same email match onboarding uses, without creating anything. An existing contact is judged by the
        // never-lower and No Access checks exactly as /grant judges it; a person with no contact yet holds no grant,
        // so only the ceiling and the No Access check on the request's firm apply. Creating the contact first to get
        // an id is forbidden: a refused request must leave no Contact and no CIAM account behind.
        (Guid ContactId, string? ExistingOid)? existingContact;
        try
        {
            existingContact = await InviteExternalUserEndpoint.FindContactByEmailAsync(dataverseClient, request.Email, ct);
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

        var requestedLevel = (ExternalAccessLevel)request.AccessLevel;
        var preGrantee = existingContact is { ContactId: var foundId } && foundId != Guid.Empty
            ? GrantGrantee.ForKey(
                ExternalGrantKey.ForContact(grantRoot.Type, grantRoot.Id, foundId), request.OrganizationId)
            : GrantGrantee.ProspectiveContact(request.OrganizationId);

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
            (contactId, onboardStatus) = await InviteExternalUserEndpoint.ProvisionAsync(
                request, dataverseClient, ciamProvisioner, emailService, portalUrl, logger, ct);
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
            grantOutcome = await GrantExternalAccessEndpoint.CreateGrantAsync(
                grantRequest, grantRoot.Type, grantRoot.Id, today, ceiling, callerSystemUserId,
                dataverseClient, participations, accessibleRecords, cache, httpContext, logger, ct);

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

            // Task 023: the upsert can succeed structurally while conferring no access — it matched an
            // EXPIRED row and this request carried no new expiry. On the invite path that is logged
            // rather than failed: the Contact WAS provisioned, so failing here would strand a real
            // onboarding over a grant the operator can fix by re-granting with an expiry date.
            if (grantOutcome.Warning is { } warning)
            {
                logger.LogWarning(
                    "[INVITE-GRANT] Contact {ContactId} was onboarded and the grant row {AccessRecordId} " +
                    "exists, but it confers no access: {Warning}",
                    grantRequest.ContactId, accessRecordId, warning);
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

        return TypedResults.Ok(new InviteAndGrantResponse(
            contactId, onboardStatus, accessRecordId, portalUrl,
            GrantedAccessLevel: grantOutcome.GrantedLevel,
            Narrowed: grantOutcome.Narrowed));
    }
}
