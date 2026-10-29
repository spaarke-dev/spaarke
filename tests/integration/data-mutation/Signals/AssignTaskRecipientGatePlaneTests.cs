using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 168 fix round 3 (R3): the <see cref="AssignTaskRecipientGate"/> composes an external assignee's access the way the
/// portal does for THAT contact's sign-in plane. A WorkforceHome contact (a customer employee signing in with their home
/// directory) resolves in the portal as a ContactOnly workforce principal, whose composition adds the two derived-member terms
/// (standing-grant membership, organization expansion); a CIAM contact's does not. The evaluator here is the REAL
/// <see cref="AccessibleRecordSetService"/> (module-boundary doubles only: membership, standing grants, the participation
/// reads, the removed marker), and the portal's events list is the REAL <see cref="ExternalDataService"/> over the fake
/// Dataverse the events-list tests use, asked with the principal the REAL portal strategy builds.
/// </summary>
public sealed class AssignTaskRecipientGatePlaneTests
{
    private static readonly Guid Contact = Guid.Parse("c1680003-0000-4000-8000-0000000000b1");
    private static readonly Guid HomeTenant = Guid.Parse("c1680003-0000-4000-8000-0000000000b2");
    private static readonly Guid HomeOid = Guid.Parse("c1680003-0000-4000-8000-0000000000b3");
    private static readonly Guid CiamOid = Guid.Parse("c1680003-0000-4000-8000-0000000000b4");
    private static readonly Guid Matter = Guid.Parse("c1680003-0000-4000-8000-0000000000b5");
    private static readonly Guid OrgA = Guid.Parse("c1680003-0000-4000-8000-0000000000b6");
    private static readonly Guid LicensedUser = Guid.Parse("c1680003-0000-4000-8000-0000000000b7");
    private const string Address = "sam.employee@customer.example";

    private static readonly DecisionCoreRecord Core = new(AccessibleRecordSetService.MatterEntity, Matter);

    /// <summary>A WorkforceHome contact as registration leaves it: the home oid, the WorkforceHome marker, the home tenant.</summary>
    private static ContactBindingRow HomeContact() =>
        new(Contact, 0, HomeOid.ToString("D"), (int)IdentityPlaneMarker.WorkforceHome, Email: Address,
            RawExternalTenantId: HomeTenant.ToString("D"));

    /// <summary>An onboarded CIAM contact: a portal-account oid, the External marker.</summary>
    private static ContactBindingRow CiamContact() =>
        new(Contact, 0, CiamOid.ToString("D"), (int)IdentityPlaneMarker.External, Email: Address);

    // ── The world: the REAL evaluator over module-boundary doubles ───────────────────────────────────────────────────────

    /// <summary>How the contact reaches the matter.</summary>
    public enum Access
    {
        /// <summary>Only through its own standing grant (contact-anchored membership).</summary>
        StandingGrant,

        /// <summary>Only through an organization it belongs to (organization expansion).</summary>
        Organization,

        /// <summary>Through an explicit grant on the matter (both planes hold it).</summary>
        Grant,
    }

    private static MembershipResponse Response(string entityType, params Guid[] ids) => new(
        entityType, new PersonIdentity(Guid.Empty, ContactId: Contact), ids, new Dictionary<string, IReadOnlyList<Guid>>(),
        ids.Length, DateTimeOffset.UtcNow.AddMinutes(5));

    private static AccessibleRecordSetService Evaluator(Access access, RemovedMarkerRead marker = RemovedMarkerRead.NotRemoved)
    {
        // Membership: the walk (by the contact, or by its organization) reaches the matter; nothing on the other tables.
        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(Contact, It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string entity, MembershipResolveOptions? _, CancellationToken _) =>
                entity == AccessibleRecordSetService.MatterEntity && access != Access.Grant
                    ? Response(entity, Matter)
                    : Response(entity));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(Contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access == Access.StandingGrant
                ? new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly)
                : StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(access == Access.Organization
                ? new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly)
                : StandingGrantState.NotHeld);

        var participations = new GrantPolicyTestDoubles.FlagStubParticipationService(RootRecordFlags.None);
        participations.GrantSets[Contact] = access == Access.Grant
            ? new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                MatterGrants = AccessibleRecordSetTestFactory.RootGrants(ExternalAccessLevel.ViewOnly, Matter),
                WorkAssignmentGrants = AccessibleRecordSetTestFactory.NoRootGrants,
            }
            : ExternalGrantSet.Empty;
        if (access == Access.Organization)
            participations.ContactOrganizations[Contact] = new[] { OrgA };

        var identity = new Mock<IContactIdentityStore>(MockBehavior.Strict);
        identity.Setup(s => s.ReadRemovedMarkerAsync(Contact, It.IsAny<CancellationToken>())).ReturnsAsync(marker);

        return new AccessibleRecordSetService(
            membership.Object, participations, standing.Object, AccessibleRecordSetTestFactory.NeverDeniesReader(), identity.Object,
            AccessibleRecordSetTestFactory.InternalSystemUsers(), AccessibleRecordSetTestFactory.NoFilingEntities(),
            NullLogger<AccessibleRecordSetService>.Instance);
    }

    /// <summary>The identity reads the gate makes: the contact row, and the portal's stored-link read for a home pair.</summary>
    private static Mock<IContactIdentityStore> Identities(ContactBindingRow row, HomeLinkLookup? homeLink = null)
    {
        var identities = new Mock<IContactIdentityStore>(MockBehavior.Strict);
        identities.Setup(i => i.GetContactAsync(Contact, It.IsAny<CancellationToken>())).ReturnsAsync(ContactLookup.Of(row));
        identities.Setup(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(homeLink ?? HomeLinkLookup.Of());
        return identities;
    }

    private static AssignTaskRecipientGate Gate(
        IContactIdentityStore identities, IAccessibleRecordSetService accessibleSet, ExternalDataService? portalData = null,
        AssignedAccessTestDoubles.FakeAssignedAccessStore? links = null) =>
        new(identities, links ?? new AssignedAccessTestDoubles.FakeAssignedAccessStore(), null!, null!, null!, accessibleSet, portalData!,
            NullLogger<AssignTaskRecipientGate>.Instance);

    /// <summary>The events list on the matter, as the portal serves it to the contact's HOME sign-in: the REAL workforce strategy
    /// resolving the ContactOnly principal the home resolver returns for it.</summary>
    private static async Task<IReadOnlyList<string?>> ListedForHomeSignIn(ExternalDataService service, IAccessibleRecordSetService evaluator)
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForContact(Contact, HomeOid.ToString("D"), HomeTenant.ToString("D")));
        var resolution = await new WorkforcePrincipalStrategy(resolver.Object, evaluator, NullLogger<WorkforcePrincipalStrategy>.Instance)
            .ResolveAsync(new DefaultHttpContext(), CancellationToken.None);
        resolution.IsResolved.Should().BeTrue("precondition: the home sign-in resolves to the contact");

        var list = await service.GetEventsAsync(ExternalDataService.TodoRootKind.Matter, Matter, resolution.Principal!);
        return list.Value.Select(e => e.SprkEventid).ToList();
    }

    /// <summary>The events list on the matter, as the portal serves it to the contact's CIAM sign-in.</summary>
    private static async Task<IReadOnlyList<string?>> ListedForCiamSignIn(ExternalDataService service, IAccessibleRecordSetService evaluator)
    {
        var principal = (await CiamContactPrincipalStrategy.ComposeAsync(evaluator, Contact, Address, CiamOid.ToString("D"), CancellationToken.None))
            .Principal;
        var list = await service.GetEventsAsync(ExternalDataService.TodoRootKind.Matter, Matter, principal);
        return list.Value.Select(e => e.SprkEventid).ToList();
    }

    // ── Must fire: a WorkforceHome contact reached only through a derived-member term ──────────────────────────────────────

    [Theory]
    [InlineData(Access.StandingGrant)]
    [InlineData(Access.Organization)]
    public async Task AWorkforceHomeContact_WhoseOnlyAccessIsADerivedMemberTerm_IsEmailed_AsTheirPortalListsTheTask(Access access)
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var task = data.Add("matters", Matter, "Assigned task");
        var service = data.NewService();
        var evaluator = Evaluator(access);

        (await Gate(Identities(HomeContact()).Object, evaluator, service).DecideAsync(Contact, Core, Guid.Parse(task), null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.ForExternal());

        // Parity: the gate's verdict is what the portal's events list shows that sign-in.
        (await ListedForHomeSignIn(service, evaluator)).Should().Equal(task);

        // The control: the CIAM composition (round 2's, used for every contact) has no Read on the matter here.
        (await evaluator.ComposeForCiamContactAsync(Contact, AccessibleRecordSetService.MatterEntity, CancellationToken.None))
            .RightsFor(Matter).Should().Be(AccessRights.None, "this access exists only on the workforce contact plane");
    }

    // ── Must not fire: the same access on a CIAM contact ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(Access.StandingGrant)]
    [InlineData(Access.Organization)]
    public async Task ACiamContact_WithTheSameDerivedOnlyAccess_IsNotEmailed_AsTheCiamPortalDoesNotListTheTask_MustNotFire(Access access)
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var task = data.Add("matters", Matter, "Assigned task");
        var service = data.NewService();
        var evaluator = Evaluator(access);

        (await Gate(Identities(CiamContact()).Object, evaluator, service).DecideAsync(Contact, Core, Guid.Parse(task), null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.NoAccess));
        (await ListedForCiamSignIn(service, evaluator)).Should().BeEmpty();
    }

    // ── A contact whose external access was removed composes EMPTY on either plane ───────────────────────────────────────

    [Theory]
    [InlineData("workforce-home")]
    [InlineData("ciam")]
    public async Task AContactWhoseExternalAccessWasRemoved_ComposesEmpty_AndIsNotEmailed_OnEitherPlane_MustNotFire(string plane)
    {
        var row = plane == "ciam" ? CiamContact() : HomeContact();
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var task = Guid.Parse(data.Add("matters", Matter, "Assigned task"));

        // The control: the same contact, unmarked, holding an explicit grant on the matter, is emailed.
        var unmarked = Gate(Identities(row).Object, Evaluator(Access.Grant), data.NewService());
        (await unmarked.DecideAsync(Contact, Core, task, null, CancellationToken.None)).Should().Be(AssignTaskRecipient.ForExternal());

        var removed = Gate(Identities(row).Object, Evaluator(Access.Grant, RemovedMarkerRead.Removed), data.NewService());
        var composed = await removed.ComposePortalPrincipalAsync(row, AssignTaskRecipientGate.PortalPlaneOf(row), CancellationToken.None);
        composed.Principal.ProjectAccess.Should().BeEmpty();
        composed.Principal.MatterAccess.Should().BeEmpty($"{plane}: a removed person confers nothing");
        composed.Principal.WorkAssignmentAccess.Should().BeEmpty();
        (await removed.DecideAsync(Contact, Core, task, null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.NoAccess));
    }

    // ── The plane: read the way every binding reader reads it; anything but a readable WorkforceHome binding is CIAM ─────

    public static TheoryData<string, ContactBindingRow, bool> Planes => new()
    {
        { "workforce-home", HomeContact(), true },
        { "ciam", CiamContact(), false },
        { "legacy oid, no marker (pre-141 CIAM)", new ContactBindingRow(Contact, 0, CiamOid.ToString("D"), null, Email: Address), false },
        { "unknown marker value", new ContactBindingRow(Contact, 0, HomeOid.ToString("D"), 999, Email: Address), false },
        { "workforce-home with no readable plane", new ContactBindingRow(Contact, 0, HomeOid.ToString("D"), null, Email: Address,
            RawExternalTenantId: HomeTenant.ToString("D")) { IdentityColumnsUnproven = true }, false },
        { "workforce-home marker, no oid", new ContactBindingRow(Contact, 0, null, (int)IdentityPlaneMarker.WorkforceHome, Email: Address,
            RawExternalTenantId: HomeTenant.ToString("D")), false },
        { "unbound", new ContactBindingRow(Contact, 0, null, null, Email: Address), false },
        { "spaarke-tenant workforce", new ContactBindingRow(Contact, 0, HomeOid.ToString("D"), (int)IdentityPlaneMarker.Workforce, Email: Address),
            false },
    };

    [Theory]
    [MemberData(nameof(Planes))]
    public async Task ThePlane_IsWorkforceHomeOnlyForAReadableWorkforceHomeBinding_AndEveryOtherComposesAsCiam(
        string because, ContactBindingRow row, bool workforceHome)
    {
        var expected = workforceHome ? ExternalContactPlane.WorkforceHome : ExternalContactPlane.Ciam;
        AssignTaskRecipientGate.PortalPlaneOf(row).Should().Be(expected, because);

        // The composition that plane selects: Strict, so the OTHER composition would throw.
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var empty = (string entity) => new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entity,
            Rights = new Dictionary<Guid, AccessRights>(),
            Sources = new AccessibleRecordSetSources(false, true, false),
        };
        if (expected == ExternalContactPlane.WorkforceHome)
        {
            sets.Setup(s => s.ComposeAsync(It.Is<WorkforcePrincipal>(p => p.IsContactOnly && p.ContactId == Contact), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((WorkforcePrincipal _, string entity, CancellationToken _) => empty(entity));
        }
        else
        {
            sets.Setup(s => s.ComposeForCiamContactAsync(Contact, It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid _, string entity, CancellationToken _) => empty(entity));
        }

        var composed = await Gate(Mock.Of<IContactIdentityStore>(), sets.Object)
            .ComposePortalPrincipalAsync(row, AssignTaskRecipientGate.PortalPlaneOf(row), CancellationToken.None);

        composed.Principal.Plane.Should().Be(
            expected == ExternalContactPlane.WorkforceHome ? CallerPrincipalPlane.Workforce : CallerPrincipalPlane.CiamContact, because);
        composed.Principal.ContactId.Should().Be(Contact);
        sets.Invocations.Should().HaveCount(3, "one composition per root table, once per decision");
    }

    // ── A home sign-in a licensed user carries resolves in the portal as that user (Kind = SystemUser), never this contact ─

    public static TheoryData<string, HomeLinkLookup?, bool, AssignTaskRefusal> HomeLinks => new()
    {
        { "an enabled licensed user carries the pair", HomeLinkLookup.Of(new HomeLinkRow(LicensedUser, false, HomeTenant.ToString("D"), HomeOid.ToString("D"))), false, AssignTaskRefusal.Unverified },
        { "only a disabled user carries it (the portal denies the sign-in)", HomeLinkLookup.Of(new HomeLinkRow(LicensedUser, true, HomeTenant.ToString("D"), HomeOid.ToString("D"))), false, AssignTaskRefusal.NoAccess },
        { "two enabled users carry it (ambiguous)", HomeLinkLookup.Of(
            new HomeLinkRow(LicensedUser, false, HomeTenant.ToString("D"), HomeOid.ToString("D")),
            new HomeLinkRow(Guid.NewGuid(), false, HomeTenant.ToString("D"), HomeOid.ToString("D"))), false, AssignTaskRefusal.Unverified },
        { "the link read failed", HomeLinkLookup.Failed, false, AssignTaskRefusal.Unverified },
        { "the link columns are missing", HomeLinkLookup.ColumnMissing, false, AssignTaskRefusal.Unverified },
        { "the link read throws", null, true, AssignTaskRefusal.Unverified },
    };

    [Theory]
    [MemberData(nameof(HomeLinks))]
    public async Task AWorkforceHomeContact_WhoseHomeSignInIsNotThisContactsInThePortal_IsNotEmailed_AndNeverComposed_MustNotFire(
        string because, HomeLinkLookup? lookup, bool throws, AssignTaskRefusal expected)
    {
        var identities = Identities(HomeContact(), lookup);
        if (throws)
        {
            identities.Setup(i => i.FindHomeLinkedSystemUsersAsync(HomeTenant, HomeOid, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new HttpRequestException("link read down"));
        }

        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);

        (await Gate(identities.Object, sets.Object).DecideAsync(Contact, Core, Guid.NewGuid(), null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(expected), because);
        sets.Invocations.Should().BeEmpty("the contact's own access is not what that sign-in sees");
    }

    [Fact]
    public async Task AWorkforceHomeContact_WithNoReadableHomeTenant_IsUnverified_MustNotFire()
    {
        var row = HomeContact() with { RawExternalTenantId = "not-a-guid" };
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);

        (await Gate(Identities(row).Object, sets.Object).DecideAsync(Contact, Core, Guid.NewGuid(), null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.Unverified));
        sets.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task AWorkforceHomeContact_LinkedToALicensedUserBySprkPrimaryContact_IsDecidedOnTheInternalPath_NeverTheExternalOne()
    {
        // The "represents" step (DecideCoreAsync) reads the systemusers whose sprk_primarycontact names the contact: such a user
        // is the contact's, so the external branch is never reached, whatever its binding's plane.
        var links = new AssignedAccessTestDoubles.FakeAssignedAccessStore();
        links.UsersByLink.GetOrAdd(Contact, _ => new List<AssignedLinkCandidate>())
            .Add(new AssignedLinkCandidate(LicensedUser, Contact, Guid.NewGuid(), false, 0, null, false));
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        var gate = new Mock<AssignTaskRecipientGate>(Identities(HomeContact()).Object, links, null!, null!, null!, sets.Object, null!,
            NullLogger<AssignTaskRecipientGate>.Instance) { CallBase = true };
        gate.Setup(g => g.WallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SecureShareWallOutcome.NotSecure);
        gate.Setup(g => g.TaskRightsAsync(LicensedUser, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(AccessRights.Read);

        (await gate.Object.DecideAsync(Contact, Core, Guid.NewGuid(), null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.ForInternal(LicensedUser));
        sets.Invocations.Should().BeEmpty("no external composition is asked");
    }
}
