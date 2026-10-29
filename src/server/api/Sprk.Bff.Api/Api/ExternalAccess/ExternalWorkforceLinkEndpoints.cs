// spaarke-SPA-external-access-platform-r3 task 048 (2026-10-16) — ADR-028 A9 stored link for licensed users.
//
// POST /api/v1/external/workforce/link writes the stored home link (systemuser.sprk_hometenantid / sprk_homeobjectid and
// their key mirrors) for a LICENSED user who signed in with their company (home-directory) account, once ONE request
// proves both of their identities (design notes/a9-bff-design.md §5; owner decision §11 item 3):
//   - the BEARER is the home token, authenticated by the WorkforceHome scheme only (a Spaarke-tenant or CIAM principal is
//     403; anonymous is 401);
//   - the BODY carries the same person's Spaarke-tenant GUEST access token for the same BFF app, validated by
//     SpaarkeGuestTokenValidator with the default scheme's own parameters plus the A9 conditions (tid, acct, idp home
//     tenant = the bearer's tid, the home-UPN consistency check).
// Then ContactIdentityBinder.LinkHomeSystemUserAsync writes the link onto the guest's ENABLED systemuser, once: the same
// link again is 200 with no write; a different link, or the pair on another systemuser, is 403 link_collision and is never
// re-pointed. Every refusal writes nothing.
//
// Its OWN route group: the ExternalCollaboration policy WITHOUT the group-level CallerPrincipalAuthorizationFilter (which
// would refuse this caller — registration_required — before the handler ran), exactly as the 047 registration group.
// The guest token is never logged, stored or echoed; logs carry the systemuser id and hashed tid/oid only. No OBO with
// either token. App-only Dataverse.

using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Api.Membership;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>The A9 link route: <c>POST /api/v1/external/workforce/link</c>. See the file header.</summary>
public static class ExternalWorkforceLinkEndpoints
{
    /// <summary>The route.</summary>
    public const string Route = "/api/v1/external/workforce/link";

    /// <summary>The bearer is not a WorkforceHome identity (a Spaarke-tenant workforce token or a CIAM token).</summary>
    public const string ReasonHomeIdentityRequired = "sdap.access.link.home_identity_required";

    /// <summary>No systemuser carries the guest token's object id.</summary>
    public const string ReasonNoSystemUser = "sdap.access.link.no_systemuser";

    /// <summary>The guest token's systemuser is disabled.</summary>
    public const string ReasonSystemUserDisabled = "sdap.access.link.systemuser_disabled";

    /// <summary>The systemuser carries a different link, or another systemuser holds this one: nothing is re-pointed.</summary>
    public const string ReasonLinkCollision = "sdap.access.link.link_collision";

    /// <summary>
    /// Two enabled systemusers carry the guest token's object id: a directory fault an administrator must resolve. Not
    /// retryable.
    /// </summary>
    public const string ReasonSystemUserAmbiguous = "sdap.access.link.systemuser_ambiguous";

    /// <summary>The body failed validation (missing, over-long or malformed token; any other property).</summary>
    public const string ErrorInvalidBody = "sdap.access.link.invalid";

    /// <summary>Dataverse could not be read or written right now: nothing was linked; try again.</summary>
    public const string ErrorUnavailable = "sdap.access.link.unavailable";

    /// <summary>The environment cannot link safely (a masked or missing A9 column, a missing key or write privilege).</summary>
    public const string ErrorEnvironment = "sdap.access.link.environment";

    /// <summary>The largest accepted guest token (an Entra access token is a few KB).</summary>
    public const int MaxTokenLength = 12 * 1024;

    /// <summary>The whole-body cap (task 048 constraint: 16 KB).</summary>
    public const int MaxRequestBodyBytes = 16 * 1024;

    /// <summary>Maps the link group. Called from <see cref="ExternalAccessEndpoints.MapExternalAccessEndpoints"/>.</summary>
    public static RouteGroupBuilder MapExternalWorkforceLinkEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/external")
            .WithTags("External Access")
            .RequireAuthorization(AuthPolicies.ExternalCollaboration);

        group.MapPost("/workforce/link", LinkAsync)
            .WithName("LinkExternalWorkforceHomeIdentity")
            .ExemptFromWorkforceCeiling("Sign-in bootstrap: stores the link from a home identity to a licensed user (R3); no table operation by a workforce contact.")
            .WithSummary("Link a licensed user's home-directory account to their Spaarke systemuser")
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBodyBytes))
            .WithDescription(
                "ADR-028 A9. WorkforceHome bearer only; the body carries the same person's Spaarke-tenant guest access " +
                "token. Both validated → the link is written once onto the guest's enabled systemuser (200 linked), or it " +
                "already exists (200 already_linked, no write). Every failed check is 403 with a reason code and writes " +
                "nothing; an existing different link is never re-pointed (403 link_collision).")
            .RequireRateLimiting("dataverse-query")
            .Accepts<ExternalWorkforceLinkRequest>("application/json")
            .Produces<ExternalWorkforceLinkResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return group;
    }

    private static async Task<IResult> LinkAsync(
        ExternalWorkforceLinkRequest? request,
        HttpContext httpContext,
        SpaarkeGuestTokenValidator guestTokens,
        ContactIdentityBinder binder,
        ILogger<Program> logger,
        CancellationToken ct)
    {
        // 0. WorkforceHome only (ADR-028 A9). The policy also admits the Spaarke-tenant and CIAM schemes; they never link.
        if (WorkforcePrincipalResolver.WorkforceHomeIdentity(httpContext.User) is not { } homeIdentity)
        {
            logger.LogWarning("[A9-LINK] Link refused: the bearer is not a WorkforceHome identity ({Reason})",
                ReasonHomeIdentityRequired);
            return Problem(StatusCodes.Status403Forbidden, ReasonHomeIdentityRequired,
                "Linking is only available when you sign in with your organization's account.");
        }

        if (Validate(request) is { } invalid)
        {
            return invalid;
        }

        var home = new ClaimsPrincipal(homeIdentity);
        var homeOid = MembershipEndpoints.ExtractAadObjectId(home);
        var rawTid = home.FindFirst("tid")?.Value
                     ?? home.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
        if (homeOid is not { } oid || !Guid.TryParse(rawTid?.Trim(), out var homeTenant) || homeTenant == Guid.Empty)
        {
            return Problem(StatusCodes.Status401Unauthorized, WorkforcePrincipalResolver.DenyMissingIdentityClaims,
                "The token is missing identity claims.");
        }

        try
        {
            // 1. The guest half of the proof. Every refusal is the caller's own token's; nothing is read from Dataverse.
            var guest = await guestTokens.ValidateAsync(
                    request!.GuestAccessToken!.Trim(), homeTenant, SpaarkeGuestTokenValidator.HomePreferredUsername(home), ct)
                .ConfigureAwait(false);
            if (guest.DenyReason == GuestTokenDeny.Unavailable)
            {
                return Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was linked; try again.");
            }

            if (guest.DenyReason is { } deny)
            {
                return Problem(StatusCodes.Status403Forbidden, deny, "Your accounts could not be linked.");
            }

            // 2. The write, once (never re-pointed).
            var result = await binder.LinkHomeSystemUserAsync(guest.GuestObjectId!.Value, homeTenant, oid, ct)
                .ConfigureAwait(false);
            return ToResult(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The exception type only: a message could quote token content.
            logger.LogError("[A9-LINK] Link threw {ExceptionType} — refused (retryable)", ex.GetType().Name);
            return Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was linked; try again.");
        }
    }

    /// <summary>The binder's outcome as the route's answer. Pure.</summary>
    internal static IResult ToResult(HomeLinkWriteResult result) => result.Outcome switch
    {
        HomeLinkWriteOutcome.Linked =>
            Results.Ok(new ExternalWorkforceLinkResponse(result.SystemUserId!.Value, "linked")),
        HomeLinkWriteOutcome.AlreadyLinked =>
            Results.Ok(new ExternalWorkforceLinkResponse(result.SystemUserId!.Value, "already_linked")),
        HomeLinkWriteOutcome.NoSystemUser =>
            Problem(StatusCodes.Status403Forbidden, ReasonNoSystemUser, "Your Spaarke account could not be found."),
        HomeLinkWriteOutcome.SystemUserDisabled =>
            Problem(StatusCodes.Status403Forbidden, ReasonSystemUserDisabled, "Your Spaarke account is disabled."),
        HomeLinkWriteOutcome.Collision =>
            Problem(StatusCodes.Status403Forbidden, ReasonLinkCollision,
                "Your account is already linked differently. An administrator must resolve it."),
        _ when result.DenyCode == ContactBindingDecision.DenyGuestSystemUserAmbiguous =>
            Problem(StatusCodes.Status403Forbidden, ReasonSystemUserAmbiguous,
                "Your Spaarke account could not be identified. An administrator must resolve it."),
        _ => result.DenyCode is ContactBindingDecision.DenyBindingColumnMasked or ContactBindingDecision.DenyBindingColumnMissing
            ? Problem(StatusCodes.Status500InternalServerError, ErrorEnvironment,
                "Linking is unavailable in this environment. Nothing was linked; contact your administrator.")
            : Problem(StatusCodes.Status503ServiceUnavailable, ErrorUnavailable, "Nothing was linked; try again."),
    };

    /// <summary>Validates the closed DTO. Pure; null when valid.</summary>
    internal static IResult? Validate(ExternalWorkforceLinkRequest? request)
    {
        var token = request?.GuestAccessToken?.Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Invalid("guestAccessToken is required.");
        }

        if (token.Length > MaxTokenLength)
        {
            return Invalid($"guestAccessToken must be at most {MaxTokenLength} characters.");
        }

        return token.Any(char.IsControl) || token.Contains(' ')
            ? Invalid("guestAccessToken is not a token.")
            : null;
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
                StatusCodes.Status500InternalServerError => "Internal Server Error",
                _ => "Service Unavailable",
            },
            detail: detail,
            extensions: new Dictionary<string, object?> { ["reasonCode"] = reasonCode, ["errorCode"] = reasonCode });
}
