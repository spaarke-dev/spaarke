using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Attaches <see cref="ContactGrantorAuthorizationFilter"/> to a contact-side Grant Access route.
/// </summary>
public static class ContactGrantorAuthorizationFilterExtensions
{
    /// <summary>
    /// Enforces the owner's contact-grantor rules (C4 / Q2, session 27) on the route: the caller is a CONTACT, holds
    /// Collaborate or Full Access on the record, and — to grant — belongs to an active organization. Deny by default for
    /// any request type the filter does not know.
    /// </summary>
    public static TBuilder AddContactGrantorAuthorizationFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new ContactGrantorAuthorizationFilter(
                services.GetRequiredService<ExternalParticipationService>(),
                services.GetRequiredService<DataverseWebApiClient>(),
                services.GetRequiredService<ILogger<ContactGrantorAuthorizationFilter>>());

            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// What the contact-grantor checks established about the caller, for the handler that runs after them.
/// </summary>
/// <param name="Principal">The resolved contact principal.</param>
/// <param name="RootType">The record's type.</param>
/// <param name="RootId">The record.</param>
/// <param name="Rights">The caller's effective post-veto rights on it (the evaluator's answer).</param>
/// <param name="Level">The level those rights carry for granting — Collaborate or Full Access.</param>
/// <param name="OrganizationIds">The caller's CONFERRING organizations (active, current memberships of active
/// organizations). Empty when the check did not need them (list, revoke).</param>
internal sealed record ContactGrantor(
    CallerPrincipal Principal,
    ExternalGrantRootType RootType,
    Guid RootId,
    AccessRights Rights,
    ExternalAccessLevel Level,
    IReadOnlyList<Guid> OrganizationIds)
{
    /// <summary>The grantor contact.</summary>
    public Guid ContactId => Principal.ContactId;
}

/// <summary>
/// unified-access-control-r2 task 140 (#1063) — the route-level gate for contact-side Grant Access (ADR-008). Owner C4:
/// "a contact user with collaborate or full access … should not be prevented from using the 'grant access' feature";
/// Q2: "a contact grants only to contacts of their own organization, at or below their own level, never
/// organization-wide".
/// </summary>
/// <remarks>
/// <para><b>Why a new filter, not <see cref="DelegationRuleFilter"/>.</b> That filter asks Dataverse, OBO as the
/// caller, whether they hold Write. A contact is not a Dataverse principal and a CIAM token cannot be exchanged OBO, so
/// its question can never be answered for a contact. This filter asks the BFF's own principal model instead — the
/// evaluator's post-veto rights already on <see cref="CallerPrincipal"/> (tasks 033, 135, 136, 137) — and copies
/// DelegationRuleFilter's shape: dispatch on the bound request TYPE, and a default branch that DENIES.</para>
/// <para><b>The checks, in order.</b></para>
/// <list type="number">
/// <item>A principal is present (the group's caller-principal filter ran). Otherwise 403 — never "anyone".</item>
/// <item>The caller is a CONTACT principal. A workforce SYSTEMUSER — including one that also carries a linked contact id
/// (task 141) — is refused 403 <see cref="UseManageAccessReasonCode"/>: systemusers grant through Manage Access, whose
/// gate is their own Dataverse Write (<see cref="DelegationRuleFilter"/>).</item>
/// <item>Grant and list: the record is named and resolvable (400 otherwise). Revoke: the named row exists and was issued
/// by the caller — an absent row and somebody else's row are the SAME 404 <see cref="NotFoundReasonCode"/>, so the
/// route is not an access-record oracle.</item>
/// <item>The caller holds Collaborate or Full Access on that record — their effective, post-veto level (owner decision
/// G3 (a)): on a Secure or Limited record that is their DIRECT grant only, and on a Restricted or inactive record it is
/// nothing. View Only and "no access at all" are the same 403 <see cref="LevelInsufficientReasonCode"/>, so the caller
/// cannot tell which happened.</item>
/// <item>Grant only: the caller belongs to at least one active organization (403 <see cref="NoOrganizationReasonCode"/>);
/// a membership read that could not be completed is 503 <see cref="MembershipUnreadableReasonCode"/>, never folded into
/// "no organization".</item>
/// </list>
/// <para><b>The handler re-runs these checks</b> through the same functions
/// (<see cref="EvaluateGrantorAsync"/>, <see cref="EvaluateRevokerAsync"/>): a handler must not trust a pipeline it
/// cannot see. Broker-only (ADR-028 A1/A3): app-only reads, no OBO, no plane branching.</para>
/// </remarks>
internal sealed class ContactGrantorAuthorizationFilter : IEndpointFilter
{
    internal const string UseManageAccessReasonCode = "sdap.access.contact_grant.use_manage_access";
    internal const string LevelInsufficientReasonCode = "sdap.access.contact_grant.level_insufficient";
    internal const string NoOrganizationReasonCode = "sdap.access.contact_grant.no_organization";
    internal const string MembershipUnreadableReasonCode = "sdap.access.contact_grant.membership_unreadable";
    internal const string NotFoundReasonCode = "sdap.access.contact_grant.not_found";
    internal const string RevokeFailedReasonCode = "sdap.access.contact_grant.revoke_failed";
    internal const string PrincipalMissingReasonCode = "sdap.access.contact_grant.principal_unresolved";
    internal const string UnmappedRequestReasonCode = "sdap.access.contact_grant.unmapped_request";

    private readonly ExternalParticipationService _participations;
    private readonly DataverseWebApiClient _dataverseClient;
    private readonly ILogger<ContactGrantorAuthorizationFilter> _logger;

    public ContactGrantorAuthorizationFilter(
        ExternalParticipationService participations,
        DataverseWebApiClient dataverseClient,
        ILogger<ContactGrantorAuthorizationFilter> logger)
    {
        _participations = participations;
        _dataverseClient = dataverseClient;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var ct = httpContext.RequestAborted;

        foreach (var argument in context.Arguments)
        {
            IResult? denial;
            switch (argument)
            {
                case ContactGrantRequest grant:
                    (_, denial) = await EvaluateGrantorAsync(
                        httpContext, grant.RecordType, grant.RecordId, requireOrganization: true, _participations, _logger, ct);
                    break;

                case ContactGrantListQuery list:
                    (_, denial) = await EvaluateGrantorAsync(
                        httpContext, list.RecordType, list.RecordId, requireOrganization: false, _participations, _logger, ct);
                    break;

                case ContactGrantRevokeRequest revoke:
                    (_, _, denial) = await EvaluateRevokerAsync(
                        httpContext, revoke.AccessRecordId, _dataverseClient, _logger, ct);
                    break;

                default:
                    continue;
            }

            return denial ?? await next(context);
        }

        // Deny by default: a route this filter is attached to whose request type it does not map is refused until
        // someone maps it — the DelegationRuleFilter rule, so a new route cannot inherit "anyone may".
        _logger.LogWarning(
            "[CONTACT-GRANT] DENIED on {Route}: no request type this filter knows was bound, so nothing can be checked. " +
            "Fail closed.", httpContext.Request.Path.Value);
        return Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", UnmappedRequestReasonCode,
            "This request could not be authorized.");
    }

    // =========================================================================================
    // The shared checks — called by the filter AND again by the handlers
    // =========================================================================================

    /// <summary>
    /// The grantor checks for a grant (<paramref name="requireOrganization"/>) or a list: a contact principal with
    /// Collaborate or Full Access on the named record, and — to grant — an active organization.
    /// </summary>
    internal static async Task<(ContactGrantor? Grantor, IResult? Denial)> EvaluateGrantorAsync(
        HttpContext httpContext,
        string? recordType,
        Guid? recordId,
        bool requireOrganization,
        ExternalParticipationService participations,
        ILogger logger,
        CancellationToken ct)
    {
        if (ContactPrincipalOrDenial(httpContext, logger) is not { } principal)
            return (null, PrincipalDenial(httpContext));

        if (!principal.IsContactPrincipal)
            return (null, UseManageAccessDenial(httpContext, logger, principal));

        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(recordType, recordId);
        if (!root.Ok)
            return (null, Problem(httpContext, StatusCodes.Status400BadRequest, "Validation Error",
                "sdap.access.contact_grant.record_invalid", root.Error!));

        var rights = principal.RightsOn(root.Type, root.Id);
        if (GrantingLevel(rights) is not { } level)
        {
            logger.LogWarning(
                "[CONTACT-GRANT] DENIED: contact {ContactId} holds {Rights} on {RootType} {RootId} — not Collaborate or Full " +
                "Access (or no access at all; the two are one answer).", principal.ContactId, rights, root.Type, root.Id);
            return (null, LevelInsufficientDenial(httpContext));
        }

        if (!requireOrganization)
            return (new ContactGrantor(principal, root.Type, root.Id, rights, level, Array.Empty<Guid>()), null);

        var memberships = await ReadMembershipsAsync(participations, principal.ContactId, logger, ct);
        if (memberships.Unreadable)
            return (null, MembershipUnreadableDenial(httpContext,
                "Your organization membership could not be checked, so nothing was granted. Try again in a moment."));

        if (memberships.ConferringOrganizationIds.Count == 0)
        {
            logger.LogWarning(
                "[CONTACT-GRANT] DENIED: contact {ContactId} belongs to no active organization, so has no colleagues to " +
                "grant (owner Q2).", principal.ContactId);
            return (null, Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", NoOrganizationReasonCode,
                "You can grant access only to colleagues in your own organization, and you are not an active member of " +
                "any organization in this system. Ask the record's team to grant access."));
        }

        return (new ContactGrantor(
            principal, root.Type, root.Id, rights, level, memberships.ConferringOrganizationIds.Distinct().ToList()), null);
    }

    /// <summary>
    /// The revoker checks: a contact principal, a row that exists AND was issued by the caller (one 404 for both
    /// failures), and Collaborate or Full Access on the row's record.
    /// </summary>
    internal static async Task<(ContactGrantor? Grantor, ExternalGrantRow? Row, IResult? Denial)> EvaluateRevokerAsync(
        HttpContext httpContext,
        Guid accessRecordId,
        DataverseWebApiClient dataverseClient,
        ILogger logger,
        CancellationToken ct)
    {
        if (ContactPrincipalOrDenial(httpContext, logger) is not { } principal)
            return (null, null, PrincipalDenial(httpContext));

        if (!principal.IsContactPrincipal)
            return (null, null, UseManageAccessDenial(httpContext, logger, principal));

        if (accessRecordId == Guid.Empty)
            return (null, null, NotFoundDenial(httpContext));

        ExternalGrantRow? row;
        try
        {
            row = await ExternalGrantLifecycle.RetrieveRowAsync(dataverseClient, accessRecordId, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            row = null;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[CONTACT-GRANT] Revoke by contact {ContactId}: access record {AccessRecordId} could not be read. Nothing " +
                "was revoked.", principal.ContactId, accessRecordId);
            return (null, null, Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Access not revoked",
                RevokeFailedReasonCode,
                "The access could not be revoked because it could not be read just now. Nothing was changed; try again."));
        }

        // An absent row and a row somebody else issued are ONE answer — the route is not an access-record oracle.
        if (row is null || row.GrantedByContactId != principal.ContactId)
        {
            logger.LogWarning(
                "[CONTACT-GRANT] Revoke by contact {ContactId}: access record {AccessRecordId} is absent or not theirs " +
                "(uniform 404).", principal.ContactId, accessRecordId);
            return (null, null, NotFoundDenial(httpContext));
        }

        if (ExternalGrantLifecycle.DeriveKey(row) is not { } key)
        {
            logger.LogError(
                "[CONTACT-GRANT] Access record {AccessRecordId} has no derivable root or grantee; refusing to revoke.",
                accessRecordId);
            return (null, null, Problem(httpContext, StatusCodes.Status500InternalServerError, "Access not revoked",
                RevokeFailedReasonCode,
                "This access record is missing the record or the person it is for, so it cannot be revoked here. " +
                "Nothing was changed; ask the record's team."));
        }

        var rights = principal.RightsOn(key.RootType, key.RootId);
        if (GrantingLevel(rights) is not { } level)
        {
            logger.LogWarning(
                "[CONTACT-GRANT] DENIED revoke: contact {ContactId} issued {AccessRecordId} but now holds {Rights} on " +
                "{RootType} {RootId}.", principal.ContactId, accessRecordId, rights, key.RootType, key.RootId);
            return (null, null, LevelInsufficientDenial(httpContext));
        }

        return (new ContactGrantor(principal, key.RootType, key.RootId, rights, level, Array.Empty<Guid>()), row, null);
    }

    /// <summary>
    /// The level a contact's rights let them GRANT with: Collaborate or Full Access through the ONE ceiling table
    /// (<see cref="ExternalAccessLevels.GrantCeilingFor"/>, task 139); <c>null</c> for View Only or nothing.
    /// </summary>
    internal static ExternalAccessLevel? GrantingLevel(AccessRights rights)
        => ExternalAccessLevels.GrantCeilingFor(rights) is { } level && level != ExternalAccessLevel.ViewOnly
            ? level
            : null;

    /// <summary>
    /// The ONE organization-membership read (<see cref="ExternalParticipationService.ReadOrganizationMembershipsAsync"/>,
    /// task 109) — its CONFERRING set is exactly the eligibility test the owner's Q2 needs: an active junction row, current
    /// on both date bounds (<c>sprk_startdate</c> ≤ today ≤ <c>sprk_enddate</c>), under an active organization. A fault —
    /// reported or thrown — is <see cref="ActiveOrgMemberships.Failed"/>, never an empty membership.
    /// </summary>
    internal static async Task<ActiveOrgMemberships> ReadMembershipsAsync(
        ExternalParticipationService participations, Guid contactId, ILogger logger, CancellationToken ct)
    {
        try
        {
            return await participations.ReadOrganizationMembershipsAsync(contactId, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex,
                "[CONTACT-GRANT] Organization memberships of contact {ContactId} could not be read; refusing (fail closed).",
                contactId);
            return ActiveOrgMemberships.Failed;
        }
    }

    // =========================================================================================
    // Denials
    // =========================================================================================

    private static CallerPrincipal? ContactPrincipalOrDenial(HttpContext httpContext, ILogger logger)
    {
        var principal = httpContext.Items[CallerPrincipal.HttpContextItemsKey] as CallerPrincipal;
        if (principal is null)
        {
            logger.LogError(
                "[CONTACT-GRANT] DENIED: no caller principal on the request — the group's caller-principal filter did " +
                "not run. Fail closed.");
        }

        return principal;
    }

    private static IResult PrincipalDenial(HttpContext httpContext)
        => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", PrincipalMissingReasonCode,
            "Your sign-in could not be resolved, so this request cannot be authorized.");

    private static IResult UseManageAccessDenial(HttpContext httpContext, ILogger logger, CallerPrincipal principal)
    {
        logger.LogWarning(
            "[CONTACT-GRANT] DENIED: systemuser {SystemUserId} called a contact-side Grant Access route; internal users " +
            "grant through Manage Access.", principal.SystemUserId);
        return Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", UseManageAccessReasonCode,
            "This is the external colleagues' Grant Access. As an internal user, use Manage Access on the record instead.");
    }

    internal static IResult LevelInsufficientDenial(HttpContext httpContext)
        => Problem(httpContext, StatusCodes.Status403Forbidden, "Forbidden", LevelInsufficientReasonCode,
            "You need Collaborate or Full Access on this record to give colleagues access to it.");

    internal static IResult MembershipUnreadableDenial(HttpContext httpContext, string detail)
        => Problem(httpContext, StatusCodes.Status503ServiceUnavailable, "Organization membership unavailable",
            MembershipUnreadableReasonCode, detail);

    internal static IResult NotFoundDenial(HttpContext httpContext)
        => Problem(httpContext, StatusCodes.Status404NotFound, "Not Found", NotFoundReasonCode,
            "No access that you granted was found with this id.");

    /// <summary>The one ProblemDetails shape of the contact-side routes: status, reason code, message and trace id.</summary>
    internal static IResult Problem(HttpContext httpContext, int status, string title, string reasonCode, string detail)
        => Results.Problem(
            statusCode: status,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode,
                ["traceId"] = httpContext.TraceIdentifier,
            });
}
