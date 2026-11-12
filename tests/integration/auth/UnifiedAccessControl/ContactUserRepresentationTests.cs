using FluentAssertions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;
using static Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Task 193 (#2338): <see cref="ContactUserRepresentation"/>, the ONE answer to "which internal users does this contact
/// represent?" that the Assigned-To materializer, the grant notifier, the personal To Do assignee and (next) Manage Access's
/// /grant all ask. Over the production rule with in-memory stores: the link store (<see cref="FakeAssignedAccessStore"/>), the
/// identity store (<see cref="InMemoryContactIdentityStore"/>) and the contact-state read
/// (<see cref="GrantPolicyTestDoubles.FlagStubParticipationService"/>).
/// </summary>
/// <remarks>
/// Pinned here: the oid-only confirmation through the binder's oid decision (the correction the materializer and the To Do
/// assignee gain), the two meanings of a missing binding column (each caller's policy), and that every failed read is
/// <see cref="ContactRepresentationStatus.Unreadable"/> — never "represents nobody", which a caller would turn into a contact
/// grant to an internal user.
/// </remarks>
public class ContactUserRepresentationTests
{
    private readonly FakeAssignedAccessStore _links = new();
    private readonly InMemoryContactIdentityStore _identities = new();
    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _participations = new(RootRecordFlags.None);

    private ContactUserRepresentation Resolver => new(_identities, _links, _participations);

    private Task<ContactRepresentation> Resolve(
        Guid contact, bool? rootIsRestricted = false,
        BindingColumnMissingPolicy columnMissing = BindingColumnMissingPolicy.DecideOnLink)
        => Resolver.ResolveAsync(contact, rootIsRestricted, columnMissing, CancellationToken.None);

    private static AssignedLinkCandidate Linked(Guid user, Guid contact, bool? isExternal = false)
        => new(user, contact, Guid.NewGuid(), IsDisabled: false, AccessMode: 0, ApplicationId: null, IsExternal: isExternal);

    private static AssignedLinkCandidate OidOnly(Guid user, Guid oid)
        => new(user, PrimaryContactId: null, oid, IsDisabled: false, AccessMode: 0, ApplicationId: null, IsExternal: false);

    /// <summary>An active contact whose binding carries <paramref name="oid"/>, and one unlinked user known by that oid.</summary>
    private (Guid Contact, Guid User) OidBoundContact(Guid oid)
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact, oid: oid.ToString());
        var user = Guid.NewGuid();
        _links.UsersByOid[oid] = new() { OidOnly(user, oid) };
        return (contact, user);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The oid-only user: confirmed through the binder's oid decision
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_OidOnlyUserWhoseOidResolvesToThisContactAlone_IsRepresented()
    {
        var oid = Guid.NewGuid();
        var (contact, user) = OidBoundContact(oid);

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Read);
        result.Users.Should().ContainSingle().Which.SystemUserId.Should().Be(user);
        result.OidUnconfirmed.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_OidOnlyUserWhoseOidTwoContactsCarry_IsNotRepresented_AndTheOidIsReportedUnconfirmed()
    {
        var oid = Guid.NewGuid();
        var (contact, _) = OidBoundContact(oid);
        _identities.AddContact(Guid.NewGuid(), oid: oid.ToString()); // the read path resolves neither contact on this oid

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Read);
        result.Users.Should().BeEmpty();
        result.OidUnconfirmed.Should().Be(ContactBindingDecision.DenyContactOidAmbiguous);
    }

    [Fact]
    public async Task ResolveAsync_OidOnlyUser_WhenTheOidLookupCannotBeRead_IsUnreadable()
    {
        var oid = Guid.NewGuid();
        var (contact, _) = OidBoundContact(oid);
        _identities.OidLookupStatus = LookupStatus.Failed;

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Unreadable);
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_UserLinkedToThisContact_IsRepresented_WithoutAskingTheOidBinding()
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact);
        var user = Guid.NewGuid();
        _links.UsersByLink[contact] = new() { Linked(user, contact) };
        _identities.OidLookupStatus = LookupStatus.Failed; // never asked: no unlinked user carries an oid

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Read);
        result.Users.Should().ContainSingle().Which.SystemUserId.Should().Be(user);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Status-first: failed reads, a missing or inactive contact
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_WhenTheContactReadFails_IsUnreadable()
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact);
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact) };
        _identities.FailGetContact = true;

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Unreadable);
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WhenTheLinkReadThrows_IsUnreadable_WithTheFault()
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact);
        _links.FailLinkRead = true;

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Unreadable);
        result.Fault.Should().BeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task ResolveAsync_InactiveContact_RepresentsNobody()
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact, stateCode: 1);
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact) };

        var result = await Resolve(contact);

        result.Status.Should().Be(ContactRepresentationStatus.Inactive);
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_ContactThatDoesNotExist_IsNotFound()
    {
        var result = await Resolve(Guid.NewGuid());

        result.Status.Should().Be(ContactRepresentationStatus.NotFound);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // A binding column not provisioned here: each caller's policy
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_BindingColumnMissing_DecideOnLink_DecidesOnTheLinkAlone()
    {
        var contact = Guid.NewGuid();
        var user = Guid.NewGuid();
        _links.UsersByLink[contact] = new() { Linked(user, contact) };
        _identities.GetContactStatus = LookupStatus.ColumnMissing;

        var result = await Resolve(contact, columnMissing: BindingColumnMissingPolicy.DecideOnLink);

        result.Status.Should().Be(ContactRepresentationStatus.ColumnMissing);
        result.Users.Should().ContainSingle().Which.SystemUserId.Should().Be(user);
    }

    [Fact]
    public async Task ResolveAsync_BindingColumnMissing_DecideOnLink_InactiveContact_RepresentsNobody()
    {
        var contact = Guid.NewGuid();
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact) };
        _identities.GetContactStatus = LookupStatus.ColumnMissing;
        _participations.ContactStates[contact] = ContactRecordState.Inactive;

        var result = await Resolve(contact, columnMissing: BindingColumnMissingPolicy.DecideOnLink);

        result.Status.Should().Be(ContactRepresentationStatus.Inactive);
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_BindingColumnMissing_WhenTheContactStateCannotBeRead_IsUnreadable()
    {
        var contact = Guid.NewGuid();
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact) };
        _identities.GetContactStatus = LookupStatus.ColumnMissing;
        _participations.ContactStates[contact] = ContactRecordState.Unreadable;

        var result = await Resolve(contact, columnMissing: BindingColumnMissingPolicy.DecideOnLink);

        result.Status.Should().Be(ContactRepresentationStatus.Unreadable);
        result.Users.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_BindingColumnMissing_FailClosed_DecidesNothing()
    {
        var contact = Guid.NewGuid();
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact) };
        _identities.GetContactStatus = LookupStatus.ColumnMissing;

        var result = await Resolve(contact, columnMissing: BindingColumnMissingPolicy.FailClosed);

        result.Status.Should().Be(ContactRepresentationStatus.ColumnMissing);
        result.Users.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Eligibility is the share rule's, for the caller's Restricted input
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)] // not read: fail closed
    public async Task ResolveAsync_ExternalFlaggedUser_IsEligibleOnlyWhereTheRecordIsKnownNotRestricted(
        bool? rootIsRestricted, bool eligible)
    {
        var contact = Guid.NewGuid();
        _identities.AddContact(contact);
        _links.UsersByLink[contact] = new() { Linked(Guid.NewGuid(), contact, isExternal: true) };

        var result = await Resolve(contact, rootIsRestricted);

        var user = result.Users.Should().ContainSingle().Subject;
        user.Eligibility.Should().Be(eligible
            ? InternalShareEndpoints.ShareEligibility.Eligible
            : InternalShareEndpoints.ShareEligibility.ExternalOnRestricted);
        user.BarredOnRestricted.Should().Be(!eligible);
    }
}
