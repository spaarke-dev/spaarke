using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// Attaches <see cref="DelegationRuleFilter"/> to the external-access management route group.
/// </summary>
public static class DelegationRuleFilterExtensions
{
    /// <summary>
    /// Enforces the delegation rule on EVERY route in the group: a caller may perform an
    /// external-access mutation on a record only if they hold <see cref="AccessRights.Write"/> on
    /// that record, evaluated as the caller.
    /// </summary>
    /// <remarks>
    /// Applied at the GROUP rather than per route, deliberately. The group is a closed
    /// mutation-only surface, and <see cref="DelegationRuleFilter"/> denies any request whose target
    /// it cannot identify — so a seventh route added tomorrow is gated from its first request
    /// instead of inheriting a hole. That failure is loud and immediate (the author hits 403 on the
    /// first call) rather than silent, which is the correct direction for an authorization default.
    /// It also mirrors the sibling <c>/api/v1/external</c> group, which carries
    /// <c>AddCallerPrincipalAuthorizationFilter</c> the same way.
    /// </remarks>
    public static TBuilder AddDelegationRuleFilter<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        return builder.AddEndpointFilter(async (context, next) =>
        {
            var services = context.HttpContext.RequestServices;
            var filter = new DelegationRuleFilter(
                services.GetRequiredService<CallerRecordAccessProbe>(),
                services.GetRequiredService<DataverseWebApiClient>(),
                services.GetRequiredService<ILogger<DelegationRuleFilter>>());

            return await filter.InvokeAsync(context, next);
        });
    }
}

/// <summary>
/// The record a delegation check is about: a Dataverse entity SET name plus a record id.
/// </summary>
internal readonly record struct DelegationTarget(string EntitySet, Guid RecordId)
{
    public override string ToString() => $"{EntitySet}({RecordId})";
}

/// <summary>
/// FR-07 / finding A-6 — the delegation rule: <b>you may grant access to a record only if you hold
/// Write on that record</b> (owner decision B-14), checked AS THE CALLER.
/// </summary>
/// <remarks>
/// <para><b>What was wrong.</b> The <c>/api/v1/external-access</c> group carried a bare
/// <c>RequireAuthorization()</c> and nothing else (<c>ExternalAccessEndpoints.cs:109-111</c>). Every
/// write on it — mint a grant, revoke one, onboard a CIAM identity, cascade-close a project,
/// provision a business unit — ran app-only behind an "are you anyone?" gate. Any authenticated user
/// could grant themselves, or anyone else, Full Access to any record. design.md §6 names this the
/// blocking prerequisite for the Manage Access PCF: without it the "+ User" button is a one-click
/// path from read-only to Full Access on a confidential matter.</para>
///
/// <para><b>Target resolution is by request TYPE, not by path.</b> Each route on this group binds a
/// distinct request DTO, and each DTO names its target record in its own way. Dispatching on the
/// bound type keeps the mapping exhaustive and checkable by the compiler-adjacent means of a
/// <c>switch</c> with a default — and the default DENIES. Path strings would have to be duplicated
/// from five other files and would silently drift.</para>
///
/// <para><b>Every exit path denies with 403</b> (ADR-003 fail-closed), including the ones that could
/// arguably be 400 or 404. That is deliberate: answering 404 for "no such access record" BEFORE
/// authorization would let an unauthorized caller enumerate access-record ids. The handler still
/// returns its precise 400/404 to callers who pass the gate.</para>
///
/// <para>ADR-008: filter at route registration, not middleware. ADR-028: the rights come from an OBO
/// evaluation (<see cref="CallerRecordAccessProbe"/>) — an app-only Write probe would answer "can
/// the application write", which is finding A-2. ADR-010: ONE filter parameterized by
/// target-resolution, not six bespoke filters.</para>
/// </remarks>
internal sealed class DelegationRuleFilter : IEndpointFilter
{
    /// <summary>The right that confers the ability to delegate access (owner decision B-14).</summary>
    private const AccessRights RequiredRight = AccessRights.Write;

    internal const string DenyNoCallerToken = "sdap.access.deny.delegation_no_caller_token";
    internal const string DenyTargetUnresolved = "sdap.access.deny.delegation_target_unresolved";
    internal const string DenyWriteRequired = "sdap.access.deny.delegation_write_required";
    internal const string DenyCheckFailed = "sdap.access.deny.delegation_check_failed";

    private readonly CallerRecordAccessProbe _probe;
    private readonly DataverseWebApiClient _dataverseClient;
    private readonly ILogger<DelegationRuleFilter> _logger;

    public DelegationRuleFilter(
        CallerRecordAccessProbe probe,
        DataverseWebApiClient dataverseClient,
        ILogger<DelegationRuleFilter> logger)
    {
        _probe = probe;
        _dataverseClient = dataverseClient;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var route = httpContext.Request.Path.Value ?? "(unknown)";
        var ct = httpContext.RequestAborted;

        // The caller's own bearer token is the credential the OBO evaluation runs on. Without it the
        // check cannot be caller-scoped at all, and running it app-only would answer for the
        // application (finding A-2). Deny rather than degrade.
        var callerToken = TokenHelper.ExtractBearerTokenOrNull(httpContext);
        if (callerToken is null)
        {
            _logger.LogWarning(
                "[DELEGATION] DENIED on {Route}: no caller bearer token, so the Write check cannot be " +
                "evaluated as the caller. Refusing to fall back to app-only (fail closed).", route);

            return Deny(httpContext, DenyNoCallerToken,
                "This operation requires Write access on the target record, evaluated as the calling user. " +
                "No caller credential was present on the request.");
        }

        DelegationTarget? target;
        try
        {
            target = await ResolveTargetAsync(context, _dataverseClient, _logger, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DELEGATION] DENIED on {Route}: resolving the target record threw. Fail closed.", route);

            return Deny(httpContext, DenyCheckFailed,
                "The target record for this operation could not be resolved.");
        }

        if (target is null)
        {
            // 403 not 400/404 — see the class remarks on enumeration.
            _logger.LogWarning(
                "[DELEGATION] DENIED on {Route}: no target record could be resolved from the request, so " +
                "there is nothing to check Write against. Fail closed (ADR-003).", route);

            return Deny(httpContext, DenyTargetUnresolved,
                "The target record for this operation could not be resolved from the request.");
        }

        AccessRights rights;
        try
        {
            rights = await _probe.GetCallerRightsAsync(callerToken, target.Value.EntitySet, target.Value.RecordId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[DELEGATION] DENIED on {Route} for {Target}: the caller access check threw. Fail closed.",
                route, target.Value);

            return Deny(httpContext, DenyCheckFailed,
                "The caller's access to the target record could not be determined.");
        }

        if ((rights & RequiredRight) != RequiredRight)
        {
            _logger.LogWarning(
                "[DELEGATION] DENIED on {Route} for {Target}: caller holds {Rights}, which does not include " +
                "{RequiredRight}. A caller may delegate access to a record only if they can write it (B-14).",
                route, target.Value, rights, RequiredRight);

            return Deny(httpContext, DenyWriteRequired,
                "You must have Write access to this record to change who else can access it.");
        }

        _logger.LogInformation(
            "[DELEGATION] ALLOWED on {Route} for {Target}: caller holds {Rights}.", route, target.Value, rights);

        return await next(context);
    }

    // =========================================================================
    // Target resolution — one branch per request DTO on this group
    // =========================================================================

    /// <summary>
    /// The record this request wants to change access on, or <c>null</c> when none can be identified.
    /// </summary>
    /// <remarks>
    /// <para>The default branch returns <c>null</c> (→ deny) on purpose. A route added to this group
    /// with an unmapped request type is denied until someone maps it, rather than silently
    /// inheriting the A-6 hole.</para>
    ///
    /// <para><b>/invite is included even though it writes no grant row.</b> It resolves-or-creates a
    /// Contact and provisions a CIAM identity against a named project — identity provisioning is a
    /// privilege, and the DTO already carries the root. Its only first-party caller
    /// (<c>external-spa/src/auth/bff-client.ts</c> <c>InviteUserRequest</c>) sends <c>projectId</c> as
    /// a required field, so requiring a resolvable root breaks nothing that exists.</para>
    /// </remarks>
    internal static async Task<DelegationTarget?> ResolveTargetAsync(
        EndpointFilterInvocationContext context,
        DataverseWebApiClient dataverseClient,
        ILogger logger,
        CancellationToken ct)
    {
        foreach (var argument in context.Arguments)
        {
            switch (argument)
            {
                // ── /grant ────────────────────────────────────────────────────
                case GrantAccessRequest grant:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveGrantRoot(grant));

                // ── /invite and /invite-and-grant (same DTO, same target) ─────
                case InviteExternalUserRequest invite:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveGrantRoot(
                        new GrantAccessRequest(
                            ContactId: Guid.Empty,      // irrelevant to root resolution
                            ProjectId: invite.ProjectId,
                            AccessLevel: default,
                            ExpiryDate: null,
                            OrganizationId: null,
                            RecordType: invite.RecordType,
                            RecordId: invite.RecordId)));

                // ── /revoke ───────────────────────────────────────────────────
                case RevokeAccessRequest revoke:
                    return await FromAccessRecordAsync(revoke, dataverseClient, logger, ct);

                // ── /close-project ────────────────────────────────────────────
                case CloseProjectRequest close:
                    return FromProjectId(close.ProjectId);

                // ── /provision-project ────────────────────────────────────────
                case ProvisionProjectRequest provision:
                    return FromProjectId(provision.ProjectId);
            }
        }

        return null;
    }

    private static DelegationTarget? FromGrantRoot(GrantExternalAccessEndpoint.GrantRootResolution root)
        => root.Ok
            ? new DelegationTarget(ExternalGrantRoot.BindFor(root.Type).EntitySet, root.Id)
            : null;

    private static DelegationTarget? FromProjectId(Guid projectId)
        => projectId == Guid.Empty
            ? null
            : new DelegationTarget(ExternalGrantRoot.BindFor(ExternalGrantRootType.Project).EntitySet, projectId);

    /// <summary>
    /// Revoke names an access-record id, not a record. The record it grants access TO is the row's
    /// root, so resolving the target means reading the row.
    /// </summary>
    /// <remarks>
    /// <para>This repeats the <c>RetrieveRowAsync</c> the handler performs as its own first step
    /// (task 010). That duplicate read is accepted rather than passed through <c>HttpContext.Items</c>:
    /// one extra Dataverse GET on a low-volume admin mutation does not materially change request cost,
    /// and a handler that trusted a row cached by a filter would be trusting authorization state it
    /// cannot verify. The POML's second escalation trigger — "escalate if the extra read materially
    /// changes request cost" — was evaluated here and does not fire.</para>
    ///
    /// <para>The row read is app-only. That is not the authorization decision; it only answers "which
    /// record is this request about". The decision itself is the caller-scoped Write check that
    /// follows, on the root this read identifies.</para>
    /// </remarks>
    private static async Task<DelegationTarget?> FromAccessRecordAsync(
        RevokeAccessRequest revoke,
        DataverseWebApiClient dataverseClient,
        ILogger logger,
        CancellationToken ct)
    {
        if (revoke.AccessRecordId == Guid.Empty)
        {
            return null;
        }

        var row = await ExternalGrantLifecycle.RetrieveRowAsync(dataverseClient, revoke.AccessRecordId, ct);
        if (row is null)
        {
            logger.LogWarning(
                "[DELEGATION] Access record {AccessRecordId} was not found while resolving the revoke " +
                "target. Denying (403, not 404 — a 404 here would let an unauthorized caller enumerate " +
                "access-record ids).", revoke.AccessRecordId);

            return null;
        }

        var key = ExternalGrantLifecycle.DeriveKey(row);
        if (key is null)
        {
            logger.LogWarning(
                "[DELEGATION] Access record {AccessRecordId} has no derivable root, so the record whose " +
                "access is being revoked is unknown. Denying.", revoke.AccessRecordId);

            return null;
        }

        return new DelegationTarget(ExternalGrantRoot.BindFor(key.Value.RootType).EntitySet, key.Value.RootId);
    }

    // =========================================================================
    // Denial
    // =========================================================================

    /// <summary>
    /// The single denial shape for this filter: 403 + ProblemDetails carrying a machine-readable
    /// deny code (ADR-003) and the correlation id (ADR-019).
    /// </summary>
    private static IResult Deny(HttpContext httpContext, string reasonCode, string detail)
        => Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "Forbidden",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            extensions: new Dictionary<string, object?>
            {
                ["reasonCode"] = reasonCode,
                ["traceId"] = httpContext.TraceIdentifier
            });
}
