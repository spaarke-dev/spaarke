using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 158 r1c-v1 (verifier item 5): the PRODUCTION <see cref="AssignedAccessStore"/> — the OData
/// filters and selects it sends for the inherited-share provenance (owner round 30) and the Assigned-To scan, and its
/// alternate-key conflict path (verifier item 1) — over an in-memory <c>sprk_assignedaccesses</c> table behind
/// <see cref="DataverseWebApiClient"/>'s virtual methods (ADR-038: no HTTP doubles). The table EVALUATES the <c>$filter</c>
/// it is sent (eq / ne / and / or / not / startswith, with Dataverse's case-insensitive string comparison), returns only the
/// <c>$select</c>ed columns, and answers 412 on a duplicate <c>sprk_ledgerkey</c> (the alternate key). Every other test
/// reaches the ledger through the store's faked internal-virtual seam, so before this class the strings the store sends to
/// Dataverse were exercised only by the live gate.
/// </summary>
[Trait("status", "task-158-uac-r2")]
public class AssignedAccessStoreODataTests
{
    private static readonly Guid Matter = Guid.Parse("15800000-0000-0000-0000-00000000aa01");
    private static readonly Guid OtherMatter = Guid.Parse("15800000-0000-0000-0000-00000000aa02");
    private static readonly Guid Filed = Guid.Parse("15800000-0000-0000-0000-00000000bb01");
    private static readonly Guid OtherFiled = Guid.Parse("15800000-0000-0000-0000-00000000bb02");
    private static readonly Guid Project = Guid.Parse("15800000-0000-0000-0000-00000000cc01");
    private static readonly Guid User = Guid.Parse("15800000-0000-0000-0000-00000000dd01");
    private static readonly Guid Team = Guid.Parse("15800000-0000-0000-0000-00000000dd02");
    private static readonly Guid Contact = Guid.Parse("15800000-0000-0000-0000-00000000dd03");

    private readonly LedgerTable _table = new();
    private readonly AssignedAccessStore _store;

    public AssignedAccessStoreODataTests() =>
        _store = new AssignedAccessStore(_table, NullLogger<AssignedAccessStore>.Instance);

    private static string Inherited(string parentTable, Guid parentId) => AssignedAccessStore.InheritedSourceField(parentTable, parentId);

    /// <summary>
    /// The Assigned-To job's scan (task 142, narrowed by task 158 r1): roots holding a live ledger row — an Assigned-To row,
    /// with or without a source field — are candidates; a root holding only inherited-share provenance is not (those rows
    /// never count toward the scan's bound), and neither is a Revoked or deactivated row's root.
    /// </summary>
    [Fact]
    public async Task TheAssignedToScan_LeavesInheritedShareRowsOut_AndKeepsAnAssignedToRowWithOrWithoutASourceField()
    {
        _table.Add(Row(project: Project, source: "sprk_assignedtointernal", state: AssignedAccessState.Shared, contact: Contact));
        _table.Add(Row(matter: Matter, source: null, state: AssignedAccessState.Granted, contact: Contact));
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(workAssignment: OtherFiled, source: "INHERITED:sprk_matter:" + Matter.ToString("D"), state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(project: OtherMatter, source: "sprk_assignedtointernal", state: AssignedAccessState.Revoked, contact: Contact));
        _table.Add(Row(matter: OtherMatter, source: "sprk_assignedtointernal", state: AssignedAccessState.Shared, contact: Contact, statecode: 1));

        var (roots, truncated) = await _store.ScanLedgerRootsAsync(CancellationToken.None);

        truncated.Should().BeFalse();
        roots.Select(r => r.RootId).Should().BeEquivalentTo(new[] { Project, Matter },
            "an Assigned-To row's root, with or without a source field — never a root holding only inherited-share rows (any " +
            "casing), a Revoked row's or a deactivated row's");
    }

    /// <summary>
    /// Owner round 71: the rootless scan returns the live Assigned-To rows whose EVERY root lookup is empty — never a row that
    /// names a root, a Revoked or deactivated one, or an inherited-share row.
    /// </summary>
    [Fact]
    public async Task TheRootlessScan_ReturnsOnlyLiveAssignedToRowsWhoseEveryRootLookupIsEmpty()
    {
        var key = AssignedAccessStore.LedgerKey(ExternalGrantRootType.Matter, Matter, "sprk_assignedattorney1",
            new AssignedSubject(AssignedSubjectKind.Contact, Contact));
        _table.Add(Row(source: "sprk_assignedattorney1", state: AssignedAccessState.Granted, contact: Contact, key: key));
        _table.Add(Row(source: null, state: AssignedAccessState.Declined, contact: Contact));
        _table.Add(Row(matter: Matter, source: "sprk_assignedattorney1", state: AssignedAccessState.Granted, contact: Contact));
        _table.Add(Row(project: Project, source: "sprk_assignedattorney1", state: AssignedAccessState.Granted, contact: Contact));
        _table.Add(Row(workAssignment: Filed, source: "sprk_assignedattorney1", state: AssignedAccessState.Granted, contact: Contact));
        _table.Add(Row(source: "sprk_assignedattorney1", state: AssignedAccessState.Revoked, contact: Contact));
        _table.Add(Row(source: "sprk_assignedattorney1", state: AssignedAccessState.Granted, contact: Contact, statecode: 1));
        _table.Add(Row(source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User));

        var (rows, truncated) = await _store.ScanRootlessLedgerRowsAsync(CancellationToken.None);

        truncated.Should().BeFalse();
        rows.Select(r => r.State).Should().BeEquivalentTo(new[] { AssignedAccessState.Granted, AssignedAccessState.Declined });
        rows.Should().OnlyContain(r => AssignedAccessStore.RootOf(r) == null);
        rows.Single(r => r.State == AssignedAccessState.Granted).LedgerKey.Should().Be(key, "the key is what names the deleted record");
    }

    /// <summary>
    /// The provenance on ONE filed record (round 30): only its live inherited-share rows, each with the principal it was
    /// passed on to — a user, or a TEAM (<c>sprk_subjectteam</c>, the column round 30 added) — never an Assigned-To row, a
    /// malformed inherited row, another record's row or a deactivated one.
    /// </summary>
    [Fact]
    public async Task TheInheritedLedgerOfARecord_IsItsInheritedRowsOnly_WithTheTeamTheyWerePassedOnTo()
    {
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User, level: 23));
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_project", Project), state: AssignedAccessState.Shared, team: Team, level: 7));
        _table.Add(Row(workAssignment: Filed, source: "sprk_assignedtointernal", state: AssignedAccessState.Shared, contact: Contact, user: User));
        _table.Add(Row(workAssignment: Filed, source: "inherited:not-a-parent", state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(workAssignment: OtherFiled, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_matter", OtherMatter), state: AssignedAccessState.Shared, user: User, statecode: 1));

        var rows = await _store.ReadInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, CancellationToken.None);

        rows.Select(AssignedAccessStore.InheritedPrincipalOf).Should().BeEquivalentTo(new DataversePrincipalRef?[]
        {
            DataversePrincipalRef.User(User),
            DataversePrincipalRef.Team(Team),
        }, "the user's and the team's rows — the team read through its own column");
        rows.Single(r => r.SubjectTeamId == Team).GrantedLevel.Should().Be(7, "the mask written comes back with the row");
    }

    /// <summary>The reverse fan-out's read (round 30): every LIVE row passed on from one parent, on any filed record — no other parent's.</summary>
    [Fact]
    public async Task TheInheritedLedgerOfAParent_IsEveryLiveRowPassedOnFromIt_OnAnyRecord()
    {
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(project: Project, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Declined, team: Team));
        _table.Add(Row(workAssignment: Filed, source: Inherited("sprk_matter", OtherMatter), state: AssignedAccessState.Shared, user: User));
        _table.Add(Row(workAssignment: OtherFiled, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User, statecode: 1));

        var (rows, truncated) = await _store.ReadInheritedLedgerByParentAsync("sprk_matter", Matter, CancellationToken.None);

        truncated.Should().BeFalse();
        rows.Select(r => AssignedAccessStore.RootOf(r)!.Value.RootId).Should().BeEquivalentTo(new[] { Filed, Project });
        rows.Single(r => r.ProjectId == Project).SubjectTeamId.Should().Be(Team);
    }

    /// <summary>
    /// Task 158 final round (main-session round 58 item 2): only rows still IN FORCE count toward the by-parent read's bound —
    /// a parent's ended (Revoked) history is left out in the query, so it can never wedge the unsecure's Step 4.5. A row whose
    /// state is EMPTY reads as Skipped and stays in force. Rows in force past the bound still report truncated (fail closed).
    /// </summary>
    [Fact]
    public async Task TheInheritedLedgerOfAParent_CountsOnlyRowsInForceTowardItsBound_AndTruncatesPastIt()
    {
        for (var i = 0; i < AssignedAccessStore.MaxScanRows + 10; i++)
            _table.Add(Row(workAssignment: Guid.NewGuid(), source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Revoked, user: User));
        var inForce = Enumerable.Range(0, AssignedAccessStore.MaxScanRows - 1)
            .Select(_ => Row(workAssignment: Guid.NewGuid(), source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User))
            .ToList();
        var emptyState = Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), user: User);
        emptyState["sprk_state"] = null;
        inForce.Add(emptyState);
        inForce.ForEach(_table.Add);

        var (rows, truncated) = await _store.ReadInheritedLedgerByParentAsync("sprk_matter", Matter, CancellationToken.None);

        truncated.Should().BeFalse("the ended history never counts toward the bound");
        rows.Should().HaveCount(AssignedAccessStore.MaxScanRows);
        rows.Should().NotContain(r => r.State == AssignedAccessState.Revoked);
        rows.Should().Contain(r => r.WorkAssignmentId == Filed, "an empty state is in force (read as Skipped, re-evaluated)");

        _table.Add(Row(workAssignment: Guid.NewGuid(), source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Declined, user: User));

        (await _store.ReadInheritedLedgerByParentAsync("sprk_matter", Matter, CancellationToken.None)).Truncated
            .Should().BeTrue("rows in force past the bound still fail closed");
    }

    /// <summary>
    /// Verifier item 1, the race: a concurrent pass created the row for the same (filed record, parent, principal) first —
    /// the alternate key answers 412. The store answers <c>null</c> and writes NOTHING over the winner's row: not a "covered
    /// by existing" (direct) record over a share the winner passed on, not anything over an operator's decision.
    /// </summary>
    [Fact]
    public async Task AnInheritedRowCreate_ThatLosesTheRaceToTheKey_AnswersNull_AndWritesNothingOverTheRowThatWon()
    {
        var principal = DataversePrincipalRef.User(User);
        var winner = Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared, user: User,
            level: 23, reason: AssignedAccessReason.RaisedFromMaskPrefix + "1",
            key: AssignedAccessStore.InheritedLedgerKey(ExternalGrantRootType.WorkAssignment, Filed, "sprk_matter", Matter, principal));
        _table.Add(winner);

        var id = await _store.CreateInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, "sprk_matter", Matter, principal,
            new AssignedAccessLedgerWrite(AssignedAccessState.CoveredByExisting, AssignedAccessReason.CoveredByExistingShare, GrantedLevel: 23),
            CancellationToken.None);

        id.Should().BeNull("it lost the race: not recorded");
        _table.Updates.Should().BeEmpty("nothing is written over the row a concurrent pass created");
        var row = (await _store.ReadInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, CancellationToken.None)).Single();
        row.State.Should().Be(AssignedAccessState.Shared, "never downgraded to direct access");
        row.Reason.Should().Be(AssignedAccessReason.RaisedFromMaskPrefix + "1");
    }

    /// <summary>A 412 that is NOT the key (no row reads back for it) is a fault, never "lost the race".</summary>
    [Fact]
    public async Task AnInheritedRowCreate_RefusedWithoutARowToReadBack_Throws()
    {
        _table.RefuseNextCreate = true;

        var create = () => _store.CreateInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, "sprk_matter", Matter,
            DataversePrincipalRef.User(User), new AssignedAccessLedgerWrite(AssignedAccessState.Shared, null, GrantedLevel: 23),
            CancellationToken.None);

        await create.Should().ThrowAsync<HttpRequestException>();
    }

    /// <summary>
    /// The create payload round 30 needs: the filed record bound, the principal bound as a TEAM (or a user), the ledger key
    /// and the mask written — read back through the store's own inherited-row read as the principal it was passed on to.
    /// </summary>
    [Fact]
    public async Task AnInheritedRowCreate_BindsTheRecordAndATeamSubject_AndReadsBackAsThatTeamWithTheMaskWritten()
    {
        var team = DataversePrincipalRef.Team(Team);

        var id = await _store.CreateInheritedLedgerAsync(ExternalGrantRootType.Project, Project, "sprk_matter", Matter, team,
            new AssignedAccessLedgerWrite(AssignedAccessState.Shared, AssignedAccessReason.SharePending, GrantedLevel: 23), CancellationToken.None);

        id.Should().NotBeNull();
        var row = (await _store.ReadInheritedLedgerAsync(ExternalGrantRootType.Project, Project, CancellationToken.None)).Single();
        AssignedAccessStore.InheritedPrincipalOf(row).Should().Be(team);
        row.SystemUserId.Should().BeNull("a team is bound as the team, never as a user");
        row.LedgerKey.Should().Be(AssignedAccessStore.InheritedLedgerKey(ExternalGrantRootType.Project, Project, "sprk_matter", Matter, team));
        row.GrantedLevel.Should().Be(23);
        row.Reason.Should().Be(AssignedAccessReason.SharePending);
        AssignedAccessStore.InheritedSourceOf(row.SourceField).Should().Be(("sprk_matter", Matter));
    }

    // ── Batch-4 integration, 158 × 140 (round 47 item 2): the conditional update, over the client's If-Match seam ────

    private static readonly AssignedAccessLedgerWrite Confirm =
        new(AssignedAccessState.Shared, Reason: null, GrantedLevel: 23);

    /// <summary>
    /// The store sends the version the row was READ at as <c>If-Match</c> (task 140's <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>).
    /// When an operator's Declined marker lands first, that send is refused (412). The row is read again, and because it no
    /// longer holds what the decision was made on, nothing is written over the marker.
    /// </summary>
    [Fact]
    public async Task AConditionalUpdate_SendsTheVersionItWasReadAt_AndNeverWritesOverARowThatChangedMeanwhile()
    {
        var stored = Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared,
            user: User, level: 23, reason: AssignedAccessReason.SharePending);
        _table.Add(stored);
        var read = (await _store.ReadInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, CancellationToken.None)).Single();
        read.ETag.Should().NotBeNullOrWhiteSpace("the Web API returns @odata.etag on every row, whatever the $select");
        _table.BeforeConditionalUpdate = id => _table.Touch(id, ("sprk_state", (int)AssignedAccessState.Declined),
            ("sprk_reason", AssignedAccessReason.RemovedOutOfBand));

        var written = await _store.UpdateLedgerIfUnchangedAsync(read, Confirm, CancellationToken.None);

        written.Should().BeFalse();
        _table.ConditionalUpdates.Should().ContainSingle().Which.Should().Be((read.Id, read.ETag!),
            "one send, with the version read; the re-read finds the row changed, so nothing is sent again");
        stored["sprk_state"].Should().Be((int)AssignedAccessState.Declined, "the operator's marker stands");
        stored["sprk_reason"].Should().Be(AssignedAccessReason.RemovedOutOfBand);
        _table.Updates.Should().BeEmpty("no unconditional write is ever made");
    }

    /// <summary>
    /// A row whose version moved on while its facts stayed the same (a write that set the same values): the decision still
    /// stands, so the write is sent once more at the fresh version and lands. The row in memory then holds what was written,
    /// and its spent version is cleared.
    /// </summary>
    [Fact]
    public async Task AConditionalUpdate_WhenOnlyTheVersionMoved_IsSentAgainAtTheFreshVersion()
    {
        var stored = Row(workAssignment: Filed, source: Inherited("sprk_matter", Matter), state: AssignedAccessState.Shared,
            user: User, level: 23, reason: AssignedAccessReason.SharePending);
        _table.Add(stored);
        var read = (await _store.ReadInheritedLedgerAsync(ExternalGrantRootType.WorkAssignment, Filed, CancellationToken.None)).Single();
        var once = false;
        _table.BeforeConditionalUpdate = id =>
        {
            if (once)
                return;
            once = true;
            _table.Touch(id); // same facts, new version
        };

        var written = await _store.UpdateLedgerIfUnchangedAsync(read, Confirm, CancellationToken.None);

        written.Should().BeTrue();
        _table.ConditionalUpdates.Should().HaveCount(2);
        _table.ConditionalUpdates[1].ETag.Should().NotBe(_table.ConditionalUpdates[0].ETag, "the second send carries the fresh version");
        stored["sprk_reason"].Should().BeNull("confirmed");
        read.Reason.Should().BeNull("the row in memory holds what was written");
        read.ETag.Should().BeNull("its version is spent: a later write in the same pass reads it again first");
    }

    // ── The table ─────────────────────────────────────────────────────────────────────────────────────────────────

    private static Dictionary<string, object?> Row(
        Guid? project = null, Guid? matter = null, Guid? workAssignment = null, string? source = null,
        AssignedAccessState state = AssignedAccessState.Shared, Guid? user = null, Guid? team = null, Guid? contact = null,
        int? level = null, string? reason = null, string? key = null, int statecode = 0) => new(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_assignedaccessid"] = Guid.NewGuid(),
            ["sprk_ledgerkey"] = key ?? Guid.NewGuid().ToString("N"),
            ["sprk_sourcefield"] = source,
            ["_sprk_project_value"] = project,
            ["_sprk_matter_value"] = matter,
            ["_sprk_workassignment_value"] = workAssignment,
            ["_sprk_subjectsystemuser_value"] = user,
            ["_sprk_subjectteam_value"] = team,
            ["_sprk_subjectcontact_value"] = contact,
            ["_sprk_subjectorganization_value"] = null,
            ["_sprk_externalrecordaccess_value"] = null,
            ["sprk_state"] = (int)state,
            ["sprk_reason"] = reason,
            ["sprk_grantedlevel"] = level,
            ["sprk_grantedexpiry"] = null,
            ["statecode"] = statecode,
        };

    /// <summary>
    /// An in-memory <c>sprk_assignedaccesses</c> table that EVALUATES the OData it is sent (see the class summary). Binds in a
    /// create or update payload (<c>sprk_SubjectTeam@odata.bind = /teams(…)</c>) land on their lookup value column
    /// (<c>_sprk_subjectteam_value</c>), as Dataverse stores them.
    /// </summary>
    private sealed class LedgerTable : DataverseWebApiClient
    {
        private const string Set = "sprk_assignedaccesses";
        private readonly List<Dictionary<string, object?>> _rows = new();

        public LedgerTable()
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                NoCredential.Instance)
        {
        }

        public List<(Guid Id, string Payload)> Updates { get; } = new();

        /// <summary>Every conditional update sent: (row id, the version sent as <c>If-Match</c>).</summary>
        public List<(Guid Id, string ETag)> ConditionalUpdates { get; } = new();

        /// <summary>Runs before a conditional update is evaluated: a write landing between the store's read and its write.</summary>
        public Action<Guid>? BeforeConditionalUpdate { get; set; }

        private long _version;

        private string NextVersion() => $"W/\"{++_version}\"";

        /// <summary>Another writer's change to a row (no values: the same facts at a new version).</summary>
        public void Touch(Guid id, params (string Column, object? Value)[] values)
        {
            var row = _rows.Single(r => (Guid)r["sprk_assignedaccessid"]! == id);
            foreach (var (column, value) in values)
                row[column] = value;
            row["@odata.etag"] = NextVersion();
        }

        /// <summary>
        /// The production contract of <see cref="DataverseWebApiClient.UpdateIfMatchAsync"/>. With no version, nothing is
        /// sent. A version other than the row's is refused with 412, and a missing row with 404. Nothing is written on either.
        /// </summary>
        public override Task UpdateIfMatchAsync(string entitySetName, Guid id, object entity, string etag,
            CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be(Set);
            if (string.IsNullOrWhiteSpace(etag))
                throw new ArgumentException("A conditional update needs the row's ETag as it was read; nothing was sent.", nameof(etag));

            ConditionalUpdates.Add((id, etag));
            BeforeConditionalUpdate?.Invoke(id);
            var row = _rows.SingleOrDefault(r => (Guid)r["sprk_assignedaccessid"]! == id)
                ?? throw new KeyNotFoundException($"{Set}({id}) was not found; nothing was created.");
            if (!string.Equals(row["@odata.etag"] as string, etag, StringComparison.Ordinal))
                throw new System.Data.DBConcurrencyException($"{Set}({id}) changed since it was read; the update was not applied.");

            Apply(row, (IDictionary<string, object?>)entity);
            row["@odata.etag"] = NextVersion();
            return Task.CompletedTask;
        }

        /// <summary>The next create answers 412 without a row behind it (a refusal that is not the alternate key).</summary>
        public bool RefuseNextCreate { get; set; }

        public void Add(Dictionary<string, object?> row)
        {
            row["@odata.etag"] = NextVersion();
            _rows.Add(row);
        }

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null,
            int? top = null, int? skip = null, CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be(Set);
            var predicate = filter is null ? (_ => true) : ODataFilter.Parse(filter);
            var columns = select?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rows = _rows.Where(predicate)
                .Take(top ?? int.MaxValue)
                .Select(r => columns is null ? r : columns.Append("@odata.etag").Distinct()
                    .ToDictionary(c => c, c => r.TryGetValue(c, out var v) ? v : null))
                .ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }

        public override Task<Guid> CreateAsync(string entitySetName, object entity, CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be(Set);
            if (RefuseNextCreate)
            {
                RefuseNextCreate = false;
                throw new HttpRequestException("Simulated 412 with no row behind it.", null, HttpStatusCode.PreconditionFailed);
            }

            var row = Row();
            foreach (var key in row.Keys.Where(k => k != "sprk_assignedaccessid" && k != "statecode").ToList())
                row[key] = null;
            Apply(row, (IDictionary<string, object?>)entity);
            if (_rows.Any(r => string.Equals(r["sprk_ledgerkey"] as string, row["sprk_ledgerkey"] as string, StringComparison.OrdinalIgnoreCase)))
                throw new HttpRequestException("Duplicate alternate key sprk_AssignedAccessLedgerKey.", null, HttpStatusCode.PreconditionFailed);

            row["@odata.etag"] = NextVersion();
            _rows.Add(row);
            return Task.FromResult((Guid)row["sprk_assignedaccessid"]!);
        }

        public override Task UpdateAsync(string entitySetName, Guid id, object entity, CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be(Set);
            Updates.Add((id, JsonSerializer.Serialize(entity)));
            var row = _rows.Single(r => (Guid)r["sprk_assignedaccessid"]! == id);
            Apply(row, (IDictionary<string, object?>)entity);
            row["@odata.etag"] = NextVersion();
            return Task.CompletedTask;
        }

        private static void Apply(Dictionary<string, object?> row, IDictionary<string, object?> payload)
        {
            foreach (var (key, value) in payload)
            {
                if (key.EndsWith("@odata.bind", StringComparison.Ordinal))
                {
                    var navigation = key[..^"@odata.bind".Length].ToLowerInvariant();
                    row[$"_{navigation}_value"] = Guid.Parse(Regex.Match((string)value!, @"\(([0-9a-fA-F-]{36})\)").Groups[1].Value);
                }
                else
                {
                    row[key] = value;
                }
            }
        }
    }

    /// <summary>
    /// The subset of OData <c>$filter</c> the store sends — <c>a eq v</c>, <c>a ne v</c>, <c>and</c>, <c>or</c>, <c>not</c>,
    /// parentheses, <c>startswith(a,'v')</c>; values <c>null</c>, integers, bare GUIDs and quoted strings — evaluated as
    /// Dataverse evaluates it, in SQL: string comparison is case-insensitive, and a comparison or <c>startswith</c> against an
    /// EMPTY column is UNKNOWN (three-valued logic — <c>not</c> keeps it unknown, and only a row whose filter is TRUE is
    /// returned); only <c>eq null</c> / <c>ne null</c> test emptiness. That is why the store writes
    /// <c>sprk_sourcefield eq null or not startswith(…)</c>. Anything else throws (a filter this double does not understand is
    /// a test failure, never "no match").
    /// </summary>
    private static class ODataFilter
    {
        public static Func<Dictionary<string, object?>, bool> Parse(string filter)
        {
            var tokens = Regex.Matches(filter, @"'(?:[^']|'')*'|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|[A-Za-z_][A-Za-z0-9_]*|-?\d+|[(),]")
                .Select(m => m.Value).ToList();
            var position = 0;
            var expression = Or();
            if (position != tokens.Count)
                throw new NotSupportedException($"Unparsed OData after '{string.Join(" ", tokens.Take(position))}' in: {filter}");
            return row => expression(row) == true;

            string Peek() => position < tokens.Count ? tokens[position] : string.Empty;
            string Next() => position < tokens.Count ? tokens[position++] : throw new NotSupportedException($"Truncated OData: {filter}");
            void Expect(string token)
            {
                if (Next() != token)
                    throw new NotSupportedException($"Expected '{token}' in: {filter}");
            }

            Func<Dictionary<string, object?>, bool?> Or()
            {
                var left = And();
                while (Peek() == "or")
                {
                    Next();
                    var (l, r) = (left, And());
                    left = row => (l(row), r(row)) switch
                    {
                        (true, _) or (_, true) => true,
                        (null, _) or (_, null) => null,
                        _ => false,
                    };
                }

                return left;
            }

            Func<Dictionary<string, object?>, bool?> And()
            {
                var left = Unary();
                while (Peek() == "and")
                {
                    Next();
                    var (l, r) = (left, Unary());
                    left = row => (l(row), r(row)) switch
                    {
                        (false, _) or (_, false) => false,
                        (null, _) or (_, null) => null,
                        _ => true,
                    };
                }

                return left;
            }

            Func<Dictionary<string, object?>, bool?> Unary()
            {
                if (Peek() == "not")
                {
                    Next();
                    var inner = Unary();
                    return row => inner(row) is { } known ? !known : null;
                }

                if (Peek() == "(")
                {
                    Next();
                    var inner = Or();
                    Expect(")");
                    return inner;
                }

                if (Peek() == "startswith")
                {
                    Next();
                    Expect("(");
                    var column = Next();
                    Expect(",");
                    var prefix = Literal(Next()) as string ?? throw new NotSupportedException($"startswith needs a string in: {filter}");
                    Expect(")");
                    return row => row.TryGetValue(column, out var v) && v is string s
                        ? s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        : null;
                }

                var name = Next();
                var op = Next();
                var literal = Literal(Next());
                return op switch
                {
                    "eq" => row => Compare(row.TryGetValue(name, out var v) ? v : null, literal),
                    "ne" => row => Compare(row.TryGetValue(name, out var v) ? v : null, literal) is { } equal ? !equal : null,
                    _ => throw new NotSupportedException($"Operator '{op}' in: {filter}"),
                };
            }
        }

        /// <summary><c>eq</c>: emptiness when the literal is <c>null</c>; otherwise UNKNOWN against an empty column.</summary>
        private static bool? Compare(object? value, object? literal) =>
            literal is null ? value is null
            : value is null ? null
            : EqualsValue(value, literal);

        private static object? Literal(string token) =>
            token == "null" ? null
            : token.StartsWith('\'') ? token[1..^1].Replace("''", "'", StringComparison.Ordinal)
            : Guid.TryParse(token, out var guid) ? guid
            : long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number
            : throw new NotSupportedException($"Literal '{token}'");

        private static bool EqualsValue(object value, object literal) => (value, literal) switch
        {
            (string s, string l) => string.Equals(s, l, StringComparison.OrdinalIgnoreCase),
            (Guid g, Guid l) => g == l,
            (string s, Guid l) => Guid.TryParse(s, out var g) && g == l,
            (int i, long l) => i == l,
            (long i, long l) => i == l,
            _ => throw new NotSupportedException($"Comparing {value.GetType().Name} with {literal.GetType().Name}"),
        };
    }

    private sealed class NoCredential : TokenCredential
    {
        public static readonly NoCredential Instance = new();

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => throw new NotSupportedException("This double issues no HTTP.");
    }
}
