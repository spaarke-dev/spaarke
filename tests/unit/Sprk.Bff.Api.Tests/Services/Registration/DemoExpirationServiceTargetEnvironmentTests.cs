using System.Net;
using System.Text.RegularExpressions;
using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.Services.Communication;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// customer-provisioning-orchestration-r1 (found at T186): <see cref="DemoExpirationService"/> removes an expired demo
/// user from the team and revokes the SPE container of the environment THE REQUEST WAS PROVISIONED INTO (its own
/// "Target Environment" lookup, as the approve path reads it) - never of a registry-wide "default" or "first active" row.
/// </summary>
/// <remarks>
/// <para>Customer provisioning adds one active <c>sprk_dataverseenvironment</c> row per stamp. The old selection picked the
/// row flagged default, else the first active row by name, so a new row that sorted first (e.g. "ACME Corporation")
/// silently redirected every expiry to it: the row had no TeamName / SpeContainerId, Steps 2 and 4 threw and were logged,
/// and the record was still marked Expired with the user's access intact.</para>
/// <para>The real <see cref="DemoExpirationService"/> pass runs over the real <see cref="RegistrationDataverseService"/>
/// against a recording HTTP stub of the admin Dataverse; the registry is doubled at its virtual reads (the
/// <c>IdentityLinkReconciliationTests</c> convention), and the ownership guard at its virtual <c>IsOwnedAsync</c>
/// (<see cref="TestSpeOwnership"/> convention) so the container the revoke asks about is recorded. What each test pins is
/// the team name queried and the container asked about; the Graph steps are not reachable here and are only counted.</para>
/// </remarks>
public class DemoExpirationServiceTargetEnvironmentTests
{
    private const string AdminDataverse = "https://admin.crm.dynamics.com";
    private const string TargetDataverse = "https://zeta-demo.crm.dynamics.com";
    private const string FirstByNameDataverse = "https://acme.crm.dynamics.com";
    private const string AadObjectId = "11111111-2222-3333-4444-555555555555";

    private static readonly Guid RequestId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid TargetEnvironmentId = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000b");
    private static readonly Guid FirstByNameEnvironmentId = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000a");

    private static readonly DataverseEnvironmentRecord FirstByName = new()
    {
        Id = FirstByNameEnvironmentId,
        Name = "ACME Corporation",
        DataverseUrl = FirstByNameDataverse,
        IsActive = true,
        TeamName = "ACME Team",
        SpeContainerId = "b!acme-container",
    };

    private static readonly DataverseEnvironmentRecord Target = new()
    {
        Id = TargetEnvironmentId,
        Name = "Zeta Demo",
        DataverseUrl = TargetDataverse,
        IsActive = true,
        TeamName = "Zeta Team",
        SpeContainerId = "b!zeta-container",
    };

    private readonly RecordingDataverse _dataverse = new();
    private readonly Mock<IGraphClientFactory> _graph = new();
    private readonly CapturingLogger<DemoExpirationService> _log = new();
    private readonly RecordingGuard _guard;

    public DemoExpirationServiceTargetEnvironmentTests()
    {
        // Steps 1 and 3 (Entra) are not reachable without a Graph; the factory refuses so each logs and moves on.
        _graph.Setup(f => f.ForApp()).Throws(new InvalidOperationException("Graph is not reachable in this test."));
        _guard = new RecordingGuard(_graph.Object);
    }

    /// <summary>
    /// MUTATION: resolving the environment from the registry's active list (default / first by name) instead of the
    /// request's own <c>DataverseEnvironmentId</c> queries "ACME Team" and asks about "b!acme-container".
    /// </summary>
    [Fact]
    public async Task ExpiredRecord_UsesTheTeamAndContainerOfItsOwnTargetEnvironment_NotTheFirstActiveRow()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var registry = Registry(
            byId: id => id == TargetEnvironmentId ? Target : null,
            active: [FirstByName, Target]);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        _dataverse.TeamNamesQueried.Should().Equal("Zeta Team");
        _guard.ContainersAsked.Should().Equal("b!zeta-container");
        registry.ReadIds.Should().Equal(new[] { TargetEnvironmentId }, "the environment is read once per record and reused by Steps 2 and 4");
        registry.ActiveListReads.Should().Be(0, "no registry-wide pick is made");
        _dataverse.TeamRemovals.Should().Be(1);
        _dataverse.ExpiredStatusWrites.Should().Be(1);
    }

    /// <summary>
    /// MUTATION: falling back to the registry's default / first active row when the request has no target environment
    /// queries a team and asks about a container; the error log naming the request is also gone.
    /// </summary>
    [Fact]
    public async Task ExpiredRecord_WithoutTargetEnvironment_SkipsTeamRemovalAndSpeRevoke_LogsAnError_AndStillExpires()
    {
        _dataverse.Provisioned = ExpiredRequest(environmentId: null);
        var registry = Registry(byId: _ => Target, active: [FirstByName, Target]);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        AssertSteps2And4Skipped(registry);
        ErrorsNaming(RequestId).Should().ContainSingle(m => m.Contains("no target environment"));
        registry.ReadIds.Should().BeEmpty();
    }

    /// <summary>MUTATION: as above, when the lookup row no longer exists.</summary>
    [Fact]
    public async Task ExpiredRecord_WhoseTargetEnvironmentIsNotFound_SkipsTeamRemovalAndSpeRevoke_LogsAnError_AndStillExpires()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var registry = Registry(byId: _ => null, active: [FirstByName, Target]);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        AssertSteps2And4Skipped(registry);
        ErrorsNaming(RequestId).Should().ContainSingle(m => m.Contains("was not found") && m.Contains(TargetEnvironmentId.ToString()));
        registry.ReadIds.Should().Equal(new[] { TargetEnvironmentId });
    }

    /// <summary>MUTATION: a registry read that throws escapes the per-step handling and the record is never marked Expired.</summary>
    [Fact]
    public async Task ExpiredRecord_WhoseTargetEnvironmentCannotBeRead_SkipsTeamRemovalAndSpeRevoke_LogsAnError_AndStillExpires()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var registry = Registry(
            byId: _ => throw new HttpRequestException("registry unavailable"),
            active: [FirstByName, Target]);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        AssertSteps2And4Skipped(registry);
        ErrorsNaming(RequestId).Should().Contain(m => m.Contains("Failed to read target environment"));
    }

    /// <summary>An environment deactivated after the demo was provisioned still holds the user's access; expiry removes it.</summary>
    [Fact]
    public async Task ExpiredRecord_WhoseTargetEnvironmentWasDeactivated_StillRemovesTheUser()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var deactivated = new DataverseEnvironmentRecord
        {
            Id = TargetEnvironmentId, Name = Target.Name, DataverseUrl = TargetDataverse, IsActive = false,
            TeamName = Target.TeamName, SpeContainerId = Target.SpeContainerId,
        };
        var registry = Registry(byId: _ => deactivated, active: []);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        _dataverse.TeamNamesQueried.Should().Equal("Zeta Team");
        _guard.ContainersAsked.Should().Equal("b!zeta-container");
    }

    /// <summary>
    /// MUTATION: calling the systemuser lookup or the team removal without the environment's URL sends them to the admin
    /// Dataverse, where the user and team do not exist (provisioning created them in the target environment).
    /// </summary>
    [Fact]
    public async Task ExpiredRecord_LooksUpTheUserAndRemovesTheTeamMembershipInTheTargetEnvironment_NotTheAdminOne()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var registry = Registry(byId: id => id == TargetEnvironmentId ? Target : null, active: [FirstByName, Target]);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        var targetHost = new Uri(TargetDataverse).Host;
        _dataverse.SystemUserLookupHosts.Should().Equal(targetHost);
        _dataverse.TeamLookupHosts.Should().Equal(targetHost);
        _dataverse.TeamRemovalHosts.Should().Equal(targetHost);
        // The registration request itself is read and updated in the admin environment.
        _dataverse.AdminOnlyHosts.Should().OnlyContain(h => h == new Uri(AdminDataverse).Host);
    }

    /// <summary>
    /// MUTATION: treating a blank <c>DataverseUrl</c> as "use the admin environment" silently repeats the wrong-environment
    /// removal; the step must be refused (logged) instead, while the rest of the expiry still runs.
    /// </summary>
    [Fact]
    public async Task ExpiredRecord_WhoseTargetEnvironmentHasNoDataverseUrl_SkipsTeamRemovalRatherThanUsingTheAdminEnvironment()
    {
        _dataverse.Provisioned = ExpiredRequest(TargetEnvironmentId);
        var noUrl = new DataverseEnvironmentRecord
        {
            Id = TargetEnvironmentId, Name = Target.Name, IsActive = true,
            TeamName = Target.TeamName, SpeContainerId = Target.SpeContainerId,
        };
        var registry = Registry(byId: _ => noUrl, active: []);

        await Sut(registry).ProcessExpirationsAsync(CancellationToken.None);

        _dataverse.SystemUserLookupHosts.Should().BeEmpty();
        _dataverse.TeamLookupHosts.Should().BeEmpty();
        _dataverse.TeamRemovals.Should().Be(0);
        ErrorsNaming(RequestId).Should().Contain(m => m.Contains("Failed to remove from Demo Team"));
        _dataverse.ExpiredStatusWrites.Should().Be(1);
    }

    private void AssertSteps2And4Skipped(RegistryDouble registry)
    {
        _dataverse.TeamNamesQueried.Should().BeEmpty("Step 2 is skipped");
        _dataverse.TeamRemovals.Should().Be(0);
        _guard.ContainersAsked.Should().BeEmpty("Step 4 is skipped");
        registry.ActiveListReads.Should().Be(0, "no registry-wide pick is made");
        _graph.Verify(f => f.ForApp(), Times.Exactly(2)); // Steps 1 and 3 still run
        _dataverse.ExpiredStatusWrites.Should().Be(1, "the record is still marked Expired");
    }

    private List<string> ErrorsNaming(Guid requestId) => _log.Entries
        .Where(e => e.Level == LogLevel.Error && e.Message.Contains(requestId.ToString()))
        .Select(e => e.Message)
        .ToList();

    private static string ExpiredRequest(Guid? environmentId) => $$"""
        {"value":[{
          "sprk_registrationrequestid":"{{RequestId}}",
          "sprk_status":{{(int)RegistrationStatus.Provisioned}},
          "sprk_demouserobjectid":"{{AadObjectId}}",
          "sprk_expirationdate":"2026-10-01T00:00:00Z",
          "_sprk_dataverseenvironmentid_value":{{(environmentId.HasValue ? $"\"{environmentId}\"" : "null")}}
        }]}
        """;

    private static RegistryDouble Registry(
        Func<Guid, DataverseEnvironmentRecord?> byId, List<DataverseEnvironmentRecord> active)
        => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = AdminDataverse }).Build(),
            byId,
            active);

    private DemoExpirationService Sut(DataverseEnvironmentService registry)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DATAVERSE_URL"] = AdminDataverse })
            .Build();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_dataverse, disposeHandler: false));

        var dataverse = new RegistrationDataverseService(
            configuration,
            new TrackingIdGenerator(),
            new StaticTokenCredential(),
            factory.Object,
            NullLogger<RegistrationDataverseService>.Instance,
            new ContactIdentityBinderFactory(
                Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now)),
            new NullMembershipCacheInvalidator(NullLogger<NullMembershipCacheInvalidator>.Instance));

        var options = Options.Create(new DemoProvisioningOptions { DemoUsersGroupId = "group-1" });
        return new DemoExpirationService(
            new GraphUserService(_graph.Object, new PasswordGenerator(), options, NullLogger<GraphUserService>.Instance),
            dataverse,
            new RegistrationEmailService(null!, Options.Create(new CommunicationOptions()), NullLogger<RegistrationEmailService>.Instance),
            _guard,
            registry,
            options,
            _log,
            NullLoggerFactory.Instance);
    }

    /// <summary>The environment registry at its virtual reads (no HTTP double); records what was asked of it.</summary>
    private sealed class RegistryDouble(
        IConfiguration configuration, Func<Guid, DataverseEnvironmentRecord?> byId, List<DataverseEnvironmentRecord> active)
        : DataverseEnvironmentService(configuration, new StaticTokenCredential(), NullLogger<DataverseEnvironmentService>.Instance)
    {
        public List<Guid> ReadIds { get; } = [];
        public int ActiveListReads { get; private set; }

        public override Task<DataverseEnvironmentRecord?> GetByIdAsync(Guid environmentId, CancellationToken ct = default)
        {
            ReadIds.Add(environmentId);
            return Task.FromResult(byId(environmentId));
        }

        public override Task<List<DataverseEnvironmentRecord>> GetActiveEnvironmentsAsync(CancellationToken ct = default)
        {
            ActiveListReads++;
            return Task.FromResult(active);
        }
    }

    /// <summary>Owns nothing and records the container each revoke asks about (the guard refuses before Graph is called).</summary>
    private sealed class RecordingGuard(IGraphClientFactory factory)
        : SpeContainerOwnershipGuard(
            factory, Array.Empty<string?>(), TestSpeOwnership.Customer("test"), NullLogger<SpeContainerOwnershipGuard>.Instance)
    {
        public List<string> ContainersAsked { get; } = [];

        public override Task<bool> IsOwnedAsync(string containerOrDriveId, CancellationToken ct = default)
        {
            ContainersAsked.Add(containerOrDriveId);
            return Task.FromResult(false);
        }
    }

    /// <summary>The admin Dataverse: serves the provisioned request, a systemuser and any team, and records the writes.</summary>
    private sealed class RecordingDataverse : HttpMessageHandler
    {
        public string Provisioned { get; set; } = "{\"value\":[]}";
        public List<string> TeamNamesQueried { get; } = [];
        public int TeamRemovals { get; private set; }
        public int ExpiredStatusWrites { get; private set; }
        public List<string> SystemUserLookupHosts { get; } = [];
        public List<string> TeamLookupHosts { get; } = [];
        public List<string> TeamRemovalHosts { get; } = [];
        public List<string> AdminOnlyHosts { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var query = Uri.UnescapeDataString(request.RequestUri.Query);

            if (request.Method == HttpMethod.Get && path.EndsWith("/sprk_registrationrequests", StringComparison.Ordinal))
            {
                AdminOnlyHosts.Add(request.RequestUri.Host);
                return Json(Provisioned);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/systemusers", StringComparison.Ordinal))
            {
                SystemUserLookupHosts.Add(request.RequestUri.Host);
                return Json("{\"value\":[{\"systemuserid\":\"99999999-0000-0000-0000-000000000001\"}]}");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/teams", StringComparison.Ordinal))
            {
                TeamLookupHosts.Add(request.RequestUri.Host);
                TeamNamesQueried.Add(Regex.Match(query, "name eq '([^']*)'").Groups[1].Value);
                return Json("{\"value\":[{\"teamid\":\"77777777-0000-0000-0000-000000000001\"," +
                            "\"_businessunitid_value\":\"66666666-0000-0000-0000-000000000001\"}]}");
            }

            if (request.Method == HttpMethod.Delete && path.Contains("/teammembership_association(", StringComparison.Ordinal))
            {
                TeamRemovals++;
                TeamRemovalHosts.Add(request.RequestUri.Host);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.Method == HttpMethod.Patch && path.Contains("/sprk_registrationrequests(", StringComparison.Ordinal))
            {
                AdminOnlyHosts.Add(request.RequestUri.Host);
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (body.Contains($"\"sprk_status\":{(int)RegistrationStatus.Expired}", StringComparison.Ordinal))
                {
                    ExpiredStatusWrites++;
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
    }

    private sealed class StaticTokenCredential : TokenCredential
    {
        private static AccessToken Token => new("test-token-not-a-credential", DateTimeOffset.UtcNow.AddHours(1));

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => Token;

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(Token);
    }
}
