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
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        return group;
    }

    private static async Task<IResult> InviteAndGrantAsync(
        InviteExternalUserRequest request,
        DataverseWebApiClient dataverseClient,
        CiamUserProvisioningService ciamProvisioner,
        RegistrationEmailService emailService,
        ITenantCache cache,
        IConfiguration configuration,
        HttpContext httpContext,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // ── Validation ───────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.Email))
            return ProblemDetailsHelper.ValidationError("Email is required.");

        if (request.ProjectId == Guid.Empty)
            return ProblemDetailsHelper.ValidationError("ProjectId is required and must be a valid GUID.");

        if (!Enum.IsDefined(typeof(ExternalAccessLevel), request.AccessLevel))
            return ProblemDetailsHelper.ValidationError(
                $"AccessLevel must be one of: {string.Join(", ", Enum.GetNames<ExternalAccessLevel>())}.");

        var portalUrl = configuration["ExternalAccess:PortalUrl"]
            ?? throw new InvalidOperationException("ExternalAccess:PortalUrl is not configured.");

        logger.LogInformation(
            "[EXT-INVITE-GRANT] Core user onboarding+granting {Email} to Project {ProjectId} at level {AccessLevel}",
            request.Email, request.ProjectId, request.AccessLevel);

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
        // The grantee is the resolved Contact (person). request.AccountId (optional) is record-keeping
        // only (sprk_accountid) — it is never the grantee.
        var grantRequest = new GrantAccessRequest(
            contactId,
            request.ProjectId,
            (ExternalAccessLevel)request.AccessLevel,
            request.ExpiryDate,
            request.AccountId);
        var callerSystemUserId = GrantExternalAccessEndpoint.ResolveCallerSystemUserId(httpContext);

        Guid accessRecordId;
        try
        {
            accessRecordId = await GrantExternalAccessEndpoint.CreateGrantAsync(
                grantRequest, callerSystemUserId, dataverseClient, cache, httpContext, logger, ct);
        }
        catch (Exception ex)
        {
            // Onboarding succeeded (Contact {ContactId} provisioned) but the grant failed. Re-running the
            // action is safe — onboarding is idempotent and re-issues only the grant.
            logger.LogError(ex,
                "[EXT-INVITE-GRANT] Onboarded Contact {ContactId} ({Email}) but the grant failed for Project {ProjectId}",
                contactId, request.Email, request.ProjectId);
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
            "[EXT-INVITE-GRANT] Onboarded ({Status}) + granted Contact {ContactId} to Project {ProjectId} — access record {AccessRecordId}",
            onboardStatus, contactId, request.ProjectId, accessRecordId);

        return TypedResults.Ok(new InviteAndGrantResponse(contactId, onboardStatus, accessRecordId, portalUrl));
    }
}
