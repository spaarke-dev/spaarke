using Azure.Core;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Services.Communication;
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
    private readonly CapturingLogger<RegistrationDataverseService> _log = new();
    private readonly RegistrationDataverseService _sut;

    public RegistrationContactLinkTests()
    {
        _factory = new RecordingBinderFactory(_demoStore);
        _sut = NewService(_factory, _log, bffEnvironment: DefaultEnvironment);
    }

    // The BFF's own environment (Dataverse:ServiceUrl) is the ONLY one its identity-link reconciliation job scans.
    private static RegistrationDataverseService NewService(
        ContactIdentityBinderFactory factory, ILogger<RegistrationDataverseService> logger, string? bffEnvironment)
        => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATAVERSE_URL"] = DefaultEnvironment,
                ["Dataverse:ServiceUrl"] = bffEnvironment,
            }).Build(),
            new TrackingIdGenerator(),
            Mock.Of<TokenCredential>(),
            Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
            logger,
            factory);

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

    // ── Verifier finding 4: a link that does not land is never logged as "the reconciliation job retries it"
    //    unless this BFF's job actually scans that environment ─────────────────────────────────────────────

    [Fact]
    public void OnlyThisBffsOwnEnvironment_IsReconciledByItsJob()
    {
        _sut.IsReconciledByThisBff(DefaultEnvironment).Should().BeTrue();
        _sut.IsReconciledByThisBff(DefaultEnvironment.ToUpperInvariant() + "/").Should().BeTrue(
            "the same environment, whatever the case or trailing slash");
        _sut.IsReconciledByThisBff(DemoEnvironment).Should().BeFalse(
            "the job reads only Dataverse:ServiceUrl, never a demo/customer provisioning target");

        NewService(_factory, NullLogger<RegistrationDataverseService>.Instance, bffEnvironment: null)
            .IsReconciledByThisBff(DefaultEnvironment).Should().BeFalse(
                "with no Dataverse:ServiceUrl nothing can be claimed to retry anything");
    }

    [Fact]
    public async Task ALinkThatDoesNotLand_InATargetEnvironment_IsReportedAsNotRetried()
    {
        var (userId, oid) = GivenAUserLinkedMeanwhile();

        var result = await _sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "Raced", "User", "raced.user@demo.spaarke.com",
            _sut.ContactLinkEnvironment(DemoEnvironment), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.Failed);
        var warning = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("NOT retried by this BFF").And.Contain(DemoEnvironment)
            .And.Contain(DefaultEnvironment, "the operator is told which environment the job DOES scan");
        _log.Entries.Should().NotContain(e => e.Message.Contains("re-decides the user"),
            "no retry is promised for an environment the job never scans");
    }

    [Fact]
    public async Task ALinkThatDoesNotLand_InThisBffsOwnEnvironment_IsReportedAsReDecidedByTheJob()
    {
        var (userId, oid) = GivenAUserLinkedMeanwhile();

        var result = await _sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "Raced", "User", "raced.user@demo.spaarke.com",
            _sut.ContactLinkEnvironment(null), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.Failed);
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("re-decides the user on its next run")
            .And.Contain("IdentityLink:Reconciliation:WritesEnabled");
        _log.Entries.Should().NotContain(e => e.Message.Contains("NOT retried"));
    }

    [Fact]
    public async Task ALinkThatFaults_InATargetEnvironment_IsReportedAsNotRetried()
    {
        var sut = NewService(new ThrowingBinderFactory(), _log, bffEnvironment: DefaultEnvironment);

        var result = await sut.LinkContactForNewSystemUserAsync(
            Guid.NewGuid(), Guid.NewGuid().ToString(), "A", "B", "a@demo.spaarke.com", DemoEnvironment,
            CancellationToken.None);

        result.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("NOT retried by this BFF");
    }

    // The just-created user was linked by something else between its creation and this link (the
    // ALinkMadeMeanwhile_IsNeverRePointed shape): the link does not land, so the outcome is Failed.
    private (Guid UserId, Guid Oid) GivenAUserLinkedMeanwhile()
    {
        var userId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        var linkedMeanwhile = Guid.NewGuid();
        _demoStore.AddContact(linkedMeanwhile, email: "someone.else@demo.spaarke.com");
        _demoStore.AddSystemUser(userId, oid, "raced.user@demo.spaarke.com", primaryContactId: linkedMeanwhile);
        return (userId, oid);
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

    private sealed class ThrowingBinderFactory() : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now))
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => throw new InvalidOperationException("the target environment could not be reached");
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
