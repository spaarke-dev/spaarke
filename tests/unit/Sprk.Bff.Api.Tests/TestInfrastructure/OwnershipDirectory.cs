using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// An in-memory Dataverse directory for the REAL <see cref="RecordOwnershipResolver"/>: business units, owner teams,
/// users and parent records, answered by evaluating a query's Equal conditions against its rows (so a dropped
/// predicate widens the match and the resolver refuses), honouring <c>TopCount</c>, and recording every owner
/// assignment a reparent makes.
/// </summary>
/// <remarks>
/// Extracted from <c>RecordOwnershipResolverTests</c> (unified-access-control-r2 task 146) so the writer-family
/// integration tests (<c>SecureChildOwnershipTests</c>) drive the SAME resolver over the SAME directory shape — one
/// harness, not a second one that could drift (root CLAUDE.md §11).
/// </remarks>
internal sealed class OwnershipDirectory
{
    // ── The standard world (Standard()) ───────────────────────────────────────────────────────────────────────
    public static readonly Guid GeneralBu = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    public static readonly Guid ChildBu = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    public static readonly Guid SecureBu = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000001");
    public static readonly Guid GeneralTeam = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    public static readonly Guid ChildTeam = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7");

    /// <summary>The Secure Record BU's DEFAULT team — a decoy that must never be chosen (task 144).</summary>
    public static readonly Guid SecureDefaultTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000002");

    /// <summary>The Secure Record BU's NAMED owner team (task 144).</summary>
    public static readonly Guid SecureNamedTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000003");

    public const string SecureBuName = "Secure Record";
    public const string SecureOwnerTeamName = "Secure Record Owners";

    public static readonly Guid CallerUserId = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");
    public static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    private readonly Dictionary<(string Entity, Guid Id), Entity> _records = new();
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Guid SystemUserId, Guid ObjectId, Guid BusinessUnit)> _users = new();
    private readonly List<(Guid TeamId, Guid BusinessUnit, bool IsDefault, int TeamType, string Name)> _teams = new();
    private readonly List<(Guid BusinessUnitId, string Name)> _businessUnits = new();
    private bool _ignoreAssignments;

    public List<string> QueriedEntities { get; } = new();
    public List<string> UserKeyColumns { get; } = new();

    /// <summary>Every owner assignment the resolver made (task 146 reparent).</summary>
    public List<(string Entity, Guid Id, Guid TeamId)> Assignments { get; } = new();

    /// <summary>
    /// One caller in the general unit; three named business units; each unit's default Owner team unless told
    /// otherwise; and in the Secure Record BU the NAMED owner team plus two near-miss decoys inserted BEFORE it — an
    /// ACCESS team with the right name and an OWNER team with the wrong name — so a named-team query that dropped
    /// <c>teamtype</c> or <c>name</c> matches two rows (or a decoy first) and refuses.
    /// </summary>
    public static OwnershipDirectory Standard(bool withDefaultTeams = true, bool withNamedSecureTeam = true)
    {
        var directory = new OwnershipDirectory()
            .WithUser(CallerUserId, CallerOid, GeneralBu)
            .WithBusinessUnit(GeneralBu, "Spaarke")
            .WithBusinessUnit(ChildBu, "Spaarke Business Unit 1")
            .WithBusinessUnit(SecureBu, SecureBuName);

        if (withDefaultTeams)
        {
            directory
                .WithTeam(GeneralTeam, GeneralBu, isDefault: true, teamType: 0, "Spaarke")
                .WithTeam(ChildTeam, ChildBu, isDefault: true, teamType: 0, "Spaarke Business Unit 1")
                .WithTeam(SecureDefaultTeam, SecureBu, isDefault: true, teamType: 0, SecureBuName);
        }

        directory
            .WithTeam(Guid.NewGuid(), SecureBu, isDefault: false, teamType: 1, SecureOwnerTeamName)
            .WithTeam(Guid.NewGuid(), SecureBu, isDefault: false, teamType: 0, SecureOwnerTeamName + " Extra");

        if (withNamedSecureTeam)
        {
            directory.WithTeam(SecureNamedTeam, SecureBu, isDefault: false, teamType: 0, SecureOwnerTeamName);
        }

        return directory;
    }

    /// <summary>
    /// The REAL resolver over this directory, with the Secure Record names configured explicitly. A <paramref
    /// name="fault"/> makes every directory read throw it — a Dataverse fault, which must propagate.
    /// </summary>
    public RecordOwnershipResolver Resolver(Exception? fault = null)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
                fault is null ? Task.FromResult(Answer(query)) : Task.FromException<EntityCollection>(fault));
        entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                Update(entity, id, fields);
                return Task.CompletedTask;
            });
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string[] _, CancellationToken _) =>
                OwnerReadFault is { } readFault ? Task.FromException<Entity>(readFault) : Task.FromResult(Row(entity, id)));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
        }).Build();

        return new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance);
    }

    /// <summary>
    /// A row with its owning business unit, and optionally its owning team, its <c>sprk_issecure</c> flag and other
    /// columns (a reparent reads the whole row).
    /// </summary>
    /// <remarks>
    /// Task 150 (round 17 item 3): on the three secure-flagged ROOT tables a row seeded with no <paramref name="isSecure"/>
    /// reads <c>false</c> — the post-backfill reality (<c>scripts/Repair-SecureFlagNulls.ps1</c>: every row holds true or
    /// false), because the resolver now reads an EMPTY flag as flagged (fail closed). A row whose flag must come back
    /// EMPTY — masked by field-level security — is seeded with <see cref="WithMaskedSecureFlag"/>.
    /// </remarks>
    public OwnershipDirectory WithRecord(
        string entity, Guid id, Guid? owningBusinessUnit, bool? isSecure = null, Guid? owningTeam = null,
        Dictionary<string, object>? extra = null)
    {
        isSecure ??= SecureFlaggedRootTables.Contains(entity) ? false : null;
        var row = new Entity(entity, id);
        if (owningBusinessUnit is { } bu)
            row["owningbusinessunit"] = new EntityReference("businessunit", bu);
        if (owningTeam is { } team)
        {
            row["owningteam"] = new EntityReference("team", team);
            row["ownerid"] = new EntityReference("team", team);
        }
        if (isSecure is { } secure)
            row["sprk_issecure"] = secure;
        foreach (var (column, value) in extra ?? new())
            row[column] = value;

        _records[(entity, id)] = row;
        return this;
    }

    /// <summary>The three tables that carry <c>sprk_issecure</c> as a security input (project, matter, work assignment).</summary>
    private static readonly HashSet<string> SecureFlaggedRootTables =
        new(StringComparer.OrdinalIgnoreCase) { "sprk_project", "sprk_matter", "sprk_workassignment" };

    /// <summary>
    /// A root row whose <c>sprk_issecure</c> comes back EMPTY — the field-secured value masked from the BFF identity (task
    /// 150, round 17 item 3). Otherwise as <see cref="WithRecord"/>.
    /// </summary>
    public OwnershipDirectory WithMaskedSecureFlag(string entity, Guid id, Guid owningBusinessUnit, Guid? owningTeam = null)
    {
        WithRecord(entity, id, owningBusinessUnit, isSecure: null, owningTeam: owningTeam);
        _records[(entity, id)].Attributes.Remove("sprk_issecure");
        return this;
    }

    /// <summary>A secure root, properly isolated: owned in the Secure Record BU by its named team, flagged.</summary>
    public OwnershipDirectory WithSecureRoot(string entity, Guid id) =>
        WithRecord(entity, id, SecureBu, isSecure: true, owningTeam: SecureNamedTeam);

    /// <summary>An ordinary root in the child business unit, owned by that unit's default team.</summary>
    public OwnershipDirectory WithOrdinaryRoot(string entity, Guid id) =>
        WithRecord(entity, id, ChildBu, isSecure: false, owningTeam: ChildTeam);

    /// <summary>Accept owner updates without applying them, so a read-back sees the old owner.</summary>
    public OwnershipDirectory IgnoringAssignments()
    {
        _ignoreAssignments = true;
        return this;
    }

    /// <summary>Task 147 r1: called after every owner assignment is recorded (a second world mirroring the move).</summary>
    public Action<string, Guid, Guid>? OnAssign { get; set; }

    public void Assign(string entity, Guid id, Dictionary<string, object> fields)
    {
        var owner = (EntityReference)fields["ownerid"];
        Assignments.Add((entity, id, owner.Id));
        OnAssign?.Invoke(entity, id, owner.Id);
        if (!_ignoreAssignments && _records.TryGetValue((entity, id), out var row))
        {
            row["owningteam"] = new EntityReference("team", owner.Id);
            row["ownerid"] = owner;
        }
    }

    // ── Task 146 b2: the failures a re-file must recover from ───────────────────────────────────────────────────

    /// <summary>When set, an OWNER update throws it — after applying when <see cref="AssignmentLandsBeforeFault"/>
    /// (a lost response), before otherwise (the assignment did not happen).</summary>
    public Exception? AssignmentFault { get; set; }

    /// <summary>With <see cref="AssignmentFault"/>: the assignment is applied, then the fault is thrown.</summary>
    public bool AssignmentLandsBeforeFault { get; set; }

    /// <summary>When set, every single-row read (<c>RetrieveAsync</c> — the owner read-backs) throws it.</summary>
    public Exception? OwnerReadFault { get; set; }

    /// <summary>When set, an update WITHOUT an owner (a filing restore) throws it.</summary>
    public Exception? RestoreFault { get; set; }

    /// <summary>Every update that set no owner (a filing restore), in order.</summary>
    public List<(string Entity, Guid Id, Dictionary<string, object> Fields)> FieldUpdates { get; } = new();

    /// <summary>An owner update is an assignment; any other update writes its columns (<see cref="DBNull"/> clears).</summary>
    public void Update(string entity, Guid id, Dictionary<string, object> fields)
    {
        if (fields.ContainsKey("ownerid"))
        {
            if (AssignmentFault is { } fault)
            {
                if (AssignmentLandsBeforeFault)
                    Assign(entity, id, fields);
                throw fault;
            }

            Assign(entity, id, fields);
            return;
        }

        if (RestoreFault is { } restoreFault)
            throw restoreFault;

        FieldUpdates.Add((entity, id, new Dictionary<string, object>(fields)));
        if (_records.TryGetValue((entity, id), out var row))
        {
            foreach (var (column, value) in fields)
            {
                if (value is DBNull)
                    row.Attributes.Remove(column);
                else
                    row[column] = value;
            }
        }
    }

    public Entity Row(string entity, Guid id) =>
        _records.TryGetValue((entity, id), out var row) ? row : new Entity(entity, id);

    public OwnershipDirectory WithUnreadable(string entity)
    {
        _unreadable.Add(entity);
        return this;
    }

    private readonly HashSet<(string Entity, string Column)> _absentColumns = new();

    /// <summary>
    /// Task 146 c1-r1: <paramref name="entity"/> has no <paramref name="column"/> in this environment (its schema step has
    /// not run). A query that SELECTS it faults as Dataverse's SDK does — <c>0x80041103</c>, QueryBuilderNoAttribute; an
    /// every-column read simply returns rows without it.
    /// </summary>
    public OwnershipDirectory WithoutColumn(string entity, string column)
    {
        _absentColumns.Add((entity, column));
        return this;
    }

    public OwnershipDirectory WithUser(Guid systemUserId, Guid objectId, Guid businessUnit)
    {
        _users.Add((systemUserId, objectId, businessUnit));
        return this;
    }

    public OwnershipDirectory WithTeam(Guid teamId, Guid businessUnit, bool isDefault, int teamType, string name = "")
    {
        _teams.Add((teamId, businessUnit, isDefault, teamType, name));
        return this;
    }

    public OwnershipDirectory WithBusinessUnit(Guid businessUnitId, string name)
    {
        _businessUnits.Add((businessUnitId, name));
        return this;
    }

    public EntityCollection Answer(QueryExpression query)
    {
        QueriedEntities.Add(query.EntityName);
        if (!query.ColumnSet.AllColumns
            && query.ColumnSet.Columns.FirstOrDefault(c => _absentColumns.Contains((query.EntityName, c))) is { } absent)
        {
            throw new System.ServiceModel.FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault
                {
                    ErrorCode = unchecked((int)0x80041103),
                    Message = $"'{query.EntityName}' entity doesn't contain attribute with Name = '{absent}'.",
                },
                new System.ServiceModel.FaultReason($"'{query.EntityName}' entity doesn't contain attribute with Name = '{absent}'."));
        }

        var conditions = query.Criteria.Conditions.ToDictionary(
            c => c.AttributeName, c => c.Values.Single(), StringComparer.OrdinalIgnoreCase);

        IEnumerable<Entity> rows = query.EntityName switch
        {
            "systemuser" => UsersMatching(conditions),
            "team" => _teams
                .Where(t => Matches(conditions, "teamid", t.TeamId)
                            && Matches(conditions, "businessunitid", t.BusinessUnit)
                            && Matches(conditions, "isdefault", t.IsDefault)
                            && Matches(conditions, "teamtype", t.TeamType)
                            && MatchesName(conditions, t.Name))
                .Select(t => new Entity("team", t.TeamId)
                {
                    // Task 148 r2: a planned owner's business unit is read from its team row.
                    ["businessunitid"] = new EntityReference("businessunit", t.BusinessUnit),
                }),
            "businessunit" => _businessUnits
                .Where(b => MatchesName(conditions, b.Name))
                .Select(b => new Entity("businessunit", b.BusinessUnitId)),
            _ => RecordsMatching(query.EntityName, conditions),
        };

        var list = rows.ToList();
        if (query.TopCount is { } top)
        {
            list = list.Take(top).ToList();
        }

        return new EntityCollection(list);
    }

    private IEnumerable<Entity> UsersMatching(IReadOnlyDictionary<string, object> conditions)
    {
        var (column, key) = conditions.Single();
        UserKeyColumns.Add(column);
        return _users
            .Where(u => column == "systemuserid" ? u.SystemUserId.Equals(key) : u.ObjectId.Equals(key))
            .Select(u => new Entity("systemuser", u.SystemUserId)
            {
                ["businessunitid"] = new EntityReference("businessunit", u.BusinessUnit),
            });
    }

    private IEnumerable<Entity> RecordsMatching(string entity, IReadOnlyDictionary<string, object> conditions)
    {
        if (_unreadable.Contains(entity))
        {
            throw new InvalidOperationException($"Test: {entity} is not readable.");
        }

        var id = (Guid)conditions[$"{entity}id"];
        return _records.TryGetValue((entity, id), out var row) ? new[] { row } : Array.Empty<Entity>();
    }

    /// <summary>An ABSENT condition matches everything — so a dropped predicate widens the match.</summary>
    private static bool Matches(IReadOnlyDictionary<string, object> conditions, string attribute, object value) =>
        !conditions.TryGetValue(attribute, out var expected) || expected.Equals(value);

    /// <summary>Dataverse compares names case-insensitively; an absent name condition matches everything.</summary>
    private static bool MatchesName(IReadOnlyDictionary<string, object> conditions, string name) =>
        !conditions.TryGetValue("name", out var expected)
        || string.Equals(expected as string, name, StringComparison.OrdinalIgnoreCase);
}
