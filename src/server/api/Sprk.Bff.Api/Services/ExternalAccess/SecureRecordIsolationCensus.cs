using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Contracts.Provisioning;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Services.ExternalAccess;

/// <summary>
/// The secure-record isolation census: ONE read of the directory, graded by <see cref="SecureBuRoleDepthAssertion"/>.
/// Two runners call it — the 15-minute <see cref="SecureRecordIsolationCensusJob"/> and the provisioning acceptance route
/// <c>POST /api/platform/secure-record-isolation-census</c> (task 260, ISS-014) — so the job and H13 cannot disagree about
/// what "isolated" means.
/// </summary>
/// <remarks>
/// <para><b>Moved out of the job by task 260.</b> The read, the paging guard and the status mapping were private to the
/// job; the acceptance route needs exactly the same answer, synchronously. A static class, not a registered service:
/// both runners already hold the Dataverse seam (<see cref="IGenericEntityService"/>) and configuration, so no DI
/// registration or interface is added (ADR-010).</para>
/// <para><b>Read-only.</b> Nine paged <c>RetrieveMultiple</c> reads; it writes nothing. Every read failure throws — a
/// census that could not be read is never graded, and a truncated one never reports isolation.</para>
/// </remarks>
public static class SecureRecordIsolationCensus
{
    /// <summary>Rows per page of each census read.</summary>
    internal const int PageSize = 5000;

    /// <summary>
    /// Page ceiling per read — 100,000 rows. Past it the census is INCOMPLETE and the read throws: a census that stopped
    /// early could omit the one principal that reaches the secure BU and report isolation.
    /// </summary>
    internal const int MaxPages = 20;

    /// <summary>Reads the directory and grades it. Throws when any read fails or is incomplete.</summary>
    public static async Task<SecureBuAssertionOutcome> EvaluateAsync(
        IGenericEntityService dataverse, IConfiguration configuration, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(configuration);

        var census = await ReadCensusAsync(dataverse, configuration, ct).ConfigureAwait(false);
        return SecureBuRoleDepthAssertion.Evaluate(census);
    }

    /// <summary>The status of a graded census: isolated, inert (no secure BU), or findings. Never <c>error</c>.</summary>
    public static string StatusOf(SecureBuAssertionOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Passed
            ? KeylessProofContract.SecureRecordIsolationCensus.Isolated
            : outcome.IsInert
                ? KeylessProofContract.SecureRecordIsolationCensus.Inert
                : KeylessProofContract.SecureRecordIsolationCensus.Findings;
    }

    /// <summary>The result both runners publish: status, headline verdict, and each finding's verdict and message.</summary>
    public static SecureRecordIsolationCensusResult ToResult(SecureBuAssertionOutcome outcome) =>
        new(StatusOf(outcome), outcome.Verdict.ToString(),
            outcome.Findings.Select(f => new SecureRecordIsolationCensusFinding(f.Verdict.ToString(), f.Message)).ToArray());

    // =====================================================================================================
    // Census reader — SDK, paged, read-only. Every failure throws; nothing degrades to "empty".
    // =====================================================================================================

    /// <summary>Reads the directory and composes the census through the shared builder.</summary>
    private static async Task<SecureBuRoleDepthCensus> ReadCensusAsync(
        IGenericEntityService dataverse, IConfiguration configuration, CancellationToken ct)
    {
        var secureBuName = SecureRecordOwnerTeam.BusinessUnitName(configuration);
        var ownerTeamName = SecureRecordOwnerTeam.OwnerTeamName(configuration);

        var businessUnits = (await RetrieveAllAsync(dataverse,
                Query("businessunit", "businessunitid", "name", "parentbusinessunitid"), ct))
            .Select(row => new BusinessUnitNode(
                row.Id,
                row.GetAttributeValue<string>("name") ?? string.Empty,
                OptionalGuid(row, "parentbusinessunitid")))
            .ToArray();

        // The guarded root-table Reads (clause 1) plus every privilege of the codified owner-role set (clause 5, task
        // 145) — read from the ONE list compiled into this assembly.
        var ownerRoleSet = SecureRecordOwnerRoleSet.Embedded;
        var privilegeNames = SecureBuRoleDepthCensusBuilder.CensusPrivilegeNames(ownerRoleSet);

        var privilegeQuery = Query("privilege", "privilegeid", "name");
        privilegeQuery.Criteria.AddCondition("name", ConditionOperator.In, privilegeNames.Cast<object>().ToArray());
        var privileges = (await RetrieveAllAsync(dataverse, privilegeQuery, ct))
            .ToDictionary(row => row.Id, row => row.GetAttributeValue<string>("name") ?? string.Empty);

        SecureBuRoleDepthCensusBuilder.RequireGuardedPrivilegesResolved(privileges.Values.ToArray());

        var depthQuery = Query("roleprivileges", "roleid", "privilegeid", "privilegedepthmask");
        depthQuery.Criteria.AddCondition("privilegeid", ConditionOperator.In, privileges.Keys.Cast<object>().ToArray());
        var depthByRootRole = new Dictionary<(Guid RootRoleId, string Privilege), PrivilegeDepth>();
        foreach (var row in await RetrieveAllAsync(dataverse, depthQuery, ct))
        {
            depthByRootRole[(RequiredGuid(row, "roleid"), privileges[RequiredGuid(row, "privilegeid")])] =
                (PrivilegeDepth)row.GetAttributeValue<int>("privilegedepthmask");
        }

        var roles = (await RetrieveAllAsync(dataverse, Query("role", "roleid", "name", "businessunitid", "parentrootroleid"), ct))
            .Select(row => new CensusRole(
                row.Id,
                row.GetAttributeValue<string>("name") ?? string.Empty,
                RequiredGuid(row, "businessunitid"),
                OptionalGuid(row, "parentrootroleid") ?? row.Id))
            .ToArray();

        var userRoles = Group(await RetrieveAllAsync(dataverse, Query("systemuserroles", "systemuserid", "roleid"), ct),
            "systemuserid", "roleid");

        var users = (await RetrieveAllAsync(dataverse,
                Query("systemuser", "systemuserid", "fullname", "domainname", "accessmode", "applicationid",
                    "isdisabled", "businessunitid"), ct))
            .Select(row =>
            {
                var name = row.GetAttributeValue<string>("fullname") ?? string.Empty;
                var isHuman = SecureBuRoleDepthCensusBuilder.IsHumanPrincipal(
                    hasApplicationId: OptionalGuid(row, "applicationid") is { } appId && appId != Guid.Empty,
                    isDisabled: row.GetAttributeValue<bool>("isdisabled"),
                    fullName: name,
                    accessMode: row.GetAttributeValue<OptionSetValue>("accessmode")?.Value ?? 0);

                return new CensusUser(
                    row.Id, name, row.GetAttributeValue<string>("domainname"), isHuman,
                    OptionalGuid(row, "businessunitid"),
                    userRoles.TryGetValue(row.Id, out var ids) ? ids : Array.Empty<Guid>());
            })
            .ToArray();

        var teamRoles = Group(await RetrieveAllAsync(dataverse, Query("teamroles", "teamid", "roleid"), ct), "teamid", "roleid");
        var teamMembers = Group(await RetrieveAllAsync(dataverse, Query("teammembership", "teamid", "systemuserid"), ct),
            "teamid", "systemuserid");

        var teams = (await RetrieveAllAsync(dataverse,
                Query("team", "teamid", "name", "isdefault", "teamtype", "businessunitid"), ct))
            .Select(row => new CensusTeam(
                row.Id,
                row.GetAttributeValue<string>("name") ?? string.Empty,
                RequiredGuid(row, "businessunitid"),
                row.GetAttributeValue<bool>("isdefault"),
                row.GetAttributeValue<OptionSetValue>("teamtype")?.Value ?? -1,
                teamRoles.TryGetValue(row.Id, out var roleIds) ? roleIds : Array.Empty<Guid>(),
                teamMembers.TryGetValue(row.Id, out var memberIds) ? memberIds : Array.Empty<Guid>()))
            .ToArray();

        return SecureBuRoleDepthCensusBuilder.Build(
            secureBuName, ownerTeamName, businessUnits, depthByRootRole, roles, users, teams, ownerRoleSet,
            privileges.Values.ToArray());
    }

    private static QueryExpression Query(string entity, params string[] columns) =>
        new(entity) { ColumnSet = new ColumnSet(columns), NoLock = true };

    /// <summary>Every page of a query, or a throw — a truncated census is never graded.</summary>
    private static async Task<List<Entity>> RetrieveAllAsync(
        IGenericEntityService dataverse, QueryExpression query, CancellationToken ct)
    {
        var rows = new List<Entity>();
        query.PageInfo = new PagingInfo { Count = PageSize, PageNumber = 1 };

        for (var page = 1; page <= MaxPages; page++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
            rows.AddRange(result.Entities);

            if (!result.MoreRecords)
            {
                return rows;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = result.PagingCookie;
        }

        throw new InvalidOperationException(
            $"The census read of '{query.EntityName}' still had rows after {MaxPages} pages of {PageSize}; it is " +
            "INCOMPLETE and will not be graded.");
    }

    private static Dictionary<Guid, IReadOnlyList<Guid>> Group(IEnumerable<Entity> rows, string keyColumn, string valueColumn) =>
        rows.GroupBy(row => RequiredGuid(row, keyColumn))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.Select(row => RequiredGuid(row, valueColumn)).ToArray());

    /// <summary>A GUID column that may arrive as a Guid (intersect tables) or an EntityReference (lookups).</summary>
    private static Guid? OptionalGuid(Entity row, string column) =>
        row.Attributes.TryGetValue(column, out var value)
            ? value switch
            {
                Guid id => id,
                EntityReference reference => reference.Id,
                _ => null
            }
            : null;

    private static Guid RequiredGuid(Entity row, string column) =>
        OptionalGuid(row, column) is { } id && id != Guid.Empty
            ? id
            : throw new InvalidOperationException(
                $"The census expected a GUID in '{row.LogicalName}.{column}' but the row did not carry one.");
}

/// <summary>A graded census as both runners publish it (the job's <c>ResultJson</c>, the acceptance route's body).</summary>
/// <param name="Status"><c>isolated</c> | <c>findings</c> | <c>inert</c> | <c>error</c> (<see cref="KeylessProofContract.SecureRecordIsolationCensus"/>).</param>
/// <param name="Verdict">The most severe <see cref="SecureBuVerdict"/>, or <c>unknown</c> when the census could not be read.</param>
/// <param name="Findings">Every violated clause: its verdict and the operator-actionable message.</param>
public sealed record SecureRecordIsolationCensusResult(
    string Status, string Verdict, IReadOnlyList<SecureRecordIsolationCensusFinding> Findings)
{
    /// <summary>The unread census: status <c>error</c>, no verdict, no findings — never a pass.</summary>
    public static SecureRecordIsolationCensusResult Unread { get; } =
        new(KeylessProofContract.SecureRecordIsolationCensus.Error, "unknown", Array.Empty<SecureRecordIsolationCensusFinding>());
}

/// <summary>One violated clause.</summary>
public sealed record SecureRecordIsolationCensusFinding(string Verdict, string Message);
