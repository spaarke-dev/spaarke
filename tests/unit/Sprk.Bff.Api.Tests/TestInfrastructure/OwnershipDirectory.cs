using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
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
/// #2143: every row carries a <c>versionnumber</c> that every applied write bumps, and
/// <see cref="IGenericEntityService.UpdateIfRowVersionMatchesAsync"/> is honoured — a write conditioned on a version the row
/// no longer has throws <see cref="System.Data.DBConcurrencyException"/> and writes nothing, as Dataverse does.
/// <see cref="BeforeWrite"/> lands another writer's change between the resolver's read and its write.
/// </remarks>
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

    /// <summary>UAC task 194b R4b-2c (owner round 136): the restricted-content owner team — named, non-default, in the
    /// CUSTOMER's top business unit (<see cref="ChildBu"/>, the root's direct child), no members.</summary>
    public static readonly Guid RestrictedContentTeam = Guid.Parse("0e5c0e5c-0086-4086-8086-000000000086");

    /// <summary>R4b-2c: a business unit BELOW the customer's top unit (<see cref="ChildBu"/>), and its default team.</summary>
    public static readonly Guid CustomerChildBu = Guid.Parse("0e5c0e5c-0086-4086-8086-0000000000c1");
    public static readonly Guid CustomerChildTeam = Guid.Parse("0e5c0e5c-0086-4086-8086-0000000000c2");

    public const string SecureBuName = "Secure Record";
    public const string SecureOwnerTeamName = "Secure Record Owners";

    public static readonly Guid CallerUserId = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");
    public static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    private readonly Dictionary<(string Entity, Guid Id), Entity> _records = new();
    private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(Guid SystemUserId, Guid ObjectId, Guid BusinessUnit, bool Disabled)> _users = new();
    private readonly List<(Guid TeamId, Guid BusinessUnit, bool IsDefault, int TeamType, string Name)> _teams = new();
    private readonly List<(Guid BusinessUnitId, string Name, Guid? Parent)> _businessUnits = new();

    /// <summary>R4b-2: team membership rows (teamid, systemuserid).</summary>
    private readonly List<(Guid TeamId, Guid SystemUserId)> _memberships = new();
    private bool _ignoreAssignments;

    public List<string> QueriedEntities { get; } = new();

    /// <summary>R4b-2c: how many business-unit reads BY ID (the walk to the customer's top unit) this directory answered.</summary>
    public int BusinessUnitIdReads { get; private set; }

    /// <summary>#2143 round 2: every query on a record table (not systemuser, team or businessunit), as sent — so a test can
    /// assert how the resolver reads a row (its <c>NoLock</c>, its column set).</summary>
    public List<QueryExpression> RecordQueries { get; } = new();
    public List<string> UserKeyColumns { get; } = new();

    /// <summary>Every owner assignment the resolver made (task 146 reparent).</summary>
    public List<(string Entity, Guid Id, Guid TeamId)> Assignments { get; } = new();

    /// <summary>
    /// One caller in the general unit; three named business units; each unit's default Owner team unless told
    /// otherwise; and in the Secure Record BU the NAMED owner team plus two near-miss decoys inserted BEFORE it — an
    /// ACCESS team with the right name and an OWNER team with the wrong name — so a named-team query that dropped
    /// <c>teamtype</c> or <c>name</c> matches two rows (or a decoy first) and refuses.
    /// </summary>
    public static OwnershipDirectory Standard(
        bool withDefaultTeams = true, bool withNamedSecureTeam = true, bool withRestrictedContentTeam = true)
    {
        // R4b-2: the general unit is the ROOT business unit (no parent); the child (the customer's top unit, as dev's
        // "Spaarke Business Unit 1") and the Secure Record unit sit under it, and a customer unit sits under the child.
        var directory = new OwnershipDirectory()
            .WithUser(CallerUserId, CallerOid, GeneralBu)
            .WithBusinessUnit(GeneralBu, "Spaarke")
            .WithBusinessUnit(ChildBu, "Spaarke Business Unit 1", parent: GeneralBu)
            .WithBusinessUnit(SecureBu, SecureBuName, parent: GeneralBu)
            .WithBusinessUnit(CustomerChildBu, "Litigation", parent: ChildBu);

        // R4b-4 (A-F2): an ACCESS team and a GROUP team with the restricted-content name, in the same unit and inserted
        // BEFORE the real one, so a team query that dropped teamtype = 0 (Owner) matches two rows (or a decoy first).
        directory
            .WithTeam(Guid.NewGuid(), ChildBu, isDefault: false, teamType: 1,
                Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam.DefaultOwnerTeamName)
            .WithTeam(Guid.NewGuid(), ChildBu, isDefault: false, teamType: 2,
                Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam.DefaultOwnerTeamName);

        if (withRestrictedContentTeam)
        {
            directory.WithTeam(RestrictedContentTeam, ChildBu, isDefault: false, teamType: 0,
                Sprk.Bff.Api.Infrastructure.Dataverse.RestrictedContentOwnerTeam.DefaultOwnerTeamName);
        }

        if (withDefaultTeams)
        {
            directory
                .WithTeam(GeneralTeam, GeneralBu, isDefault: true, teamType: 0, "Spaarke")
                .WithTeam(ChildTeam, ChildBu, isDefault: true, teamType: 0, "Spaarke Business Unit 1")
                .WithTeam(SecureDefaultTeam, SecureBu, isDefault: true, teamType: 0, SecureBuName)
                .WithTeam(CustomerChildTeam, CustomerChildBu, isDefault: true, teamType: 0, "Litigation");
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
    public RecordOwnershipResolver Resolver(Exception? fault = null, ILogger<RecordOwnershipResolver>? logger = null)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression query, CancellationToken _) =>
                (fault ?? QueryFault) is { } queryFault
                    ? Task.FromException<EntityCollection>(queryFault)
                    : Task.FromResult(Answer(query)));
        entities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, Dictionary<string, object> fields, CancellationToken _) =>
            {
                BeforeWrite?.Invoke(this, fields);
                Update(entity, id, fields);
                AfterWrite?.Invoke(this, fields);
                return Task.CompletedTask;
            });
        entities
            .Setup(e => e.UpdateIfRowVersionMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(),
                It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string version, Dictionary<string, object> fields, CancellationToken _) =>
            {
                BeforeWrite?.Invoke(this, fields);
                UpdateIfVersion(entity, id, version, fields);
                AfterWrite?.Invoke(this, fields);
                return Task.CompletedTask;
            });
        entities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string entity, Guid id, string[] _, CancellationToken _) =>
                OwnerReadFault is { } readFault ? Task.FromException<Entity>(readFault) : Task.FromResult(Snapshot(Row(entity, id))));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
        }).Build();

        return new RecordOwnershipResolver(entities.Object, configuration, logger ?? NullLogger<RecordOwnershipResolver>.Instance);
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

        // #2143: a re-seeded row is a NEW version of it (a write), never the old version again.
        row[VersionColumn] = _records.TryGetValue((entity, id), out var previous) ? VersionOf(previous) + 1 : 1L;
        _records[(entity, id)] = row;
        return this;
    }

    private const string VersionColumn = "versionnumber";

    private static long VersionOf(Entity row) => row.GetAttributeValue<long?>(VersionColumn) ?? 0L;

    private static void Bump(Entity row) => row[VersionColumn] = VersionOf(row) + 1;

    /// <summary>#2143: the row's current <c>versionnumber</c>.</summary>
    public long Version(string entity, Guid id) => _records.TryGetValue((entity, id), out var row) ? VersionOf(row) : 0L;

    /// <summary>#2143: every conditional write the resolver sent, in order, with the version it was conditioned on and
    /// whether it was applied (false: the version had moved, nothing was written).</summary>
    public List<(string Entity, Guid Id, string Version, Dictionary<string, object> Fields, bool Applied)> ConditionalWrites { get; } = new();

    /// <summary>
    /// #2143: runs before every write the resolver sends (conditional or not) reaches the row, with the directory and the
    /// fields about to be written — so a test lands ANOTHER writer's change (<see cref="Write"/>) between the resolver's
    /// read and its write.
    /// </summary>
    public Action<OwnershipDirectory, Dictionary<string, object>>? BeforeWrite { get; set; }

    /// <summary>
    /// #2143 round 3: runs after a write the resolver sent has been APPLIED to the row (a refused conditional write never
    /// reaches it) — so a test lands another writer's change between the resolver's write and its next read.
    /// </summary>
    public Action<OwnershipDirectory, Dictionary<string, object>>? AfterWrite { get; set; }

    /// <summary>
    /// #2143: an <see cref="IGenericEntityService.UpdateIfRowVersionMatchesAsync"/> — applied as <see cref="Update"/> only
    /// while the row is still at <paramref name="version"/>; otherwise <see cref="System.Data.DBConcurrencyException"/>.
    /// </summary>
    public void UpdateIfVersion(string entity, Guid id, string version, Dictionary<string, object> fields)
    {
        var current = Version(entity, id).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(current, version, StringComparison.Ordinal))
        {
            ConditionalWrites.Add((entity, id, version, new Dictionary<string, object>(fields), false));
            throw new System.Data.DBConcurrencyException($"Test: {entity} {id} is at version {current}, not {version}.");
        }

        ConditionalWrites.Add((entity, id, version, new Dictionary<string, object>(fields), true));
        Update(entity, id, fields);
    }

    /// <summary>
    /// #2143: a writer's field PATCH landing on the row, as Dataverse applies it — its filing lookups only, read the way the
    /// writers read their own payloads (<see cref="RecordReparent.ParentChangesWithClearsIn{TValue}"/> plus the lineage
    /// lookups, <see cref="RecordReparent.WithLineageLookupsIn{TValue}"/>): an <see cref="EntityReference"/> or an
    /// <c>@odata.bind</c> sets a lookup, a null clears a column. The resolver reads the row AFTER the change, so a harness
    /// that captures the PATCH without landing it would read as a concurrent writer having undone the change. A row this
    /// directory does not hold is left alone; other columns are not modelled.
    /// </summary>
    public void ApplyPatch<TValue>(string entity, Guid id, IEnumerable<KeyValuePair<string, TValue>> fields)
    {
        if (!_records.ContainsKey((entity, id)))
            return;

        var payload = fields.ToList();
        var changes = RecordReparent.WithLineageLookupsIn(
            entity, RecordReparent.ParentChangesWithClearsIn(entity, payload), payload);
        Write(entity, id, changes.ToDictionary(change => change.Key, change => (object?)change.Value));
    }

    /// <summary>#2143: <see cref="ApplyPatch{TValue}"/> for a Web API PATCH addressed as <c>&lt;entity set&gt;(&lt;id&gt;)</c> with a
    /// JSON body — the row is found by its id (test ids are unique across tables).</summary>
    public void ApplyPatch(string relativePath, string jsonBody)
    {
        var open = relativePath.LastIndexOf('(');
        if (open < 0 || !Guid.TryParse(relativePath[(open + 1)..relativePath.LastIndexOf(')')], out var id))
            return;

        var row = _records.Keys.FirstOrDefault(key => key.Id == id);
        if (row.Entity is null)
            return;

        var body = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(jsonBody)
            ?? new Dictionary<string, System.Text.Json.JsonElement>();
        ApplyPatch(row.Entity, id, body);
    }

    /// <summary>
    /// #2143: a write by ANY writer — the change a test's <c>applyChange</c> makes, or another writer's concurrent re-file or
    /// re-own: lookups set (a <see cref="DBNull"/> or <c>null</c> clears), <c>ownerid</c> moves <c>owningteam</c>, and the
    /// version is bumped. Not recorded in <see cref="Assignments"/> or <see cref="FieldUpdates"/> (the resolver's writes).
    /// </summary>
    public void Write(string entity, Guid id, Dictionary<string, object?> fields)
    {
        var row = _records[(entity, id)];
        foreach (var (column, value) in fields)
        {
            if (value is null or DBNull)
            {
                row.Attributes.Remove(column);
            }
            else if (column == "ownerid" && value is EntityReference owner)
            {
                SetOwner(row, owner);
            }
            else
            {
                row[column] = value;
            }
        }

        Bump(row);
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
            SetOwner(row, owner);
            Bump(row);
        }
    }

    /// <summary>
    /// An owner write as Dataverse applies it: <c>ownerid</c> and <c>owningteam</c> name the team, and <c>owningbusinessunit</c>
    /// follows to the team's business unit (#2143 — the re-file reads it to tell a row held in the Secure Record business
    /// unit). A team this directory does not hold leaves the business unit as it was.
    /// </summary>
    private void SetOwner(Entity row, EntityReference owner)
    {
        row["owningteam"] = new EntityReference("team", owner.Id);
        row["ownerid"] = owner;
        if (_teams.FirstOrDefault(t => t.TeamId == owner.Id) is { TeamId: var found } team && found != Guid.Empty)
            row["owningbusinessunit"] = new EntityReference("businessunit", team.BusinessUnit);
    }

    // ── Task 146 b2: the failures a re-file must recover from ───────────────────────────────────────────────────

    /// <summary>When set, an OWNER update throws it — after applying when <see cref="AssignmentLandsBeforeFault"/>
    /// (a lost response), before otherwise (the assignment did not happen).</summary>
    public Exception? AssignmentFault { get; set; }

    /// <summary>With <see cref="AssignmentFault"/>: the assignment is applied, then the fault is thrown.</summary>
    public bool AssignmentLandsBeforeFault { get; set; }

    /// <summary>When set, every single-row read (<c>RetrieveAsync</c> — the owner read-backs) throws it.</summary>
    public Exception? OwnerReadFault { get; set; }

    /// <summary>
    /// When set, every query (<c>RetrieveMultipleAsync</c> — the whole-row and parent reads) throws it. PR #2161 round 5 (K1):
    /// set mid-call (from <see cref="BeforeWrite"/>) so the owner read-back still answers while the filing read-back faults.
    /// </summary>
    public Exception? QueryFault { get; set; }

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

            Bump(row);
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
        _users.Add((systemUserId, objectId, businessUnit, false));
        return this;
    }

    /// <summary>A systemuser with <c>isdisabled = true</c> (issue #1576): it keeps its oid, and a query that filters
    /// <c>isdisabled = false</c> does not return it.</summary>
    public OwnershipDirectory WithDisabledUser(Guid systemUserId, Guid objectId, Guid businessUnit)
    {
        _users.Add((systemUserId, objectId, businessUnit, true));
        return this;
    }

    public OwnershipDirectory WithTeam(Guid teamId, Guid businessUnit, bool isDefault, int teamType, string name = "")
    {
        _teams.Add((teamId, businessUnit, isDefault, teamType, name));
        return this;
    }

    public OwnershipDirectory WithBusinessUnit(Guid businessUnitId, string name, Guid? parent = null)
    {
        _businessUnits.Add((businessUnitId, name, parent));
        return this;
    }

    /// <summary>R4b-2: <paramref name="systemUserId"/> is a member of <paramref name="teamId"/>.</summary>
    public OwnershipDirectory WithTeamMember(Guid teamId, Guid systemUserId)
    {
        _memberships.Add((teamId, systemUserId));
        return this;
    }

    /// <summary>
    /// The shared isolation predicate (ebilling-r1 CLAUDE.md §3, 2026-10-28): <paramref name="teamId"/> is a team of the business unit
    /// named <see cref="SecureBuName"/> — keyed on the Secure Record business unit, never on a team id.
    /// </summary>
    public bool IsTeamOfTheSecureRecordBusinessUnit(Guid teamId) =>
        _teams.Any(t => t.TeamId == teamId
                        && _businessUnits.Any(b => b.BusinessUnitId == t.BusinessUnit
                                                   && string.Equals(b.Name, SecureBuName, StringComparison.OrdinalIgnoreCase)));

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

        // R4b-2: a Null condition (the root business unit) has no value; it is read separately.
        var conditions = query.Criteria.Conditions.Where(c => c.Operator != ConditionOperator.Null).ToDictionary(
            c => c.AttributeName, c => c.Values.Single(), StringComparer.OrdinalIgnoreCase);
        var rootOnly = query.Criteria.Conditions.Any(c =>
            c.Operator == ConditionOperator.Null && string.Equals(c.AttributeName, "parentbusinessunitid", StringComparison.OrdinalIgnoreCase));

        if (query.EntityName == "businessunit" && conditions.ContainsKey("businessunitid"))
            BusinessUnitIdReads++;

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
            // R4b-2c: by id too (the walk to the customer's top unit), with its parent.
            "businessunit" => _businessUnits
                .Where(b => MatchesName(conditions, b.Name) && Matches(conditions, "businessunitid", b.BusinessUnitId)
                            && (!rootOnly || b.Parent is null))
                .Select(b => b.Parent is { } parent
                    ? new Entity("businessunit", b.BusinessUnitId) { ["parentbusinessunitid"] = new EntityReference("businessunit", parent) }
                    : new Entity("businessunit", b.BusinessUnitId)),
            "teammembership" => _memberships
                .Where(m => Matches(conditions, "teamid", m.TeamId))
                .Select(m => new Entity("teammembership", Guid.NewGuid()) { ["systemuserid"] = m.SystemUserId }),
            _ => RecordQuery(query, conditions),
        };

        var list = rows.ToList();
        if (query.TopCount is { } top)
        {
            list = list.Take(top).ToList();
        }

        return new EntityCollection(list);
    }

    private IEnumerable<Entity> RecordQuery(QueryExpression query, IReadOnlyDictionary<string, object> conditions)
    {
        RecordQueries.Add(query);
        return RecordsMatching(query.EntityName, conditions);
    }

    private IEnumerable<Entity> UsersMatching(IReadOnlyDictionary<string, object> conditions)
    {
        // The KEY condition (systemuserid or azureactivedirectoryobjectid) plus, optionally, isdisabled (issue #1576),
        // which Dataverse evaluates against the row like any column. Anything else is not a query this fake answers.
        var (column, key) = conditions.Single(c => !string.Equals(c.Key, "isdisabled", StringComparison.OrdinalIgnoreCase));
        UserKeyColumns.Add(column);
        return _users
            .Where(u => column == "systemuserid" ? u.SystemUserId.Equals(key) : u.ObjectId.Equals(key))
            .Where(u => Matches(conditions, "isdisabled", u.Disabled))
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
        return _records.TryGetValue((entity, id), out var row) ? new[] { Snapshot(row) } : Array.Empty<Entity>();
    }

    /// <summary>
    /// #2143 round 2 (F3c): a COPY of the row, as the SDK hands back — a later write to the directory never changes a row the
    /// resolver already read (the live row aliased into the resolver would make a pre-change read show the change).
    /// </summary>
    private static Entity Snapshot(Entity row)
    {
        var copy = new Entity(row.LogicalName, row.Id) { RowVersion = row.RowVersion };
        foreach (var (column, value) in row.Attributes)
            copy[column] = value;
        return copy;
    }

    /// <summary>An ABSENT condition matches everything — so a dropped predicate widens the match.</summary>
    private static bool Matches(IReadOnlyDictionary<string, object> conditions, string attribute, object value) =>
        !conditions.TryGetValue(attribute, out var expected) || expected.Equals(value);

    /// <summary>Dataverse compares names case-insensitively; an absent name condition matches everything.</summary>
    private static bool MatchesName(IReadOnlyDictionary<string, object> conditions, string name) =>
        !conditions.TryGetValue("name", out var expected)
        || string.Equals(expected as string, name, StringComparison.OrdinalIgnoreCase);
}
