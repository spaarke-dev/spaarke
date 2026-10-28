using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// UAC task 194b R4b-2 (owner round 131, the 086 ruling): the owner team of the HARD-DENY tables' ordinary rows
/// (<see cref="Services.Dataverse.RecordOwnershipResolver.IsRestrictedContentTable"/>). Where the ordinary answer for such a row
/// would be a business unit's default team, the row goes to this team instead: a NAMED owner team in the ROOT business unit
/// with NO members, whose own role grants Read only, at Basic, on exactly those tables — so no business-unit depth reaches
/// the rows. Secure rows still go to the Secure Record owner team.
/// </summary>
/// <remarks>
/// <para>Identified like the Secure Record owner team (<see cref="SecureRecordOwnerTeam.IdentifyAsync"/>): by its configured
/// name, an Owner team (<c>teamtype = 0</c>), NOT a default team, exactly one match. It must sit in the root business unit
/// (<c>parentbusinessunitid</c> null) and have no member of any kind.</para>
/// <para>FAIL CLOSED: a team that is missing, ambiguous, outside the root business unit or with any member is a refusal, never
/// a fallback to the default team. A Dataverse fault propagates (the caller's retryable 5xx), as the resolver's other reads
/// do. The answer is per pass when the caller hands a <see cref="Services.Dataverse.RecordOwnerLookupCache"/>.</para>
/// </remarks>
public static class RestrictedContentOwnerTeam
{
    /// <summary>Configuration key for the team's name.</summary>
    public const string OwnerTeamNameConfigKey = "RestrictedContent:OwnerTeamName";

    /// <summary>The team's name when <see cref="OwnerTeamNameConfigKey"/> is not set.</summary>
    public const string DefaultOwnerTeamName = "Spaarke Restricted Content Owners";

    /// <summary>How many members the check reads at most (it refuses on the first; the count is for the report).</summary>
    internal const int MemberReadLimit = 5;

    /// <summary>The configured team name, else <see cref="DefaultOwnerTeamName"/>.</summary>
    public static string OwnerTeamName(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return configuration[OwnerTeamNameConfigKey] is { } configured && !string.IsNullOrWhiteSpace(configured)
            ? configured.Trim()
            : DefaultOwnerTeamName;
    }

    /// <summary>
    /// WHICH team owns the hard-deny tables' ordinary rows, and whether it may: the root business unit (exactly one), the named
    /// non-default Owner team (exactly one), in that business unit, with no member. Never throws for an answer; a Dataverse
    /// fault propagates.
    /// </summary>
    public static async Task<RestrictedContentOwnerTeamIdentity> IdentifyAsync(
        IGenericEntityService dataverse, IConfiguration configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        var teamName = OwnerTeamName(configuration);

        // 1. The root business unit: the one with no parent. TOP 2, so a second root is a state we refuse, not a pick.
        var rootQuery = new QueryExpression("businessunit") { ColumnSet = new ColumnSet("businessunitid"), TopCount = 2, NoLock = true };
        rootQuery.Criteria.AddCondition("parentbusinessunitid", ConditionOperator.Null);
        var roots = (await dataverse.RetrieveMultipleAsync(rootQuery, ct).ConfigureAwait(false)).Entities;
        if (roots.Count != 1 || roots[0].Id == Guid.Empty)
        {
            return new(roots.Count > 1
                ? RestrictedContentOwnerTeamStatus.RootBusinessUnitAmbiguous
                : RestrictedContentOwnerTeamStatus.RootBusinessUnitNotFound, null, null, 0);
        }

        var rootBu = roots[0].Id;

        // 2. The NAMED owner team, wherever it is, so a team outside the root business unit is told apart from a missing one.
        var teamQuery = new QueryExpression(SecureRecordOwnerTeam.TeamEntity)
        {
            ColumnSet = new ColumnSet("teamid", SecureRecordOwnerTeam.BusinessUnitColumn),
            TopCount = 2,
            NoLock = true,
        };
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.NameColumn, ConditionOperator.Equal, teamName);
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.TeamTypeColumn, ConditionOperator.Equal, SecureRecordOwnerTeam.OwnerTeamType);
        teamQuery.Criteria.AddCondition(SecureRecordOwnerTeam.IsDefaultColumn, ConditionOperator.Equal, false);
        var teams = (await dataverse.RetrieveMultipleAsync(teamQuery, ct).ConfigureAwait(false)).Entities;
        if (teams.Count == 0 || teams[0].Id == Guid.Empty)
            return new(RestrictedContentOwnerTeamStatus.OwnerTeamNotFound, null, rootBu, 0);
        if (teams.Count > 1)
            return new(RestrictedContentOwnerTeamStatus.OwnerTeamAmbiguous, null, rootBu, 0);

        var teamId = teams[0].Id;
        var teamBu = teams[0].GetAttributeValue<Microsoft.Xrm.Sdk.EntityReference>(SecureRecordOwnerTeam.BusinessUnitColumn)?.Id;
        if (teamBu != rootBu)
            return new(RestrictedContentOwnerTeamStatus.OwnerTeamNotInRootBusinessUnit, teamId, rootBu, 0);

        // 3. No member of any kind (human or application user). The membership rows themselves, never a cached count.
        var memberQuery = new QueryExpression("teammembership")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            TopCount = MemberReadLimit,
            NoLock = true,
        };
        memberQuery.Criteria.AddCondition("teamid", ConditionOperator.Equal, teamId);
        var members = (await dataverse.RetrieveMultipleAsync(memberQuery, ct).ConfigureAwait(false)).Entities.Count;
        return members > 0
            ? new(RestrictedContentOwnerTeamStatus.OwnerTeamHasMembers, teamId, rootBu, members)
            : new(RestrictedContentOwnerTeamStatus.Resolved, teamId, rootBu, 0);
    }
}

/// <summary>Why <see cref="RestrictedContentOwnerTeam.IdentifyAsync"/> did or did not identify a usable team.</summary>
public enum RestrictedContentOwnerTeamStatus
{
    /// <summary>One root business unit, one named non-default Owner team in it, no member. The only usable outcome.</summary>
    Resolved,

    /// <summary>No business unit without a parent was found.</summary>
    RootBusinessUnitNotFound,

    /// <summary>More than one business unit has no parent.</summary>
    RootBusinessUnitAmbiguous,

    /// <summary>No non-default Owner team has the configured name.</summary>
    OwnerTeamNotFound,

    /// <summary>More than one does — refused rather than choosing one.</summary>
    OwnerTeamAmbiguous,

    /// <summary>The named team is not in the root business unit.</summary>
    OwnerTeamNotInRootBusinessUnit,

    /// <summary>The named team has at least one member.</summary>
    OwnerTeamHasMembers,
}

/// <summary>The outcome of <see cref="RestrictedContentOwnerTeam.IdentifyAsync"/>: the status, the team and root business unit
/// when found, and how many members were read (at most <see cref="RestrictedContentOwnerTeam.MemberReadLimit"/>; the member
/// check a reconcile report carries).</summary>
public sealed record RestrictedContentOwnerTeamIdentity(
    RestrictedContentOwnerTeamStatus Status, Guid? OwnerTeamId, Guid? RootBusinessUnitId, int MembersFound)
{
    /// <summary>True only for <see cref="RestrictedContentOwnerTeamStatus.Resolved"/>: the only answer that may be assigned.</summary>
    public bool IsResolved => Status == RestrictedContentOwnerTeamStatus.Resolved && OwnerTeamId is { } id && id != Guid.Empty;
}
