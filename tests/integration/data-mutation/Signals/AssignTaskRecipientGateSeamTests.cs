using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Task 168 fix round 2 (review item 4): the <see cref="AssignTaskRecipientGate"/>'s Dataverse-facing seams, run for REAL over
/// their real dependency (the notifier tests double the seams whole, so their own logic ran nowhere): the No Access wall over the
/// REAL <see cref="SecureShareNoAccessGuard"/>; the task Read over a stubbed <see cref="CallerRecordAccessProbe"/>; the Restricted
/// state over a stubbed <see cref="ExternalParticipationService"/> flag read; the contact's grants over a stubbed
/// <see cref="IAccessibleRecordSetService"/>; and (review item 1) the portal events list's per-row rule over the REAL
/// <see cref="ExternalDataService"/> and the fake Dataverse the events-list tests use, asserted to give the list's own verdict.
/// </summary>
public sealed class AssignTaskRecipientGateSeamTests
{
    private static readonly Guid Contact = Guid.Parse("c1680000-0000-4000-8000-0000000000a1");
    private static readonly Guid User = Guid.Parse("c1680000-0000-4000-8000-0000000000a2");
    private static readonly Guid UserOid = Guid.Parse("c1680000-0000-4000-8000-0000000000a3");
    private static readonly Guid LinkedContact = Guid.Parse("c1680000-0000-4000-8000-0000000000a4");
    private static readonly Guid TaskId = Guid.Parse("c1680000-0000-4000-8000-0000000000a5");
    private static readonly Guid Matter = Guid.Parse("c1680000-0000-4000-8000-0000000000a6");
    private static readonly Guid SecureProject = Guid.Parse("c1680000-0000-4000-8000-0000000000a7");
    private static readonly Guid PortalOid = Guid.Parse("c1680000-0000-4000-8000-0000000000a8");
    private const string Address = "pat.counsel@outside.example";

    private static AssignTaskRecipientGate Gate(
        ExternalParticipationService? participations = null,
        SecureShareNoAccessGuard? guard = null,
        CallerRecordAccessProbe? probe = null,
        IAccessibleRecordSetService? accessibleSet = null,
        ExternalDataService? portalData = null,
        IContactIdentityStore? identities = null,
        AssignedAccessTestDoubles.FakeAssignedAccessStore? links = null) =>
        new(identities ?? Mock.Of<IContactIdentityStore>(), links ?? new AssignedAccessTestDoubles.FakeAssignedAccessStore(),
            participations!, guard!, probe!, accessibleSet!, portalData!, NullLogger<AssignTaskRecipientGate>.Instance);

    private static AccessibleRecordSet Set(string entity, bool capped = false, params (Guid Id, AccessRights Rights)[] rights) => new()
    {
        PrincipalKind = WorkforcePrincipalKind.ContactOnly,
        EntityType = entity,
        Rights = rights.ToDictionary(r => r.Id, r => r.Rights),
        Sources = new AccessibleRecordSetSources(false, true, false),
        Capped = capped,
    };

    /// <summary>The portal's composition for <see cref="Contact"/>: Read on <paramref name="matters"/>, nothing else.</summary>
    private static Mock<IAccessibleRecordSetService> Grants(bool cappedMatters = false, params Guid[] matters)
    {
        var sets = new Mock<IAccessibleRecordSetService>(MockBehavior.Strict);
        sets.Setup(s => s.ComposeForCiamContactAsync(Contact, AccessibleRecordSetService.MatterEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Set(AccessibleRecordSetService.MatterEntity, cappedMatters, matters.Select(m => (m, AccessRights.Read)).ToArray()));
        sets.Setup(s => s.ComposeForCiamContactAsync(Contact, AccessibleRecordSetService.ProjectEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Set(AccessibleRecordSetService.ProjectEntity));
        sets.Setup(s => s.ComposeForCiamContactAsync(Contact, AccessibleRecordSetService.WorkAssignmentEntity, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Set(AccessibleRecordSetService.WorkAssignmentEntity));
        return sets;
    }

    /// <summary>An onboarded external contact: a CIAM binding and an email, as the portal's invite leaves it.</summary>
    private static ContactBindingRow ExternalContact() =>
        new(Contact, 0, PortalOid.ToString("D"), (int)IdentityPlaneMarker.External, Email: Address);

    // ── PortalListsTaskAsync: THE events list's per-row rule (review item 1) ─────────────────────────────────────────────

    /// <summary>The events list for the matter, as the portal serves it to the contact (the same principal composition).</summary>
    private static async Task<IReadOnlyList<string?>> ListedOnTheMatter(ExternalDataService service, Mock<IAccessibleRecordSetService> sets)
    {
        var principal = (await CiamContactPrincipalStrategy.ComposeAsync(sets.Object, Contact, Address, PortalOid.ToString("D"), CancellationToken.None))
            .Principal;
        var list = await service.GetEventsAsync(ExternalDataService.TodoRootKind.Matter, Matter, principal);
        return list.Value.Select(e => e.SprkEventid).ToList();
    }

    [Fact]
    public async Task PortalListsTaskAsync_ATaskAlsoFiledToASecureProjectTheContactCannotRead_IsWithheld_AsTheEventsListWithholdsIt()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddSecureRoot(ExternalGrantRootType.Project, SecureProject);
        var plain = data.Add("matters", Matter, "Matter only");
        var alsoSecure = data.Add("matters", Matter, "Matter and secure project", project: SecureProject);
        var sets = Grants(false, Matter);
        var service = data.NewService();
        var gate = Gate(accessibleSet: sets.Object, portalData: service);

        (await gate.PortalListsTaskAsync(ExternalContact(), Guid.Parse(alsoSecure), CancellationToken.None)).Should().BeFalse();
        (await gate.PortalListsTaskAsync(ExternalContact(), Guid.Parse(plain), CancellationToken.None)).Should().BeTrue();

        // The same verdict the events list gives for those rows.
        (await ListedOnTheMatter(service, sets)).Should().Equal(plain);
    }

    [Fact]
    public async Task DecideAsync_AnExternalContactWithReadOnTheMatter_OnATaskAlsoUnderASecureProject_IsRefused_MustNotFire()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddSecureRoot(ExternalGrantRootType.Project, SecureProject);
        var plain = Guid.Parse(data.Add("matters", Matter, "Matter only"));
        var alsoSecure = Guid.Parse(data.Add("matters", Matter, "Matter and secure project", project: SecureProject));
        var identities = new Mock<IContactIdentityStore>();
        identities.Setup(i => i.GetContactAsync(Contact, It.IsAny<CancellationToken>())).ReturnsAsync(ContactLookup.Of(ExternalContact()));
        var sets = Grants(false, Matter);
        var service = data.NewService();
        var gate = Gate(accessibleSet: sets.Object, portalData: service, identities: identities.Object);
        var core = new DecisionCoreRecord("sprk_matter", Matter);

        (await gate.DecideAsync(Contact, core, alsoSecure, null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.Refused(AssignTaskRefusal.NoAccess));
        (await gate.DecideAsync(Contact, core, plain, null, CancellationToken.None))
            .Should().Be(AssignTaskRecipient.ForExternal(), "the control: a task filed under the matter alone is emailed");
        (await ListedOnTheMatter(service, sets)).Should().Equal(plain.ToString());
    }

    [Fact]
    public async Task PortalListsTaskAsync_AFlagReadThatFails_IsNull_NotListed()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse { FlagReadThrows = true };
        var task = Guid.Parse(data.Add("matters", Matter, "Matter and secure project", project: SecureProject));

        (await Gate(accessibleSet: Grants(false, Matter).Object, portalData: data.NewService())
                .PortalListsTaskAsync(ExternalContact(), task, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task PortalListsTaskAsync_WithheldWhileACompositionIsCapped_IsNull_TheContactMayHoldTheRoot()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        data.AddSecureRoot(ExternalGrantRootType.Project, SecureProject);
        var task = Guid.Parse(data.Add("matters", Matter, "Matter and secure project", project: SecureProject));

        (await Gate(accessibleSet: Grants(true, Matter).Object, portalData: data.NewService())
                .PortalListsTaskAsync(ExternalContact(), task, CancellationToken.None))
            .Should().BeNull();
    }

    [Fact]
    public async Task PortalListsTaskAsync_AsksAboutTheTaskRow_OnTheEventsTable()
    {
        var data = new ExternalRootEventReadTests.FakeDataverse();
        var task = Guid.Parse(data.Add("matters", Matter, "Matter only"));

        (await Gate(accessibleSet: Grants(false, Matter).Object, portalData: data.NewService())
                .PortalListsTaskAsync(ExternalContact(), task, CancellationToken.None))
            .Should().BeTrue();
        data.Urls.Should().Contain(u => u.Contains($"/sprk_events({task})", StringComparison.OrdinalIgnoreCase));
    }
}
