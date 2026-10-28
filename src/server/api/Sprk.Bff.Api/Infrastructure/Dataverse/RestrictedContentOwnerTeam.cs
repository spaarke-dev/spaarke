using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// UAC task 194b R4b-2 (owner round 131, the 086 ruling) and R4b-2c (owner round 136): the owner team of the HARD-DENY tables'
/// ordinary rows (<see cref="Services.Dataverse.RecordOwnershipResolver.IsRestrictedContentTable"/>). Where the ordinary answer
/// for such a row would be a business unit's default team, the row goes instead to a NAMED owner team with NO members in the
/// CUSTOMER's top business unit — the direct child of the root that the answer's business unit sits under (provisioning H10's
/// <c>customerBusinessUnitId</c>, a sibling of Secure Record). Its own role grants Read only, at Basic, on exactly those tables,
/// so no business-unit depth reaches the rows. Secure rows still go to the Secure Record owner team.
/// </summary>
/// <remarks>
/// <para><b>Derived from the answer (owner round 136).</b> The Spaarke root business unit holds only Spaarke product data;
/// customer records belong to the customer's own top business unit. So the team is found from the answer itself: from the
/// default team's business unit, the parents are walked up to the direct child of the root, and the team is the one in THAT
/// unit. Several customer units (and dev's sibling test units) each have their own.</para>
/// <para>Identified like the Secure Record owner team (<see cref="SecureRecordOwnerTeam.IdentifyAsync"/>): by its configured
/// name, an Owner team (<c>teamtype = 0</c>), NOT a default team, exactly one match in that unit, with no member of any
/// kind.</para>
/// <para>FAIL CLOSED: an answer in the root itself (no customer unit), a customer unit that is the Secure Record unit, a
/// parent chain that cannot be read or loops, and a team that is missing, ambiguous or has a member are each a refusal, never
/// a fallback to the default team. A Dataverse fault propagates (the caller's retryable 5xx), as the resolver's other reads do.
/// The walk and the team are kept for the pass when the caller hands a <see cref="Services.Dataverse.RecordOwnerLookupCache"/>.</para>
/// </remarks>
public static class RestrictedContentOwnerTeam
{
    /// <summary>Configuration key for the team's name.</summary>
    public const string OwnerTeamNameConfigKey = "RestrictedContent:OwnerTeamName";

    /// <summary>The team's name when <see cref="OwnerTeamNameConfigKey"/> is not set.</summary>
    public const string DefaultOwnerTeamName = "Spaarke Restricted Content Owners";

    /// <summary>How many members the check reads at most (it refuses on the first; the count is for the report).</summary>
    internal const int MemberReadLimit = 5;

    /// <summary>How many parents the walk reads at most before it calls the chain a loop (Dataverse allows far fewer levels).</summary>
    internal const int MaxParentWalk = 32;

    /// <summary>The configured team name, else <see cref="DefaultOwnerTeamName"/>.</summary>
    public static string OwnerTeamName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[OwnerTeamNameConfigKey] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : DefaultOwnerTeamName;
    }

    /// <summary>
    /// The customer's top business unit above <paramref name="businessUnitId"/>: the direct child of the root it sits under
    /// (itself when it is one). Never throws for an answer; a Dataverse fault propagates.
    /// </summary>
    public static async Task<(Guid? TopBusinessUnitId, RestrictedContentOwnerTeamStatus Status)> CustomerTopBusinessUnitAsync(
        IGenericEntityService dataverse, Guid businessUnitId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        var visited = new HashSet<Guid>();
        Guid? below = null;
        var current = businessUnitId;
        for (var step = 0; step <= MaxParentWalk; step++)
        {
            if (current == Guid.Empty || !visited.Add(current))
                return (null, RestrictedContentOwnerTeamStatus.ParentChainLoops);

            var query = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("parentbusinessunitid"), TopCount = 1, NoLock = true };
            query.Criteria.AddCondition("businessunitid", ConditionOperator.Equal, current);
            var row = (await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false)).Entities.FirstOrDefault();
            if (row is null)
                return (null, RestrictedContentOwnerTeamStatus.ParentChainUnreadable);

            var parent = row.GetAttributeValue<EntityReference>("parentbusinessunitid")?.Id;
            if (parent is null || parent == Guid.Empty)
            {
                // `current` is the root: the unit below it on the walk is the customer's top unit — or there is none, when the
                // answer's own unit is the root (customer records never go there).
                return below is { } top
                    ? (top, RestrictedContentOwnerTeamStatus.Resolved)
                    : (null, RestrictedContentOwnerTeamStatus.AnswerIsRootBusinessUnit);
            }

            below = current;
            current = parent.Value;
        }

        return (null, RestrictedContentOwnerTeamStatus.ParentChainLoops);
    }

    /// <summary>
    /// WHICH team owns the ordinary hard-deny rows of the customer top business unit <paramref name="topBusinessUnitId"/>, and
    /// whether it may: not the Secure Record unit, the named non-default Owner team (exactly one) in it, with no member. Never
    /// throws for an answer; a Dataverse fault propagates.
    /// </summary>
    public static async Task<RestrictedContentOwnerTeamIdentity> IdentifyAsync(
        IGenericEntityService dataverse, IConfiguration configuration, Guid topBusinessUnitId, Guid? secureBusinessUnitId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        if (secureBusinessUnitId is { } secure && secure == topBusinessUnitId)
            return new(RestrictedContentOwnerTeamStatus.CustomerBusinessUnitIsSecureRecord, null, topBusinessUnitId, 0);

        var teamQuery = new QueryExpression(SecureRecordOwnerTeam.TeamEntity)
        {
            ColumnSet = new ColumnSet("teamid"),
            TopCount = 2,
            NoLock = true,
        };
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.NameColumn, ConditionOperator.Equal, OwnerTeamName(configuration));
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.TeamTypeColumn, ConditionOperator.Equal, SecureRecordOwnerTeam.OwnerTeamType);
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.IsDefaultColumn, ConditionOperator.Equal, false);
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.BusinessUnitColumn, ConditionOperator.Equal, topBusinessUnitId);
        var teams = (await dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false)).Entities;
        if (teams.Count == 0 || teams[0].Id == Guid.Empty)
            return new(RestrictedContentOwnerTeamStatus.OwnerTeamNotFound, null, topBusinessUnitId, 0);
        if (teams.Count > 1)
            return new(RestrictedContentOwnerTeamStatus.OwnerTeamAmbiguous, null, topBusinessUnitId, 0);

        // No member of any kind (human or application user). The membership rows themselves, never a cached count.
        var teamId = teams[0].Id;
        var memberQuery = new QueryExpression("teammembership")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            TopCount = MemberReadLimit,
            NoLock = true,
        };
        memberQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        var members = (await dataverse.RetrieveMultipleAsync(memberQuery, ct).ConfigureAwait(false)).Entities.Count;
        return members > 0
            ? new(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers, teamId, topBusinessUnitId, members)
            : new(RestrictedContentOwnerTeamStatus.Resolved, teamId, topBusinessUnitId, 0);
    }
}

/// <summary>Why the restricted-content owner team was or was not identified for an answer.</summary>
public enum RestrictedContentOwnerTeamStatus
{
    /// <summary>A customer top business unit, one named non-default Owner team in it, no member. The only usable outcome.</summary>
    Resolved,

    /// <summary>The answer's business unit is the root itself: there is no customer business unit (owner round 136).</summary>
    AnswerIsRootBusinessUnit,

    /// <summary>The answer's top business unit is the Secure Record unit.</summary>
    CustomerBusinessUnitIsSecureRecord,

    /// <summary>The Secure Record business unit name matches more than one unit, so the top unit cannot be classified.</summary>
    SecureBusinessUnitAmbiguous,

    /// <summary>A business unit on the walk up to the root could not be read (it is gone).</summary>
    ParentChainUnreadable,

    /// <summary>The walk up to the root met a unit twice, or did not reach the root within the bound.</summary>
    ParentChainLoops,

    /// <summary>No non-default Owner team has the configured name in the customer's top business unit.</summary>
    OwnerTeamNotFound,

    /// <summary>More than one does — refused rather than choosing one.</summary>
    OwnerTeamAmbiguous,

    /// <summary>The named team has at least one member.</summary>
    OwnerTeamHasMembers,
}

/// <summary>The outcome of identifying the restricted-content owner team for one customer top business unit: the status, the
/// team when found, the customer top business unit (when the walk reached one), and how many members were read (at most
/// <see cref="RestrictedContentOwnerTeam.MemberReadLimit"/>; the member check a reconcile report carries).</summary>
public sealed record RestrictedContentOwnerTeamIdentity(
    RestrictedContentOwnerTeamStatus Status, Guid? OwnerTeamId, Guid? CustomerBusinessUnitId, int MembersFound)
{
    /// <summary>True only for <see cref="RestrictedContentOwnerTeamStatus.Resolved"/>: the only answer that may be assigned.</summary>
    public bool IsResolved => Status == RestrictedContentOwnerTeamStatus.Resolved && OwnerTeamId is { } id && id != Guid.Empty;
}
