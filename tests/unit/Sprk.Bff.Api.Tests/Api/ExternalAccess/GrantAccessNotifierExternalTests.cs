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
/// access) and its message entry point <see cref="GrantAccessNotifier.NotifyMessagePostedAsync"/> (a posted message told to
/// the record's other member contacts). The PRODUCTION notifier; doubled are only the I/O boundaries — the identity
/// store, the ledger store's link read, the Dataverse client (the member read and the record's name) and the email
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

    public GrantAccessNotifierExternalTests()
    {
        _dataverse.Names[_matter] = RecordName;
    }

    // ── fixture ─────────────────────────────────────────────────────────────────────────────────────

    private GrantAccessNotifier Notifier(string? portalUrl = Portal, string? customerId = CustomerKey)
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
            new NotificationService(Mock.Of<IGenericEntityService>(), NullLogger<NotificationService>.Instance),
            _dataverse, _identities.Object, _links, guard, participations, _emails,
            new CustomerIdentity(
                Options.Create(new CustomerOptions { Id = customerId ?? string.Empty }), config,
                NullLogger<CustomerIdentity>.Instance),
            config, _log);
    }

    private static ClaimsPrincipal Granter(Guid? oid = null) => new(new ClaimsIdentity(
        new[] { new Claim("oid", (oid ?? GranterOid).ToString()), new Claim("name", "Gina Granter") }, "Test"));

    /// <summary>An active contact the identity store returns; <paramref name="oid"/> = null is a contact never onboarded.</summary>
    private Guid Contact(Guid? oid = null, string? email = "partner@outside.test", int stateCode = 0)
    {
        var id = Guid.NewGuid();
        _identities
            .Setup(i => i.GetContactAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactLookup.Of(new ContactBindingRow(
                id, stateCode, oid?.ToString("D"), null, Email: email)));
        return id;
    }

    private static string Link(Guid record, string type = "matter") =>
        $"{Portal}/?customer={CustomerKey}&record={type}:{record:D}";

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

    // ── FR-07 / FR-11: a posted message → each OTHER member contact, once ────────────────────────────────────────────────

    private static MessagePostedNotification Posted(Guid author, params (ExternalGrantRootType Type, Guid Id)[] roots) =>
        new(roots.Select(r => new MessageNotifyRoot(r.Type, r.Id)).ToList(), Guid.NewGuid(), author);

    [Fact]
    public async Task APostedMessage_EmailsEachOtherMemberContactOnce_AndNeverTheAuthor()
    {
        var author = Contact(oid: Guid.NewGuid(), email: "author@outside.test");
        var memberA = Contact(oid: Guid.NewGuid(), email: "a@outside.test");
        var memberB = Contact(oid: Guid.NewGuid(), email: "b@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { author, memberA, memberB, memberA };

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(author, (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Sent);
        _emails.Sent.Select(s => s.To).Should().BeEquivalentTo(new[] { "a@outside.test", "b@outside.test" });
        _emails.Sent.Should().OnlyContain(s => s.LinkUrl == Link(_matter));
        _emails.Sent.Should().OnlyContain(s => s.Subject.Contains(RecordName));
    }

    [Fact]
    public async Task APostedMessage_ByAContactWithNoGrantRowOfTheirOwn_StillNeverEmailsThemselves()
    {
        // The author is excluded by identity, not by being absent from the grant rows (a systemuser author's derived contact).
        var author = Contact(oid: Guid.NewGuid(), email: "author@spaarke.test");
        var member = Contact(oid: Guid.NewGuid(), email: "m@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { member, author };

        await Notifier().NotifyMessagePostedAsync(Posted(author, (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        _emails.Sent.Should().ContainSingle().Which.To.Should().Be("m@outside.test");
    }

    [Fact]
    public async Task TheMemberRead_IsScopedToTheRootsPostedOn_ActiveAndUnexpiredOnly()
    {
        var other = Guid.NewGuid();
        _dataverse.Members[_matter] = Array.Empty<Guid?>();
        _dataverse.Members[other] = new Guid?[] { Contact(oid: Guid.NewGuid()) };

        await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        var filter = _dataverse.Filters.Should().ContainSingle("one root, one read").Subject;
        filter.Should().Contain($"_sprk_matter_value eq {_matter:D}");
        filter.Should().Contain("statecode eq 0").And.Contain("sprk_expiresdate ge ");
        filter.Should().NotContain(other.ToString("D"), "another root's members are never read, so never told");
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task APostedMessage_UnderTwoRoots_EmailsAMemberOfBothOnce_WithTheLinkToTheFirstRoot()
    {
        var project = Guid.NewGuid();
        var both = Contact(oid: Guid.NewGuid(), email: "both@outside.test");
        var onlyProject = Contact(oid: Guid.NewGuid(), email: "p@outside.test");
        _dataverse.Names[project] = "Project Falcon";
        _dataverse.Members[_matter] = new Guid?[] { both };
        _dataverse.Members[project] = new Guid?[] { both, onlyProject };

        await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter), (ExternalGrantRootType.Project, project)),
            CancellationToken.None);

        _emails.Sent.Select(s => (s.To, s.LinkUrl)).Should().BeEquivalentTo(new[]
        {
            ("both@outside.test", Link(_matter)),
            ("p@outside.test", Link(project, "project")),
        });
    }

    [Fact]
    public async Task OrganizationGrantRows_AndMembersWhoAreNotOnboardedOrActiveOrHaveNoEmail_AreSkipped()
    {
        var notOnboarded = Contact(oid: null, email: "new@outside.test");
        var inactive = Contact(oid: Guid.NewGuid(), email: "gone@outside.test", stateCode: 1);
        var noEmail = Contact(oid: Guid.NewGuid(), email: null);
        var good = Contact(oid: Guid.NewGuid(), email: "ok@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { null, notOnboarded, inactive, noEmail, good, Guid.Empty };

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Sent);
        _emails.Sent.Should().ContainSingle().Which.To.Should().Be("ok@outside.test");
    }

    [Fact]
    public async Task APostedMessage_WithNoOtherMembers_SendsNothing()
    {
        var author = Contact(oid: Guid.NewGuid());
        _dataverse.Members[_matter] = new Guid?[] { author };

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(author, (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.NotApplicable);
        _emails.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task OneFailedSend_IsLogged_TheOtherMembersAreStillEmailed_NothingIsThrown()
    {
        var bad = Contact(oid: Guid.NewGuid(), email: "bad@outside.test");
        var good = Contact(oid: Guid.NewGuid(), email: "good@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { bad, good };
        _emails.Fail = to => to == "bad@outside.test" ? new HttpRequestException("mailbox full") : null;

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().ContainSingle().Which.To.Should().Be("good@outside.test");
        _log.Warnings.Should().Contain(w => w.Contains("Could not email contact"));
    }

    [Fact]
    public async Task AMemberReadThatFails_IsLogged_AndTheOtherRootsStillGoOut()
    {
        var project = Guid.NewGuid();
        var member = Contact(oid: Guid.NewGuid(), email: "p@outside.test");
        _dataverse.Names[project] = "Project Falcon";
        _dataverse.FailingRoots.Add(_matter);
        _dataverse.Members[project] = new Guid?[] { member };

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter), (ExternalGrantRootType.Project, project)),
            CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().ContainSingle().Which.LinkUrl.Should().Be(Link(project, "project"));
        _log.Warnings.Should().Contain(w => w.Contains("Could not read the members"));
    }

    [Fact]
    public async Task AMemberWhoseContactCannotBeRead_IsSkippedAndLogged_TheOthersStillGoOut()
    {
        var unreadable = Guid.NewGuid();
        _identities.Setup(i => i.GetContactAsync(unreadable, It.IsAny<CancellationToken>())).ReturnsAsync(ContactLookup.Failed);
        var good = Contact(oid: Guid.NewGuid(), email: "good@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { unreadable, good };

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().ContainSingle().Which.To.Should().Be("good@outside.test");
    }

    [Fact]
    public async Task APostedMessage_WithoutAPortalLink_SendsNothing_AndSaysSo()
    {
        var member = Contact(oid: Guid.NewGuid());
        _dataverse.Members[_matter] = new Guid?[] { member };

        var outcome = await Notifier(portalUrl: null).NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().BeEmpty();
        _log.Warnings.Should().Contain(w => w.Contains("portal link"));
    }

    [Fact]
    public async Task TheEmailForAMessage_NamesNoMessageText_AndCarriesOnlyThePortalLink()
    {
        var member = Contact(oid: Guid.NewGuid(), email: "m@outside.test");
        _dataverse.Members[_matter] = new Guid?[] { member };

        await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), CancellationToken.None);

        var sent = _emails.Sent.Should().ContainSingle().Subject;
        sent.LinkUrl.Should().Be(Link(_matter));
        sent.LinkUrl.Should().NotContainAny("api://", "user_impersonation", "token", "azurewebsites");
        sent.CorrelationId.Should().StartWith("portal-msg-");
    }

    [Fact]
    public async Task ACancelledNotification_StopsQuietly_ReportingFailed()
    {
        var member = Contact(oid: Guid.NewGuid());
        _dataverse.Members[_matter] = new Guid?[] { member };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var outcome = await Notifier().NotifyMessagePostedAsync(
            Posted(Guid.NewGuid(), (ExternalGrantRootType.Matter, _matter)), cts.Token);

        outcome.Should().Be(GrantNotificationOutcome.Failed);
        _emails.Sent.Should().BeEmpty();
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
        public RecordingEmails() : base(null!, NullLogger<RegistrationEmailService>.Instance)
        {
        }

        public List<(string To, string Subject, string Message, string LinkUrl, string CorrelationId)> Sent { get; } = new();

        public Func<string, Exception?>? Fail { get; set; }

        public override Task SendPortalNotificationEmailAsync(
            string recipientEmail, string subject, string headline, string message, string linkUrl, string linkText,
            string correlationId, CancellationToken cancellationToken = default)
        {
            if (Fail?.Invoke(recipientEmail) is { } failure)
                throw failure;
            Sent.Add((recipientEmail, subject, message, linkUrl, correlationId));
            return Task.CompletedTask;
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
    /// The Dataverse client: answers the member read (<c>sprk_externalrecordaccesses</c>, recording each filter) from
    /// <see cref="Members"/> by the root id in the filter, and a record's name read from <see cref="Names"/>. No HTTP.
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

        public Dictionary<Guid, Guid?[]> Members { get; } = new();
        public HashSet<Guid> FailingRoots { get; } = new();
        public Dictionary<Guid, string> Names { get; } = new();
        public List<string> Filters { get; } = new();

        public override Task<List<T>> QueryAsync<T>(
            string entitySetName, string? filter = null, string? select = null, int? top = null, int? skip = null,
            CancellationToken cancellationToken = default)
        {
            entitySetName.Should().Be("sprk_externalrecordaccesses");
            select.Should().Be("_sprk_contact_value");
            Filters.Add(filter!);

            var root = Members.Keys.Concat(FailingRoots).Distinct().FirstOrDefault(
                id => filter!.Contains(id.ToString("D"), StringComparison.OrdinalIgnoreCase));
            if (FailingRoots.Contains(root))
                throw new HttpRequestException("Simulated Dataverse failure.");

            var rows = Members.TryGetValue(root, out var contacts)
                ? contacts.Select(c => new Dictionary<string, object?> { ["_sprk_contact_value"] = c }).ToList()
                : new List<Dictionary<string, object?>>();
            return Task.FromResult(JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(rows))!);
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

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
