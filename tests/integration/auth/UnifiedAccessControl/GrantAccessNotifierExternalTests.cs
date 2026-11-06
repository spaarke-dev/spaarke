using System.Security.Claims;
using System.Text.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Registration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// FR-07 (spaarke-SPA-external-access-platform-r3 task 036): the external-contact email branch of
/// <see cref="GrantAccessNotifier.NotifyContactGrantAsync"/> (a grant that gave an already-onboarded external contact
/// access). The PRODUCTION notifier; doubled are only the I/O boundaries — the identity
/// store, the ledger store's link read, the Dataverse client (the record's name) and the email
/// service, which RECORDS each send instead of reaching the shared mailbox. The cases that depend on the grant endpoint's
/// "access gained" decision (same-level re-grant, expiry-only change, lower level, organization grant) run through the
/// production endpoint in <c>GrantExternalContactEmailTests</c> (tests/integration/auth). Link rules are asserted here
/// (<see cref="GrantAccessNotifier.BuildPortalLink"/>).
/// </summary>
public class GrantAccessNotifierExternalTests
{
    private const string Portal = "https://external.spaarke.test";
    private const string CustomerKey = "acme";
    private const string RecordName = "Smith v. Smith";

    private static readonly Guid GranterOid = Guid.Parse("0a0a0a0a-1111-2222-3333-444444444444");

    private readonly Guid _matter = Guid.NewGuid();
    private readonly FakeDataverse _dataverse = new();
    private readonly Mock<IContactIdentityStore> _identities = new();
    private readonly FakeLinks _links = new();
    private readonly RecordingEmails _emails = new();
    private readonly ListLogger _log = new();
    private readonly Mock<IGenericEntityService> _bell = new();

    public GrantAccessNotifierExternalTests()
    {
        _dataverse.Names[_matter] = RecordName;
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────────

    private GrantAccessNotifier Notifier(
        string? portalUrl = Portal, string? customerId = CustomerKey, TimeSpan? sendTimeout = null)
    {
        var settings = new Dictionary<string, string?> { ["ExternalAccess:PortalUrl"] = portalUrl };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var participations = new Mock<ExternalParticipationService>(
            new HttpClient(), Mock.Of<ITenantCache>(), config, Mock.Of<TokenCredential>(),
            Mock.Of<IHttpContextAccessor>(), NullLogger<ExternalParticipationService>.Instance, null!).Object;
        var guard = new SecureShareNoAccessGuard(
            participations, Mock.Of<INoAccessListReader>(), _identities.Object,
            Mock.Of<IGenericEntityService>(), NullLogger<SecureShareNoAccessGuard>.Instance);

        return new GrantAccessNotifier(
            new NotificationService(_bell.Object, NullLogger<NotificationService>.Instance),
            _dataverse, _identities.Object, _links, guard, participations, _emails,
            new CustomerIdentity(
                Options.Create(new CustomerOptions { Id = customerId ?? string.Empty }), config,
                NullLogger<CustomerIdentity>.Instance),
            config, _log)
        {
            SendTimeout = sendTimeout ?? GrantAccessNotifier.DefaultSendTimeout,
        };
    }

    private static ClaimsPrincipal Granter(Guid? oid = null) => new(new ClaimsIdentity(
        new[] { new Claim("oid", (oid ?? GranterOid).ToString()), new Claim("name", "Gina Granter") }, "Test"));

    /// <summary>A caller authenticated by the WorkforceHome scheme: its oid is the CUSTOMER directory's (ADR-028 A9).</summary>
    private static ClaimsPrincipal HomeGranter(Guid? oid = null) => new(new ClaimsIdentity(
        new[] { new Claim("oid", (oid ?? GranterOid).ToString()), new Claim("name", "Hanna Home") },
        AuthSchemes.WorkforceHome));

    /// <summary>An active contact the identity store returns; <paramref name="oid"/> = null is a contact never onboarded.</summary>
    private Guid Contact(Guid? oid = null, string? email = "partner@outside.test", int stateCode = 0, int? plane = null)
    {
        var id = Guid.NewGuid();
        _identities
            .Setup(i => i.GetContactAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactLookup.Of(new ContactBindingRow(
                id, stateCode, oid?.ToString("D"), plane, Email: email)));
        return id;
    }

    private static string Link(Guid record, string type = "matter") =>
        $"{Portal}/?customer={CustomerKey}&record={type}:{record:D}";

    // ── Task 046 guard (d): a WorkforceHome caller's oid is never compared with systemuser.azureactivedirectoryobjectid ──

    [Fact]
    public async Task AHomeCaller_IsNeverLookedUpByOid_AndNobodyIsExcludedAsTheGranter()
    {
        var other = Guid.NewGuid();
        _dataverse.GranterSystemUserId = other; // would exclude `other` if the oid lookup were (wrongly) made

        var outcome = await Notifier().NotifyUserShareAsync(
            ExternalGrantRootType.Matter, _matter, other, ExternalAccessLevel.Collaborate, HomeGranter());

        _dataverse.SystemUserQueries.Should().BeEmpty("a home oid is the customer directory's, never compared with the Spaarke-tenant oid");
        outcome.Should().NotBe(GrantNotificationOutcome.NotApplicable, "nobody is excluded, so the recipient is told");
    }

    [Fact]
    public async Task ANonHomeCaller_IsStillLookedUpByOid_AndExcludedAsTheGranter()
    {
        var granterSystemUser = Guid.NewGuid();
        _dataverse.GranterSystemUserId = granterSystemUser;

        var outcome = await Notifier().NotifyUserShareAsync(
            ExternalGrantRootType.Matter, _matter, granterSystemUser, ExternalAccessLevel.Collaborate, Granter());

        _dataverse.SystemUserQueries.Should().ContainSingle().Which.Should().Contain(GranterOid.ToString());
        outcome.Should().Be(GrantNotificationOutcome.NotApplicable, "the granter is never told about their own share");
    }

    // ── FR-07: a grant that gave an onboarded external contact access → exactly one email, with the link ──────────────

    [Fact]
    public async Task OnboardedExternalContact_GainingAccess_GetsExactlyOneEmail_WithThePortalLinkAndTheRecordName()
    {
        var contact = Contact(oid: Guid.NewGuid());

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Sent);
        var sent = _emails.Sent.Should().ContainSingle("exactly one email per qualifying event").Subject;
        sent.To.Should().Be("partner@outside.test");
        sent.LinkUrl.Should().Be(Link(_matter));
        sent.Subject.Should().Contain(RecordName).And.Contain("matter");
        sent.Message.Should().Contain("Gina Granter");
    }

    [Theory]
    [InlineData(ExternalGrantRootType.Project, "project")]
    [InlineData(ExternalGrantRootType.Matter, "matter")]
    [InlineData(ExternalGrantRootType.WorkAssignment, "workassignment")]
    public async Task TheLink_NamesTheRootsTypeToken_AndTheRecordId(ExternalGrantRootType type, string token)
    {
        var contact = Contact(oid: Guid.NewGuid());
        var record = Guid.NewGuid();

        await Notifier().NotifyContactGrantAsync(type, record, contact, ExternalAccessLevel.ViewOnly, Granter());

        _emails.Sent.Should().ContainSingle().Which.LinkUrl.Should().Be(Link(record, token));
    }

    [Fact]
    public async Task TheLink_CarriesNoBffUrlScopeOrToken()
    {
        var contact = Contact(oid: Guid.NewGuid());

        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        var link = _emails.Sent.Should().ContainSingle().Subject.LinkUrl;
        var uri = new Uri(link);
        uri.Host.Should().Be("external.spaarke.test", "the portal origin, not a backend");
        uri.AbsolutePath.Should().Be("/");
        System.Web.HttpUtility.ParseQueryString(uri.Query).AllKeys.Should().BeEquivalentTo(new[] { "customer", "record" });
        link.Should().NotContainAny("api://", "user_impersonation", "access_token", "id_token", "bearer", "scope", "token", "azurewebsites", "/api/");
        _emails.Sent.Should().ContainSingle().Which.Message.Should().NotContainAny("api://", "azurewebsites", "token");
    }

    [Fact]
    public void BuildPortalLink_EncodesEveryQueryValue_AndDropsAnyQueryOnThePortalUrl()
    {
        var record = Guid.NewGuid();

        var link = GrantAccessNotifier.BuildPortalLink(
            "https://external.spaarke.com/?customer=evil&x=1#frag", "a b&c=d", ExternalGrantRootType.Project, record);

        link.Should().Be($"https://external.spaarke.com/?customer=a%20b%26c%3Dd&record=project:{record:D}");
        System.Web.HttpUtility.ParseQueryString(new Uri(link!).Query)["customer"].Should().Be("a b&c=d");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    [InlineData("http://external.spaarke.com")]
    [InlineData("https://user:pw@external.spaarke.com")]
    [InlineData("javascript:alert(1)")]
    public void BuildPortalLink_RefusesAPortalUrlThatIsNotAnHttpsOriginWithoutCredentials(string? portalUrl)
    {
        GrantAccessNotifier.BuildPortalLink(portalUrl, CustomerKey, ExternalGrantRootType.Matter, Guid.NewGuid())
            .Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void BuildPortalLink_RefusesAnEmptyCustomerKey_AndAnEmptyRecord(string? key)
    {
        GrantAccessNotifier.BuildPortalLink(Portal, key, ExternalGrantRootType.Matter, Guid.NewGuid()).Should().BeNull();
        GrantAccessNotifier.BuildPortalLink(Portal, CustomerKey, ExternalGrantRootType.Matter, Guid.Empty).Should().BeNull();
    }

    [Fact]
    public void BuildPortalLink_KeepsAPortalPath_WithoutADoubleSlash()
    {
        var record = Guid.NewGuid();

        GrantAccessNotifier.BuildPortalLink("https://portal.test/app/", CustomerKey, ExternalGrantRootType.Matter, record)
            .Should().Be($"https://portal.test/app/?customer={CustomerKey}&record=matter:{record:D}");
    }

    // ── negatives the notifier itself decides ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ContactNotYetOnboarded_GetsNoExternalEmail_TheInvitePathHandlesIt()
    {
        var contact = Contact(oid: null);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task OnboardedContactWithNoEmailAddress_GetsNoEmail(string? email)
    {
        var contact = Contact(oid: Guid.NewGuid(), email: email);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task InactiveContact_GetsNoEmail()
    {
        var contact = Contact(oid: Guid.NewGuid(), stateCode: 1);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ContactThatRepresentsAnInternalUser_EvenADisabledOne_GetsNoExternalEmail()
    {
        var oid = Guid.NewGuid();
        var contact = Contact(oid: oid);
        _links.ByContact[contact] = new[]
        {
            new AssignedLinkCandidate(Guid.NewGuid(), contact, oid, IsDisabled: true, AccessMode: 0, ApplicationId: null, IsExternal: false),
        };

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable, "an internal user's contact is told in-app, never by this email");
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task TheGranter_IsNeverEmailed()
    {
        var oid = Guid.NewGuid();
        var contact = Contact(oid: oid);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter(oid));

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task ABindingColumnNotProvisionedHere_CannotShowOnboarding_SoNoEmail()
    {
        var contact = Guid.NewGuid();
        _identities.Setup(i => i.GetContactAsync(contact, It.IsAny<CancellationToken>())).ReturnsAsync(ContactLookup.ColumnMissing);

        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        _emails.Sent.Should().BeEmpty();
    }

    // ── review K4: a workforce-plane binding is an employee's identity, not an onboarded external contact ──────────────────

    [Fact]
    public async Task ContactBoundOnTheWorkforcePlane_GetsNoExternalEmail()
    {
        var contact = Contact(oid: Guid.NewGuid(), plane: (int)IdentityPlaneMarker.Workforce);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty("a workforce binding is not a CIAM account");
    }

    // r3 047: a home-registered (WorkforceHome) contact signs in to the same portal and is emailed too, when no licensed user
    // carries its home pair (task 079: AWorkforceHomeContact_NotLinked_IsEmailedExactlyAsACiamContactIs).
    [Theory]
    [InlineData(IdentityPlaneMarker.External)]
    public async Task ContactBoundOnAPortalPlane_IsEmailed(IdentityPlaneMarker plane)
    {
        var contact = Contact(oid: Guid.NewGuid(), plane: (int)plane);

        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        _emails.Sent.Should().ContainSingle();
    }

    // ── r3 task 079 (#2458): a WorkforceHome contact whose home sign-in resolves as a licensed user is not emailed ────────

    private static readonly Guid HomeTenant = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000079");
    private static readonly Guid HomeOid = Guid.Parse("a3a3a3a3-0000-4000-8000-000000000079");
    private static readonly Guid LicensedUser = Guid.Parse("c4c4c4c4-0000-4000-8000-000000000079");
    private const string HomeAddress = "sam.employee@customer.test";

    private static HomeLinkRow LinkRow(Guid user, bool disabled) =>
        new(user, disabled, HomeTenant.ToString("D"), HomeOid.ToString("D"));

    /// <summary>
    /// An active WorkforceHome contact AS THE REAL STORE READS IT: <c>GetContactAsync</c> selects <c>ContactSelect</c>, which
    /// carries no <c>sprk_externaltenantid</c>, so the row has no tenant; the home directory comes only from the home-tenant
    /// read (<paramref name="tenant"/>, the stored tenant by default). The stored-link read answers <paramref name="link"/>
    /// (no licensed user by default).
    /// </summary>
    private Guid HomeContact(HomeTenantLookup? tenant = null, HomeLinkLookup? link = null, bool noEmail = false)
    {
        var id = Guid.NewGuid();
        _identities.Setup(i => i.GetContactAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactLookup.Of(new ContactBindingRow(
                id, 0, HomeOid.ToString("D"), (int)IdentityPlaneMarker.WorkforceHome, Email: noEmail ? null : HomeAddress)));
        _identities.Setup(i => i.ReadHomeTenantAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant ?? HomeTenantLookup.FromStored(HomeTenant.ToString("D")));
        _identities.Setup(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(link ?? HomeLinkLookup.Of());
        return id;
    }

    /// <summary>No log line names the person: no email address, no name, no tenant value (ADR-015).</summary>
    private void NoIdentityValueWasLogged()
        => _log.All.Should().NotContain(line => line.Contains(HomeAddress) || line.Contains(HomeTenant.ToString("D"))
                                                 || line.Contains(HomeTenant.ToString("N")) || line.Contains("Gina Granter"));

    [Fact]
    public async Task AWorkforceHomeContact_NotLinked_IsEmailedExactlyAsACiamContactIs()
    {
        var home = HomeContact();
        var ciam = Contact(oid: Guid.NewGuid(), plane: (int)IdentityPlaneMarker.External);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, home, ExternalAccessLevel.Collaborate, Granter());
        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, ciam, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Sent);
        _emails.Sent.Should().HaveCount(2);
        var (homeSent, ciamSent) = (_emails.Sent[0], _emails.Sent[1]);
        homeSent.To.Should().Be(HomeAddress);
        homeSent.LinkUrl.Should().Be(Link(_matter)).And.Be(ciamSent.LinkUrl, "the same deep link");
        homeSent.Subject.Should().Contain(RecordName).And.Be(ciamSent.Subject, "the same template");
        homeSent.Message.Should().Be(ciamSent.Message);
        _identities.Verify(i => i.ReadHomeTenantAsync(home, It.IsAny<CancellationToken>()), Times.Once);
        _identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()), Times.Once,
            "the stored-link read is asked with the stored home tenant and the bound oid, as the portal's home sign-in asks it");
        NoIdentityValueWasLogged();
    }

    [Fact]
    public async Task AWorkforceHomeContact_WhoseHomePairAnEnabledLicensedUserCarries_IsNotEmailed_NorToldInApp()
    {
        var contact = HomeContact(link: HomeLinkLookup.Of(LinkRow(LicensedUser, disabled: false)));

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable,
            "the home sign-in resolves in the portal as the licensed user, whose own rights apply there");
        outcome.Should().NotBe(GrantNotificationOutcome.Sent);
        _emails.Sent.Should().BeEmpty();
        _bell.Invocations.Should().BeEmpty("the licensed user is not sent the internal notice for a contact grant either");
        NoIdentityValueWasLogged();
    }

    [Fact]
    public async Task AWorkforceHomeContact_WhoseHomePairOnlyADisabledUserCarries_IsNotEmailed()
    {
        var contact = HomeContact(link: HomeLinkLookup.Of(LinkRow(LicensedUser, disabled: true)));

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable, "the portal admits that home sign-in nowhere");
        _emails.Sent.Should().BeEmpty();
        _bell.Invocations.Should().BeEmpty();
        NoIdentityValueWasLogged();
    }

    public static TheoryData<string, HomeTenantLookup?, HomeLinkLookup?, string?> CouldNotTell => new()
    {
        { "two enabled users carry the pair (ambiguous)", null,
            HomeLinkLookup.Of(LinkRow(LicensedUser, disabled: false), LinkRow(Guid.Parse("c5c5c5c5-0000-4000-8000-000000000079"), disabled: false)), null },
        { "the stored tenant reads empty (null, masked or unparsable)", HomeTenantLookup.FromStored(null), null, null },
        { "the stored tenant is not a GUID", HomeTenantLookup.FromStored("contoso.example"), null, null },
        { "the tenant read failed", HomeTenantLookup.Failed, null, null },
        { "the tenant column is not provisioned", HomeTenantLookup.ColumnMissing, null, null },
        { "the tenant read throws", null, null, "tenant" },
        { "the stored-link read failed", null, HomeLinkLookup.Failed, null },
        { "the stored-link columns are not provisioned", null, HomeLinkLookup.ColumnMissing, null },
        { "the stored-link read throws", null, null, "link" },
    };

    [Theory]
    [MemberData(nameof(CouldNotTell))]
    public async Task AWorkforceHomeContact_WhoseHomeLinkCannotBeTold_IsNotEmailed_AndTheGrantStands_FailClosed(
        string because, HomeTenantLookup? tenant, HomeLinkLookup? link, string? throws)
    {
        var contact = HomeContact(tenant, link);
        if (throws == "tenant")
        {
            _identities.Setup(i => i.ReadHomeTenantAsync(contact, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("tenant read down"));
        }
        else if (throws == "link")
        {
            _identities.Setup(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("link read down"));
        }

        var act = () => Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        (await act.Should().NotThrowAsync(because)).Subject.Should().Be(GrantNotificationOutcome.Failed, because);
        _emails.Sent.Should().BeEmpty(because);
        _bell.Invocations.Should().BeEmpty(because);
        _log.Warnings.Should().Contain(w => w.Contains("[GRANT-NOTIFY]") && w.Contains(contact.ToString()), because);
        if ((tenant is not null && tenant.HomeTenantId is null) || throws == "tenant")
        {
            _identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
                Times.Never, "no stored-link read without a stored tenant");
        }

        NoIdentityValueWasLogged();
    }

    // Review fix (079): the home check runs AFTER the checks that decide nobody is emailed anyway (no address; the caller is the
    // contact), so a contact that would never be emailed costs no extra read and is NotApplicable, never Failed.
    [Fact]
    public async Task AWorkforceHomeContactWithNoEmailAddress_IsNotApplicable_WithoutTheHomeReads_EvenWhenThoseReadsWouldFail()
    {
        var contact = HomeContact(tenant: HomeTenantLookup.Failed, noEmail: true);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
        NoHomeReadWasMade();
        NoIdentityValueWasLogged();
    }

    [Fact]
    public async Task AWorkforceHomeContactThatIsTheCaller_IsNotApplicable_WithoutTheHomeReads_EvenWhenThoseReadsWouldFail()
    {
        var contact = HomeContact(tenant: HomeTenantLookup.Failed);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter(HomeOid));

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
        NoHomeReadWasMade();
        NoIdentityValueWasLogged();
    }

    private void NoHomeReadWasMade()
    {
        _identities.Verify(i => i.ReadHomeTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData((int)IdentityPlaneMarker.External)]
    [InlineData(null)] // a pre-141 CIAM binding (no plane marker)
    public async Task ANonWorkforceHomeContact_IsEmailedAsToday_WithoutTheHomeReads(int? plane)
    {
        var contact = Contact(oid: Guid.NewGuid(), plane: plane);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Sent);
        _emails.Sent.Should().ContainSingle();
        _identities.Verify(i => i.ReadHomeTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData((int)IdentityPlaneMarker.Workforce)] // a Spaarke-tenant workforce binding is never emailed
    [InlineData(null)] // never onboarded
    public async Task AContactNotEmailedToday_StaysUnemailed_WithoutTheHomeReads(int? plane)
    {
        var contact = plane is null ? Contact(oid: null) : Contact(oid: Guid.NewGuid(), plane: plane);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
        _identities.Verify(i => i.ReadHomeTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AWorkforceHomeContact_ThatAnInternalUserRepresents_NeverReachesTheHomeCheck()
    {
        var contact = HomeContact();
        _links.ByContact[contact] = new[]
        {
            new AssignedLinkCandidate(Guid.NewGuid(), contact, HomeOid, IsDisabled: true, AccessMode: 0, ApplicationId: null, IsExternal: false),
        };

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable, "the internal-user path decides it, unchanged");
        _emails.Sent.Should().BeEmpty();
        _identities.Verify(i => i.ReadHomeTenantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _identities.Verify(i => i.FindHomeLinkedSystemUsersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── review K3: the granter's UPN is never shown to an external party ────────────────────────────────────────────────

    [Fact]
    public async Task ATokenWithOnlyAPreferredUsername_NamesNoOneInTheExternalEmail()
    {
        var contact = Contact(oid: Guid.NewGuid());
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("oid", GranterOid.ToString()),
                new Claim("preferred_username", "gina.granter@firm.example"),
                new Claim(ClaimTypes.Upn, "gina.granter@firm.example"),
                new Claim("email", "gina.granter@firm.example"),
            }, "Test"));

        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, caller);

        var message = _emails.Sent.Should().ContainSingle().Subject.Message;
        message.Should().NotContainAny("gina", "firm.example", "@");
        message.Should().Contain("You were given access");
    }

    [Fact]
    public async Task ANameClaimShapedLikeAnEmailAddress_IsNotUsedEither()
    {
        var contact = Contact(oid: Guid.NewGuid());
        var caller = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", GranterOid.ToString()), new Claim("name", "gina.granter@firm.example") }, "Test"));

        await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, caller);

        _emails.Sent.Should().ContainSingle().Which.Message.Should().NotContain("@");
    }

    // ── review K2: a hung send is bounded ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AHungSend_IsGivenUpAfterTheTimeout_AsFailed_NotAwaitedForever()
    {
        var contact = Contact(oid: Guid.NewGuid());
        _emails.HangUntilCancelled = true;

        var started = System.Diagnostics.Stopwatch.StartNew();
        var outcome = await Notifier(sendTimeout: TimeSpan.FromMilliseconds(100)).NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        started.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "the send is bounded by the timeout, not by the mailbox");
        _log.Warnings.Should().Contain(w => w.Contains("did not finish within"));
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public void TheDefaultSendTimeout_IsFifteenSeconds()
        => GrantAccessNotifier.DefaultSendTimeout.Should().Be(TimeSpan.FromSeconds(15));

    // ── best effort: a failed send is logged and never thrown ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AFailedSend_IsLoggedAndReportedFailed_NeverThrown()
    {
        var contact = Contact(oid: Guid.NewGuid());
        _emails.Fail = _ => new HttpRequestException("Graph is down");

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _log.Warnings.Should().Contain(w => w.Contains("Could not email contact"));
    }

    [Fact]
    public async Task ASenderThatIsNotConfiguredOrApproved_IsLoggedAndSkipped_NeverSentAsTheUser()
    {
        var contact = Contact(oid: Guid.NewGuid());
        _emails.Fail = _ => new SdapProblemException(
            code: "SENDER_NOT_APPROVED", title: "Invalid Sender", detail: "not approved", statusCode: 400);

        var outcome = await Notifier().NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _log.Warnings.Should().Contain(w => w.Contains("SENDER_NOT_APPROVED") && w.Contains("sender is not configured"));
        _emails.Sent.Should().BeEmpty("nothing was sent, and there is no fallback to another sender");
    }

    [Theory]
    [InlineData(null, CustomerKey)]
    [InlineData("", CustomerKey)]
    [InlineData(Portal, "")]
    public async Task WithoutAPortalUrlOrACustomerKey_NoLinkCanBeBuilt_SoNothingIsSent(string? portalUrl, string customerId)
    {
        var contact = Contact(oid: Guid.NewGuid());

        var outcome = await Notifier(portalUrl, customerId).NotifyContactGrantAsync(
            ExternalGrantRootType.Matter, _matter, contact, ExternalAccessLevel.Collaborate, Granter());

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().BeEmpty();
        _log.Warnings.Should().Contain(w => w.Contains("portal link"));
    }

    [Fact]
    public void ThePortalNotificationTemplate_IsAnEmbeddedResource_WithEveryPlaceholderTheServiceFills()
    {
        // LoadTemplateAsync names the resource {namespace}.{file}; a template that is not embedded would fail every send.
        using var stream = typeof(RegistrationEmailService).Assembly.GetManifestResourceStream(
            "Sprk.Bff.Api.Services.Registration.EmailTemplates.PortalNotificationTemplate.html");

        stream.Should().NotBeNull();
        var html = new StreamReader(stream!).ReadToEnd();
        html.Should().ContainAll("{{Headline}}", "{{Message}}", "{{LinkUrl}}", "{{LinkText}}", "{{SupportEmail}}", "{{Year}}");
    }

    // ── doubles ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The email seam: records each send; <see cref="Fail"/> makes a send throw instead.</summary>
    private sealed class RecordingEmails : RegistrationEmailService
    {
        public RecordingEmails() : base(
            null!,
            Options.Create(new CommunicationOptions { DefaultMailbox = "mailbox-central@spaarke.com" }),
            NullLogger<RegistrationEmailService>.Instance)
        {
        }

        public List<(string To, string Subject, string Message, string LinkUrl, string CorrelationId)> Sent { get; } = new();

        public Func<string, Exception?>? Fail { get; set; }

        /// <summary>A send that waits on its token and never completes by itself (a hung mailbox).</summary>
        public bool HangUntilCancelled { get; set; }

        public override async Task SendPortalNotificationEmailAsync(
            string recipientEmail, string subject, string headline, string message, string linkUrl, string linkText,
            string correlationId, CancellationToken cancellationToken = default)
        {
            if (HangUntilCancelled)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Fail?.Invoke(recipientEmail) is { } failure)
                throw failure;
            Sent.Add((recipientEmail, subject, message, linkUrl, correlationId));
        }
    }

    /// <summary>The ledger store's link read: the systemusers a contact may represent (none by default).</summary>
    private sealed class FakeLinks : AssignedAccessStore
    {
        public FakeLinks() : base(null!, NullLogger<AssignedAccessStore>.Instance)
        {
        }

        public Dictionary<Guid, AssignedLinkCandidate[]> ByContact { get; } = new();

        internal override Task<IReadOnlyList<AssignedLinkCandidate>> ReadLinkCandidatesAsync(
            Guid contactId, Guid? boundOid, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AssignedLinkCandidate>>(
                ByContact.TryGetValue(contactId, out var rows) ? rows : Array.Empty<AssignedLinkCandidate>());
    }

    /// <summary>
    /// The Dataverse client: answers a record's name read from <see cref="Names"/>. No HTTP.
    /// </summary>
    private sealed class FakeDataverse : DataverseWebApiClient
    {
        public FakeDataverse()
            : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            }).Build(), NullLogger<DataverseWebApiClient>.Instance, NoOpCredential.Instance)
        {
        }

        public Dictionary<Guid, string> Names { get; } = new();

        /// <summary>Every <c>systemusers</c> filter the notifier queried (the granter lookup by oid).</summary>
        public List<string?> SystemUserQueries { get; } = new();

        /// <summary>The systemuser the oid lookup answers with; null = no systemuser resolves.</summary>
        public Guid? GranterSystemUserId { get; set; }

        public override Task<List<T>> QueryAsync<T>(
            string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
            CancellationToken cancellationToken = default)
        {
            var rows = new List<T>();
            if (entitySetName == "systemusers")
            {
                SystemUserQueries.Add(filter);
                if (GranterSystemUserId is { } id && typeof(T) == typeof(GrantExternalAccessEndpoint.SystemUserRow))
                {
                    rows.Add((T)(object)new GrantExternalAccessEndpoint.SystemUserRow { systemuserid = id });
                }
            }

            return Task.FromResult(rows);
        }

        public override Task<T?> RetrieveAsync<T>(
            string entitySetName, Guid id, string? select = null, CancellationToken cancellationToken = default)
            where T : default
        {
            if (Names.TryGetValue(id, out var name) && select is not null)
            {
                return Task.FromResult(JsonSerializer.Deserialize<T>(
                    JsonSerializer.Serialize(new Dictionary<string, object?> { [select] = name })));
            }

            return Task.FromResult<T?>(default);
        }

        private sealed class NoOpCredential : TokenCredential
        {
            public static readonly NoOpCredential Instance = new();

            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("FakeDataverse issues no HTTP.");

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
                => throw new NotSupportedException("FakeDataverse issues no HTTP.");
        }
    }

    /// <summary>Collects the formatted text of every warning the notifier logs.</summary>
    private sealed class ListLogger : ILogger<GrantAccessNotifier>
    {
        public List<string> Warnings { get; } = new();

        /// <summary>Every line logged, at any level.</summary>
        public List<string> All { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, exception);
            All.Add(text);
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(text);
        }
    }
}
