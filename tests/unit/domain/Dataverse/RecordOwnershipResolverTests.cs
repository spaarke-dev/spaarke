using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// Unit tests for <see cref="RecordOwnershipResolver"/> — write-path invariant I-6 (spaarkeai-word-add-in-r1 task 080):
/// every record the BFF creates is owned by a business unit's DEFAULT OWNER TEAM, resolved RECORD-FIRST.
/// </summary>
/// <remarks>
/// <para>
/// These guard an ACCESS-CONTROL invariant. A wrong answer moves a record into the wrong business unit, and a
/// fallback that answers when it should refuse is worse than no answer: a secure record's child owned by the
/// caller's GENERAL business unit is readable by everyone in that unit. So the two refusals are pinned as hard as the
/// happy paths — (1) a named target that cannot be read, and (2) nothing to resolve from at all.
/// </para>
/// <para>
/// The Dataverse boundary is a small in-memory directory that answers the three queries by evaluating their
/// CONDITIONS, not by recognising their shape. That is what lets a test catch a dropped predicate: the directory holds
/// a non-default Owner team and a default Access team beside each unit's default Owner team, exactly as dev does, so a
/// team query missing <c>isdefault</c> or <c>teamtype</c> matches two rows and the resolver refuses.
/// </para>
/// </remarks>
public class RecordOwnershipResolverTests
{
    private static readonly Guid GeneralBu = Guid.Parse("06fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildBu = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SecureBu = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000001");

    private static readonly Guid GeneralTeam = Guid.Parse("09fbf21c-1872-f011-b4cb-7c1e52671ad0");
    private static readonly Guid ChildTeam = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7");

    /// <summary>The Secure Record BU's DEFAULT team — what this resolver answered before task 144, and must never again.</summary>
    private static readonly Guid SecureDefaultTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000002");

    /// <summary>The Secure Record BU's NAMED owner team (task 144) — the owner of every secure record and its children.</summary>
    private static readonly Guid SecureNamedTeam = Guid.Parse("d9ec0b6f-0000-4000-8000-000000000003");

    private const string SecureBuName = "Secure Record";
    private const string SecureOwnerTeamName = "Secure Record Owners";

    private static readonly Guid CallerUserId = Guid.Parse("8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c");
    private static readonly Guid CallerOid = Guid.Parse("5a5a5a5a-0000-4000-8000-00000000cafe");

    private static readonly Guid MatterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecureProjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    // =====================================================================================
    // Record-first: a filed record follows what it is filed against
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToARecord_ReturnsThatRecordsBusinessUnitTeam_NotTheCallers()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                CallerSystemUserId = CallerUserId, // sits in GeneralBu — must NOT decide
            },
            CancellationToken.None);

        team.Should().Be(ChildTeam, "the document belongs with its matter, whoever uploaded it");
        directory.QueriedEntities.Should().NotContain("systemuser", "a resolvable target never consults the caller");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToASecureRecord_OwnsItByTheNamedSecureTeam_NeverTheDefaultOrTheCallers()
    {
        // The cross-business-unit case that matters: a caller in the general unit files to a SECURE project. The
        // child must land with the team that owns the secure record itself — the Secure Record BU's NAMED owner team
        // (task 144). Owned by the general team it would be readable by everyone there; owned by the Secure Record
        // BU's DEFAULT team it would follow that team's uncurated membership, the hole #967 closes.
        var directory = Directory().WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        team.Should().Be(SecureNamedTeam).And.NotBe(SecureDefaultTeam).And.NotBe(GeneralTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheSecureRecordsNamedTeamIsMissing_Refuses_NeverFallsBackToTheDefaultTeam()
    {
        var directory = Directory(withNamedSecureTeam: false).WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = SecureProjectId },
            CancellationToken.None);

        team.Should().BeNull("the Secure Record BU's default team is present in the directory and must not be chosen");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTwoNamedSecureTeamsMatch_Refuses()
    {
        var directory = Directory()
            .WithTeam(Guid.NewGuid(), SecureBu, isDefault: false, teamType: 0, SecureOwnerTeamName)
            .WithRecord("sprk_matter", MatterId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().BeNull("two teams answering one name is ambiguous; the resolver never picks one");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenOnlyTheDefaultSecureTeamCarriesTheOwnerTeamName_Refuses()
    {
        // The case only `isdefault = false` defends: the default team bears the configured owner-team name and no
        // named team exists. Dropping that predicate would hand every secure child to the default team.
        var directory = new FakeDirectory()
            .WithBusinessUnit(SecureBu, SecureBuName)
            .WithTeam(SecureDefaultTeam, SecureBu, isDefault: true, teamType: 0, SecureOwnerTeamName)
            .WithRecord("sprk_project", SecureProjectId, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_project", TargetRecordId = SecureProjectId },
            CancellationToken.None);

        team.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheSecureBusinessUnitNameIsAmbiguous_Refuses()
    {
        var directory = Directory()
            .WithBusinessUnit(Guid.NewGuid(), SecureBuName) // a second BU with the configured name
            .WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "sprk_matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().BeNull("whether this business unit is the Secure Record BU cannot be decided");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheActingUserSitsInTheSecureBusinessUnit_StillNeverAnswersTheDefaultTeam()
    {
        // Should never happen — the Secure Record BU holds no users, and provisioning plus the census job report
        // otherwise — but if it does, an unfiled record is owned by the named team, never the retired default team.
        var userInSecureBuOid = Guid.Parse("5a5a5a5a-0000-4000-8000-0000000005ec");
        var directory = Directory().WithUser(Guid.NewGuid(), userInSecureBuOid, SecureBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = userInSecureBuOid },
            CancellationToken.None);

        team.Should().Be(SecureNamedTeam).And.NotBe(SecureDefaultTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheTargetIsNamedByItsFriendlyAlias_ReadsTheLogicalEntity()
    {
        // A queued job's AssociationType may carry "matter"; reading an entity called "matter" would fail and
        // REFUSE a legitimate save.
        var directory = Directory().WithRecord("sprk_matter", MatterId, ChildBu);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { TargetEntityLogicalName = "Matter", TargetRecordId = MatterId },
            CancellationToken.None);

        team.Should().Be(ChildTeam);
        directory.QueriedEntities.Should().Contain("sprk_matter").And.NotContain("matter");
    }

    // =====================================================================================
    // Refuse branch 1 — a NAMED target that cannot be resolved never falls back to the caller
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenTheNamedTargetDoesNotExist_RefusesWithoutFallingBackToTheCaller()
    {
        var directory = Directory(); // no such project row

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId, // resolvable — and must not be used
            },
            CancellationToken.None);

        team.Should().BeNull("an unresolvable target might be secure; answering with the caller's unit could expose it");
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenReadingTheNamedTargetFaults_PropagatesTheFault_AndNeverUsesTheCaller()
    {
        // A throttled or failed read is not an answer. Refusing would tell the user to check a record that is fine
        // (a permanent 403 for a transient fault); falling back would risk a secure record's isolation. It surfaces.
        var directory = Directory().WithUnreadable("sprk_project");

        var act = () => Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_project",
                TargetRecordId = SecureProjectId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheNamedTargetHasNoOwningBusinessUnit_Refuses()
    {
        var directory = Directory().WithRecord("sprk_matter", MatterId, owningBusinessUnit: null);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = "sprk_matter",
                TargetRecordId = MatterId,
                CallerSystemUserId = CallerUserId,
            },
            CancellationToken.None);

        team.Should().BeNull();
        directory.QueriedEntities.Should().NotContain("systemuser");
    }

    // =====================================================================================
    // Nothing named: the acting user's business unit
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenFiledToNothing_ReturnsTheCallersBusinessUnitTeam()
    {
        var team = await Build(Directory()).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenOnlyTheCallersObjectIdIsKnown_LooksTheUserUpByObjectId()
    {
        // The background path: a queued save carries the Entra oid, never a systemuserid.
        var directory = Directory();

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = CallerOid },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
        directory.UserKeyColumns.Should().ContainSingle().Which.Should().Be("azureactivedirectoryobjectid");
    }

    // =====================================================================================
    // Refuse branch 2 — nothing to resolve from, or an ambiguous answer
    // =====================================================================================

    [Fact]
    public async Task ResolveOwningTeam_WhenNeitherTargetNorCallerIsKnown_Refuses()
    {
        var directory = Directory();

        var team = await Build(directory).ResolveOwningTeamAsync(new RecordOwnershipContext(), CancellationToken.None);

        team.Should().BeNull("an app-owned record in ROOT is the defect this resolver exists to remove");
        directory.QueriedEntities.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheCallerMapsToTwoUsers_RefusesRatherThanChoosingABusinessUnit()
    {
        var directory = Directory().WithUser(Guid.NewGuid(), CallerOid, ChildBu); // a second user, same oid

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerObjectId = CallerOid },
            CancellationToken.None);

        team.Should().BeNull("the same fact must have one answer — RecordContainerResolver refuses this case too");
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheBusinessUnitHasNoDefaultOwnerTeam_Refuses()
    {
        var directory = Directory(withDefaultTeams: false);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().BeNull();
    }

    [Fact]
    public async Task ResolveOwningTeam_WhenTheUnitAlsoHasNonDefaultAndAccessTeams_ReturnsOnlyTheDefaultOwnerTeam()
    {
        // Both team predicates are load-bearing. The directory holds, in the SAME unit as the default Owner team, a
        // non-default Owner team and a default Access team (dev has both). Drop `isdefault` and two Owner teams
        // match; drop `teamtype` and two default teams match — either way the answer becomes ambiguous and wrong.
        // Decoys are inserted BEFORE the default Owner team, so a dropped predicate is caught even if TOP 2 also
        // regressed to TOP 1 (the first matching row would then be a decoy, not the answer).
        var directory = new FakeDirectory()
            .WithUser(CallerUserId, CallerOid, GeneralBu)
            .WithTeam(Guid.NewGuid(), GeneralBu, isDefault: false, teamType: 0)
            .WithTeam(Guid.NewGuid(), GeneralBu, isDefault: true, teamType: 1)
            .WithTeam(GeneralTeam, GeneralBu, isDefault: true, teamType: 0);

        var team = await Build(directory).ResolveOwningTeamAsync(
            new RecordOwnershipContext { CallerSystemUserId = CallerUserId },
            CancellationToken.None);

        team.Should().Be(GeneralTeam);
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private static RecordOwnershipResolver Build(FakeDirectory directory)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) => directory.Answer(query));

        // The configured names are set explicitly, so a test proves the CONFIGURED names are honoured.
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SecureRecord:BusinessUnitName"] = SecureBuName,
            ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
        }).Build();

        return new RecordOwnershipResolver(entities.Object, configuration, NullLogger<RecordOwnershipResolver>.Instance);
    }

    /// <summary>
    /// One caller in the general unit; three named business units; each unit's default Owner team present unless told
    /// otherwise; and in the Secure Record BU the NAMED owner team plus two near-miss decoys (task 144) — an ACCESS team
    /// with the right name and an OWNER team with the wrong name — inserted BEFORE the named team, so a named-team
    /// query that dropped <c>teamtype</c> or <c>name</c> matches two rows (or a decoy first) and refuses.
    /// </summary>
    private static FakeDirectory Directory(bool withDefaultTeams = true, bool withNamedSecureTeam = true)
    {
        var directory = new FakeDirectory()
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
    /// An in-memory Dataverse that evaluates a query's Equal conditions against its rows and honours TopCount.
    /// </summary>
    private sealed class FakeDirectory
    {
        private readonly Dictionary<(string Entity, Guid Id), Guid?> _records = new();
        private readonly HashSet<string> _unreadable = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<(Guid SystemUserId, Guid ObjectId, Guid BusinessUnit)> _users = new();
        private readonly List<(Guid TeamId, Guid BusinessUnit, bool IsDefault, int TeamType, string Name)> _teams = new();
        private readonly List<(Guid BusinessUnitId, string Name)> _businessUnits = new();

        public List<string> QueriedEntities { get; } = new();
        public List<string> UserKeyColumns { get; } = new();

        public FakeDirectory WithRecord(string entity, Guid id, Guid? owningBusinessUnit)
        {
            _records[(entity, id)] = owningBusinessUnit;
            return this;
        }

        public FakeDirectory WithUnreadable(string entity)
        {
            _unreadable.Add(entity);
            return this;
        }

        public FakeDirectory WithUser(Guid systemUserId, Guid objectId, Guid businessUnit)
        {
            _users.Add((systemUserId, objectId, businessUnit));
            return this;
        }

        public FakeDirectory WithTeam(Guid teamId, Guid businessUnit, bool isDefault, int teamType, string name = "")
        {
            _teams.Add((teamId, businessUnit, isDefault, teamType, name));
            return this;
        }

        public FakeDirectory WithBusinessUnit(Guid businessUnitId, string name)
        {
            _businessUnits.Add((businessUnitId, name));
            return this;
        }

        public EntityCollection Answer(QueryExpression query)
        {
            QueriedEntities.Add(query.EntityName);
            var conditions = query.Criteria.Conditions.ToDictionary(
                c => c.AttributeName, c => c.Values.Single(), StringComparer.OrdinalIgnoreCase);

            IEnumerable<Entity> rows = query.EntityName switch
            {
                "systemuser" => UsersMatching(conditions),
                "team" => _teams
                    .Where(t => Matches(conditions, "businessunitid", t.BusinessUnit)
                                && Matches(conditions, "isdefault", t.IsDefault)
                                && Matches(conditions, "teamtype", t.TeamType)
                                && MatchesName(conditions, t.Name))
                    .Select(t => new Entity("team", t.TeamId)),
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
            if (!_records.TryGetValue((entity, id), out var businessUnit))
            {
                return Array.Empty<Entity>();
            }

            var row = new Entity(entity, id);
            if (businessUnit is { } bu)
            {
                row["owningbusinessunit"] = new EntityReference("businessunit", bu);
            }

            return new[] { row };
        }

        /// <summary>An ABSENT condition matches everything — so a dropped predicate widens the match.</summary>
        private static bool Matches(IReadOnlyDictionary<string, object> conditions, string attribute, object value) =>
            !conditions.TryGetValue(attribute, out var expected) || expected.Equals(value);

        /// <summary>Dataverse compares names case-insensitively; an absent name condition matches everything.</summary>
        private static bool MatchesName(IReadOnlyDictionary<string, object> conditions, string name) =>
            !conditions.TryGetValue("name", out var expected)
            || string.Equals(expected as string, name, StringComparison.OrdinalIgnoreCase);
    }
}
