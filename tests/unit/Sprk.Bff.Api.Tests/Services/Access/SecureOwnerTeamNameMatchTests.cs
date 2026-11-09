using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Access;

/// <summary>
/// uac-r2 task 198 — whether a team can be the Secure Record owner team is decided by the name match Dataverse makes, never
/// by a comparison in code. <see cref="SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync"/> answers "cannot be" only
/// when Dataverse finds no team with that id under the configured name; <see cref="SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync"/>
/// matches the same names the same way.
/// </summary>
/// <remarks>
/// The double answers each <c>team</c> and <c>businessunit</c> read by EVALUATING its conditions, with names compared as
/// Dataverse's default collation compares them: case- and accent-insensitive. A setting that differs from the live name only
/// by an accent therefore matches, exactly as it does live.
/// </remarks>
public sealed class SecureOwnerTeamNameMatchTests
{
    private static readonly Guid SecureBu = Guid.Parse("5b5b5b5b-0000-4000-8000-000000000198");
    private static readonly Guid OtherBu = Guid.Parse("5b5b5b5b-0000-4000-8000-000000000199");
    private static readonly Guid SecureTeam = Guid.Parse("5e5e5e5e-0000-4000-8000-000000000198");
    private static readonly Guid OrdinaryTeam = Guid.Parse("9d9d9d9d-0000-4000-8000-000000000198");
    private static readonly Guid DefaultTeam = Guid.Parse("9e9e9e9e-0000-4000-8000-000000000198");
    private static readonly Guid AccessTeam = Guid.Parse("9f9f9f9f-0000-4000-8000-000000000198");

    private const string LiveBuName = "Secure Record";
    private const string LiveTeamName = "Secure Record Owners";

    private static readonly CompareInfo Collation = CultureInfo.InvariantCulture.CompareInfo;

    /// <summary>Dataverse's default collation: case- and accent-insensitive.</summary>
    private static bool SameName(string? a, string? b) =>
        a is not null && b is not null && Collation.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) == 0;

    private sealed record TeamRow(Guid Id, Guid BusinessUnit, string Name, bool IsDefault, int TeamType);

    private static readonly TeamRow[] Teams =
    {
        new(SecureTeam, SecureBu, LiveTeamName, false, 0),
        new(DefaultTeam, SecureBu, LiveBuName, true, 0),
        new(OrdinaryTeam, OtherBu, "Matter Team", false, 0),
        new(AccessTeam, SecureBu, LiveTeamName, false, 1),
    };

    private static readonly (Guid Id, string Name)[] BusinessUnits = { (SecureBu, LiveBuName), (OtherBu, "Spaarke") };

    private readonly List<QueryExpression> _queries = new();

    /// <summary>When set, every read throws it (a Dataverse fault).</summary>
    private Exception? _fault;

    /// <summary>When set, this environment holds no Secure Record unit and no team in it (an environment without secure records).</summary>
    private bool _noSecureUnit;

    private IGenericEntityService Dataverse()
    {
        var mock = new Mock<IGenericEntityService>(MockBehavior.Strict);
        mock.Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                _queries.Add(q);
                if (_fault is not null)
                    throw _fault;
                return new EntityCollection(q.EntityName switch
                {
                    "team" => Teams.Where(t => !(_noSecureUnit && t.BusinessUnit == SecureBu))
                        .Where(t => Matches(q, "teamid", t.Id) && Matches(q, "businessunitid", t.BusinessUnit)
                                               && Matches(q, "isdefault", t.IsDefault) && Matches(q, "teamtype", t.TeamType)
                                               && Matches(q, "name", t.Name))
                        .Select(t => Project(q, new Entity("team", t.Id)
                        {
                            ["teamid"] = t.Id,
                            ["name"] = t.Name,
                            ["isdefault"] = t.IsDefault,
                            ["teamtype"] = new OptionSetValue(t.TeamType),
                        }))
                        .Take(q.TopCount ?? int.MaxValue)
                        .ToList(),
                    "businessunit" => BusinessUnits.Where(b => !(_noSecureUnit && b.Id == SecureBu))
                        .Where(b => Matches(q, "businessunitid", b.Id) && Matches(q, "name", b.Name))
                        .Select(b => new Entity("businessunit", b.Id))
                        .Take(q.TopCount ?? int.MaxValue)
                        .ToList(),
                    _ => throw new InvalidOperationException($"Test: unexpected read of {q.EntityName}."),
                });
            });
        return mock.Object;
    }

    /// <summary>The row with only the columns the query selected, as Dataverse returns it.</summary>
    private static Entity Project(QueryExpression q, Entity row)
    {
        var projected = new Entity(row.LogicalName, row.Id);
        foreach (var column in q.ColumnSet.Columns.Where(row.Contains))
            projected[column] = row[column];
        return projected;
    }

    /// <summary>An absent condition matches; an Equal condition matches as Dataverse would (names by collation).</summary>
    private static bool Matches(QueryExpression q, string attribute, object actual)
    {
        var condition = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == attribute);
        if (condition is null)
            return true;
        condition.Operator.Should().Be(ConditionOperator.Equal);
        var expected = condition.Values.Single();
        return (actual, expected) switch
        {
            (string a, string e) => SameName(a, e),
            (int a, OptionSetValue e) => a == e.Value,
            _ => actual.Equals(expected),
        };
    }

    private static IConfiguration Config(string? buName = null, string? teamName = null)
    {
        var values = new Dictionary<string, string?>();
        if (buName is not null)
            values[SecureRecordOwnerTeam.BusinessUnitNameConfigKey] = buName;
        if (teamName is not null)
            values[SecureRecordOwnerTeam.OwnerTeamNameConfigKey] = teamName;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // ── Must fire: an accent variant of the setting still names the real team ─────────────────────────────────

    [Theory]
    [InlineData("Secure Record Ówners")]
    [InlineData("Sécure Record Owners")]
    [InlineData("SECURE RECORD OWNÈRS")]
    public async Task CannotBeSecureOwnerTeam_AnAccentVariantOfTheTeamSetting_IsNotRuledOut(string teamSetting)
    {
        var config = Config(teamName: teamSetting);

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), config, SecureTeam, CancellationToken.None))
            .Should().BeFalse("Dataverse matches the setting to the real team's name, so the full resolution must decide");
    }

    [Theory]
    [InlineData("Secure Record Ówners", null)]
    [InlineData(null, "Sécure Récord")]
    [InlineData("Sécure Record Owners", "SECURE RÉCORD")]
    public async Task ResolveSecureOwnerTeam_AccentVariantSettings_ResolveTheRealTeam(string? teamSetting, string? buSetting)
    {
        var config = Config(buName: buSetting, teamName: teamSetting);
        var dataverse = Dataverse();

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(dataverse, config, SecureTeam, CancellationToken.None))
            .Should().BeFalse();
        var resolved = await SecureChildShareSynchronizer.ResolveSecureOwnerTeamAsync(dataverse, config, CancellationToken.None);

        resolved.Refusal.Should().BeNull();
        resolved.TeamId.Should().Be(SecureTeam, "the id compare is what tells the Secure team apart");
    }

    [Fact]
    public async Task CannotBeSecureOwnerTeam_TheNameIsMatchedByTheQuery_NotInCode()
    {
        var config = Config(teamName: "Secure Record Ówners");

        await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), config, SecureTeam, CancellationToken.None);

        _queries.Should().NotBeEmpty();
        _queries[0].ColumnSet.Columns.Should().NotContain("name", "the team's name is never read back to compare in code");
        _queries[0].Criteria.Conditions.Should().ContainSingle(c => c.AttributeName == "name")
            .Which.Values.Single().Should().Be("Secure Record Ówners", "the configured name is the query's condition");
    }

    // ── Must not fire: a team Dataverse does not match is still ruled out ────────────────────────────────────

    [Fact]
    public async Task CannotBeSecureOwnerTeam_ATeamWithAnotherName_IsRuledOut()
    {
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), Config(), OrdinaryTeam, CancellationToken.None))
            .Should().BeTrue();
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(
                Dataverse(), Config(teamName: "Secure Record Ówners"), OrdinaryTeam, CancellationToken.None))
            .Should().BeTrue("an accent-insensitive match still does not match another name");
    }

    // ── K1: Secure settings PRESENT that name no unit or team Dataverse finds never rule the real Secure team out ──────

    [Theory]
    [InlineData(null, "Secure Record Readers")]   // the team setting names no team
    [InlineData("Secure Records", null)]          // the unit setting names no unit
    [InlineData("Secure Records", "Secure Record Readers")]
    public async Task CannotBeSecureOwnerTeam_WrongSecureSettings_DoNotRuleOutTheRealSecureTeam_AndIsSecureIsUnknown(
        string? buSetting, string? teamSetting)
    {
        var config = Config(buName: buSetting, teamName: teamSetting);
        var dataverse = Dataverse();

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(dataverse, config, SecureTeam, CancellationToken.None))
            .Should().BeFalse("a setting that finds nothing proves nothing about the team that owns the row");
        var (secure, undetermined) = await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(
            dataverse, config, SecureTeam, CancellationToken.None);
        secure.Should().BeNull("the configured Secure team is not found, so whether this is it cannot be told");
        undetermined.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CannotBeSecureOwnerTeam_WrongSecureSettings_DoNotRuleOutAnOrdinaryTeamEither()
    {
        // Nothing is ruled out by name while the configured names find no Secure team; the caller fails closed.
        var config = Config(teamName: "Secure Record Readers");

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), config, OrdinaryTeam, CancellationToken.None))
            .Should().BeFalse();
        (await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), config, OrdinaryTeam, CancellationToken.None))
            .Secure.Should().BeNull();
    }

    [Fact]
    public async Task AWrongUnitSetting_WithTheRightTeamName_RulesOutAnOrdinaryTeamByName_ButTheSecureTeamIsUnknown()
    {
        // The team name finds the Secure team, so a team without that name is not it (no unit read needed, task 149 F7);
        // the real Secure team's unit is not the configured one, so whether it is the Secure team cannot be told.
        var config = Config(buName: "Secure Records", teamName: LiveTeamName);

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), config, OrdinaryTeam, CancellationToken.None))
            .Should().BeTrue();
        _queries.Should().NotContain(q => q.EntityName == "businessunit");
        (await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), config, SecureTeam, CancellationToken.None))
            .Secure.Should().BeNull();
    }

    [Fact]
    public async Task CannotBeSecureOwnerTeam_AReadFault_DoesNotRuleTheTeamOut()
    {
        _fault = new InvalidOperationException("Test: Dataverse is unavailable.");

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), Config(), OrdinaryTeam, CancellationToken.None))
            .Should().BeFalse("a fault is doubt, never \"cannot be Secure\"");
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(
                Dataverse(), Config(teamName: LiveTeamName), OrdinaryTeam, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task IsSecureOwnerTeam_AReadFault_Propagates_SoTheCallerGrantsNothing()
    {
        _fault = new InvalidOperationException("Test: Dataverse is unavailable.");

        var act = () => SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), Config(), OrdinaryTeam, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CorrectSecureSettings_AnOrdinaryTeam_IsRuledOut_AndTheSecureTeamIsSecure()
    {
        var config = Config(buName: LiveBuName, teamName: LiveTeamName);

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), config, OrdinaryTeam, CancellationToken.None))
            .Should().BeTrue();
        (await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), config, OrdinaryTeam, CancellationToken.None))
            .Should().Be(((bool?)false, (string?)null));
        (await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), config, SecureTeam, CancellationToken.None))
            .Should().Be(((bool?)true, (string?)null));
    }

    // ── Secure settings ABSENT: the compiled defaults, unchanged ───────────────────────────────────────────────────────

    [Fact]
    public async Task AbsentSecureSettings_InAnEnvironmentWithoutASecureUnit_AnOrdinaryTeamIsRuledOut_AndIsNotSecure()
    {
        // No SecureRecord:* setting and no unit or team carrying the default names: no record is secure here, so a
        // team-owned row is ordinary (the behaviour before the K1 rule).
        _noSecureUnit = true;

        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), Config(), OrdinaryTeam, CancellationToken.None))
            .Should().BeTrue("the team exists and Dataverse finds no row with the default team name");
        (await SecureChildShareSynchronizer.IsSecureOwnerTeamAsync(Dataverse(), Config(), OrdinaryTeam, CancellationToken.None))
            .Should().Be(((bool?)false, (string?)null));
    }

    [Fact]
    public void SecureSettingsConfigured_IsTrueWhenEitherKeyIsSet_AndFalseWhenBothAreUnsetOrBlank()
    {
        SecureChildShareSynchronizer.SecureSettingsConfigured(Config()).Should().BeFalse();
        SecureChildShareSynchronizer.SecureSettingsConfigured(Config(buName: " ", teamName: "")).Should().BeFalse();
        SecureChildShareSynchronizer.SecureSettingsConfigured(Config(buName: LiveBuName)).Should().BeTrue();
        SecureChildShareSynchronizer.SecureSettingsConfigured(Config(teamName: LiveTeamName)).Should().BeTrue();
    }

    [Fact]
    public async Task CannotBeSecureOwnerTeam_TheDefaultTeamOrAnAccessTeam_IsRuledOut()
    {
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(
                Dataverse(), Config(teamName: LiveBuName), DefaultTeam, CancellationToken.None))
            .Should().BeTrue("a default team is never the Secure team, whatever its name");
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), Config(), AccessTeam, CancellationToken.None))
            .Should().BeTrue("an Access team is never the Secure team, whatever its name");
    }

    [Fact]
    public async Task CannotBeSecureOwnerTeam_ATeamThatIsNotFound_IsNotRuledOut()
    {
        (await SecureChildShareSynchronizer.CannotBeSecureOwnerTeamAsync(Dataverse(), Config(), Guid.NewGuid(), CancellationToken.None))
            .Should().BeFalse("not found is doubt, and doubt costs only the full resolution");
    }
}
