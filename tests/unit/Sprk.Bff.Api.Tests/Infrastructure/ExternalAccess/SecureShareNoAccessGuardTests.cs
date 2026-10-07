// unified-access-control-r2 task 143 — the ONE write-time No Access check for internal users on secure records.
//
// The PRODUCTION guard and the PRODUCTION deny-list reader run; the doubles sit at module boundaries only — the
// reader's wire seam (GrantPolicyTestDoubles.SeamNoAccessListReader: a row comes back only when the query's SUBJECT
// filter names its subject), the participation service's virtual reads (flags, memberships, referenced organizations)
// and the identity row store (the task-141 link). ADR-038: no Mock<HttpMessageHandler>.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class SecureShareNoAccessGuardTests
{
    private const string Project = "sprk_project";

    private static readonly Guid Record = Guid.Parse("14300000-0000-0000-0000-0000000000a1");
    private static readonly Guid User = Guid.Parse("14300000-0000-0000-0000-0000000000b1");
    private static readonly Guid UserOid = Guid.Parse("14300000-0000-0000-0000-0000000000b2");
    private static readonly Guid LinkedContact = Guid.Parse("14300000-0000-0000-0000-0000000000c1");
    private static readonly Guid BoundContact = Guid.Parse("14300000-0000-0000-0000-0000000000c2");
    private static readonly Guid Firm = Guid.Parse("14300000-0000-0000-0000-0000000000d1");
    private static readonly Guid ReferencedOrg = Guid.Parse("14300000-0000-0000-0000-0000000000d2");

    private readonly GrantPolicyTestDoubles.FlagStubParticipationService _reads =
        new(defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false));

    private readonly GrantPolicyTestDoubles.SeamNoAccessListReader _list = new();
    private readonly InMemoryContactIdentityStore _identities = new();

    public SecureShareNoAccessGuardTests()
    {
        _identities.AddSystemUser(User, UserOid, "user@customer.example");
    }

    private SecureShareNoAccessGuard Guard() =>
        new(_reads, _list, _identities, AssignedAccessTestDoubles.NoFilingRows(), NullLogger<SecureShareNoAccessGuard>.Instance);

    private Task<SecureShareWallDecision> Check() => Guard().CheckAsync(Project, Record, User, CancellationToken.None);

    // ── The three subject forms ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASystemUserSubjectEntry_OnTheSecureRecord_Walls()
    {
        var entry = _list.DenySystemUserOnRecord(User, Record);

        var decision = await Check();

        decision.Outcome.Should().Be(SecureShareWallOutcome.Walled);
        decision.EntryIds.Should().Equal(entry);
    }

    [Fact]
    public async Task AnEntryNamingTheUsersLinkedContact_Walls()
    {
        _identities.SystemUsers[User].PrimaryContactId = LinkedContact;
        _list.DenyContactOnRecord(LinkedContact, Record);

        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Walled);
    }

    [Fact]
    public async Task AnEntryNamingAContactBoundToTheUsersOid_Walls_EvenWithNoPrimaryContactLink()
    {
        _identities.AddContact(BoundContact, oid: UserOid.ToString());
        _list.DenyContactOnRecord(BoundContact, Record);

        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Walled,
            "a contact bound to the user's oid represents them too — the wall over-matches, never under-matches");
    }

    [Fact]
    public async Task AnEntryNamingAnOrganizationTheLinkedContactBelongsTo_Walls()
    {
        _identities.SystemUsers[User].PrimaryContactId = LinkedContact;
        _reads.ContactOrganizations[LinkedContact] = new[] { Firm };
        _list.DenyOrganizationOnRecord(Firm, Record);

        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Walled, "owner N4: an organization entry binds its members");
    }

    [Fact]
    public async Task AnEntryWhoseObjectIsAnOrganizationTheRecordReferences_Walls()
    {
        _reads.RecordOrganizations[Record] = new[] { ReferencedOrg };
        _list.DenySystemUserOnOrganization(User, ReferencedOrg);

        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Walled, "the record side over-matches ANY referenced organization (B-10)");
    }

    [Fact]
    public async Task AnEntryForSomeoneElse_DoesNotWall()
    {
        _list.DenySystemUserOnRecord(Guid.NewGuid(), Record);
        _list.DenyContactOnRecord(Guid.NewGuid(), Record);

        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.NotWalled);
    }

    // ── Q4 scope ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OnANonSecureRecord_NothingWalls_AndTheListIsNotEvenRead()
    {
        _reads.Flags[Record] = RootRecordFlags.None;
        _list.DenySystemUserOnRecord(User, Record);

        var decision = await Check();

        decision.Outcome.Should().Be(SecureShareWallOutcome.NotSecure);
        _list.Queries.Should().Be(0);
    }

    // ── Fail closed (criterion 5) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhenTheFlagsCannotBeRead_Refuses()
    {
        _reads.Flags[Record] = RootRecordFlags.Unreadable;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheRecordsFlagsDoNotComeBack_Refuses()
    {
        _reads.Absent[Record] = true;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheFlagReadThrows_Refuses()
    {
        _reads.ThrowOnRead = true;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheLinkCannotBeRead_Refuses()
    {
        _identities.SystemUsers.Remove(User); // the row store answers Failed for an unknown user
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheOidBindingCannotBeRead_Refuses()
    {
        _identities.OidLookupStatus = LookupStatus.Failed;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheLinkedContactsMembershipsCannotBeRead_Refuses()
    {
        _identities.SystemUsers[User].PrimaryContactId = LinkedContact;
        _reads.MembershipsUnreadable = true;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheRecordsOrganizationsCannotBeRead_Refuses()
    {
        _reads.ReferencedOrganizationsThrow = new HttpRequestException("Dataverse 503");
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task WhenTheRecordsOrganizationsComeBackUnresolved_Refuses()
    {
        // Task 143 r1: the PRODUCTION fault shape. The real read reports Unresolved for the record and never throws for
        // it — read as "references nothing", a wall on an organization the record references would be skipped.
        _reads.UnreadableReferencedOrganizations[Record] = true;
        _list.DenySystemUserOnOrganization(User, ReferencedOrg);

        var decision = await Check();

        decision.Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
        decision.Fault.Should().Be("referenced-organizations");
    }

    [Fact]
    public async Task WhenTheDenyListReadFails_Refuses()
    {
        _list.Faults = true;
        (await Check()).Outcome.Should().Be(SecureShareWallOutcome.Unverifiable);
    }

    [Fact]
    public async Task RefusesShare_IsTrueForWalledAndUnverifiable_AndFalseOtherwise()
    {
        new SecureShareWallDecision(SecureShareWallOutcome.Walled, Array.Empty<Guid>()).RefusesShare.Should().BeTrue();
        new SecureShareWallDecision(SecureShareWallOutcome.Unverifiable, Array.Empty<Guid>()).RefusesShare.Should().BeTrue();
        new SecureShareWallDecision(SecureShareWallOutcome.NotWalled, Array.Empty<Guid>()).RefusesShare.Should().BeFalse();
        new SecureShareWallDecision(SecureShareWallOutcome.NotSecure, Array.Empty<Guid>()).RefusesShare.Should().BeFalse();
    }
}
