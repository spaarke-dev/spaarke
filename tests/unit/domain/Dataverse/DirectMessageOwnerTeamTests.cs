using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Dataverse;

/// <summary>
/// field-mapping-server-write-path-r1 task 054a — <see cref="DirectMessageOwnerTeam"/>: a Direct 1:1 message not filed under a
/// record is owned by a NAMED, non-default, memberless Owner team in a dedicated business unit that holds no users, is a direct
/// child of the root, and is neither the root nor the Secure Record unit. Every other configuration refuses with its own stable
/// reason code; the verdict is cached briefly and a fault never is. The Secure Record owner team, found by the same
/// parameterised lookup, keeps its behaviour.
/// </summary>
/// <remarks>
/// The fake answers each read only when its filter is exactly the one the lookup's own filter builders produce, so a lookup
/// that dropped or changed a predicate finds nothing (ADR-038 A2).
/// </remarks>
public class DirectMessageOwnerTeamTests
{
    private const string DirectBuName = "Direct Messages";
    private const string DirectTeamName = "Direct Message Owners";

    private static readonly Guid RootBu = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid CustomerBu = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid DirectBu = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid SecureBu = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly Guid DirectTeam = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid DirectDefaultTeam = Guid.Parse("20000000-0000-4000-8000-000000000002");
    private static readonly Guid SecureTeam = Guid.Parse("20000000-0000-4000-8000-000000000003");
    private static readonly Guid SomeUser = Guid.Parse("30000000-0000-4000-8000-000000000001");

    private readonly FakeDirectory _directory = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-09T10:00:00Z"));

    public DirectMessageOwnerTeamTests()
    {
        // A well-formed environment: root → { customer, Direct Messages, Secure Record }.
        _directory.BusinessUnit(RootBu, "Spaarke Root", parent: null);
        _directory.BusinessUnit(CustomerBu, "Customer", parent: RootBu);
        _directory.BusinessUnit(DirectBu, DirectBuName, parent: RootBu);
        _directory.BusinessUnit(SecureBu, SecureRecordOwnerTeam.DefaultBusinessUnitName, parent: RootBu);
        _directory.Team(DirectDefaultTeam, DirectBuName, DirectBu, isDefault: true, teamType: 0);
        _directory.Team(DirectTeam, DirectTeamName, DirectBu, isDefault: false, teamType: 0);
        _directory.Team(SecureTeam, SecureRecordOwnerTeam.DefaultOwnerTeamName, SecureBu, isDefault: false, teamType: 0);
    }

    private static IConfiguration Config(string? buName = DirectBuName, string? teamName = DirectTeamName, string? secureBuName = null)
    {
        var values = new Dictionary<string, string?>
        {
            [DirectMessageOwnerTeam.BusinessUnitNameConfigKey] = buName,
            [DirectMessageOwnerTeam.OwnerTeamNameConfigKey] = teamName,
        };
        if (secureBuName is not null)
            values[SecureRecordOwnerTeam.BusinessUnitNameConfigKey] = secureBuName;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private DirectMessageOwnerTeam Lookup(IConfiguration? configuration = null) =>
        new(new FakeWebApi(_directory), configuration ?? Config(), _time, NullLogger<DirectMessageOwnerTeam>.Instance);

    // ── Success ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_WellFormedSetup_ReturnsTheNamedTeam()
    {
        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.TeamId.Should().Be(DirectTeam, "the NAMED team, never the unit's default team");
        result.Refusal.Should().BeNull();
        result.Fault.Should().BeNull();
        _directory.Reads.Select(r => r.EntitySet).Should().Equal(
            "businessunits", "teams", "businessunits", "businessunits", "teammemberships", "systemusers");
    }

    // ── Each refusal state ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null, DirectTeamName)]
    [InlineData(DirectBuName, null)]
    [InlineData(null, null)]
    [InlineData("  ", DirectTeamName)]
    [InlineData(DirectBuName, "")]
    public async Task Resolve_SettingUnset_RefusesNotConfigured_WithoutReading(string? buName, string? teamName)
    {
        var result = await Lookup(Config(buName, teamName)).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.NotConfigured);
        _directory.Reads.Should().BeEmpty("there is no default to look up");
    }

    [Theory]
    [InlineData(null, "Secure Record")]
    [InlineData(null, "secure record ")]
    [InlineData("Isolated Records", "Isolated Records")]
    public async Task Resolve_BusinessUnitIsTheSecureRecordUnit_Refuses(string? secureBuName, string directBuName)
    {
        var result = await Lookup(Config(directBuName, DirectTeamName, secureBuName)).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitIsSecureRecord);
        _directory.Reads.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_BusinessUnitMissing_RefusesNotFound()
    {
        var result = await Lookup(Config(buName: "No Such Unit")).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitNotFound);
    }

    [Fact]
    public async Task Resolve_TwoBusinessUnitsCarryTheName_RefusesAmbiguous()
    {
        _directory.BusinessUnit(Guid.NewGuid(), DirectBuName, parent: RootBu);

        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitAmbiguous);
    }

    [Fact]
    public async Task Resolve_NoNamedTeam_RefusesTeamNotFound()
    {
        var result = await Lookup(Config(teamName: "Nobody")).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.OwnerTeamNotFound);
    }

    [Fact]
    public async Task Resolve_TwoNamedTeams_RefusesAmbiguous()
    {
        _directory.Team(Guid.NewGuid(), DirectTeamName, DirectBu, isDefault: false, teamType: 0);

        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.OwnerTeamAmbiguous);
    }

    [Fact]
    public async Task Resolve_TeamNameIsTheUnitsDefaultTeam_RefusesTeamIsDefault()
    {
        // The default team carries the unit's name; naming it is refused, never used.
        var result = await Lookup(Config(teamName: DirectBuName)).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.OwnerTeamIsDefault);
    }

    [Fact]
    public async Task Resolve_TeamHasAMember_Refuses()
    {
        _directory.Member(DirectTeam, SomeUser);

        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.OwnerTeamHasMembers);
    }

    [Fact]
    public async Task Resolve_BusinessUnitHasAUser_Refuses()
    {
        _directory.User(SomeUser, DirectBu);

        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitHasUsers);
    }

    [Fact]
    public async Task Resolve_BusinessUnitIsTheRoot_Refuses()
    {
        // The root holds users in practice; the root refusal is decided first, by the tree, not by the user count.
        _directory.Team(Guid.NewGuid(), DirectTeamName, RootBu, isDefault: false, teamType: 0);
        _directory.User(SomeUser, RootBu);

        var result = await Lookup(Config(buName: "Spaarke Root")).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitIsRoot);
    }

    [Fact]
    public async Task Resolve_BusinessUnitNotADirectChildOfTheRoot_Refuses()
    {
        var nested = Guid.NewGuid();
        _directory.BusinessUnit(nested, "Nested Direct", parent: CustomerBu);
        _directory.Team(Guid.NewGuid(), DirectTeamName, nested, isDefault: false, teamType: 0);

        var result = await Lookup(Config(buName: "Nested Direct")).ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, DirectMessageOwnerTeamRefusal.BusinessUnitNotRootChild);
    }

    [Theory]
    [InlineData(FaultPoint.BusinessUnitByName, DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable)]
    [InlineData(FaultPoint.NamedTeam, DirectMessageOwnerTeamRefusal.OwnerTeamUnreadable)]
    [InlineData(FaultPoint.BusinessUnitById, DirectMessageOwnerTeamRefusal.BusinessUnitUnreadable)]
    [InlineData(FaultPoint.Membership, DirectMessageOwnerTeamRefusal.OwnerTeamMembershipUnreadable)]
    [InlineData(FaultPoint.Users, DirectMessageOwnerTeamRefusal.BusinessUnitUsersUnreadable)]
    public async Task Resolve_ReadFaults_RefusesUnreadable_WithTheFault(FaultPoint point, string expected)
    {
        _directory.Fault = point;

        var result = await Lookup().ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(result, expected);
        result.Fault.Should().NotBeNull("an unreadable answer is never zero, and the caller logs the fault");
    }

    // ── Cache ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Resolve_SecondCallWithinTheLifetime_ReusesTheVerdict_ThenReadsAgainAfterIt()
    {
        var lookup = Lookup();

        var first = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);
        var readsAfterFirst = _directory.Reads.Count;
        _time.Advance(DirectMessageOwnerTeam.CacheLifetime - TimeSpan.FromSeconds(1));
        var second = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        second.Should().Be(first);
        _directory.Reads.Should().HaveCount(readsAfterFirst, "the checks run once, not per message");

        _time.Advance(TimeSpan.FromSeconds(2));
        var third = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        third.TeamId.Should().Be(DirectTeam);
        _directory.Reads.Should().HaveCount(readsAfterFirst * 2, "an expired verdict is read again");
    }

    [Fact]
    public async Task Resolve_ARefusalThatIsNotAFault_IsCachedToo()
    {
        _directory.Member(DirectTeam, SomeUser);
        var lookup = Lookup();

        await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);
        var reads = _directory.Reads.Count;
        var again = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        AssertRefused(again, DirectMessageOwnerTeamRefusal.OwnerTeamHasMembers);
        _directory.Reads.Should().HaveCount(reads);
    }

    [Theory]
    [InlineData(FaultPoint.BusinessUnitByName)]
    [InlineData(FaultPoint.NamedTeam)]
    [InlineData(FaultPoint.BusinessUnitById)]
    [InlineData(FaultPoint.DefaultTeam)]
    [InlineData(FaultPoint.Membership)]
    [InlineData(FaultPoint.Users)]
    public async Task Resolve_AFault_IsNeverCached_TheNextCallReadsAgain(FaultPoint point)
    {
        var configuration = point == FaultPoint.DefaultTeam ? Config(teamName: DirectBuName) : Config();
        var lookup = Lookup(configuration);
        _directory.Fault = point;

        var faulted = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);
        faulted.IsResolved.Should().BeFalse();
        faulted.Fault.Should().NotBeNull();

        _directory.Fault = FaultPoint.None;
        var reads = _directory.Reads.Count;
        var recovered = await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None);

        _directory.Reads.Count.Should().BeGreaterThan(reads, "a fault is not a verdict; it is read again");
        if (point == FaultPoint.DefaultTeam)
            AssertRefused(recovered, DirectMessageOwnerTeamRefusal.OwnerTeamIsDefault);
        else
            recovered.TeamId.Should().Be(DirectTeam);
    }

    [Fact]
    public async Task Resolve_ChangedSetting_IsReadFresh()
    {
        var values = new Dictionary<string, string?>
        {
            [DirectMessageOwnerTeam.BusinessUnitNameConfigKey] = DirectBuName,
            [DirectMessageOwnerTeam.OwnerTeamNameConfigKey] = "Nobody",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var lookup = Lookup(configuration);

        AssertRefused(await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None), DirectMessageOwnerTeamRefusal.OwnerTeamNotFound);
        configuration[DirectMessageOwnerTeam.OwnerTeamNameConfigKey] = DirectTeamName;

        (await lookup.ResolveDirectMessageOwnerTeamAsync(CancellationToken.None)).TeamId.Should().Be(DirectTeam);
    }

    // ── CannotBeDirectOwnerTeamAsync ─────────────────────────────────────────

    [Fact]
    public async Task CannotBe_TheNamedTeamItself_AnswersNo()
    {
        (await Lookup().CannotBeDirectOwnerTeamAsync(DirectTeam, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task CannotBe_AnotherNamedTeam_AnswersYes()
    {
        (await Lookup().CannotBeDirectOwnerTeamAsync(SecureTeam, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CannotBe_ADefaultTeamCarryingTheName_AnswersYes()
    {
        var team = Guid.NewGuid();
        _directory.Team(team, DirectTeamName, CustomerBu, isDefault: true, teamType: 0);

        (await Lookup().CannotBeDirectOwnerTeamAsync(team, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CannotBe_AnAccessTeamCarryingTheName_AnswersYes()
    {
        var team = Guid.NewGuid();
        _directory.Team(team, DirectTeamName, DirectBu, isDefault: false, teamType: 1);

        (await Lookup().CannotBeDirectOwnerTeamAsync(team, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task CannotBe_InDoubt_AnswersNo()
    {
        (await Lookup().CannotBeDirectOwnerTeamAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().BeFalse("a team row that is not found leaves the full resolution to decide");
        (await Lookup().CannotBeDirectOwnerTeamAsync(Guid.Empty, CancellationToken.None)).Should().BeFalse();
        (await Lookup(Config(teamName: null)).CannotBeDirectOwnerTeamAsync(SecureTeam, CancellationToken.None))
            .Should().BeFalse("unset is not 'no Direct team'; the full resolution refuses it");
    }

    [Fact]
    public async Task CannotBe_IsOneTeamRead()
    {
        await Lookup().CannotBeDirectOwnerTeamAsync(SecureTeam, CancellationToken.None);

        _directory.Reads.Should().ContainSingle().Which.Filter.Should().Be($"teamid eq {SecureTeam}");
    }

    // ── Secure Record unchanged ──────────────────────────────────────────────

    [Fact]
    public async Task SecureRecord_Resolve_KeepsItsFourReadsAndItsAnswer()
    {
        var result = await SecureRecordOwnerTeam.ResolveAsync(
            new FakeWebApi(_directory), new ConfigurationBuilder().Build(), CancellationToken.None);

        result.Status.Should().Be(SecureOwnerTeamStatus.Resolved);
        result.OwnerTeamId.Should().Be(SecureTeam);
        result.BusinessUnitId.Should().Be(SecureBu);
        _directory.Reads.Select(r => (r.EntitySet, r.Filter)).Should().Equal(
            ("businessunits", SecureRecordOwnerTeam.BusinessUnitFilter(SecureRecordOwnerTeam.DefaultBusinessUnitName)),
            ("teams", SecureRecordOwnerTeam.NamedOwnerTeamFilter(SecureBu, SecureRecordOwnerTeam.DefaultOwnerTeamName)),
            ("teammemberships", SecureRecordOwnerTeam.TeamMembershipFilter(SecureTeam)),
            ("systemusers", SecureRecordOwnerTeam.BusinessUnitUsersFilter(SecureBu)));
    }

    [Fact]
    public async Task SecureRecord_Resolve_HasNoPlacementRule()
    {
        // The Direct placement rule (a direct child of the root) is the Direct lookup's, not the shared one's.
        _directory.BusinessUnit(Guid.Parse("10000000-0000-4000-8000-000000000009"), "Nested Secure", parent: CustomerBu);
        _directory.Team(Guid.Parse("20000000-0000-4000-8000-000000000009"), "Nested Owners",
            Guid.Parse("10000000-0000-4000-8000-000000000009"), isDefault: false, teamType: 0);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [SecureRecordOwnerTeam.BusinessUnitNameConfigKey] = "Nested Secure",
            [SecureRecordOwnerTeam.OwnerTeamNameConfigKey] = "Nested Owners",
        }).Build();

        var result = await SecureRecordOwnerTeam.ResolveAsync(new FakeWebApi(_directory), configuration, CancellationToken.None);

        result.Status.Should().Be(SecureOwnerTeamStatus.Resolved);
    }

    [Fact]
    public async Task SecureRecord_Resolve_TeamWithAMember_StillRefusesWithItsOwnStatus()
    {
        _directory.Member(SecureTeam, SomeUser);

        var result = await SecureRecordOwnerTeam.ResolveAsync(
            new FakeWebApi(_directory), new ConfigurationBuilder().Build(), CancellationToken.None);

        result.Status.Should().Be(SecureOwnerTeamStatus.OwnerTeamHasMembers);
        result.MemberIds.Should().Equal(SomeUser);
    }

    [Fact]
    public async Task SecureRecord_Identify_ReadsOnlyTheUnitAndTheTeam()
    {
        var identity = await SecureRecordOwnerTeam.IdentifyAsync(
            new FakeWebApi(_directory), new ConfigurationBuilder().Build(), CancellationToken.None);

        identity.IsIdentified.Should().BeTrue();
        identity.OwnerTeamId.Should().Be(SecureTeam);
        _directory.Reads.Select(r => r.EntitySet).Should().Equal("businessunits", "teams");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void AssertRefused(DirectMessageOwnerTeamResolution result, string refusal)
    {
        result.IsResolved.Should().BeFalse();
        result.TeamId.Should().BeNull("a refusal never carries a team to fall back to");
        result.Refusal.Should().Be(refusal);
    }

    public enum FaultPoint
    {
        None,
        BusinessUnitByName,
        NamedTeam,
        DefaultTeam,
        BusinessUnitById,
        Membership,
        Users,
    }

    private sealed class FakeDirectory
    {
        private readonly List<(Guid Id, string Name, Guid? Parent)> _units = new();
        private readonly List<(Guid Id, string Name, Guid Unit, bool IsDefault, int TeamType)> _teams = new();
        private readonly List<(Guid Team, Guid User)> _members = new();
        private readonly List<(Guid User, Guid Unit)> _users = new();

        public List<(string EntitySet, string? Filter)> Reads { get; } = new();

        public FaultPoint Fault { get; set; }

        public void BusinessUnit(Guid id, string name, Guid? parent) => _units.Add((id, name, parent));

        public void Team(Guid id, string name, Guid unit, bool isDefault, int teamType) => _teams.Add((id, name, unit, isDefault, teamType));

        public void Member(Guid team, Guid user) => _members.Add((team, user));

        public void User(Guid user, Guid unit) => _users.Add((user, unit));

        /// <summary>The rows a read returns, matched on the exact filter the lookup's own builders produce.</summary>
        public List<object> Answer(string entitySet, string? filter)
        {
            Reads.Add((entitySet, filter));
            filter ??= string.Empty;
            switch (entitySet)
            {
                case "businessunits" when filter.StartsWith("name eq", StringComparison.Ordinal):
                    Throw(FaultPoint.BusinessUnitByName);
                    // Dataverse matches name case-insensitively.
                    return _units
                        .Where(u => string.Equals(filter, SecureRecordOwnerTeam.BusinessUnitFilter(u.Name), StringComparison.OrdinalIgnoreCase))
                        .Select(u => (object)new { businessunitid = u.Id, name = u.Name })
                        .ToList();

                case "businessunits":
                    Throw(FaultPoint.BusinessUnitById);
                    return _units
                        .Where(u => filter == DirectMessageOwnerTeam.BusinessUnitByIdFilter(u.Id))
                        .Select(u => (object)new { businessunitid = u.Id, _parentbusinessunitid_value = u.Parent })
                        .ToList();

                case "teams" when filter.StartsWith("teamid eq", StringComparison.Ordinal):
                    return _teams
                        .Where(t => filter == $"teamid eq {t.Id}")
                        .Select(t => (object)new { name = t.Name, isdefault = t.IsDefault, teamtype = t.TeamType })
                        .ToList();

                case "teams" when filter.Contains("isdefault eq true", StringComparison.Ordinal):
                    Throw(FaultPoint.DefaultTeam);
                    return _teams
                        .Where(t => t.IsDefault && filter == DirectMessageOwnerTeam.DefaultTeamFilter(t.Unit, t.Name))
                        .Select(t => (object)new { teamid = t.Id })
                        .ToList();

                case "teams":
                    Throw(FaultPoint.NamedTeam);
                    return _teams
                        .Where(t => !t.IsDefault && t.TeamType == SecureRecordOwnerTeam.OwnerTeamType
                                    && filter == SecureRecordOwnerTeam.NamedOwnerTeamFilter(t.Unit, t.Name))
                        .Select(t => (object)new { teamid = t.Id, name = t.Name })
                        .ToList();

                case "teammemberships":
                    Throw(FaultPoint.Membership);
                    return _members
                        .Where(m => filter == SecureRecordOwnerTeam.TeamMembershipFilter(m.Team))
                        .Select(m => (object)new { systemuserid = m.User })
                        .ToList();

                case "systemusers":
                    Throw(FaultPoint.Users);
                    return _users
                        .Where(u => filter == SecureRecordOwnerTeam.BusinessUnitUsersFilter(u.Unit))
                        .Select(u => (object)new { systemuserid = u.User })
                        .ToList();

                default:
                    throw new InvalidOperationException($"Unexpected read of {entitySet}.");
            }
        }

        private void Throw(FaultPoint point)
        {
            if (Fault == point)
                throw new HttpRequestException($"Simulated Dataverse fault at {point}.");
        }
    }

    private sealed class FakeWebApi : DataverseWebApiClient
    {
        private readonly FakeDirectory _directory;

        public FakeWebApi(FakeDirectory directory)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new NoCredential())
            => _directory = directory;

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null, int? top = null,
            int? skip = null, CancellationToken cancellationToken = default)
        {
            var rows = _directory.Answer(entitySetName, filter);
            if (top is { } limit)
                rows = rows.Take(limit).ToList();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
        }
    }

    private sealed class NoCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This double issues no HTTP.");

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            throw new NotSupportedException("This double issues no HTTP.");
    }
}
