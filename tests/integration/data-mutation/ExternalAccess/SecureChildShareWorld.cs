using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
    private readonly HashSet<(string Table, Guid Id)> _failingRowReads = new();
    private readonly HashSet<string> _endlessTables = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<Guid> _refusedOwnerWrites = new();
    private readonly HashSet<Guid> _ignoredOwnerWrites = new();
    private readonly List<(string Table, int AfterCount, Action<SecureChildShareWorld> Change)> _afterQueries = new();

    /// <summary>Every table queried, in order.</summary>
    public List<string> QueriedTables { get; } = new();

    /// <summary>
    /// The NAMED owner team of THIS world (task 148): <see cref="SecureTeam"/> unless the world was built to match another
    /// harness's ids (<see cref="Standard(Guid, Guid)"/>). <see cref="SecureRoot"/> and <see cref="SecureChild"/> use it.
    /// </summary>
    public Guid OwnerTeam { get; private init; } = SecureTeam;

    /// <summary>The Secure Record BU of THIS world (task 148).</summary>
    public Guid OwnerTeamBusinessUnit { get; private init; } = SecureBu;

    /// <summary>
    /// Task 148: every owner write (an <c>ownerid</c> update), in order, with the shared sequence when <see cref="Sequence"/>
    /// is set — so a test can order a child's re-own against the share writes and the root's own updates.
    /// </summary>
    public List<(string Table, Guid Id, DataversePrincipalRef Owner, int Sequence)> OwnerWrites { get; } = new();

    /// <summary>Task 148: the sequence a host harness shares with its own recorded writes (null = 0).</summary>
    public Func<int>? Sequence { get; set; }

    /// <summary>A world with the Secure Record BU, its default team, two near-miss decoys and the named team.</summary>
    public static SecureChildShareWorld Standard() => Standard(SecureBu, SecureTeam);

    /// <summary>
    /// The standard world with the Secure Record BU and its named owner team carrying the given ids (task 148) — so a host
    /// harness whose own Dataverse double resolves those ids (the provisioning fixture) and this world agree on which team
    /// isolates a record. A user in the general BU (<see cref="SomeUser"/>) is seeded too.
    /// </summary>
    public static SecureChildShareWorld Standard(Guid secureBu, Guid secureTeam)
    {
        var world = new SecureChildShareWorld { OwnerTeam = secureTeam, OwnerTeamBusinessUnit = secureBu };
        world.Add("businessunit", GeneralBu, ("name", "Spaarke"));
        world.Add("businessunit", secureBu, ("name", SecureBuName));
        world.Team(GeneralTeam, GeneralBu, "Spaarke", isDefault: true, teamType: 0);
        world.Team(SecureDefaultTeam, secureBu, SecureBuName, isDefault: true, teamType: 0);
        world.Team(Guid.NewGuid(), secureBu, SecureOwnerTeamName, isDefault: false, teamType: 1); // access team, right name
        world.Team(Guid.NewGuid(), secureBu, SecureOwnerTeamName + " Extra", isDefault: false, teamType: 0);
        world.Team(secureTeam, secureBu, SecureOwnerTeamName, isDefault: false, teamType: 0);
        world.User(SomeUser, GeneralBu);
        return world;
    }

    /// <summary>A systemuser in a business unit — what a user-owned row's <c>owningbusinessunit</c> derives from (task 148).</summary>
    public SecureChildShareWorld User(Guid id, Guid businessUnit) =>
        Add("systemuser", id, ("businessunitid", new EntityReference("businessunit", businessUnit)));

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
        Add(table, id, ("owningteam", TeamRef(OwnerTeam)), ("sprk_issecure", true));

    /// <summary>An ordinary root, owned by the general BU's default team.</summary>
    public SecureChildShareWorld OrdinaryRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", false));

    /// <summary>A root flagged secure whose provisioning did not complete (still owned by an ordinary team).</summary>
    public SecureChildShareWorld FlaggedNotIsolatedRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(GeneralTeam)), ("sprk_issecure", true));

    /// <summary>
    /// A root owned by an ordinary team whose <c>sprk_issecure</c> comes back EMPTY — the field-secured value masked from
    /// this identity (task 150, round 17 item 3): the row carries no value for the column at all.
    /// </summary>
    public SecureChildShareWorld MaskedFlagNotIsolatedRoot(string table, Guid id) =>
        Add(table, id, ("owningteam", TeamRef(GeneralTeam)));

    /// <summary>A child owned by the Secure team, filed through the given lookups (column, target table, target id).</summary>
    public SecureChildShareWorld SecureChild(string table, Guid id, params (string Column, string Target, Guid TargetId)[] lookups) =>
        Child(table, id, ("owningteam", TeamRef(OwnerTeam)), lookups);

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

    /// <summary>Task 147: lets every table be queried again (a transient Dataverse fault that has cleared).</summary>
    public SecureChildShareWorld ClearQueryFaults()
    {
        _failingTables.Clear();
        _failingIdListReads.Clear();
        return this;
    }

    /// <summary>
    /// Makes every read of ONE row by its id throw (task 149 r1: a lineage fault on one record — the walk's single-row
    /// reads of a root or an intermediate). Queries that do not name the row by id still answer.
    /// </summary>
    public SecureChildShareWorld FailingRowReadsOf(string table, Guid id)
    {
        _failingRowReads.Add((table, id));
        return this;
    }

    private readonly HashSet<(string Table, Guid Id)> _failingIdListReads = new();

    /// <summary>
    /// Task 173: makes every read of ONE row through an id LIST (<c>id IN (…)</c>, the parent walk's batched read) throw.
    /// Listings that do not name the row by id still answer. <see cref="ClearQueryFaults"/> clears it.
    /// </summary>
    public SecureChildShareWorld FailingIdListReadsOf(string table, Guid id)
    {
        _failingIdListReads.Add((table, id));
        return this;
    }

    /// <summary>
    /// Makes every PAGED query of one table report more rows to come, however many pages have been read (task 149 r2: a
    /// table larger than the synchronizer's page ceiling, without seeding a hundred thousand rows).
    /// </summary>
    public SecureChildShareWorld EndlessPagesOf(string table)
    {
        _endlessTables.Add(table);
        return this;
    }

    /// <summary>Task 173: every <c>sprk_accesspermission</c> write, in order.</summary>
    public List<(string Table, Guid Id, int Value)> AccessPermissionWrites { get; } = new();

    private readonly HashSet<Guid> _refusedAccessPermissionWrites = new();

    /// <summary>Task 173: an Access Permission write to THIS row throws (recorded first).</summary>
    public SecureChildShareWorld RefusingAccessPermissionWritesOf(Guid id)
    {
        _refusedAccessPermissionWrites.Add(id);
        return this;
    }

    /// <summary>Task 148: an owner write to THIS row throws (recorded first) — Dataverse refusing the re-own.</summary>
    public SecureChildShareWorld RefusingOwnerWritesOf(Guid id)
    {
        _refusedOwnerWrites.Add(id);
        return this;
    }

    /// <summary>Task 148: an owner write to THIS row is accepted (recorded) but not applied — the read-back must catch it.</summary>
    public SecureChildShareWorld IgnoringOwnerWritesOf(Guid id)
    {
        _ignoredOwnerWrites.Add(id);
        return this;
    }

    /// <summary>Task 148: lets owner writes to this row through again (the fault cleared before a second call).</summary>
    public SecureChildShareWorld ClearOwnerWriteFaults()
    {
        _refusedOwnerWrites.Clear();
        _ignoredOwnerWrites.Clear();
        return this;
    }

    /// <summary>
    /// Task 147 r1: changes the world once, right after the <paramref name="count"/>-th query of <paramref name="table"/> has
    /// been answered — the state between two reads of the same thing (e.g. a second Secure Record Owners team appearing
    /// between the job's own team check and the synchronizer's).
    /// </summary>
    public SecureChildShareWorld AfterQueriesOf(string table, int count, Action<SecureChildShareWorld> change)
    {
        _afterQueries.Add((table, count, change));
        return this;
    }

    /// <summary>Task 147 r1: deletes a row (a record removed between two runs).</summary>
    public SecureChildShareWorld Remove(string table, Guid id)
    {
        _rows.Remove((table, id));
        return this;
    }

    /// <summary>True when the row exists.</summary>
    public bool Has(string table, Guid id) => _rows.ContainsKey((table, id));

    /// <summary>Task 158 r1: every row delete, in order (the isolated-create compensation).</summary>
    public List<(string Table, Guid Id)> Deletes { get; } = new();

    /// <summary>Task 158 r1: a host harness removing its own copy of a deleted row.</summary>
    public Action<string, Guid>? OnDeleted { get; set; }

    /// <summary>Task 158 r1: deletes from this row fail (Dataverse refusing the compensation delete).</summary>
    public bool DeletesFail { get; set; }

    /// <summary>
    /// Task 158 r1c-v1 (verifier item 2): deletes ANSWER success but the row survives — only a read-back can tell (the
    /// shape task 133's "accepted, not applied" revoke double models for shares).
    /// </summary>
    public bool DeletesIgnored { get; set; }

    /// <summary>Task 158 r1: an <see cref="IGenericEntityService.DeleteAsync"/> of this world.</summary>
    public void Delete(string table, Guid id)
    {
        Deletes.Add((table, id));
        if (DeletesFail)
            throw new InvalidOperationException("Test: Dataverse refused the delete.");
        if (DeletesIgnored)
            return;
        _rows.Remove((table, id));
        OnDeleted?.Invoke(table, id);
    }

    /// <summary>A row's current owner (team first, then user), or null.</summary>
    public DataversePrincipalRef? OwnerOf(string table, Guid id) =>
        !_rows.TryGetValue((table, id), out var row)
            ? null
            : row.GetAttributeValue<EntityReference>("owningteam") is { } team
                ? DataversePrincipalRef.Team(team.Id)
                : row.GetAttributeValue<EntityReference>("owninguser") is { } user
                    ? DataversePrincipalRef.User(user.Id)
                    : null;

    /// <summary>Task 148: sets a column on an existing row (a host harness mirroring its root's flag).</summary>
    public void Set(string table, Guid id, string column, object? value)
    {
        if (!_rows.TryGetValue((table, id), out var row))
            return;
        if (value is null)
            row.Attributes.Remove(column);
        else
            row[column] = value;
    }

    /// <summary>
    /// Task 147: stamps a row's <c>modifiedon</c>, as any write would (a row created or edited outside the product, which
    /// the reconciliation job's recent-changes pass lists).
    /// </summary>
    public SecureChildShareWorld Modified(string table, Guid id, DateTime atUtc)
    {
        Set(table, id, "modifiedon", DateTime.SpecifyKind(atUtc, DateTimeKind.Utc));
        return this;
    }

    /// <summary>
    /// Task 148: applies an owner change the way Dataverse does — the team or user owner replaces the other — without
    /// recording it (a host harness mirroring an owner move IT recorded).
    /// </summary>
    public void MoveOwner(string table, Guid id, DataversePrincipalRef owner)
    {
        if (!_rows.TryGetValue((table, id), out var row))
            return;
        row.Attributes.Remove("owningteam");
        row.Attributes.Remove("owninguser");
        if (owner.Kind == DataversePrincipalKind.Team)
            row["owningteam"] = TeamRef(owner.Id);
        else
            row["owninguser"] = new EntityReference("systemuser", owner.Id);
    }

    /// <summary>
    /// Task 148: an <see cref="IGenericEntityService.UpdateAsync"/> of this world. Only owner changes are modelled (the
    /// reconciler's one write); anything else is a test failure.
    /// </summary>
    public void Update(string table, Guid id, Dictionary<string, object> fields)
    {
        // Task 173: the inherited Access Permission pass writes ONE column, sprk_accesspermission.
        if (fields.Count == 1 && fields.TryGetValue("sprk_accesspermission", out var level) && level is OptionSetValue option)
        {
            AccessPermissionWrites.Add((table, id, option.Value));
            if (_refusedAccessPermissionWrites.Contains(id))
                throw new InvalidOperationException("Test: Dataverse refused the Access Permission write.");
            if (!_rows.TryGetValue((table, id), out var target))
                throw new InvalidOperationException($"Test: {table} {id} does not exist.");
            target["sprk_accesspermission"] = option;
            return;
        }

        if (!fields.TryGetValue("ownerid", out var value) || value is not EntityReference owner || fields.Count != 1)
            throw new NotSupportedException($"The test world models owner updates only (got {string.Join(",", fields.Keys)}).");

        var principal = owner.LogicalName == "team" ? DataversePrincipalRef.Team(owner.Id) : DataversePrincipalRef.User(owner.Id);
        OwnerWrites.Add((table, id, principal, Sequence?.Invoke() ?? 0));
        if (_refusedOwnerWrites.Contains(id))
            throw new InvalidOperationException("Test: Dataverse refused the re-own.");
        if (_ignoredOwnerWrites.Contains(id))
            return;
        MoveOwner(table, id, principal);
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
    public SecureChildShareSynchronizer Synchronizer(FakeRecordShareTable shares, SecureShareNoAccessGuard? noAccessGuard = null)
        => SynchronizerOver(() => this, shares, noAccessGuard);

    /// <summary>
    /// The REAL synchronizer over whichever world <paramref name="current"/> answers at query time — for a shared host
    /// fixture whose world changes per test — and any share seam. <paramref name="noAccessGuard"/> is task 143's REAL guard;
    /// by default one that walls nobody (<see cref="NobodyWalled"/>).
    /// </summary>
    public static SecureChildShareSynchronizer SynchronizerOver(
        Func<SecureChildShareWorld> current, IDataverseRecordShareService shares, SecureShareNoAccessGuard? noAccessGuard = null)
        => new(EntitiesOver(current).Object, shares, noAccessGuard ?? NobodyWalled(), Configuration(),
            NullLogger<SecureChildShareSynchronizer>.Instance);

    /// <summary>
    /// Task 143's REAL guard over flags that call every record NOT secure — so it answers
    /// <see cref="SecureShareWallOutcome.NotSecure"/> and walls nobody. The default for tests that are not about the No
    /// Access list; the walled cases build the guard over secure flags and a deny-list entry.
    /// </summary>
    public static SecureShareNoAccessGuard NobodyWalled() =>
        new(new GrantPolicyTestDoubles.FlagStubParticipationService(defaultFlags: RootRecordFlags.None),
            new GrantPolicyTestDoubles.SeamNoAccessListReader(),
            new Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InMemoryContactIdentityStore(),
            AssignedAccessTestDoubles.NoFilingRows(),
            NullLogger<SecureShareNoAccessGuard>.Instance);

    /// <summary>
    /// A strict <see cref="IGenericEntityService"/> whose queries this world answers, and whose owner updates it applies
    /// (task 148); callers add other setups.
    /// </summary>
    public static Mock<IGenericEntityService> EntitiesOver(Func<SecureChildShareWorld> current)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) => Task.FromResult(current().Answer(query)));
        entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                current().Update(table, id, fields);
                return Task.CompletedTask;
            });
        entities
            .Setup(e => e.DeleteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, CancellationToken _) =>
            {
                current().Delete(table, id);
                return Task.CompletedTask;
            });
        return entities;
    }

    /// <summary>
    /// Task 148: the REAL <see cref="SecureChildReconciler"/> over this world — the REAL ownership resolver and the REAL
    /// synchronizer over the same rows, the given share seam, and <paramref name="webApi"/> for the platform-cascade rows.
    /// </summary>
    public static SecureChildReconciler ReconcilerOver(
        Func<SecureChildShareWorld> current, IDataverseRecordShareService shares,
        Spaarke.Dataverse.DataverseWebApiClient webApi, SecureShareNoAccessGuard? noAccessGuard = null,
        Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator? accessCacheInvalidator = null)
    {
        var entities = EntitiesOver(current).Object;
        var configuration = Configuration();
        var resolver = new Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver(
            entities, configuration, NullLogger<Sprk.Bff.Api.Services.Dataverse.RecordOwnershipResolver>.Instance);
        var synchronizer = new SecureChildShareSynchronizer(
            entities, shares, noAccessGuard ?? NobodyWalled(), configuration, NullLogger<SecureChildShareSynchronizer>.Instance);
        return new SecureChildReconciler(
            entities, resolver, synchronizer, webApi, configuration, NullLogger<SecureChildReconciler>.Instance,
            accessCacheInvalidator);
    }

    /// <summary>
    /// Task 173: the REAL <see cref="Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver"/> over this world, as the host
    /// registers it unconditionally (<c>AddCoreAncestorResolver</c>) — so every job harness composes it like the host does
    /// (no asymmetric registration; the reconciliation job requires it).
    /// </summary>
    public static Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver CoreAncestorsOver(
        Func<SecureChildShareWorld> current, params string[] tablesWithoutAccessPermission) =>
        new(EntitiesOver(current).Object, ColumnProbe(tablesWithoutAccessPermission),
            NullLogger<Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver>.Instance);

    /// <summary>
    /// Task 173: the column probe of this world — every lineage lookup, root column and stamp column exists, and so does
    /// <c>sprk_accesspermission</c>, except on the tables named.
    /// </summary>
    public static Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.EntityColumnProbe ColumnProbe(
        params string[] tablesWithoutAccessPermission) => (table, _) =>
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (SecureChildLineage.Children.TryGetValue(table, out var lineage))
            columns.UnionWith(lineage.Lookups.Keys);
        if (Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.IntermediateRootColumns.TryGetValue(table, out var roots))
            columns.UnionWith(roots.Select(r => r.Column));
        columns.UnionWith(Sprk.Bff.Api.Services.Dataverse.CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute));
        if (!tablesWithoutAccessPermission.Contains(table, StringComparer.OrdinalIgnoreCase))
            columns.Add("sprk_accesspermission");
        return Task.FromResult<IReadOnlySet<string>>(columns);
    };

    /// <summary>The two Secure Record names this world uses, as configuration.</summary>
    public static IConfiguration Configuration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
        }).Build();

    // ── Query evaluation ────────────────────────────────────────────────────────────────────────────────────────

    public EntityCollection Answer(QueryExpression query)
    {
        QueriedTables.Add(query.EntityName);
        if (_failingTables.Contains(query.EntityName))
            throw new InvalidOperationException($"Test: {query.EntityName} cannot be read.");
        if (query.Criteria.Conditions.Any(c =>
                c.AttributeName == query.EntityName + "id" && c.Operator == ConditionOperator.Equal
                && c.Values.Single() is Guid id && _failingRowReads.Contains((query.EntityName, id))))
            throw new InvalidOperationException($"Test: this {query.EntityName} row cannot be read.");

        if (query.Criteria.Conditions.Any(c =>
                c.AttributeName == query.EntityName + "id" && c.Operator == ConditionOperator.In
                && c.Values.Any(v => v is Guid id && _failingIdListReads.Contains((query.EntityName, id)))))
            throw new InvalidOperationException($"Test: a {query.EntityName} row in this id list cannot be read.");

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
            more = matched.Count > skip + page.Count || _endlessTables.Contains(query.EntityName);
            matched = matched.Skip(skip).Take(page.Count).ToList();
        }

        var projected = matched.Select(r => Project(r, query.ColumnSet)).ToList();
        var answer = new EntityCollection(projected) { MoreRecords = more, PagingCookie = more ? "cookie" : null };

        var asked = QueriedTables.Count(t => string.Equals(t, query.EntityName, StringComparison.OrdinalIgnoreCase));
        foreach (var hook in _afterQueries.Where(h => h.AfterCount == asked
                     && string.Equals(h.Table, query.EntityName, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            _afterQueries.Remove(hook);
            hook.Change(this);
        }

        return answer;
    }

    private Entity Project(Entity row, ColumnSet columns)
    {
        var copy = new Entity(row.LogicalName, row.Id);
        foreach (var (column, value) in row.Attributes)
        {
            if (columns.AllColumns || columns.Columns.Contains(column))
                copy[column] = value;
        }

        // Task 148: owningbusinessunit DERIVES from the owner, as in Dataverse — the ownership resolver reads it, and a
        // stored copy would go stale on every re-own.
        if ((columns.AllColumns || columns.Columns.Contains("owningbusinessunit"))
            && !row.Attributes.ContainsKey("owningbusinessunit")
            && OwningBusinessUnitOf(row) is { } bu)
        {
            copy["owningbusinessunit"] = new EntityReference("businessunit", bu);
        }

        return copy;
    }

    private Guid? OwningBusinessUnitOf(Entity row)
    {
        if (row.GetAttributeValue<EntityReference>("owningteam") is { } team
            && _rows.TryGetValue(("team", team.Id), out var teamRow))
            return teamRow.GetAttributeValue<EntityReference>("businessunitid")?.Id;
        if (row.GetAttributeValue<EntityReference>("owninguser") is { } user
            && _rows.TryGetValue(("systemuser", user.Id), out var userRow))
            return userRow.GetAttributeValue<EntityReference>("businessunitid")?.Id;
        return null;
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
            // Task 158 r1: the job's empty-flag scan, and the pair listing's spelling-independent LIKE.
            ConditionOperator.Null => actual is null,
            // Batch-4 integration (round 46 item 2) and task 158: rows whose ledger is set, and the inheritance's listings.
            ConditionOperator.NotNull => actual is not null,
            ConditionOperator.Like => actual is string text && condition.Values.Single() is string pattern && Like(text, pattern),
            // Task 147: the reconciliation job's recent-changes pass filters on modifiedon. A row with no modifiedon
            // (every row a test does not touch) never matches.
            ConditionOperator.GreaterEqual => actual is DateTime at && condition.Values.Single() is DateTime since && at >= since,
            // Task 173: the Access Permission sweep pages by id (rows are answered in id order, as Dataverse orders them).
            ConditionOperator.GreaterThan => actual is Guid a && condition.Values.Single() is Guid after && a.CompareTo(after) > 0,
            _ => throw new NotSupportedException($"The test world does not evaluate {condition.Operator}."),
        };
    }

    /// <summary>Dataverse LIKE: <c>%</c> any run, <c>_</c> one character, case-insensitive.</summary>
    private static bool Like(string text, string pattern) =>
        System.Text.RegularExpressions.Regex.IsMatch(text,
            "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("%", ".*").Replace("_", ".") + "$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

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
