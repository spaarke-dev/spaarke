using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.IdentityBindingTestKit;

namespace Sprk.Bff.Api.Tests.Services.Registration;

/// <summary>
/// unified-access-control-r2 task 141 — <c>RegistrationDataverseService.CreateSystemUserAsync</c> links (or
/// creates) the new user's contact in the SAME environment it created the systemuser in, never the default one.
/// </summary>
/// <remarks>
/// The environment is asserted at the seam where it is decided: <see cref="ContactIdentityBinderFactory.CreateStore"/>
/// is overridden to RECORD the URL it is asked for and return an in-memory store, so no HTTP double is needed
/// (ADR-038 B1). That <c>CreateSystemUserAsync</c> calls the link with <c>ContactLinkEnvironment(targetDataverseUrl)</c>
/// is pinned by a source assertion, because the systemuser POST itself is real HTTP.
/// </remarks>
public class RegistrationContactLinkTests
{
    private const string DefaultEnvironment = "https://spaarkedev1.crm.dynamics.com";
    private const string DemoEnvironment = "https://spaarke-demo.crm.dynamics.com";

    private readonly InMemoryContactIdentityStore _demoStore = new();
    private readonly RecordingBinderFactory _factory;
    private readonly RegistrationDataverseService _sut;

    public RegistrationContactLinkTests()
    {
        _factory = new RecordingBinderFactory(_demoStore);
        _sut = new RegistrationDataverseService(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATAVERSE_URL"] = DefaultEnvironment,
            }).Build(),
            new TrackingIdGenerator(),
            Mock.Of<TokenCredential>(),
            Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
            NullLogger<RegistrationDataverseService>.Instance,
            _factory);
    }

    [Fact]
    public void TheLinkEnvironment_IsTheTargetEnvironment_WhenOneIsGiven()
    {
        _sut.ContactLinkEnvironment(DemoEnvironment + "/").Should().Be(DemoEnvironment);
        _sut.ContactLinkEnvironment(null).Should().Be(DefaultEnvironment,
            "with no target the systemuser was created in this service's own environment");
    }

    [Fact]
    public async Task ANewUser_GetsAContactKeyedByItsOid_AndALink_InTheTargetEnvironment()
    {
        var userId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        _demoStore.AddSystemUser(userId, oid, "new.user@demo.spaarke.com");

        var result = await _sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "New", "User", "new.user@demo.spaarke.com",
            _sut.ContactLinkEnvironment(DemoEnvironment), CancellationToken.None);

        _factory.Environments.Should().Equal(DemoEnvironment);
        result!.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        var created = _demoStore.ContactsBoundTo(oid).Should().ContainSingle().Subject;
        _demoStore.SystemUsers[userId].PrimaryContactId.Should().Be(created.Id);
        (created.FirstName, created.LastName, created.Email).Should().Be(("New", "User", "new.user@demo.spaarke.com"));
    }

    /// <remarks>
    /// The just-created systemuser row carries no version, so the link re-reads the user first. If something
    /// linked it in between, that link is NOT re-pointed (it used to go out as an unconditional If-Match: *).
    /// </remarks>
    [Fact]
    public async Task ALinkMadeMeanwhile_IsNeverRePointed()
    {
        var userId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var linkedMeanwhile = Guid.NewGuid();
        _demoStore.AddContact(linkedMeanwhile, email: "someone.else@demo.spaarke.com");
        _demoStore.AddSystemUser(userId, oid, "raced.user@demo.spaarke.com", primaryContactId: linkedMeanwhile);

        var result = await _sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "Raced", "User", "raced.user@demo.spaarke.com",
            _sut.ContactLinkEnvironment(DemoEnvironment), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.Failed);
        _demoStore.SystemUsers[userId].PrimaryContactId.Should().Be(linkedMeanwhile, "an existing link is never re-pointed");
        _demoStore.Writes.Should().NotContain(w => w.Op == "link");
    }

    [Fact]
    public async Task AnUnusableOid_AttemptsNothing()
    {
        var result = await _sut.LinkContactForNewSystemUserAsync(
            Guid.NewGuid(), "not-an-oid", "A", "B", "a@b.example", DemoEnvironment, CancellationToken.None);

        result.Should().BeNull();
        _factory.Environments.Should().BeEmpty();
    }

    [Fact]
    public void CreateSystemUserAsync_LinksInTheEnvironmentItCreatedTheUserIn()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src", "server", "api", "Sprk.Bff.Api", "Services",
            "Registration", "RegistrationDataverseService.cs"));
        var body = source[source.IndexOf("public async Task<Guid> CreateSystemUserAsync(", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("public string ContactLinkEnvironment(", StringComparison.Ordinal)];

        body.Should().Contain("ContactLinkEnvironment(targetDataverseUrl)",
            "the link must go to the systemuser's own environment, never the default one");
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }

    private sealed class RecordingBinderFactory(IContactIdentityStore store) : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now))
    {
        public List<string> Environments { get; } = new();

        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
        {
            Environments.Add(dataverseBaseUrl);
            return store;
        }
    }
}
