// spaarkeai-word-add-in-r1 task 080 — record ownership assignment.
//
// Owner decision 2026-09-22: "the records should be owned by the acting user's BU default owner team.
// So if testuser1 is in spaarke business unit 1 team/business unit, then the record they create is
// assigned to that team (not the user)."
//
// Component Justification (CLAUDE.md §11):
//   (1) Existing — nothing resolves "business unit → default owner team". The two neighbours both do
//       something else: UserOrgContextReader (Services/Ai/Context/) reads BU and team NAMES to bind AI
//       prompt context, and CommunicationEnrichmentService.ResolveTeamIdByNameAsync (:738) resolves a team
//       by NAME from an email-category routing gate. Neither answers "which team owns what this caller
//       creates".
//   (2) Extension — No. UserOrgContextReader is ADR-013 in-zone AI code; having CRUD create paths inject it
//       would be precisely the CRUD→AI dependency root CLAUDE.md §10 bullet 3 forbids. The Communication
//       resolver keys off an email category, not the caller's business unit, so it cannot be generalized
//       without changing what it means.
//   (3) Cost-of-doing-nothing — every record the BFF creates app-only defaults to the calling application
//       user, which lives in the ROOT business unit: measured 2026-09-22, ALL 512 sprk_document rows sit in
//       root. Users sit in child BUs and Dataverse Deep depth traverses DOWNWARD, so they reach none of
//       them. Concretely: task 063's Run Index and task 064's create-To-Do both return 403 for every
//       ordinary user, and every future per-record gate on a BFF-created entity fails the same way.
//
// Placement Justification (bff-extensions.md): lives in Services/Dataverse/ beside CoreAncestorResolver —
// it is a Dataverse lookup with no AI concern. It deliberately does NOT live in Spaarke.Dataverse: that
// shared library must not depend on BFF services, so create paths there receive an already-resolved team id
// as a parameter (mirroring RecordCreationRequest.OwnerSystemUserId, the shipped precedent).

using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Resolves the team that should own a record the given caller creates: the <b>default owner team</b> of the
/// caller's business unit.
/// </summary>
/// <remarks>
/// <para>
/// Setting <c>ownerid</c> to that team makes <c>owningbusinessunit</c> <b>derive</b> to the caller's BU —
/// <c>owningbusinessunit</c> is never assigned directly. This is the shape <c>sprk_matter</c> rows already
/// have in every environment checked, which is why it is the target rather than an invention.
/// </para>
/// <para>
/// <b>Fail-closed.</b> An unresolvable team returns <c>null</c> and callers MUST refuse the create. Falling
/// back to app-only ownership is exactly the defect this type exists to remove, and a silent fallback would
/// reintroduce it invisibly — a record that looks created but is unreachable by the person who created it.
/// </para>
/// </remarks>
public interface IRecordOwnershipResolver
{
    /// <summary>
    /// Resolves the default owner team of <paramref name="callerSystemUserId"/>'s business unit.
    /// Returns <c>null</c> when the user, the business unit, or the team cannot be resolved.
    /// </summary>
    Task<Guid?> ResolveOwningTeamAsync(Guid callerSystemUserId, CancellationToken ct);

    /// <summary>
    /// Same resolution keyed by the caller's <b>Entra object id</b> instead of their Dataverse systemuserid.
    /// </summary>
    /// <remarks>
    /// Background paths have an OID and nothing else: <c>UploadFinalizationWorker</c> runs from a queue
    /// message carrying <c>payload.UserId</c> (an OID), with no <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// to hand to <c>ICallerSystemUserResolver</c>. Rather than fabricate a principal at each such call site,
    /// this overload does the same two-query chain from the other end — the cross-reference is
    /// <c>systemuser.azureactivedirectoryobjectid</c> (ADR-028), the same key that resolver uses.
    /// </remarks>
    Task<Guid?> ResolveOwningTeamForObjectIdAsync(Guid callerObjectId, CancellationToken ct);
}

/// <inheritdoc cref="IRecordOwnershipResolver" />
public sealed class RecordOwnershipResolver : IRecordOwnershipResolver
{
    private const string SystemUserEntity = "systemuser";
    private const string TeamEntity = "team";
    private const string BusinessUnitColumn = "businessunitid";

    /// <summary>
    /// Owner-team teamtype. Dataverse defines 0 = Owner, 1 = Access. BOTH this and <c>isdefault</c> are
    /// required: dev contains non-default Owner teams (auto-created, GUID-shaped names) AND Access teams,
    /// so filtering on either predicate alone selects the wrong team.
    /// </summary>
    private const int OwnerTeamType = 0;

    private readonly IDataverseService _dataverse;
    private readonly ILogger<RecordOwnershipResolver> _logger;

    public RecordOwnershipResolver(IDataverseService dataverse, ILogger<RecordOwnershipResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<Guid?> ResolveOwningTeamAsync(Guid callerSystemUserId, CancellationToken ct)
        => ResolveCoreAsync("systemuserid", callerSystemUserId, ct);

    /// <inheritdoc />
    public Task<Guid?> ResolveOwningTeamForObjectIdAsync(Guid callerObjectId, CancellationToken ct)
        => ResolveCoreAsync("azureactivedirectoryobjectid", callerObjectId, ct);

    /// <summary>
    /// The shared two-query chain. <paramref name="userKeyColumn"/> selects which identity the caller is
    /// known by — the Dataverse systemuserid, or the Entra object id cross-reference (ADR-028).
    /// </summary>
    private async Task<Guid?> ResolveCoreAsync(string userKeyColumn, Guid userKey, CancellationToken ct)
    {
        if (userKey == Guid.Empty)
        {
            _logger.LogWarning(
                "Cannot resolve an owning team: no caller identity was supplied ({UserKeyColumn}). The record "
                + "must be refused rather than created app-owned (task 080).",
                userKeyColumn);
            return null;
        }

        try
        {
            // 1. The caller's business unit.
            var userQuery = new QueryExpression(SystemUserEntity)
            {
                ColumnSet = new ColumnSet(BusinessUnitColumn),
                TopCount = 1,
                NoLock = true
            };
            userQuery.Criteria.AddCondition(userKeyColumn, ConditionOperator.Equal, userKey);

            var users = await _dataverse.RetrieveMultipleAsync(userQuery, ct).ConfigureAwait(false);
            var businessUnitId = users.Entities.FirstOrDefault()
                ?.GetAttributeValue<EntityReference>(BusinessUnitColumn)?.Id;

            if (businessUnitId is null || businessUnitId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Cannot resolve an owning team: no systemuser with {UserKeyColumn}={UserKey}, or that user "
                    + "has no business unit.",
                    userKeyColumn, userKey);
                return null;
            }

            // 2. That business unit's DEFAULT OWNER team. Both predicates are load-bearing — see OwnerTeamType.
            var teamQuery = new QueryExpression(TeamEntity)
            {
                ColumnSet = new ColumnSet("teamid"),
                TopCount = 1,
                NoLock = true
            };
            teamQuery.Criteria.AddCondition(BusinessUnitColumn, ConditionOperator.Equal, businessUnitId.Value);
            teamQuery.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
            teamQuery.Criteria.AddCondition("teamtype", ConditionOperator.Equal, OwnerTeamType);

            var teams = await _dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false);
            var teamId = teams.Entities.FirstOrDefault()?.Id;

            if (teamId is null || teamId == Guid.Empty)
            {
                _logger.LogWarning(
                    "Cannot resolve an owning team: business unit {BusinessUnitId} has no default owner team "
                    + "(isdefault = true AND teamtype = {OwnerTeamType}).",
                    businessUnitId, OwnerTeamType);
                return null;
            }

            _logger.LogDebug(
                "Resolved owning team {TeamId} for caller {UserKeyColumn}={UserKey} (business unit {BusinessUnitId}).",
                teamId, userKeyColumn, userKey, businessUnitId);

            return teamId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail-closed, never fail-open-to-app-ownership: an unresolved team is a refusal upstream.
            _logger.LogWarning(ex,
                "Owning-team lookup failed for caller {UserKeyColumn}={UserKey}; the record must be refused "
                + "rather than created app-owned.",
                userKeyColumn, userKey);
            return null;
        }
    }
}
