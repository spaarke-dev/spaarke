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
    /// access-management action on a record only if they hold <see cref="AccessRights.Write"/> AND
    /// <see cref="AccessRights.Share"/> on that record, evaluated as the caller (owner round 89, task 179).
    /// </summary>
    /// <remarks>
    /// Applied at the GROUP rather than per route, deliberately. The group is a closed
    /// access-management surface — mutations, plus two reads: task 063's share list (which discloses who can
    /// reach a record) and task 118's <c>/can-manage-access</c> (which reports THIS filter's verdict to the
    /// client, so the Manage Access affordance gates on the server's rule rather than on a table-level proxy
    /// for it) — and <see cref="DelegationRuleFilter"/> denies any request whose target
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
/// <paramref name="TableWritePrivilege"/> is set only for an ORGANIZATION-owned table: Dataverse refuses
/// <c>RetrievePrincipalAccess</c> on such a table (400 0x80040800), and Write on any of its rows is the table's Write
/// privilege, so the filter asks that privilege instead of the record's rights.
/// <paramref name="WriteSuffices"/> is set only for <c>/assigned-access/sync</c> (task 179): that route applies the
/// record's OWN Assigned-To columns, exactly as the app-only reconciliation job does within minutes, so it changes nothing
/// a Write-holder could not cause by saving the record. Its default (<c>false</c>) is the full rule, so a target built
/// without it, <c>default</c> included, asks for Share too.
/// </summary>
internal readonly record struct DelegationTarget(
    string EntitySet, Guid RecordId, string? TableWritePrivilege = null, bool WriteSuffices = false)
{
    public override string ToString() => $"{EntitySet}({RecordId})";
}

/// <summary>
/// FR-07 / finding A-6 — the delegation rule: <b>you may change who can access a record only if you hold
/// Write on that record AND Dataverse's Share privilege on its table at a depth that reaches it</b>
/// (owner decision B-14, tightened by owner round 89 / task 179), checked AS THE CALLER.
/// </summary>
/// <remarks>
/// <para><b>Share since round 89 (task 179).</b> Write alone (task 118, D-1) let any role that can edit a record open
/// Manage Access and grant. The owner made Dataverse's own Share privilege, set per security role, the second half of
/// the test. It is read from the SAME <c>RetrievePrincipalAccess</c> answer the Write check already used: Dataverse
/// reports <c>ShareAccess</c> on the record for a caller whose roles grant Share at a depth covering it (user, business
/// unit, parent-child business unit or organization), so the rule costs no new round trip. Microsoft Learn documents
/// that the privilege check runs before the record-access check and that a share cannot give rights the role does not
/// allow (<i>How access to a record is determined</i>); whether <c>RetrievePrincipalAccess</c> applies that check to a
/// SHARED ShareAccess is not documented, so the live gate in the task notes confirms it on dev. Either way the answer is
/// Dataverse's own, never a client mirror. Two exceptions, each stated where its target is resolved:
/// <list type="bullet">
/// <item>an ORGANIZATION-owned table has no Share privilege at all (Dataverse creates none: on dev
/// <c>sprk_noaccessentry</c> has Create, Read, Write, Delete, Append and AppendTo only), so <c>/no-access/enforce</c>
/// keeps the table Write privilege. Asking for a privilege that cannot exist would refuse every role, the access
/// administrator included;</item>
/// <item><c>/assigned-access/sync</c> keeps Write (see <see cref="DelegationTarget"/>).</item>
/// </list></para>
///
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
    internal const string DenyNoCallerToken = "sdap.access.deny.delegation_no_caller_token";
    internal const string DenyTargetUnresolved = "sdap.access.deny.delegation_target_unresolved";
    internal const string DenyWriteRequired = "sdap.access.deny.delegation_write_required";
    internal const string DenyCheckFailed = "sdap.access.deny.delegation_check_failed";

    /// <summary>
    /// Task 179 (owner round 89): the caller holds Write on the record but Dataverse reports no Share on it: their
    /// security roles do not grant the table's Share privilege at a depth that reaches the record.
    /// </summary>
    internal const string DenyShareRequired = "sdap.access.deny.delegation_share_required";

    /// <summary>The one user-facing sentence for both record-rights refusals: it names the whole rule.</summary>
    internal const string RightsRequiredDetail =
        "To change who else can access this record you need Write access to it and the Share privilege on its table " +
        "(set in your security role).";

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
                "[DELEGATION] DENIED on {Route}: no caller bearer token, so the Write and Share check cannot be " +
                "evaluated as the caller. Refusing to fall back to app-only (fail closed).", route);

            return Deny(httpContext, DenyNoCallerToken,
                "This operation requires Write access and the Share privilege on the target record, evaluated as the " +
                "calling user. No caller credential was present on the request.");
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
                "there is nothing to check Write and Share against. Fail closed (ADR-003).", route);

            return Deny(httpContext, DenyTargetUnresolved,
                "The target record for this operation could not be resolved from the request.");
        }

        if (target.Value.TableWritePrivilege is { } privilege)
        {
            // Organization-owned table: the caller's table Write privilege IS Write on the row. The probe answers
            // false on any failure (fail closed). A caller without it gets the same 403 whether or not the row
            // exists, so the id stays unenumerable; a caller with it may already read the table, so the handler's
            // own 404 for an absent row discloses nothing new. No Share half here (task 179): an organization-owned
            // table has no Share privilege to hold (see the class remarks).
            if (!await _probe.CallerHoldsPrivilegeAsync(callerToken, privilege, ct))
            {
                _logger.LogWarning(
                    "[DELEGATION] DENIED on {Route} for {Target}: caller does not hold {Privilege} (organization-owned " +
                    "table, so the table privilege is Write on the row).", route, target.Value, privilege);

                return Deny(httpContext, DenyWriteRequired,
                    "You must have Write access to this record to change who else can access it.");
            }

            _logger.LogInformation(
                "[DELEGATION] ALLOWED on {Route} for {Target}: caller holds {Privilege}.", route, target.Value, privilege);

            return await next(context);
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

        // ONE answer decides both halves (task 179): the rights RetrievePrincipalAccess returned for this record. Write is
        // checked first, so a caller holding neither keeps the long-standing write_required code.
        if ((rights & AccessRights.Write) != AccessRights.Write)
        {
            _logger.LogWarning(
                "[DELEGATION] DENIED on {Route} for {Target}: caller holds {Rights}, which does not include " +
                "Write. A caller may delegate access to a record only if they can write it (B-14).",
                route, target.Value, rights);

            return Deny(httpContext, DenyWriteRequired, RightsRequiredDetail);
        }

        if (!target.Value.WriteSuffices && (rights & AccessRights.Share) != AccessRights.Share)
        {
            _logger.LogWarning(
                "[DELEGATION] DENIED on {Route} for {Target}: caller holds {Rights}, which includes Write but not " +
                "Share. Managing access needs the table's Share privilege at a depth that reaches the record (round 89).",
                route, target.Value, rights);

            return Deny(httpContext, DenyShareRequired, RightsRequiredDetail);
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
                // Task 144: a project, matter OR work assignment. The target comes from the SAME resolver the
                // handler uses (recordType + recordId, else the legacy projectId), so the record whose Write is
                // checked here is the record the handler re-owns — a request cannot authorize against a project
                // it can write and then re-own a matter it cannot. An unknown recordType resolves to nothing and
                // is denied by the null path below.
                case ProvisionProjectRequest provision:
                    return FromGrantRoot(ProvisionProjectEndpoint.ResolveRoot(provision));

                // ── /unsecure-project (task 061; three root types since task 144) ──
                // Removing the secure designation is at least as consequential as applying it, so it
                // is gated by the same delegation check (Write and Share on the record), evaluated as the caller, on
                // the root the handler's own resolver names. Omitting this case would not have opened a hole — an
                // unresolved target denies — but it would have made the route permanently 403.
                case UnsecureProjectRequest unsecure:
                    return FromGrantRoot(UnsecureProjectEndpoint.ResolveRoot(unsecure));

                // ── /set-record-share-expiry (task 098, FR-33) ────────────────
                // Changing when every share on a record ends changes who can access it, so it takes the same
                // delegation check (Write and Share). The target comes from the SAME ResolveRoot the handler uses, and
                // that request has no legacy projectId — so the record authorized here is the record whose
                // shares are written. Without this case the route would deny every caller (default branch).
                case SetRecordShareExpiryRequest shareExpiry:
                    return FromGrantRoot(SetRecordShareExpiryEndpoint.ResolveRoot(shareExpiry));

                // ── /share-user, /unshare-user, /user-shares (task 063, FR-29) ─────
                // Internal system-user shares change — or, for the list, disclose — who can reach a record, so they
                // take the same delegation check (Write and Share on the record). Each target comes from the SAME
                // explicit-root resolver its handler uses, and none of these requests has a legacy projectId, so the
                // record authorized is the record whose shares are read or written. Without these cases every call would deny (default branch).
                case ShareRecordWithUserRequest shareUser:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(shareUser.RecordType, shareUser.RecordId));

                case UnshareRecordWithUserRequest unshareUser:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(unshareUser.RecordType, unshareUser.RecordId));

                case RecordUserSharesQuery userShares:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(userShares.RecordType, userShares.RecordId));

                // ── /can-manage-access (task 118, FR-07 / D-1 option C) ───────────
                // The one route on this group whose PURPOSE is to be gated. It reports this filter's own verdict
                // to the client so the Manage Access affordance asks the server's question instead of guessing at
                // it from a table-level privilege. Mapping it here is what makes the answer true: without this
                // case the default branch would deny every caller, and the client — which reads any non-200 as
                // "no" — would hide the affordance from everyone, which is precisely the failure the task's
                // code-before-config ordering exists to prevent.
                case RecordAccessGateQuery gate:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(gate.RecordType, gate.RecordId));

                // ── /no-access/enforce (task 143) ────────────────────────────────
                // The target is the No Access ENTRY itself, so the caller must hold Write on the entry (owner O2: the
                // access-administrator role). sprk_noaccessentry is ORGANIZATION-owned, which RetrievePrincipalAccess
                // refuses (400 0x80040800 on dev, so every caller was denied); Write on any row of such a table is
                // the table Write privilege, so that is what is asked. A caller without it gets this filter's 403
                // whether or not the entry exists. The removals the handler then makes on each covered record are
                // bounded by the entry AUTHOR's Write on that record (owner N5), decided inside the enforcer.
                // Without this case every caller would be denied.
                case NoAccessEnforceRequest enforce:
                    return enforce.EntryId is { } entryId && entryId != Guid.Empty
                        ? new DelegationTarget(NoAccessEnforceEndpoint.EntrySet, entryId, NoAccessEnforceEndpoint.EntryWritePrivilege)
                        : null;

                // ── /assigned-access/sync, /assigned-access, /assigned-access/dismiss (task 142) ──
                // The Assigned-To routes change (or, for the list, disclose) who can reach a record, so they are gated on
                // the record as /share-user is — the post-save script, a wizard and the "Update Access" ribbon command all
                // call them as the user. Each target comes from the SAME explicit-root resolver its handler
                // uses, so the record authorized is the record materialized. Without these cases the default branch below
                // would deny every caller — the filter "attached" but never reaching the request type.
                // Task 179: /sync alone keeps Write. It applies the record's own Assigned-To columns, exactly what the
                // app-only AssignedAccessReconciliationJob applies within five minutes, and the post-save script and every
                // create wizard call it on each save. Requiring Share there would protect nothing and would warn every
                // Write-holder without Share on every save. /assigned-access (the Manage Access list) and /dismiss (a
                // decision about who gets access) take the full rule.
                case AssignedAccessSyncRequest sync:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(sync.RecordType, sync.RecordId)) is { } syncTarget
                        ? syncTarget with { WriteSuffices = true }
                        : null;

                case AssignedAccessListQuery assignedList:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(assignedList.RecordType, assignedList.RecordId));

                case AssignedAccessDismissRequest dismiss:
                    return FromGrantRoot(GrantExternalAccessEndpoint.ResolveExplicitRoot(dismiss.RecordType, dismiss.RecordId));
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
    /// record is this request about". The decision itself is the caller-scoped Write and Share check that
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
