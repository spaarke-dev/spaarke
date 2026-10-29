// spaarke-SPA-external-access-platform-r3 task 047 (2026-10-15) — ADR-028 A9 registration.
//
// POST /api/v1/external/registration registers a workforce contact who signed in with their CUSTOMER home-directory token
// (the WorkforceHome scheme) and whom the 046 resolution did not resolve (403 registration_required). Design:
// notes/a9-bff-design.md §4 ("Registering to an existing contact") and §6; owner decisions §11 items 1–2 and 2026-10-15.
//
// Its OWN route group: the ExternalCollaboration policy (endpoint-level authorization, ADR-008) WITHOUT the group-level
// CallerPrincipalAuthorizationFilter, which would refuse this caller (registration_required) before the handler ran. The
// handler admits ONLY a WorkforceHome identity: a Spaarke-tenant workforce token or a CIAM token is 403; anonymous is 401.
//
// Order (binding, design §6):
//   0. The caller is a WorkforceHome identity; the body is a closed, valid DTO (400 otherwise).
//   1. The 046 resolution (IWorkforcePrincipalResolver → stored link, else WorkforceHome contact). Resolved → 200, no write.
//      Anything but registration_required is that resolution's own answer (401/403).
//   2. The token's `email` claim (never preferred_username) → else 403 registration_email_missing.
//   3. ContactIdentityBinder.RegisterHomeContactAsync: probe readability, licensed-user refusal, the email's contacts (bind
//      only on confirmExistingRecord, else 409; ambiguity → 403 + flag), else create one contact owned by the default team
//      of ExternalAccess:CustomerBusinessUnitId (absent → 500, nothing created). App-only Dataverse; no OBO; no email sent.
// Logs carry the contact id and hashed tid/oid only — never the email or the names.

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The A9 registration route: <c>POST /api/v1/external/registration</c>. See the file header.</summary>
public static class ExternalRegistrationEndpoints
{
    /// <summary>The token email is an enabled systemuser's: the SPA runs the interactive link instead (design §5).</summary>
    public const string ReasonLicensedUserLinkRequired = "sdap.access.licensed_user_link_required";

    /// <summary>The email's contacts cannot be bound (several, bound, linked, partner): an administrator must resolve it.</summary>
    public const string ReasonContactConflict = "sdap.access.registration_contact_conflict";

    /// <summary>One bindable contact carries the email: the form asks the person to confirm it is theirs (409).</summary>
    public const string ReasonExistingRecordFound = "sdap.access.existing_record_found";

    /// <summary>The token carries no usable <c>email</c> claim.</summary>
    public const string ReasonEmailMissing = "sdap.access.registration_email_missing";

    /// <summary><c>ExternalAccess:CustomerBusinessUnitId</c> is absent (or has no single default team): nothing created.</summary>
    public const string ReasonOwnerUnconfigured = ContactBindingDecision.DenyOwnerUnconfigured;

    /// <summary>The caller is not a WorkforceHome identity (a Spaarke-tenant workforce token or a CIAM token).</summary>
    public const string ReasonHomeIdentityRequired = "sdap.access.registration_home_identity_required";

    /// <summary>The body failed validation (a missing, over-long or control-character value).</summary>
    public const string ErrorInvalidBody = "sdap.access.registration_invalid";

    /// <summary>Dataverse could not be read or written right now: nothing was registered; try again.</summary>
    public const string ErrorUnavailable = "sdap.access.registration_unavailable";

    /// <summary>The environment cannot register safely (a masked or missing binding column, or no key): an operator fix.</summary>
    public const string ErrorEnvironment = "sdap.access.registration_environment";

    /// <summary>The largest first or last name (design §6).</summary>
    public const int MaxNameLength = 50;

    /// <summary>The largest department (design §6).</summary>
    public const int MaxDepartmentLength = 100;

    /// <summary>The whole-body cap: the form is four short fields.</summary>
    public const int MaxRequestBodyBytes = 4 * 1024;

    /// <summary>Maps the registration group. Called from <see cref="ExternalAccessEndpoints.MapExternalAccessEndpoints"/>.</summary>
    public static RouteGroupBuilder MapExternalRegistrationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/external")
            .WithTags("External Access")
            .RequireAuthorization(AuthPolicies.ExternalCollaboration);

        group.MapPost("/registration", RegisterAsync)
            .WithName("RegisterExternalWorkforceContact")
            .ExemptFromWorkforceCeiling("Bootstrap: creates or (on confirmation) binds the caller's own contact before any principal exists (R3 registration); an already-resolved caller gets 200 with no write.")
            .WithSummary("Register a workforce contact signed in with their home-directory account")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithDescription(
                "ADR-028 A9. WorkforceHome tokens only. An already-resolved caller gets 200 and nothing is written. Otherwise, " +
                "with the email from the token: 403 licensed_user_link_required for a licensed user's email; 409 " +
                "existing_record_found when one existing contact carries it (resubmit with confirmExistingRecord: true to " +
                "bind it); 403 registration_contact_conflict when the email's contacts cannot be bound; else 201 with the new " +
                "contact. 403 registration_email_missing when the token has no email; 500 registration_owner_unconfigured " +
                "when the stamp has no customer business unit (nothing created).")
            .RequireRateLimiting("dataverse-query")
            .Accepts<ExternalRegistrationRequest>("application/json")
            .Produces<ExternalRegistrationResponse>(StatusCodes.Status201Created)
            .Produces<ExternalRegistrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> RegisterAsync(
        ExternalRegistrationRequest? request,
        HttpContext httpContext,
        IWorkforcePrincipalResolver resolver,
        ContactIdentityBinder binder,
        IOptionsMonitor<ExternalAccessOptions> options,
        IConfiguration configuration,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // 0. WorkforceHome only (ADR-028 A9). The policy also admits the Spaarke-tenant and CIAM schemes; they never register.
        if (WorkforcePrincipalResolver.WorkforceHomeIdentity(httpContext.User) is not { } homeIdentity)
        {
            logger.LogWarning("[A9-REG] Registration refused: the caller is not a WorkforceHome identity ({Reason})",
                ReasonHomeIdentityRequired);
            return Problem(StatusCodes.Status403Forbidden, ReasonHomeIdentityRequired,
                "Registration is only available when you sign in with your organization's account.");
        }

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var home = new ClaimsPrincipal(homeIdentity);

        // 1. The 046 resolution — the same reads and decisions every home request runs. Resolved → 200, no write.
        WorkforcePrincipalResolution resolution;
        try
        {
            resolution = await resolver.ResolveAsync(home, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[A9-REG] The home resolution threw — registration refused (retryable)");
            return Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was registered; try again.");
        }

        if (resolution.IsResolved)
        {
            var principal = resolution.Principal!;
            return Results.Ok(new ExternalRegistrationResponse(principal.ContactId, principal.SystemUserId, "resolved"));
        }

        if (resolution.DenyReason == WorkforceDenyReason.MissingIdentityClaims)
        {
            return Problem(StatusCodes.Status401Unauthorized, resolution.DenyCode, "The token is missing identity claims.");
        }

        // A registered home contact whose external access an administrator removed: the person is answered as not invited,
        // the same answer as their ordinary refusal. The resolver's log keeps the specific code.
        if (resolution.DenyCode == ContactBindingDecision.DenyContactRemoved)
        {
            return NotInvited();
        }

        if (resolution.DenyCode != WorkforcePrincipalResolver.DenyRegistrationRequired)
        {
            return Problem(StatusCodes.Status403Forbidden, resolution.DenyCode, "Your account cannot be registered.");
        }

        // The resolver accepted these claims (registration_required is only answered after both parsed).
        var oid = MembershipEndpoints.ExtractAadObjectId(home);
        var rawTid = home.FindFirst("tid")?.Value
                     ?? home.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (oid is not { } homeOid || !Guid.TryParse(rawTid?.Trim(), out var homeTenant) || homeTenant == Guid.Empty)
        {
            return Problem(StatusCodes.Status401Unauthorized, WorkforcePrincipalResolver.DenyMissingIdentityClaims,
                "The token is missing identity claims.");
        }

        // 2. The email comes from the token's email claim ONLY (never preferred_username or upn; never the body).
        if (TokenEmail(home) is not { } email)
        {
            logger.LogWarning("[A9-REG] Registration refused: the home token carries no usable email claim ({Reason})",
                ReasonEmailMissing);
            return Problem(StatusCodes.Status403Forbidden, ReasonEmailMissing,
                "Your sign-in did not include an email address, so you cannot be registered. Contact your administrator.");
        }

        // 3. The ordered registration (probe, licensed user, existing contact, create).
        HomeRegistrationResult result;
        try
        {
            result = await binder.RegisterHomeContactAsync(
                new HomeRegistrationRequest(
                    homeTenant, homeOid, email,
                    request!.FirstName!.Trim(), request.LastName!.Trim(),
                    string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim(),
                    request.ConfirmExistingRecord == true,
                    options.CurrentValue.CustomerBusinessUnitId,
                    SecureRecordOwnerTeam.BusinessUnitName(configuration),
                    WorkforceCallerClaims.EmailDomainOwnerVerifiedOf(home)),
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[A9-REG] Registration threw — refused (retryable)");
            return Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was registered; try again.");
        }

        return ToResult(result);
    }

    /// <summary>The binder's outcome as the route's answer. Pure.</summary>
    internal static IResult ToResult(HomeRegistrationResult result) => result.Outcome switch
    {
        HomeRegistrationOutcome.Created =>
            Results.Created("/api/v1/external/me", new ExternalRegistrationResponse(result.ContactId, null, "created")),
        HomeRegistrationOutcome.BoundExisting =>
            Results.Ok(new ExternalRegistrationResponse(result.ContactId, null, "bound")),
        HomeRegistrationOutcome.AlreadyRegistered =>
            Results.Ok(new ExternalRegistrationResponse(result.ContactId, null, "already_registered")),
        HomeRegistrationOutcome.LicensedUser =>
            Problem(StatusCodes.Status403Forbidden, ReasonLicensedUserLinkRequired,
                "This email belongs to a licensed user. Link your account instead of registering."),
        HomeRegistrationOutcome.ExistingRecordFound =>
            Problem(StatusCodes.Status409Conflict, ReasonExistingRecordFound,
                "An existing record carries your email. Confirm it is yours to continue."),
        HomeRegistrationOutcome.Conflict =>
            Problem(StatusCodes.Status403Forbidden, ReasonContactConflict,
                "Your record cannot be registered automatically. An administrator must resolve it."),
        HomeRegistrationOutcome.OwnerUnconfigured =>
            Problem(StatusCodes.Status500InternalServerError, ReasonOwnerUnconfigured,
                "Registration is not configured for this organization. Nothing was created; contact your administrator."),
        _ => DeniedResult(result),
    };

    private static IResult DeniedResult(HomeRegistrationResult result) => result.DenyCode switch
    {
        // No current invite names this sign-in: a new invitation (naming their organization's tenant) is the fix, not a
        // retry. Nothing was bound or created. The answer says nothing about what records exist.
        ContactBindingDecision.DenyContactNotInvited => NotInvited(),

        // C5: the email belongs to a person whose external access an administrator removed. Final, not a retry: only an
        // administrator reinstates the person. The person is answered exactly as the ordinary not-invited refusal (status,
        // code, title, detail); the binder's log keeps the specific code for operators. A removed marker whose empty read is
        // not proven is not a removal: it falls to the retryable 503 below.
        ContactBindingDecision.DenyContactRemoved when !result.RemovedMarkerUnproven => NotInvited(),

        // A squatted uniqueness mirror: an administrator must resolve the record (the holder is flagged).
        ContactBindingDecision.DenyContactKeyConflict =>
            Problem(StatusCodes.Status403Forbidden, ReasonContactConflict,
                "Your record cannot be registered automatically. An administrator must resolve it."),

        // The environment cannot register safely: an operator fix, not a retry.
        ContactBindingDecision.DenyBindingColumnMasked
            or ContactBindingDecision.DenyBindingColumnMissing
            or ContactBindingDecision.DenyContactCreateUnavailable =>
            Problem(StatusCodes.Status500InternalServerError, ErrorEnvironment,
                "Registration is unavailable in this environment. Nothing was registered; contact your administrator."),

        // A read or write that did not land: retryable, nothing registered.
        _ => Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was registered; try again."),
    };

    /// <summary>
    /// The registration's ordinary refusal when no current invite names this sign-in: 403
    /// <see cref="ContactBindingDecision.DenyContactNotInvited"/>. A person whose external access was removed gets this same answer.
    /// </summary>
    private static IResult NotInvited()
        => Problem(StatusCodes.Status403Forbidden, ContactBindingDecision.DenyContactNotInvited,
            "This sign-in cannot be registered until you are invited. Ask the person who invited you to send a new "
            + "invitation.");

    /// <summary>
    /// The token's <c>email</c> claim (or its <see cref="ClaimTypes.Email"/> mapping), trimmed — exactly one distinct
    /// non-empty value, else null. Never <c>preferred_username</c> or <c>upn</c> (a UPN is not a verified mailbox).
    /// </summary>
    internal static string? TokenEmail(ClaimsPrincipal home)
    {
        var values = home.Claims
            .Where(c => c.Type is "email" or ClaimTypes.Email)
            .Select(c => c.Value?.Trim())
            .Where(v => !string.IsNullOrEmpty(v) && !v.Any(char.IsControl))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return values.Count == 1 ? values[0] : null;
    }

    /// <summary>Validates the closed DTO (design §6). Pure; null when valid.</summary>
    internal static IResult? Validate(ExternalRegistrationRequest? request)
    {
        if (request is null)
        {
            return Invalid("A body with firstName and lastName is required.");
        }

        return InvalidText(request.FirstName, "firstName", required: true, MaxNameLength)
               ?? InvalidText(request.LastName, "lastName", required: true, MaxNameLength)
               ?? InvalidText(request.Department, "department", required: false, MaxDepartmentLength);
    }

    private static IResult? InvalidText(string? value, string field, bool required, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return required ? Invalid($"{field} is required.") : null;
        }

        if (trimmed.Length > max)
        {
            return Invalid($"{field} must be at most {max} characters.");
        }

        return trimmed.Any(char.IsControl) ? Invalid($"{field} must not contain control characters.") : null;
    }

    private static IResult Invalid(string detail)
        => Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Bad Request", detail: detail,
            extensions: new Dictionary<string, object?> { ["errorCode"] = ErrorInvalidBody });

    private static IResult Problem(int status, string? reasonCode, string detail)
        => Results.Problem(
            statusCode: status,
            title: status switch
            {
                StatusCodes.Status401Unauthorized => "Unauthorized",
                StatusCodes.Status403Forbidden => "Forbidden",
                StatusCodes.Status409Conflict => "Conflict",
                StatusCodes.Status500InternalServerError => "Internal Server Error",
                _ => "Service Unavailable",
            },
            detail: detail,
            extensions: new Dictionary<string, object?> { ["reasonCode"] = reasonCode, ["errorCode"] = reasonCode });
}
