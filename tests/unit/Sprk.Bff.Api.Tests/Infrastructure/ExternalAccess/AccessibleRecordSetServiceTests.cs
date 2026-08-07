// teams-app-r1 Task 022 — AccessibleRecordSetService composition tests (spec FR-06 / design §5).
//
// The core authorization gate: accessible(principal) is composed per identity plane. These tests
// protect the composition contract that task 030 (broker authz-before-stream) depends on, covering
// POSITIVE and NEGATIVE paths for all THREE principal planes:
//   (1) systemuser            → ADR-034 membership ∪ the caller's OWN contact grants (project-scoped;
//                               §6.5 Path-B amendment, external-access-r2 UAT 2026-08-07 — "parallel
//                               workforce/contact access"). Standing-grant is NEVER consulted for a
//                               systemuser; membership-only still holds for non-project entities.
//   (2) contact + grant       → sprk_externalrecordaccess grants ONLY; standing membership NEVER unioned
//   (3) contact + standing    → grants ∪ contact-anchored membership (task 021), gated on the flag
// Plus the load-bearing negative: a contact WITHOUT a standing grant gets ONLY explicit grants,
// never automatic membership.
//
// Module-boundary substitutes only (IMembershipResolverService, IContactStandingGrantReader, and a
// thin ExternalParticipationService subclass overriding its virtual grant loader) per tests/CLAUDE.md.

using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class AccessibleRecordSetServiceTests
{
    private const string ProjectEntity = "sprk_project";
    private const string MatterEntity = "sprk_matter";

    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly Guid MemberRecordA = Guid.Parse("a0000000-0000-0000-0000-000000000001");
    private static readonly Guid MemberRecordB = Guid.Parse("a0000000-0000-0000-0000-000000000002");
    private static readonly Guid GrantedProject = Guid.Parse("b0000000-0000-0000-0000-000000000001");
    private static readonly Guid StandingMatter = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid UnrelatedRecord = Guid.Parse("f0000000-0000-0000-0000-0000000000ff");

    // ─────────────────────────────────────────────────────────────────────
    // (1) systemuser plane — ADR-034 membership only (automatic)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUserPrincipal_ReturnsExactlyAdr034Membership()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA, MemberRecordB));

        var standing = new Mock<IContactStandingGrantReader>();
        var sut = CreateSut(membership.Object, NoParticipations(), standing.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.PrincipalKind.Should().Be(WorkforcePrincipalKind.SystemUser);
        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA, MemberRecordB });
        set.Sources.SystemUserMembership.Should().BeTrue();
        set.Sources.ContactGrants.Should().BeFalse();
        set.Sources.StandingGrantMembership.Should().BeFalse();

        // A systemuser NEVER consults grants or the standing-grant flag (design §5 exact rule).
        standing.Verify(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        membership.Verify(
            m => m.ResolveByContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IsRecordAccessibleAsync_SystemUser_RecordOutsideMembership_DeniesFalse()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, NoParticipations(), NeverStanding());

        (await sut.IsRecordAccessibleAsync(SystemUserPrincipal(), MatterEntity, MemberRecordA, CancellationToken.None))
            .Should().BeTrue("the record is in the systemuser's ADR-034 membership");
        (await sut.IsRecordAccessibleAsync(SystemUserPrincipal(), MatterEntity, UnrelatedRecord, CancellationToken.None))
            .Should().BeFalse("a record outside membership must be denied, not omitted");
    }

    // ─────────────────────────────────────────────────────────────────────
    // (1b) systemuser + linked contact grant — membership ∪ contact grants (project-scoped)
    //      §6.5 Path-B amendment (external-access-r2 UAT 2026-08-07 — parallel workforce/contact access)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUserWithLinkedContactGrant_OnProject_UnionsMembershipAndGrants()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        // The systemuser's DERIVED contact (SystemUserPrincipal().ContactId) holds a grant.
        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), NeverStanding());

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.PrincipalKind.Should().Be(WorkforcePrincipalKind.SystemUser);
        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA, GrantedProject },
            "an internal systemuser who is also a granted contact sees membership ∪ their own contact grants");
        set.Sources.SystemUserMembership.Should().BeTrue();
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse("standing-grant is never consulted for a systemuser");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserNoLinkedContact_EmailFallbackResolvesContactGrants()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity)); // no ADR-034 membership

        // No derived contact (sprk_primarycontact null) but a verified email that resolves to a contact.
        var principal = new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = SystemUserId,
            ContactId = null,
            Oid = Oid.ToString("D"),
            TenantId = Tenant,
            Email = "ralph.schroeder@hotmail.com",
        };
        var participations = new FakeParticipationService(
            new[] { new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly } },
            resolveContactId: ContactId);

        var sut = CreateSut(membership.Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject },
            "with no linked contact, the verified-email fallback finds the caller's contact grants");
        set.Sources.ContactGrants.Should().BeTrue();
    }

    [Fact]
    public async Task ComposeAsync_SystemUserNoLinkedContactNoEmail_AppliesMembershipOnly()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        // No derived contact AND no email → the contact-grants term cannot resolve; membership only.
        var principal = new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = SystemUserId,
            ContactId = null,
            Oid = Oid.ToString("D"),
            TenantId = Tenant,
            Email = string.Empty,
        };
        // Strict-ish: resolveContactId null so even if called, nothing resolves.
        var sut = CreateSut(membership.Object, new FakeParticipationService(Array.Empty<ExternalParticipation>()), NeverStanding());

        var set = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA });
        set.Sources.ContactGrants.Should().BeFalse("no derived contact and no email → no contact-grants term");
    }

    // ─────────────────────────────────────────────────────────────────────
    // (2) contact + grant, NO standing — grants only (load-bearing negative)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactWithGrantNoStanding_ReturnsExactlyGrantsAndNeverAutomaticMembership()
    {
        var membership = new Mock<IMembershipResolverService>();
        var standing = new Mock<IContactStandingGrantReader>();
        standing
            .Setup(s => s.HasStandingGrantAsync(ContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // NO standing grant

        var participations = ParticipationsFor(GrantedProject);
        var sut = CreateSut(membership.Object, participations, standing.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.PrincipalKind.Should().Be(WorkforcePrincipalKind.ContactOnly);
        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject });
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse();

        // THE load-bearing negative: no standing grant ⇒ contact-anchored membership is NEVER unioned.
        membership.Verify(
            m => m.ResolveByContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task IsRecordAccessibleAsync_ContactWithGrantNoStanding_NonGrantedRecord_DeniesFalse()
    {
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, ParticipationsFor(GrantedProject), NeverStanding());

        (await sut.IsRecordAccessibleAsync(ContactPrincipal(), ProjectEntity, GrantedProject, CancellationToken.None))
            .Should().BeTrue("the project was explicitly granted");
        (await sut.IsRecordAccessibleAsync(ContactPrincipal(), ProjectEntity, UnrelatedRecord, CancellationToken.None))
            .Should().BeFalse("a non-granted record must be denied for a contact without a standing grant");
    }

    // ─────────────────────────────────────────────────────────────────────
    // (3) contact + standing grant — grants ∪ contact-anchored membership
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactWithStandingGrant_UnionsGrantsAndContactAnchoredMembership()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), AlwaysStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject, MemberRecordA },
            "the set is grants ∪ standing-grant contact-anchored membership");
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    [Fact]
    public async Task ComposeAsync_ContactWithStandingGrant_NonProjectEntity_SkipsGrantsButUnionsMembership()
    {
        // Grants are sprk_project-scoped (design §5 gap #2); standing membership spans all entities.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, MatterEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, StandingMatter));

        // Even if a (project) participation exists, it must NOT leak into a matter query.
        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), AlwaysStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { StandingMatter });
        set.RecordIds.Should().NotContain(GrantedProject, "project grants do not apply to a matter query");
        set.Sources.ContactGrants.Should().BeFalse("grants are project-scoped in R1");
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    [Fact]
    public async Task IsRecordAccessibleAsync_ContactWithStandingGrant_RecordOutsideUnion_DeniesFalse()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), AlwaysStanding());

        (await sut.IsRecordAccessibleAsync(ContactPrincipal(), ProjectEntity, MemberRecordA, CancellationToken.None))
            .Should().BeTrue("in standing-grant membership");
        (await sut.IsRecordAccessibleAsync(ContactPrincipal(), ProjectEntity, GrantedProject, CancellationToken.None))
            .Should().BeTrue("in explicit grants");
        (await sut.IsRecordAccessibleAsync(ContactPrincipal(), ProjectEntity, UnrelatedRecord, CancellationToken.None))
            .Should().BeFalse("outside both union terms → denied");
    }

    [Fact]
    public async Task IsRecordAccessibleAsync_EmptyRecordId_DeniesFalseWithoutComposing()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = CreateSut(membership.Object, NoParticipations(), NeverStanding());

        (await sut.IsRecordAccessibleAsync(SystemUserPrincipal(), MatterEntity, Guid.Empty, CancellationToken.None))
            .Should().BeFalse("an empty record id cannot prove access — fail closed");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    private static AccessibleRecordSetService CreateSut(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        IContactStandingGrantReader standing)
        => new(membership, participations, standing, NullLogger<AccessibleRecordSetService>.Instance);

    private static WorkforcePrincipal SystemUserPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = SystemUserId,
        ContactId = ContactId, // derived contact — must NOT be used on the systemuser plane
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static WorkforcePrincipal ContactPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.ContactOnly,
        ContactId = ContactId,
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static MembershipResponse Response(string entityType, params Guid[] ids) => new(
        entityType,
        new PersonIdentity(Guid.Empty, ContactId: ContactId),
        ids,
        new Dictionary<string, IReadOnlyList<Guid>>(),
        ids.Length,
        DateTimeOffset.UtcNow.AddMinutes(5));

    private static IContactStandingGrantReader AlwaysStanding()
    {
        var m = new Mock<IContactStandingGrantReader>();
        m.Setup(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        return m.Object;
    }

    private static IContactStandingGrantReader NeverStanding()
    {
        var m = new Mock<IContactStandingGrantReader>();
        m.Setup(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        return m.Object;
    }

    private static ExternalParticipationService NoParticipations() => new FakeParticipationService(Array.Empty<ExternalParticipation>());

    private static ExternalParticipationService ParticipationsFor(params Guid[] projectIds)
        => new FakeParticipationService(projectIds
            .Select(id => new ExternalParticipation { ProjectId = id, AccessLevel = ExternalAccessLevel.ViewOnly })
            .ToList());

    /// <summary>
    /// Thin test double overriding only the virtual grant loader — avoids driving the real Dataverse
    /// OData path while composing on the real service surface (design §5 reuse).
    /// </summary>
    private sealed class FakeParticipationService : ExternalParticipationService
    {
        private readonly IReadOnlyList<ExternalParticipation> _participations;
        private readonly Guid? _resolveContactId;

        public FakeParticipationService(
            IReadOnlyList<ExternalParticipation> participations, Guid? resolveContactId = null)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
            _participations = participations;
            _resolveContactId = resolveContactId;
        }

        public override Task<IReadOnlyList<ExternalParticipation>> GetParticipationsAsync(
            Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_participations);

        // Email-fallback resolution (systemuser with no derived contact). Returns the configured
        // contact id regardless of the (oid, email) passed — the test controls whether a match exists.
        public override Task<Guid?> ResolveExternalContactAsync(
            string? oid, string? email, CancellationToken ct = default)
            => Task.FromResult(_resolveContactId);
    }
}
