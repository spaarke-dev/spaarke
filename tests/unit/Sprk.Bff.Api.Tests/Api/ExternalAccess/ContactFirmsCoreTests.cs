using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// ebilling-r1 task 130-0b: one definition of a caller's firms. <see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync"/>
/// (the contact plane) delegates to the contact-keyed core <see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadContactFirmsAsync"/>, and
/// the assistant's systemuser read (<see cref="ExternalInvoiceSubmissionAuthorizationFilter.ReadSystemUserFirmsAsync"/>) reaches the same core
/// with the contact of the identity resolution: a failed systemuser or contact-binding read is unknown, no linked contact is no firm, a linked
/// contact gives its conferring memberships, and a contact that is not Active gives none.
/// </summary>
[Trait("category", "external-access")]
public sealed class ContactFirmsCoreTests
{
    private static readonly Guid Contact = Guid.Parse("13000000-0000-4000-8000-0000000000c1");
    private static readonly Guid FaultyContact = Guid.Parse("13000000-0000-4000-8000-0000000000c2");
    private static readonly Guid InactiveContact = Guid.Parse("13000000-0000-4000-8000-0000000000c3");
    private static readonly Guid UnreadableMembershipsContact = Guid.Parse("13000000-0000-4000-8000-0000000000c4");
    private static readonly Guid NoMembershipsContact = Guid.Parse("13000000-0000-4000-8000-0000000000c5");
    private static readonly Guid SystemUser = Guid.Parse("13000000-0000-4000-8000-0000000000e1");
    private static readonly Guid FirmA = Guid.Parse("13000000-0000-4000-8000-0000000000a1");

    private readonly Mock<ExternalParticipationService> _participations = new(
        new HttpClient(), Mock.Of<ITenantCache>(), new ConfigurationBuilder().AddInMemoryCollection().Build(), Mock.Of<TokenCredential>(),
        Mock.Of<IHttpContextAccessor>(), NullLogger<ExternalParticipationService>.Instance, null!);

    private readonly Mock<IIdentityNormalizationService> _identity = new(MockBehavior.Strict);

    public ContactFirmsCoreTests()
    {
        _participations.Setup(p => p.QueryContactStateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == InactiveContact ? ContactRecordState.Inactive : ContactRecordState.Active);
        _participations.Setup(p => p.ReadOrganizationMembershipsAsync(Contact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(new[] { FirmA, FirmA }, Array.Empty<Guid>(), Unreadable: false));
        _participations.Setup(p => p.ReadOrganizationMembershipsAsync(FaultyContact, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("The membership read failed (test)."));
        _participations.Setup(p => p.ReadOrganizationMembershipsAsync(InactiveContact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(new[] { FirmA }, Array.Empty<Guid>(), Unreadable: false));
        _participations.Setup(p => p.ReadOrganizationMembershipsAsync(UnreadableMembershipsContact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(new[] { FirmA }, Array.Empty<Guid>(), Unreadable: true));
        _participations.Setup(p => p.ReadOrganizationMembershipsAsync(NoMembershipsContact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActiveOrgMemberships(Array.Empty<Guid>(), Array.Empty<Guid>(), Unreadable: false));
    }

    /// <summary>
    /// Fix round 1 K5: one principal state per row, as the contact plane sees it (a <see cref="CallerPrincipal"/>) and as the assistant's
    /// systemuser plane sees it (the identity resolution's contact, or its contact-binding fault), with the state both must reach.
    /// </summary>
    public static TheoryData<string, string> States => new()
    {
        { "active-firms", nameof(CallerFirmsState.Known) },
        { "inactive-contact", nameof(CallerFirmsState.NoFirm) },
        { "unreadable-contact", nameof(CallerFirmsState.Unknown) },
        { "unreadable-memberships", nameof(CallerFirmsState.Unknown) },
        { "membership-read-fault", nameof(CallerFirmsState.Unknown) },
        { "no-memberships", nameof(CallerFirmsState.NoFirm) },
        { "no-contact", nameof(CallerFirmsState.NoFirm) },
    };

    private static Guid ContactOf(string state) => state switch
    {
        "inactive-contact" => InactiveContact,
        "unreadable-memberships" => UnreadableMembershipsContact,
        "membership-read-fault" => FaultyContact,
        "no-memberships" => NoMembershipsContact,
        "unreadable-contact" or "no-contact" => Guid.Empty,
        _ => Contact,
    };

    private static CallerPrincipal Principal(string state) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = ContactOf(state),
        ContactUnreadable = state == "unreadable-contact",
        Email = "partner@firm.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static PersonIdentity Person(string state) =>
        new(SystemUser, ContactId: ContactOf(state) is var contact && contact != Guid.Empty ? contact : null)
        {
            Faults = state == "unreadable-contact" ? IdentityReadFaults.ContactBinding : default,
        };

    [Theory(DisplayName = "130-0b K5: the contact plane (ReadCallerFirmsAsync) and the systemuser plane through the core give identical firms in every principal state")]
    [MemberData(nameof(States))]
    public async Task TheContactPlane_AndTheCore_GiveIdenticalFirms(string state, string expectedState)
    {
        var expected = Enum.Parse<CallerFirmsState>(expectedState);
        var contactPlane = await ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync(
            Principal(state), _participations.Object, NullLogger.Instance, CancellationToken.None);
        var systemUserPlane = await SystemUserFirms(Person(state));

        contactPlane.Unreadable.Should().Be(systemUserPlane.Unreadable, state);
        contactPlane.Firms.Should().Equal(systemUserPlane.Firms, state);
        CallerFirms.From(contactPlane.Firms, contactPlane.Unreadable).State.Should().Be(expected, state);
        CallerFirms.From(systemUserPlane.Firms, systemUserPlane.Unreadable).State.Should().Be(expected, state);
    }

    private Task<(IReadOnlyList<Guid> Firms, bool Unreadable)> SystemUserFirms(PersonIdentity person)
    {
        _identity.Setup(i => i.ResolveAsync(SystemUser, It.IsAny<CancellationToken>())).ReturnsAsync(person);
        return ExternalInvoiceSubmissionAuthorizationFilter.ReadSystemUserFirmsAsync(
            SystemUser, _identity.Object, _participations.Object, NullLogger.Instance, CancellationToken.None);
    }

    [Fact(DisplayName = "130-0b a systemuser with a linked Active contact has that contact's firms")]
    public async Task ASystemuser_WithALinkedContact_HasItsFirms()
    {
        var (firms, unreadable) = await SystemUserFirms(new PersonIdentity(SystemUser, ContactId: Contact));

        unreadable.Should().BeFalse();
        firms.Should().Equal(FirmA);
    }

    [Fact(DisplayName = "130-0b a systemuser with no linked contact has no firm (known, not a fault), and nothing else is read")]
    public async Task ASystemuser_WithNoLinkedContact_HasNoFirm()
    {
        var (firms, unreadable) = await SystemUserFirms(new PersonIdentity(SystemUser));

        CallerFirms.From(firms, unreadable).State.Should().Be(CallerFirmsState.NoFirm);
        _participations.Verify(p => p.ReadOrganizationMembershipsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    [Theory(DisplayName = "130-0b a failed systemuser or contact-binding read makes the firms unknown (never 'no firm'), even with a contact")]
    [InlineData("contact-binding", false)]
    [InlineData("contact-binding", true)]
    [InlineData("systemuser", false)]
    [InlineData("systemuser", true)]
    public async Task AnIdentityFault_MakesTheFirmsUnknown(string fault, bool withContact)
    {
        var person = new PersonIdentity(SystemUser, ContactId: withContact ? Contact : null)
        {
            Faults = fault == "systemuser" ? IdentityReadFaults.SystemUser : IdentityReadFaults.ContactBinding,
        };

        var (firms, unreadable) = await SystemUserFirms(person);

        CallerFirms.From(firms, unreadable).State.Should().Be(CallerFirmsState.Unknown);
    }

    [Fact(DisplayName = "130-0b a fault on another identity path (teams) does not decide the firms")]
    public async Task AnUnrelatedIdentityFault_DoesNotDecideTheFirms()
    {
        var (firms, unreadable) = await SystemUserFirms(new PersonIdentity(SystemUser, ContactId: Contact) { Faults = IdentityReadFaults.Teams });

        unreadable.Should().BeFalse();
        firms.Should().Equal(FirmA);
    }

    [Fact(DisplayName = "130-0b a systemuser whose linked contact is not Active has no firm")]
    public async Task ASystemuser_WithAnInactiveContact_HasNoFirm()
    {
        var (firms, unreadable) = await SystemUserFirms(new PersonIdentity(SystemUser, ContactId: InactiveContact));

        CallerFirms.From(firms, unreadable).State.Should().Be(CallerFirmsState.NoFirm);
    }
}
