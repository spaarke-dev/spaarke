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
        _sut = NewService(_factory, _log);
    }

    // DATAVERSE_URL is registration's default target. The identity-link reconciliation job reconciles it, the BFF's
    // own Dataverse:ServiceUrl and every active registry environment (third fix round), so the service itself no
    // longer reads Dataverse:ServiceUrl.
    private static RegistrationDataverseService NewService(
        ContactIdentityBinderFactory factory, ILogger<RegistrationDataverseService> logger,
        ContactIdentityBinder? ownEnvironmentBinder = null, string dataverseUrl = DefaultEnvironment, string? serviceUrl = null)
        => new(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DATAVERSE_URL"] = dataverseUrl,
                ["Dataverse:ServiceUrl"] = serviceUrl,
            }).Build(),
            new TrackingIdGenerator(),
            Mock.Of<TokenCredential>(),
            Mock.Of<IHttpClientFactory>(f => f.CreateClient(It.IsAny<string>()) == new HttpClient()),
            logger,
            factory,
            ownEnvironmentBinder ?? InertOwnEnvironmentBinder(),
            // Task 132: the BU-bind eviction hook — inert here; its behaviour is pinned in AccessCacheFaultCachingTests.
            new Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator(
                NullLogger<Sprk.Bff.Api.Services.Ai.Membership.NullMembershipCacheInvalidator>.Instance));

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

    // ── Third fix round (the verifier's open gap, notes §12.1): a link that does not land in a provisioning
    //    target IS re-decided — the identity-link reconciliation job reconciles every environment this BFF
    //    provisions into. The operator signal says so; IdentityLinkReconciliationTests proves the job does it. ──

    [Theory]
    [InlineData(DemoEnvironment)]
    [InlineData(null)]
    public async Task ALinkThatDoesNotLand_InAnyRegistrationEnvironment_IsReportedAsReDecidedByTheJob(string? target)
    {
        var (userId, oid) = GivenAUserLinkedMeanwhile();

        var result = await _sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "Raced", "User", "raced.user@demo.spaarke.com",
            _sut.ContactLinkEnvironment(target), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.Failed);
        var warning = _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain("re-decides the user on its next run")
            .And.Contain("every environment it provisions users into")
            .And.Contain("IdentityLink:Reconciliation:WritesEnabled")
            .And.Contain(_sut.ContactLinkEnvironment(target));
        warning.Message.Should().NotContain("NOT retried", "the gap the second round logged is closed");
    }

    [Fact]
    public async Task ALinkThatFaults_InATargetEnvironment_IsReportedAsReDecidedByTheJob()
    {
        var sut = NewService(new ThrowingBinderFactory(), _log);

        var result = await sut.LinkContactForNewSystemUserAsync(
            Guid.NewGuid(), Guid.NewGuid().ToString(), "A", "B", "a@demo.spaarke.com", DemoEnvironment,
            CancellationToken.None);

        result.Should().BeNull();
        _log.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning)
            .Which.Message.Should().Contain("re-decides the user on its next run").And.Contain(DemoEnvironment);
    }

    [Fact]
    public void TheBinderForAnEnvironment_IsBuiltForExactlyThatEnvironment()
    {
        _sut.ContactBinderFor(DemoEnvironment + "/").Store.Should().BeSameAs(_demoStore);
        _factory.Environments.Should().Equal(DemoEnvironment);
    }

    // ── ISS-005 (#1673 review F1): a registration approval whose target is THIS BFF's own environment - the default
    //    target (DATAVERSE_URL) naming the same environment as Dataverse:ServiceUrl, in any spelling - links through the
    //    owner-aware (DI) binder, so the licensed-user contact it creates is owned, or refused. Never an ownerless
    //    contact from a factory binder. A genuinely different environment is unchanged (the control). ──

    private const string OwnerBind = "ownerid@odata.bind";
    private const string ServiceEnvironment = "https://spaarkedev1.crm.dynamics.com";
    private const string ServiceEnvironmentApi = "https://spaarkedev1.api.crm.dynamics.com";
    private static readonly Guid CustomerBu = Guid.Parse("cb15f587-1111-4111-8111-00000000000a");
    private static readonly Guid CustomerTeam = Guid.Parse("7ea70000-2222-4222-8222-00000000000b");

    private (RegistrationDataverseService Sut, InMemoryContactIdentityStore Own, Guid UserId, Guid Oid) OwnEnvironmentService(
        string serviceUrl, string dataverseUrl, Guid? customerBusinessUnit)
    {
        var own = new InMemoryContactIdentityStore();
        own.DefaultTeams[CustomerBu] = new List<Guid> { CustomerTeam };
        var userId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        own.AddSystemUser(userId, oid, "new.user@customer.example");
        return (NewService(_factory, _log, OwnerAwareBinder(own, customerBusinessUnit), dataverseUrl, serviceUrl), own, userId, oid);
    }

    [Theory]
    [InlineData(ServiceEnvironment, ServiceEnvironment)]
    [InlineData(ServiceEnvironment, ServiceEnvironment + "/")]
    [InlineData(ServiceEnvironment, ServiceEnvironmentApi)]
    [InlineData(ServiceEnvironmentApi, ServiceEnvironment)]
    [InlineData(ServiceEnvironment, "HTTPS://SpaarkeDev1.API.CRM.Dynamics.com/")]
    public async Task ApprovingWithNoTarget_WhenDataverseUrlIsTheOwnEnvironment_OwnsTheNewContact(string serviceUrl, string dataverseUrl)
    {
        var (sut, own, userId, oid) = OwnEnvironmentService(serviceUrl, dataverseUrl, CustomerBu);

        // The approve endpoint with no target: the contact link goes to ContactLinkEnvironment(null) = DATAVERSE_URL.
        var result = await sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "New", "User", "new.user@customer.example",
            sut.ContactLinkEnvironment(null), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        own.CreatePayloads.Should().ContainSingle().Which[OwnerBind].Should().Be($"/teams({CustomerTeam:D})");
        own.ContactsBoundTo(oid).Should().ContainSingle().Which.OwnerTeamId.Should().Be(CustomerTeam);
        _factory.Environments.Should().BeEmpty("the own environment is never served by a factory (ownerless) binder");
    }

    [Theory]
    [InlineData(ServiceEnvironment, ServiceEnvironment)]
    [InlineData(ServiceEnvironment, ServiceEnvironmentApi)]
    public async Task ApprovingWithNoTarget_InTheOwnEnvironment_WithTheCustomerBusinessUnitUnset_IsRefused_AndCreatesNothing(
        string serviceUrl, string dataverseUrl)
    {
        var (sut, own, userId, oid) = OwnEnvironmentService(serviceUrl, dataverseUrl, customerBusinessUnit: null);

        var result = await sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "New", "User", "new.user@customer.example",
            sut.ContactLinkEnvironment(null), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.Denied);
        result.DenyCode.Should().Be("sdap.access.registration_owner_unconfigured");
        result.IsLinked.Should().BeFalse();
        own.Contacts.Should().BeEmpty();
        own.CreatePayloads.Should().BeEmpty();
        own.Writes.Should().BeEmpty();
        own.SystemUsers[userId].PrimaryContactId.Should().BeNull("no contact, so no link");
        _factory.Environments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://spaarke-demo.crm.dynamics.com")]
    [InlineData("https://spaarkedev10.crm.dynamics.com")] // a longer name sharing the own environment's prefix
    [InlineData("https://spaarkedev1.crm4.dynamics.com")] // the same name in another region
    public async Task ADifferentEnvironment_StillUsesTheFactoryBinder_AndStaysOwnerless(string target)
    {
        var (sut, own, _, _) = OwnEnvironmentService(ServiceEnvironment, ServiceEnvironment, CustomerBu);
        var userId = Guid.NewGuid();
        var oid = Guid.NewGuid();
        _demoStore.AddSystemUser(userId, oid, "new.user@demo.spaarke.com");

        var result = await sut.LinkContactForNewSystemUserAsync(
            userId, oid.ToString(), "New", "User", "new.user@demo.spaarke.com",
            sut.ContactLinkEnvironment(target), CancellationToken.None);

        result!.Outcome.Should().Be(SystemUserLinkOutcome.CreatedAndLinked);
        _factory.Environments.Should().Equal(target);
        _demoStore.CreatePayloads.Should().ContainSingle().Which.Should().NotContainKey(OwnerBind);
        own.CreatePayloads.Should().BeEmpty("the owner-aware binder is for the own environment only");
    }

    [Theory]
    [InlineData(ServiceEnvironment, ServiceEnvironment)]
    [InlineData(ServiceEnvironment, ServiceEnvironment + "/")]
    [InlineData(ServiceEnvironment, ServiceEnvironmentApi)]
    [InlineData(ServiceEnvironmentApi, ServiceEnvironment)]
    public void TheBinderForTheOwnEnvironment_IsTheDiRegisteredOne_InEveryUrlSpelling(string serviceUrl, string spelling)
    {
        var ownBinder = OwnerAwareBinder(new InMemoryContactIdentityStore(), CustomerBu);
        var sut = NewService(_factory, _log, ownBinder, DefaultEnvironment, serviceUrl);

        sut.ContactBinderFor(spelling).Should().BeSameAs(ownBinder);
        _factory.Environments.Should().BeEmpty();
    }

    // Eviction (task 132) shares the one comparison: the `.api.` spelling of the own environment is the own environment.
    [Theory]
    [InlineData(ServiceEnvironment, ServiceEnvironmentApi, true)]
    [InlineData(ServiceEnvironmentApi, ServiceEnvironment, true)]
    [InlineData(ServiceEnvironment, "https://spaarke-demo.crm.dynamics.com", false)]
    [InlineData(ServiceEnvironment, null, true)] // unknown evicts: an unnecessary eviction costs a re-read
    [InlineData(ServiceEnvironment, "  ", true)]
    [InlineData(null, "https://spaarke-demo.crm.dynamics.com", true)]
    public void IsThisBffsEnvironment_UsesTheSharedComparison_AndTreatsAnUnknownUrlAsYes(string? serviceUrl, string? target, bool expected)
    {
        var sut = NewService(_factory, _log, serviceUrl: serviceUrl);

        sut.IsThisBffsEnvironment(target).Should().Be(expected);
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
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now), FactoryConfiguration())
    {
        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
            => throw new InvalidOperationException("the target environment could not be reached");
    }

    private sealed class RecordingBinderFactory(IContactIdentityStore store) : ContactIdentityBinderFactory(
        Mock.Of<IHttpClientFactory>(), NullLoggerFactory.Instance, Tenants(CustomerTenant), new FakeTimeProvider(Now), FactoryConfiguration())
    {
        public List<string> Environments { get; } = new();

        public override IContactIdentityStore CreateStore(string dataverseBaseUrl, Func<CancellationToken, Task<string>> getToken)
        {
            Environments.Add(dataverseBaseUrl);
            return store;
        }
    }
}
