using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Actions;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.Signals;

/// <summary>
/// Ontology task 215 (#2457): the <see cref="AssignTaskRecipientGate"/> decides who represents the assignee contact through uac-r2's
/// <see cref="ContactUserRepresentation"/> (task 193). This table pins, for each representation outcome, the gate's verdict and commit
/// warning, so the gate keeps the email / no-email answer and the warning it gave on origin/master (b005279c4d). It also pins the
/// gate's two intended differences from the grant notifier (D-199): several eligible users refuse, and the Restricted bar is applied
/// after the count, read at most once and only for the one user flagged external.
/// <para>Real: the gate's decision, <see cref="ContactUserRepresentation"/>, the binder's oid decision and the share eligibility rule.
/// Doubled: the identity store (contact and oid reads), uac-r2's link-candidate read, the contact-state read, and the gate's
/// Dataverse seams (No Access wall NotSecure, the task Read granted, the record's Restricted state as the row says, and on the
/// external path a CIAM composition that holds no grant on the matter, so the external path answers no_access).</para>
/// <para>The default contact is ONBOARDED (active, a bound portal oid, an email), so a row that reaches the external path gets past
/// <c>IsOnboardedWithEmail</c> and is seen composing the portal principal (<c>ExternalPathEntered</c>). Only "none" is expected
/// there: no internal user represents the contact, which is master's external path. Every other row asserts it never got
/// there, even where its warning would read the same (an internal row misrouted external answers no_access too).</para>
/// </summary>
public sealed class AssignTaskRecipientGateRepresentationTableTests
{
    private static readonly Guid Contact = Guid.Parse("c2150000-0000-4000-8000-000000000001");
    private static readonly Guid OtherContact = Guid.Parse("c2150000-0000-4000-8000-000000000002");
    private static readonly Guid Matter = Guid.Parse("c2150000-0000-4000-8000-000000000003");
    private static readonly Guid TaskId = Guid.Parse("c2150000-0000-4000-8000-000000000004");
    private static readonly Guid UserA = Guid.Parse("c2150000-0000-4000-8000-000000000005");
    private static readonly Guid UserB = Guid.Parse("c2150000-0000-4000-8000-000000000006");
    private static readonly Guid BoundOid = Guid.Parse("c2150000-0000-4000-8000-000000000007");
    private static readonly Guid AssignerOid = Guid.Parse("c2150000-0000-4000-8000-000000000008");
    private static readonly Guid PortalOid = Guid.Parse("c2150000-0000-4000-8000-000000000009");
    private const string Address = "assignee@firm.example";

    private sealed class World
    {
        public Mock<IContactIdentityStore> Identities { get; } = new();
        public AssignedAccessTestDoubles.FakeAssignedAccessStore Links { get; } = new();
        public GrantPolicyTestDoubles.FlagStubParticipationService Participations { get; } = new(RootRecordFlags.None);
        public Mock<AssignTaskRecipientGate> Gate { get; }

        /// <summary>The record's Restricted state the seam answers (null: could not be read).</summary>
        public bool? Restricted { get; set; } = false;

        public int RestrictedReads { get; private set; }
        public bool ExternalPathEntered { get; private set; }

        public World()
        {
            Gate = new Mock<AssignTaskRecipientGate>(Identities.Object, Links, Participations, null!, null!, null!, null!,
                NullLogger<AssignTaskRecipientGate>.Instance)
            { CallBase = true };
            Gate.Setup(g => g.WallAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(SecureShareWallOutcome.NotSecure);
            Gate.Setup(g => g.TaskRightsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(AccessRights.Read);
            Gate.Setup(g => g.RestrictedAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    RestrictedReads++;
                    return Restricted;
                });
            // The external path: a CIAM contact (no WorkforceHome home-link read), one composition per decision, no grant on the
            // matter, so it refuses no_access before the portal's events-list rule is asked.
            Gate.Setup(g => g.HomeLinkAsync(It.IsAny<ContactBindingRow>(), It.IsAny<CancellationToken>()))
                .Returns(() => throw new InvalidOperationException("a CIAM contact never asks the home link"));
            Gate.Setup(g => g.ComposePortalPrincipalAsync(It.IsAny<ContactBindingRow>(), It.IsAny<ExternalContactPlane>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ContactBindingRow contact, ExternalContactPlane _, CancellationToken _) =>
                {
                    ExternalPathEntered = true;
                    return Composition(contact.ContactId);
                });
            Gate.Setup(g => g.ContactRights(It.IsAny<ContactPrincipalComposition>(), It.IsAny<string>(), It.IsAny<Guid>()))
                .Returns(AccessRights.None);
            Gate.Setup(g => g.PortalListsTaskAsync(It.IsAny<ContactBindingRow>(), It.IsAny<ContactPrincipalComposition>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(() => throw new InvalidOperationException("no grant, so the events-list rule is never asked"));

            // An ONBOARDED contact (active, a bound portal oid that no user carries, an email): a row that wrongly fell through to
            // the external path gets past the onboarding check and composes the portal principal, which the table sees.
            ContactRead(ContactLookup.Of(Row(Contact, state: 0, PortalOid)));
        }

        public void ContactRead(ContactLookup lookup) =>
            Identities.Setup(i => i.GetContactAsync(Contact, It.IsAny<CancellationToken>())).ReturnsAsync(lookup);

        public void OidRead(ContactLookup lookup) =>
            Identities.Setup(i => i.FindContactsByOidAsync(BoundOid, It.IsAny<CancellationToken>())).ReturnsAsync(lookup);

        public void Link(Guid user, bool? disabled = false, bool external = false, Guid? applicationId = null, int? accessMode = 0) =>
            Links.UsersByLink.GetOrAdd(Contact, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(user, Contact, Guid.NewGuid(), disabled, accessMode, applicationId, external));

        public void UnlinkedWithOid(Guid user) =>
            Links.UsersByOid.GetOrAdd(BoundOid, _ => new List<AssignedLinkCandidate>())
                .Add(new AssignedLinkCandidate(user, null, BoundOid, false, 0, null, false));

        public Task<AssignTaskRecipient> DecideAsync() =>
            Gate.Object.DecideAsync(Contact, new DecisionCoreRecord("sprk_matter", Matter), TaskId, AssignerOid, CancellationToken.None);
    }

    private static ContactBindingRow Row(Guid contactId, int state, Guid? oid) =>
        new(contactId, state, oid?.ToString("D"), (int)IdentityPlaneMarker.External, Email: Address);

    /// <summary>A CIAM composition naming <paramref name="contactId"/>, with empty sets (its rights come from the stubbed
    /// <c>ContactRights</c>).</summary>
    private static ContactPrincipalComposition Composition(Guid contactId)
    {
        AccessibleRecordSet Empty(string entity) => new()
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entity,
            Rights = new Dictionary<Guid, AccessRights>(),
            Sources = new AccessibleRecordSetSources(false, true, false),
        };

        return new ContactPrincipalComposition(
            new CallerPrincipal { Plane = CallerPrincipalPlane.CiamContact, ContactId = contactId, ProjectAccess = [] },
            Empty(AccessibleRecordSetService.ProjectEntity), Empty(AccessibleRecordSetService.MatterEntity),
            Empty(AccessibleRecordSetService.WorkAssignmentEntity));
    }

    /// <summary>Each representation outcome, as the world that produces it.</summary>
    private static World Arrange(string outcome)
    {
        var w = new World();
        switch (outcome)
        {
            case "none":
                break;
            case "one-active":
                w.Link(UserA);
                break;
            case "one-disabled":
                w.Link(UserA, disabled: true);
                break;
            case "one-not-a-person":
                w.Link(UserA, applicationId: Guid.NewGuid());
                break;
            case "one-support-user":
                w.Link(UserA, accessMode: 3);
                break;
            case "one-disabled-unknown":
                w.Link(UserA, disabled: null);
                break;
            case "several":
                w.Link(UserA);
                w.Link(UserB);
                break;
            case "several-one-disabled":
                w.Link(UserA);
                w.Link(UserB, disabled: true);
                break;
            case "several-one-flagged-external-on-restricted":
                w.Restricted = true;
                w.Link(UserA);
                w.Link(UserB, external: true);
                break;
            case "one-flagged-external-on-restricted":
                w.Restricted = true;
                w.Link(UserA, external: true);
                break;
            case "one-flagged-external-on-standard":
                w.Restricted = false;
                w.Link(UserA, external: true);
                break;
            case "one-flagged-external-restricted-unreadable":
                w.Restricted = null;
                w.Link(UserA, external: true);
                break;
            case "one-not-flagged-on-restricted":
                w.Restricted = true;
                w.Link(UserA);
                break;
            case "contact-inactive":
                w.Link(UserA);
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 1, oid: null)));
                break;
            case "contact-missing":
                w.Link(UserA);
                w.ContactRead(ContactLookup.Of());
                break;
            case "contact-unreadable":
                w.Link(UserA);
                w.ContactRead(ContactLookup.Failed);
                break;
            case "links-unreadable":
                w.Link(UserA);
                w.Links.FailLinkRead = true;
                break;
            case "column-missing-active":
                w.Link(UserA);
                w.ContactRead(ContactLookup.ColumnMissing);
                break;
            case "column-missing-active-no-user":
                w.ContactRead(ContactLookup.ColumnMissing);
                break;
            case "column-missing-inactive":
                w.Link(UserA);
                w.ContactRead(ContactLookup.ColumnMissing);
                w.Participations.ContactStates[Contact] = ContactRecordState.Inactive;
                break;
            case "column-missing-state-unreadable":
                w.Link(UserA);
                w.ContactRead(ContactLookup.ColumnMissing);
                w.Participations.ContactStates[Contact] = ContactRecordState.Unreadable;
                break;
            case "bound-oid-resolves-here":
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                w.UnlinkedWithOid(UserA);
                w.OidRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                break;
            case "bound-oid-resolves-elsewhere":
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                w.UnlinkedWithOid(UserA);
                w.OidRead(ContactLookup.Of(Row(OtherContact, state: 0, BoundOid)));
                break;
            case "bound-oid-on-two-contacts":
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                w.UnlinkedWithOid(UserA);
                w.OidRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid), Row(OtherContact, state: 0, BoundOid)));
                break;
            case "bound-oid-lookup-unreadable":
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                w.UnlinkedWithOid(UserA);
                w.OidRead(ContactLookup.Failed);
                break;
            case "bound-oid-and-linked-user":
                // The linked user represents the contact; the oid-only user whose oid resolves elsewhere does not.
                w.ContactRead(ContactLookup.Of(Row(Contact, state: 0, BoundOid)));
                w.Link(UserB);
                w.UnlinkedWithOid(UserA);
                w.OidRead(ContactLookup.Of(Row(OtherContact, state: 0, BoundOid)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null);
        }

        return w;
    }

    /// <summary>
    /// The table. Expected: the commit warning (null = emailed), the internal user emailed (Internal only), how many times the
    /// record's Restricted state was read, and whether the external path composed the portal principal (only "none": no internal
    /// user represents the onboarded contact, and it holds no grant, so no_access). Every row matches the gate on origin/master
    /// b005279c4d. "one-support-user" is access mode 3 (not a person); "one-disabled-unknown" is an unreadable isdisabled, which
    /// ClassifyEligibility counts as disabled.
    /// </summary>
    [Theory]
    [InlineData("none", AssignTaskRecipient.NoAccessWarning, null, 0, true)]
    [InlineData("one-active", null, "A", 0, false)]
    [InlineData("one-disabled", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("one-not-a-person", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("one-support-user", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("one-disabled-unknown", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("several", AssignTaskRecipient.SeveralUsersWarning, null, 0, false)]
    [InlineData("several-one-disabled", null, "A", 0, false)]
    [InlineData("several-one-flagged-external-on-restricted", AssignTaskRecipient.SeveralUsersWarning, null, 0, false)]
    [InlineData("one-flagged-external-on-restricted", AssignTaskRecipient.NoAccessWarning, null, 1, false)]
    [InlineData("one-flagged-external-on-standard", null, "A", 1, false)]
    [InlineData("one-flagged-external-restricted-unreadable", AssignTaskRecipient.UnverifiedWarning, null, 1, false)]
    [InlineData("one-not-flagged-on-restricted", null, "A", 0, false)]
    [InlineData("contact-inactive", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("contact-missing", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("contact-unreadable", AssignTaskRecipient.UnverifiedWarning, null, 0, false)]
    [InlineData("links-unreadable", AssignTaskRecipient.UnverifiedWarning, null, 0, false)]
    [InlineData("column-missing-active", null, "A", 0, false)]
    [InlineData("column-missing-active-no-user", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("column-missing-inactive", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("column-missing-state-unreadable", AssignTaskRecipient.UnverifiedWarning, null, 0, false)]
    [InlineData("bound-oid-resolves-here", null, "A", 0, false)]
    [InlineData("bound-oid-resolves-elsewhere", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("bound-oid-on-two-contacts", AssignTaskRecipient.NoAccessWarning, null, 0, false)]
    [InlineData("bound-oid-lookup-unreadable", AssignTaskRecipient.UnverifiedWarning, null, 0, false)]
    [InlineData("bound-oid-and-linked-user", null, "B", 0, false)]
    public async Task DecideAsync_ForEachRepresentationOutcome_GivesTheMasterVerdictAndWarning(
        string outcome, string? warning, string? emailed, int restrictedReads, bool externalPath)
    {
        var w = Arrange(outcome);

        var verdict = await w.DecideAsync();

        verdict.Warning.Should().Be(warning);
        if (emailed is null)
        {
            verdict.Kind.Should().Be(AssignTaskRecipientKind.Refused);
            verdict.UserId.Should().BeNull();
        }
        else
        {
            verdict.Should().Be(AssignTaskRecipient.ForInternal(emailed == "A" ? UserA : UserB));
        }

        w.RestrictedReads.Should().Be(restrictedReads, "the Restricted state is read only for the one eligible user flagged external");
        w.ExternalPathEntered.Should().Be(externalPath, "only a contact no internal user represents is decided as an external contact");
    }
}
