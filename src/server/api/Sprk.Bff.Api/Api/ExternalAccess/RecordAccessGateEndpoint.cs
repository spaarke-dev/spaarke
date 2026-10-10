using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;

using Sprk.Bff.Api.Services.Access;

namespace Sprk.Bff.Api.Api.ExternalAccess;

/// <summary>
/// <c>GET /api/v1/external-access/can-manage-access?recordType=&amp;recordId=</c> — lets a client ASK the
/// delegation question instead of GUESSING at it: <i>may I change who can access this record?</i>
/// </summary>
/// <remarks>
/// <para><b>Why this exists</b> (unified-access-control-r2 task 118, owner decision D-1 option C). The
/// Manage Access affordance in <c>TrackingFieldTrio</c> gated on a Dataverse TABLE-level privilege —
/// <c>hasEntityPrivilege('sprk_externalrecordaccess', Create, Global)</c> — which is a different question
/// from the one this server enforces, with the opposite fail direction. The server asks "do you hold Write
/// on THIS record" and denies what it cannot evaluate; the client asked "may you create rows in that table
/// anywhere" and ALLOWED what it could not evaluate. A caller with the table privilege but no Write on a
/// confidential matter was offered the button and then refused by the server — the affordance promised an
/// action the caller could not perform, on exactly the records where that matters most.</para>
///
/// <para><b>The status code IS the answer, and that is deliberate.</b> This route is registered on the
/// <c>/api/v1/external-access</c> management group, so <see cref="DelegationRuleFilter"/> — the SAME filter
/// that gates every grant, revoke, share and expiry change — runs before this handler. A caller without
/// Write never reaches the code below; they get the filter's 403. So the client is not told a mirror of the
/// rule, computed somewhere a mirror could drift from the original: it is told the OUTCOME OF THE RULE
/// ITSELF, produced by the one implementation of it. Two gates that cannot disagree, because there is still
/// only one gate.</para>
///
/// <para><b>What this endpoint is not.</b> It is not an enforcement point and must never become one — no
/// write is permitted here, and no other route may consult it in place of the filter (ADR-008: resource
/// authorization lives in the filter, at route registration). It is also not a permissions API: it answers
/// about ONE record for the CALLER, and deliberately does not return the rights mask. Handing a client the
/// raw rights would invite it to re-derive authorization rules client-side, which is the pattern this
/// project exists to remove (CLAUDE.md §11 — one component that answers one question well).</para>
///
/// <para><b>The record's OWNER, on request only</b> (task 150, round 46 item 4; round 53 item 2). With
/// <c>includeOwner=true</c> the answer also names the record's owning team, whether it is the Secure Record Owners team,
/// and whether that team owns it inside the Secure Record business unit (<see cref="ReadOwnerAsync"/>). These are facts
/// about the RECORD, not the caller's rights — the rights mask is still never returned — and they decide nothing here.
/// The Access ribbon needs them to tell a provisioned secure record from one whose secure transition did not finish (a
/// secure record reassigned outside Spaarke to a team in another business unit, or a legacy one still user-owned), and
/// both from one owned by ANOTHER team inside the Secure Record business unit (the retired default team before task 144's
/// migration — already isolated, so nothing to finish), because which team and business unit those are is the server's
/// configuration. Opt-in, so the Manage Access gates (<c>TrackingFieldTrio</c>, the flyout's own visibility) still make
/// one rights probe and no read.</para>
///
/// <para><b>It adds no second <c>RetrievePrincipalAccess</c> caller.</b> The rights come from
/// <see cref="Infrastructure.ExternalAccess.CallerRecordAccessProbe"/>, invoked once by the filter on the
/// caller's OBO token. This handler re-probes nothing — unlike
/// <see cref="InternalShareEndpoints.ShareAsync"/>, which re-probes because it needs the rights THEMSELVES to
/// intersect a requested level. Here the decision is the whole answer, so a second probe would double the
/// Dataverse round trips on a form-load path and buy nothing.</para>
///
/// <para><b>🔴 Removing the group filter silently inverts this endpoint's meaning.</b> This handler has no
/// rights logic of its own — by design — so detached from <c>AddDelegationRuleFilter()</c> it would answer
/// <c>true</c> to everyone, and every client gate built on it would fail OPEN: the exact defect task 118
/// closed. Equally, deleting this route's <c>case</c> from <c>DelegationRuleFilter.ResolveTargetAsync</c>
/// would send it to the default DENY branch and hide the affordance from everyone. Both directions are
/// pinned by <c>RecordAccessGateTests</c> (<c>tests/integration/auth/UnifiedAccessControl/</c>), whose
/// negatives each have a positive twin differing only in the caller's rights.</para>
///
/// <para><b>Why this gate ignores Secure and Access Permission — by design (task 138).</b> This route answers
/// ONE question: may the caller change who can access this record? That is the delegation rule (Write on the
/// record, owner decision B-14 / C4), and it is the same on a Standard, Limited, Restricted or Secure record —
/// a Write-holder on a Restricted record may still share it with a colleague (+ User), and may revoke existing
/// grants. The record's flags govern a DIFFERENT question: WHICH grant types apply. That is enforced at write
/// time, in the one policy function the grant routes share (<c>ExternalGrantLifecycle.DecideGrantPolicy</c>:
/// contact and organization grants refused on Restricted, organization-wide grants refused on Secure or
/// Limited), and the Manage Access dialog hides the options that do not apply. Folding the flags into this
/// gate would hide the whole dialog on a Restricted record and take "+ User" and revoke with it. Task 139 keeps
/// this gate a pure delegation answer.</para>
///
/// <para>ADR-001 Minimal API · ADR-008 authorization by the group's endpoint filter · ADR-019 refusals are
/// ProblemDetails with a stable reason code and the trace id.</para>
/// </remarks>
public static class RecordAccessGateEndpoint
{
    /// <summary>The request named no record this handler can resolve.</summary>
    /// <remarks>
    /// Unreachable through the route — the filter resolves the same root first and denies an unresolvable
    /// one with 403. Checked anyway, on the sibling routes' discipline: a handler must not trust a pipeline
    /// it cannot see.
    /// </remarks>
    internal const string RecordUnresolvedReasonCode = "sdap.access.gate.record_unresolved";

    /// <summary>Registers the route on the external-access management group.</summary>
    public static RouteGroupBuilder MapRecordAccessGateEndpoint(this RouteGroupBuilder group)
    {
        group.MapGet("/can-manage-access", Handle)
            .WithName("GetCallerRecordAccessGate")
            .WithSummary("Whether the caller may change who can access this record")
            .WithDescription(
                "Answers the delegation question (spec FR-07 / owner decision B-14) for one record: 200 with " +
                "canManageAccess = true when the caller holds Write on it and the Share privilege on its table (owner round 89), 403 with a " +
                "sdap.access.deny.delegation_* reason code when they do not, or when it could not be " +
                "established. Clients gate the Manage Access affordance on this and MUST treat anything other " +
                "than 200 + canManageAccess = true as a denial. With includeOwner=true the 200 also names the " +
                "record's owning team, whether it is the Secure Record Owners team, and whether that team owns it inside " +
                "the Secure Record business unit (null = could not be told). For a work assignment or project the 200 also " +
                "lists followsParents — the matters / projects it is filed under directly (owner round 84: its access follows " +
                "them and is locked) — and parentUnverifiable when that could not be read.")
            .Produces<RecordAccessGateResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return group;
    }

    /// <summary>Handles <c>GET /api/v1/external-access/can-manage-access</c>.</summary>
    /// <returns>
    /// 200 with <c>canManageAccess = true</c> (and, with <c>includeOwner=true</c>, the record's owner facts). 400 only for
    /// a record the root resolver rejects. Every denial is the group filter's 403 and never reaches here.
    /// </returns>
    internal static async Task<IResult> Handle(
        [AsParameters] RecordAccessGateQuery query,
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        IGenericEntityService dataverse,
        ILogger<Program> logger,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var root = GrantExternalAccessEndpoint.ResolveExplicitRoot(query.RecordType, query.RecordId);
        if (!root.Ok)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Validation Error",
                detail: root.Error!,
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = httpContext.TraceIdentifier,
                    ["reasonCode"] = RecordUnresolvedReasonCode,
                });
        }

        // Reaching this line IS the answer: DelegationRuleFilter established Write and Share on this record, as the
        // caller, over OBO. Nothing here re-decides it.
        //
        // Task 175 (owner round 87): the floor the record's parents set — the ribbon's Remove Secure rule, the form's and
        // Manage Access's lowest offered Access Permission. Facts about the record (its own lookups, which the caller can
        // already read), not about the caller's rights; DIRECT parents only by name (task 174 F1-d: a grandparent's name is
        // never disclosed); the floor folds the whole chain.
        var parents = await ReadFollowsParentsAsync(dataverse, SecureRecordRoot.For(root.Type), root.Id, logger, ct);
        if (query.IncludeOwner != true)
        {
            return TypedResults.Ok(new RecordAccessGateResponse(root.Id, CanManageAccess: true)
            {
                FollowsParents = parents.Parents,
                ParentUnverifiable = parents.Unverifiable,
                FloorSecure = parents.FloorSecure,
                FloorAccessPermission = parents.FloorAccessPermission,
            });
        }

        // Round 46 item 4: who OWNS the record — asked only by the Access ribbon's secure-state rule, for a record flagged
        // secure. Facts about the record, never about the caller's rights; the delegation answer above is unchanged.
        var owner = await ReadOwnerAsync(
            dataverseClient, configuration, SecureRecordRoot.For(root.Type), root.Id, logger, httpContext.TraceIdentifier, ct);
        return TypedResults.Ok(new RecordAccessGateResponse(
            root.Id, CanManageAccess: true, owner.OwningTeamId, owner.OwnedBySecureOwnerTeam,
            owner.OwningTeamInSecureBusinessUnit)
        {
            FollowsParents = parents.Parents,
            ParentUnverifiable = parents.Unverifiable,
            FloorSecure = parents.FloorSecure,
            FloorAccessPermission = parents.FloorAccessPermission,
        });
    }

    /// <summary>
    /// Task 175: the record's DIRECT filing parents and the floor they set (owner round 87), through the ONE walk
    /// (<see cref="SecureRootInheritance.ReadSecureParentsAsync"/>, every level). A matter files under nothing; a parentless
    /// record has no floor (both <c>null</c>). Never throws: an unreadable filing answers <c>Unverifiable = true</c> with no
    /// parents and no floor, and the ribbon and Manage Access then fail closed.
    /// </summary>
    private static async Task<(IReadOnlyList<RecordAccessParent> Parents, bool Unverifiable, bool? FloorSecure, string? FloorAccessPermission)>
        ReadFollowsParentsAsync(IGenericEntityService dataverse, SecureRecordRoot root, Guid recordId, ILogger logger, CancellationToken ct)
    {
        if (!SecureRootInheritance.Inherits(root.LogicalName))
            return (Array.Empty<RecordAccessParent>(), false, null, null);

        try
        {
            var answer = await SecureRootInheritance.ReadSecureParentsAsync(
                dataverse, logger, root.LogicalName, recordId, ct, maxDepth: SecureRootInheritance.MaxFilingDepth);
            if (!answer.IsKnown)
            {
                logger.LogWarning("[ACCESS-GATE] What {RecordType} {RecordId} is filed under could not be read ({Why}).",
                    root.WireToken, recordId, answer.Unverifiable);
                return (Array.Empty<RecordAccessParent>(), true, null, null);
            }

            if (answer.DirectParents.Count == 0)
                return (Array.Empty<RecordAccessParent>(), false, null, null);

            var (floorSecure, floorRank) = SecureRootInheritance.FloorOf(answer);
            return (answer.DirectParents
                    .Select(p => new RecordAccessParent(SecureRootInheritance.WireTokenFor(p.Parent.Table), p.Parent.Id, p.Parent.Name))
                    .ToList(),
                false, floorSecure,
                floorRank == 2 ? EffectiveAccessPermission.Restricted
                : floorRank == 1 ? EffectiveAccessPermission.Limited
                : EffectiveAccessPermission.Standard);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "[ACCESS-GATE] What {RecordType} {RecordId} is filed under could not be read.", root.WireToken, recordId);
            return (Array.Empty<RecordAccessParent>(), true, null, null);
        }
    }

    /// <summary>
    /// Task 150 (round 46 item 4; round 53 item 2): the record's owning team, whether it is the Secure Record Owners team,
    /// and whether that team owns it inside the Secure Record business unit — the facts the Access ribbon needs to tell a
    /// PROVISIONED secure record (owned by that team, its own container recorded) from one whose transition did not finish
    /// (reassigned outside Spaarke to a team in another business unit, or a legacy one provisioned before task 133's owner
    /// move, still user-owned), and both from one owned by ANOTHER team inside the Secure Record business unit (the retired
    /// default team before task 144's migration: already isolated — provisioning refuses it 409
    /// <c>owned_by_other_secure_team</c>, and the ribbon hides Make Secure). App-only reads; the caller already passed the
    /// delegation filter (Write and Share on THIS record).
    /// </summary>
    /// <remarks>
    /// <para><b>Unknown is never an answer</b> (ADR-003). The record's owner could not be read, it was read with neither
    /// owner column, or which team is the Secure Record Owners team could not be told
    /// (<see cref="SecureRecordOwnerTeam.IdentifyAsync"/> refused: absent, ambiguous or unreadable) — each answers
    /// <c>OwnedBySecureOwnerTeam = null</c>, logged. A team owner read without its owning business unit answers
    /// <c>OwningTeamInSecureBusinessUnit = null</c>. The ribbon offers Make Secure on neither basis.</para>
    /// <para><b>Placement</b> (CLAUDE.md §10/§11): an extension of this route, opt-in, so the Manage Access gates' form-load
    /// path makes no extra read. Which team and business unit are the Secure Record ones is decided by the ONE rule
    /// provisioning uses (<see cref="SecureRecordOwnerTeam"/>, its first two steps), and "inside" by the one predicate its
    /// <c>owned_by_other_secure_team</c> refusal uses (<see cref="SecureRecordOwnerTeam.IsInSecureBusinessUnit"/>) — never
    /// re-implemented here.</para>
    /// </remarks>
    internal static async Task<RecordOwnerFacts> ReadOwnerAsync(
        DataverseWebApiClient dataverseClient,
        IConfiguration configuration,
        SecureRecordRoot root,
        Guid recordId,
        ILogger logger,
        string traceId,
        CancellationToken ct)
    {
        OwnerRow? row;
        try
        {
            var rows = await dataverseClient.QueryAsync<OwnerRow>(
                root.EntitySet,
                filter: $"{root.IdColumn} eq {recordId}",
                select: OwnerSelect(root),
                top: 1,
                cancellationToken: ct);
            row = rows.FirstOrDefault();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex,
                "[ACCESS-GATE] The owner of {RecordType} {RecordId} could not be read; reporting it as unknown. " +
                "TraceId={TraceId}", root.WireToken, recordId, traceId);
            return RecordOwnerFacts.Unknown;
        }

        if (row?._owninguser_value is { } user && user != Guid.Empty)
            return new RecordOwnerFacts(OwningTeamId: null, OwnedBySecureOwnerTeam: false, OwningTeamInSecureBusinessUnit: null);

        if (row?._owningteam_value is not { } team || team == Guid.Empty)
        {
            logger.LogWarning(
                "[ACCESS-GATE] {RecordType} {RecordId} was read with neither an owning user nor an owning team " +
                "(found: {Found}); reporting its owner as unknown. TraceId={TraceId}",
                root.WireToken, recordId, row is not null, traceId);
            return RecordOwnerFacts.Unknown;
        }

        var secureTeam = await SecureRecordOwnerTeam.IdentifyAsync(dataverseClient, configuration, ct);
        if (!secureTeam.IsIdentified)
        {
            logger.LogWarning(secureTeam.Fault,
                "[ACCESS-GATE] {RecordType} {RecordId} is owned by team {TeamId}, but which team is the Secure Record " +
                "Owners team could not be told ({Refusal}); reporting whether it is as unknown. TraceId={TraceId}",
                root.WireToken, recordId, team, secureTeam.Refusal, traceId);
            return new RecordOwnerFacts(OwningTeamId: team, OwnedBySecureOwnerTeam: null, OwningTeamInSecureBusinessUnit: null);
        }

        // Round 53 item 2: inside the Secure Record business unit — the ONE predicate provisioning's 409
        // owned_by_other_secure_team uses — tells another team there (already isolated) from a team outside it.
        var inSecureBusinessUnit =
            SecureRecordOwnerTeam.IsInSecureBusinessUnit(row._owningbusinessunit_value, secureTeam.BusinessUnitId!.Value);
        if (inSecureBusinessUnit is null)
        {
            logger.LogWarning(
                "[ACCESS-GATE] {RecordType} {RecordId} is owned by team {TeamId} but was read without its owning business " +
                "unit; reporting whether that is the Secure Record business unit as unknown. TraceId={TraceId}",
                root.WireToken, recordId, team, traceId);
        }

        return new RecordOwnerFacts(
            OwningTeamId: team,
            OwnedBySecureOwnerTeam: team == secureTeam.OwnerTeamId,
            OwningTeamInSecureBusinessUnit: inSecureBusinessUnit);
    }

    /// <summary>
    /// The one read <see cref="ReadOwnerAsync"/> makes of the record: its id, both owner columns and its owning business unit.
    /// </summary>
    internal static string OwnerSelect(SecureRecordRoot root) =>
        $"{root.IdColumn},_owningteam_value,_owninguser_value,_owningbusinessunit_value";

    /// <summary>The record's owner, as <see cref="RecordAccessGateResponse"/> reports it.</summary>
    internal sealed record RecordOwnerFacts(Guid? OwningTeamId, bool? OwnedBySecureOwnerTeam, bool? OwningTeamInSecureBusinessUnit)
    {
        /// <summary>The owner could not be told.</summary>
        public static readonly RecordOwnerFacts Unknown = new(null, null, null);
    }

    /// <summary>The owner columns of a secure root (the same JSON names provisioning's root read uses).</summary>
    internal sealed class OwnerRow
    {
        [JsonPropertyName("_owningteam_value")]
        public Guid? _owningteam_value { get; set; }

        [JsonPropertyName("_owninguser_value")]
        public Guid? _owninguser_value { get; set; }

        /// <summary>The business unit the record's owner places it in (round 53 item 2).</summary>
        [JsonPropertyName("_owningbusinessunit_value")]
        public Guid? _owningbusinessunit_value { get; set; }
    }
}
