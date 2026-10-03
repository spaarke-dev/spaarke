using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.AccessControl;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// An in-memory Dataverse for the REAL <see cref="SecureChildShareSynchronizer"/> (task 149): business units, owner teams,
/// roots and child rows, answered by EVALUATING each <see cref="QueryExpression"/> — its Equal/In conditions, nested
/// And/Or filters, <c>TopCount</c>, paging and column set — against the rows.
/// </summary>
/// <remarks>
/// <para><b>Why evaluate rather than script.</b> The synchronizer's correctness rests on which rows its queries select:
/// "owned by the Secure team", "this id", "the one named team that is not the default". A double that answered by table
/// name alone would pass a query that dropped the owner predicate — and that query mirrors ordinary rows. Here a dropped
/// predicate WIDENS the match, so the ordinary decoy rows every test seeds are selected and the test fails.</para>
/// <para><b>Projection is honoured.</b> A row comes back with only the columns the query asked for, so a lookup the lineage
/// map forgot reads as absent — the child then has no route to its root and is not mirrored.</para>
/// <para>The POA plane is <see cref="FakeRecordShareTable"/> (task 063's strict double), passed in by each test.</para>
/// </remarks>
internal sealed class SecureChildShareWorld
{
    public static readonly Guid GeneralBu = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    public static readonly Guid GeneralTeam = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    public static readonly Guid SecureBu = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000001");

    /// <summary>The Secure Record BU's DEFAULT team — a decoy that must never be taken for the owner team.</summary>
    public static readonly Guid SecureDefaultTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000002");

    /// <summary>The NAMED owner team (task 144): the owner of every secure root and secure child.</summary>
    public static readonly Guid SecureTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000003");

    public static readonly Guid SomeUser = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");

    public const string SecureBuName = "Secure Record";
    public const string SecureOwnerTeamName = "Secure Record Owners";

    private readonly Dictionary<(string Table, Guid Id), Entity> _rows = new();
    private readonly HashSet<string> _failingTables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every table queried, in order.</summary>
    public List<string> QueriedTables { get; } = new();

    /// <summary>A world with the Secure Record BU, its default team, two near-miss decoys and the named team.</summary>
    public static SecureChildShareWorld Standard()
    {
        var world = new SecureChildShareWorld();
        world.Add("businessunit", GeneralBu, ("name", "Spaarke"));
        world.Add("businessunit", SecureBu, ("name", SecureBuName));
        world.Team(GeneralTeam, GeneralBu, "Spaarke", isDefault: true, teamType: 0);
        world.Team(SecureDefaultTeam, SecureBu, SecureBuName, isDefault: true, teamType: 0);
        world.Team(Guid.NewGuid(), SecureBu, SecureOwnerTeamName, isDefault: false, teamType: 1); // access team, right name
        world.Team(Guid.NewGuid(), SecureBu, SecureOwnerTeamName + " Extra", isDefault: false, teamType: 0);
        world.Team(SecureTeam, SecureBu, SecureOwnerTeamName, isDefault: false, teamType: 0);
        return world;
    }

    /// <summary>An environment with no Secure Record BU at all.</summary>
    public static SecureChildShareWorld WithoutSecureBusinessUnit()
    {
        var world = new SecureChildShareWorld();
        world.Add("businessunit", GeneralBu, ("name", "Spaarke"));
        world.Team(GeneralTeam, GeneralBu, "Spaarke", isDefault: true, teamType: 0);
        return world;
    }

    public SecureChildShareWorld Team(Guid id, Guid businessUnit, string name, bool isDefault, int teamType) =>
        Add("team", id,
            ("businessunitid", new EntityReference("businessunit", businessUnit)),
            ("name", name), ("isdefault", isDefault), ("teamtype", new OptionSetValue(teamType)));

    /// <summary>A secure root: owned by the named team, flagged.</summary>
    public SecureChildShareWorld SecureRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(SecureTeam)), ("sprk_issecure", true));

    /// <summary>An ordinary root, owned by the general BU's default team.</summary>
    public SecureChildShareWorld OrdinaryRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", false));

    /// <summary>A root flagged secure whose provisioning did not complete (still owned by an ordinary team).</summary>
    public SecureChildShareWorld FlaggedNotIsolatedRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", true));

    /// <summary>A child owned by the Secure team, filed through the given lookups (column, target table, target id).</summary>
    public SecureChildShareWorld SecureChild(string table, Guid id, params (string Column, string Target, Guid TargetId)[] lookups) =>
        Child(table, id, ("owningteam", TeamRef(SecureTeam)), lookups);

    /// <summary>A child owned by an ORDINARY team (a pre-146 row, or a child of an ordinary record).</summary>
    public SecureChildShareWorld OrdinaryChild(string table, Guid id, params (string Column, string Target, Guid TargetId)[] lookups) =>
        Child(table, id, ("owningteam", TeamRef(GeneralTeam)), lookups);

    /// <summary>A child owned by a USER (a run-as-user, client-created or pre-146 row).</summary>
    public SecureChildShareWorld UserOwnedChild(string table, Guid id, params (string Column, string Target, Guid TargetId)[] lookups) =>
        Child(table, id, ("owninguser", new EntityReference("systemuser", SomeUser)), lookups);

    private SecureChildShareWorld Child(
        string table, Guid id, (string, object) owner, (string Column, string Target, Guid TargetId)[] lookups)
        => Add(table, id, lookups.Select(l => (l.Column, (object)new EntityReference(l.Target, l.TargetId)))
            .Prepend(owner).ToArray());

    /// <summary>Makes every query of one table throw (a Dataverse fault).</summary>
    public SecureChildShareWorld FailingQueriesOf(string table)
    {
        _failingTables.Add(table);
        return this;
    }

    public SecureChildShareWorld Add(string table, Guid id, params (string Column, object Value)[] columns)
    {
        var row = new Entity(table, id);
        foreach (var (column, value) in columns)
            row[column] = value;
        _rows[(table, id)] = row;
        return this;
    }

    private static EntityReference TeamRef(Guid id) => new("team", id);

    /// <summary>The REAL synchronizer over this world and the given share table.</summary>
    public SecureChildShareSynchronizer Synchronizer(FakeRecordShareTable shares) => SynchronizerOver(() => this, shares);

    /// <summary>
    /// The REAL synchronizer over whichever world <paramref name="current"/> answers at query time — for a shared host
    /// fixture whose world changes per test — and any share seam.
    /// </summary>
    public static SecureChildShareSynchronizer SynchronizerOver(
        Func<SecureChildShareWorld> current, IDataverseRecordShareService shares)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(current().Answer(query)));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
        }).Build();

        return new SecureChildShareSynchronizer(
            entities.Object, shares, configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
    }

    // ── Query evaluation ────────────────────────────────────────────────────────────────────────────────────────

    public EntityCollection Answer(QueryExpression query)
    {
        QueriedTables.Add(query.EntityName);
        if (_failingTables.Contains(query.EntityName))
            throw new InvalidOperationException($"Test: {query.EntityName} cannot be read.");

        var matched = _rows.Values
            .Where(r => r.LogicalName == query.EntityName && Matches(r, query.Criteria))
            .OrderBy(r => r.Id)
            .ToList();

        if (query.TopCount is { } top)
            matched = matched.Take(top).ToList();

        var more = false;
        if (query.PageInfo is { Count: > 0 } page)
        {
            var skip = (Math.Max(page.PageNumber, 1) - 1) * page.Count;
            more = matched.Count > skip + page.Count;
            matched = matched.Skip(skip).Take(page.Count).ToList();
        }

        var projected = matched.Select(r => Project(r, query.ColumnSet)).ToList();
        return new EntityCollection(projected) { MoreRecords = more, PagingCookie = more ? "cookie" : null };
    }

    private static Entity Project(Entity row, ColumnSet columns)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (column, value) in row.Attributes)
        {
            if (columns.AllColumns || columns.Columns.Contains(column))
                copy[column] = value;
        }

        return copy;
    }

    private static bool Matches(Entity row, FilterExpression filter)
    {
        var results = filter.Conditions.Select(c => Matches(row, c))
            .Concat(filter.Filters.Select(f => Matches(row, f)))
            .ToList();
        if (results.Count == 0)
            return true;
        return filter.FilterOperator == LogicalOperator.Or ? results.Any(r => r) : results.All(r => r);
    }

    private static bool Matches(Entity row, ConditionExpression condition)
    {
        var actual = condition.AttributeName == row.LogicalName + "id"
            ? row.Id
            : row.Attributes.TryGetValue(condition.AttributeName, out var value) ? value : null;

        return condition.Operator switch
        {
            ConditionOperator.Equal => Same(actual, condition.Values.Single()),
            ConditionOperator.In => condition.Values.Any(v => Same(actual, v)),
            _ => throw new NotSupportedException($"The test world does not evaluate {condition.Operator}."),
        };
    }

    private static bool Same(object? actual, object? expected) => (actual, expected) switch
    {
        (null, _) => false,
        (EntityReference r, Guid g) => r.Id == g,
        (Guid a, Guid g) => a == g,
        (OptionSetValue o, int i) => o.Value == i,
        (string s, string t) => string.Equals(s, t, StringComparison.OrdinalIgnoreCase),
        (bool b, bool c) => b == c,
        _ => Equals(actual, expected),
    };
}
