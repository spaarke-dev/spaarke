using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;

namespace Sprk.Bff.Api.Tests.TestInfrastructure;

/// <summary>
/// uac-r2 task 198: the REAL <see cref="DirectMessageOwnerTeam"/> (fm task 054a) over an in-memory business-unit and team
/// directory, for callers of the lookup (<c>DirectThreadAccessService</c>). The directory answers the lookup's reads on the
/// exact filters its own builders produce; any other read throws. <see cref="Unconfigured"/> is the lookup with no
/// <c>DirectMessage:*</c> settings, which reads nothing (every caller built before 054a).
/// </summary>
internal sealed class DirectMessageOwnerTeamDouble
{
    public const string BusinessUnitName = "Spaarke Direct Messages";
    public const string OwnerTeamName = "Spaarke Direct Message Owners";

    public static readonly Guid RootUnitId = Guid.Parse("d0d0d0d0-0000-0000-0000-0000000000d0");
    public static readonly Guid DirectUnitId = Guid.Parse("d0d0d0d0-0000-0000-0000-0000000000d1");

    private readonly List<(Guid Id, string Name, Guid? Parent)> _units = new()
    {
        (RootUnitId, "Spaarke", null),
        (DirectUnitId, BusinessUnitName, RootUnitId),
    };

    private readonly List<(Guid Id, string Name, Guid Unit, bool IsDefault, int TeamType)> _teams = new();
    private readonly List<(Guid Team, Guid User)> _members = new();
    private readonly List<(Guid User, Guid Unit)> _users = new();

    /// <summary>When set, every read throws (a Dataverse fault).</summary>
    public bool Faults { get; set; }

    /// <summary>fm task 054 piece 3b: how many reads the lookup has sent (a fault included).</summary>
    public int Reads { get; private set; }

    /// <summary>When set, every read throws what it returns instead (#1821: the caller's own cancellation, observed mid-read).</summary>
    public Func<Exception>? Fault { get; set; }

    /// <summary>The Direct team: the named, non-default, memberless Owner team in the Direct unit.</summary>
    public DirectMessageOwnerTeamDouble WithDirectTeam(Guid teamId) => Team(teamId, OwnerTeamName, DirectUnitId);

    /// <summary>Any other team (a different name: the lookup's one-read check proves it is not the Direct team).</summary>
    public DirectMessageOwnerTeamDouble Team(Guid teamId, string name, Guid unit, bool isDefault = false, int teamType = 0)
    {
        _teams.Add((teamId, name, unit, isDefault, teamType));
        return this;
    }

    /// <summary>A user homed in the Direct unit: the lookup refuses (bu_has_users).</summary>
    public DirectMessageOwnerTeamDouble WithUserInDirectUnit(Guid userId)
    {
        _users.Add((userId, DirectUnitId));
        return this;
    }

    /// <summary>Task 198 Part C: the Direct unit holds no user again (a refusal that recovers).</summary>
    public DirectMessageOwnerTeamDouble WithoutUsersInDirectUnit()
    {
        _users.RemoveAll(u => u.Unit == DirectUnitId);
        return this;
    }

    /// <summary>The lookup configured with <see cref="BusinessUnitName"/> and <see cref="OwnerTeamName"/>; its verdict cache runs
    /// on <paramref name="time"/> (the system clock by default).</summary>
    public DirectMessageOwnerTeam Lookup(TimeProvider? time = null) => new(
        new Directory(this),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DirectMessageOwnerTeam.BusinessUnitNameConfigKey] = BusinessUnitName,
            [DirectMessageOwnerTeam.OwnerTeamNameConfigKey] = OwnerTeamName,
        }).Build(),
        time ?? TimeProvider.System,
        NullLogger<DirectMessageOwnerTeam>.Instance);

    /// <summary>The lookup with no Direct-message settings: it reads nothing and refuses (not_configured).</summary>
    public static DirectMessageOwnerTeam Unconfigured() => new(
        new Directory(new DirectMessageOwnerTeamDouble()),
        new ConfigurationBuilder().Build(),
        TimeProvider.System,
        NullLogger<DirectMessageOwnerTeam>.Instance);

    private List<object> Answer(string entitySet, string? filter)
    {
        Reads++;
        return AnswerRead(entitySet, filter);
    }

    private List<object> AnswerRead(string entitySet, string? filter)
    {
        if (Fault is not null)
            throw Fault();
        if (Faults)
            throw new HttpRequestException("Simulated Dataverse fault.");
        filter ??= string.Empty;
        switch (entitySet)
        {
            case "businessunits" when filter.StartsWith("name eq", StringComparison.Ordinal):
                return _units
                    .Where(u => string.Equals(filter, SecureRecordOwnerTeam.BusinessUnitFilter(u.Name), StringComparison.OrdinalIgnoreCase))
                    .Select(u => (object)new { businessunitid = u.Id, name = u.Name })
                    .ToList();
            case "businessunits":
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
                return _teams
                    .Where(t => t.IsDefault && filter == DirectMessageOwnerTeam.DefaultTeamFilter(t.Unit, t.Name))
                    .Select(t => (object)new { teamid = t.Id })
                    .ToList();
            case "teams":
                return _teams
                    .Where(t => !t.IsDefault && t.TeamType == SecureRecordOwnerTeam.OwnerTeamType
                                && filter == SecureRecordOwnerTeam.NamedOwnerTeamFilter(t.Unit, t.Name))
                    .Select(t => (object)new { teamid = t.Id, name = t.Name })
                    .ToList();
            case "teammemberships":
                return _members
                    .Where(m => filter == SecureRecordOwnerTeam.TeamMembershipFilter(m.Team))
                    .Select(m => (object)new { systemuserid = m.User })
                    .ToList();
            case "systemusers":
                return _users
                    .Where(u => filter == SecureRecordOwnerTeam.BusinessUnitUsersFilter(u.Unit))
                    .Select(u => (object)new { systemuserid = u.User })
                    .ToList();
            default:
                throw new InvalidOperationException($"Unexpected read of {entitySet}.");
        }
    }

    private sealed class Directory : DataverseWebApiClient
    {
        private readonly DirectMessageOwnerTeamDouble _world;

        public Directory(DirectMessageOwnerTeamDouble world)
            : base(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com" })
                    .Build(),
                NullLogger<DataverseWebApiClient>.Instance,
                new NoCredential())
            => _world = world;

        public override Task<List<T>> QueryAsync<T>(string entitySetName, string? filter = null, string? select = null, int? top = null,
            int? skip = null, CancellationToken cancellationToken = default)
        {
            var rows = _world.Answer(entitySetName, filter);
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
