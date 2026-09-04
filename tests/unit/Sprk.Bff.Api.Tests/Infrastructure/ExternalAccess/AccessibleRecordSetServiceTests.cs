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
using Spaarke.Dataverse;   // AccessRights — task 032 rights-fidelity assertions
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

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
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA, MemberRecordB));

        var standing = new Mock<IContactStandingGrantReader>();
        var sut = CreateSut(membership.Object, NoParticipations(), standing.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.PrincipalKind.Should().Be(WorkforcePrincipalKind.SystemUser);
        // With NO matter grants in the fake, the result is EXACTLY ADR-034 membership (task 028: the
        // matter grant term is now applied for a systemuser — ContactGrants=true — but contributes
        // nothing here, so the RESULT is unchanged: membership only).
        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA, MemberRecordB });
        set.Sources.SystemUserMembership.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse();

        // A systemuser NEVER consults the standing-grant flag (design §5 exact rule).
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
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), AlwaysStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject, MemberRecordA },
            "the set is grants ∪ standing-grant contact-anchored membership");
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    [Fact]
    public async Task ComposeAsync_ContactWithStandingGrant_MatterEntity_ConsultsMatterGrantsWithoutLeakingProjectGrants()
    {
        // Task 028: grants now span project/matter/work-assignment. A matter query consults MATTER
        // grants (not project grants) — a project grant must NOT leak into a matter set — unioned with
        // standing membership (which spans all entities).
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, StandingMatter));

        var grantedMatter = Guid.Parse("d0000000-0000-0000-0000-000000000001");
        // The contact holds BOTH a project grant (must not leak) and a matter grant (must apply).
        var participations = new FakeParticipationService(
            new[] { new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly } },
            matters: new HashSet<Guid> { grantedMatter });
        var sut = CreateSut(membership.Object, participations, AlwaysStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { StandingMatter, grantedMatter });
        set.RecordIds.Should().NotContain(GrantedProject, "project grants do not apply to a matter query");
        set.Sources.ContactGrants.Should().BeTrue("matter grants are now a grant-supported term (task 028)");
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    [Fact]
    public async Task IsRecordAccessibleAsync_ContactWithStandingGrant_RecordOutsideUnion_DeniesFalse()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
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
    // (4) polymorphic grants (task 028) — matter / work-assignment grant terms
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactWithMatterGrantNoStanding_ReturnsExactlyMatterGrants()
    {
        var grantedMatter = Guid.Parse("d0000000-0000-0000-0000-000000000001");
        var participations = new FakeParticipationService(
            Array.Empty<ExternalParticipation>(), matters: new HashSet<Guid> { grantedMatter });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { grantedMatter },
            "a contact granted a matter sees exactly that matter (grant-only, no standing membership)");
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse();
    }

    [Fact]
    public async Task ComposeAsync_ContactWithWorkAssignmentGrant_ReturnsExactlyWorkAssignmentGrants()
    {
        const string waEntity = "sprk_workassignment";
        var grantedWa = Guid.Parse("e0000000-0000-0000-0000-000000000001");
        var participations = new FakeParticipationService(
            Array.Empty<ExternalParticipation>(), workAssignments: new HashSet<Guid> { grantedWa });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), waEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { grantedWa },
            "a standalone work assignment is a first-class grantable root (task 028)");
        set.Sources.ContactGrants.Should().BeTrue();
    }

    [Fact]
    public async Task ComposeAsync_ContactWithProjectGrantOnly_MatterQuery_ReturnsEmpty()
    {
        // Negative cross-type: a project-only grant must NOT surface any matter (no type bleed).
        var participations = new FakeParticipationService(
            new[] { new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly } });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.RecordIds.Should().BeEmpty("a project grant does not confer matter access");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    // ─────────────────────────────────────────────────────────────────────
    // (6) RIGHTS FIDELITY — task 032 / FR-19. The evaluator answers
    //     (recordId → rights), composed additively with HIGHEST WINS.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactWithViewOnlyProjectGrant_YieldsReadOnly_NotCollaborate()
    {
        // The defect FR-19 removes: a deliberate ViewOnly grant used to arrive as Collaborate, because
        // the set carried no level and the workforce strategy stamped one over everything (register A-8).
        var standing = new Mock<IContactStandingGrantReader>();
        var sut = CreateSut(
            new Mock<IMembershipResolverService>().Object,
            new FakeParticipationService(new[]
            {
                new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly }
            }),
            standing.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        // EXACTLY Read — asserting equality, not a flag test, so a stray Write/Create/Delete bit fails.
        Assert.Equal(AccessRights.Read, set.RightsFor(GrantedProject));
    }

    [Fact]
    public async Task ComposeAsync_MatterAndWorkAssignmentGrants_CarryTheirOwnLevels()
    {
        // FR-19 acceptance: levels are carried for matters and work assignments, not projects alone.
        // Before task 032 these root types were IReadOnlySet<Guid> and STRUCTURALLY could not carry one.
        var standing = new Mock<IContactStandingGrantReader>();
        var sut = CreateSut(
            new Mock<IMembershipResolverService>().Object,
            new FakeParticipationService(
                Array.Empty<ExternalParticipation>(),
                matterGrants: new[]
                {
                    new ExternalRootGrant { RecordId = StandingMatter, AccessLevel = ExternalAccessLevel.FullAccess }
                }),
            standing.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        Assert.Equal(
            AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete,
            set.RightsFor(StandingMatter));
    }

    [Fact]
    public async Task ComposeAsync_SameRecordFromTwoTermsAtDifferentLevels_TakesTheMax()
    {
        // Highest-wins across TERMS: a ViewOnly grant on a record the caller also reaches through
        // membership must not drag the membership rights down to Read.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject));

        var standing = new Mock<IContactStandingGrantReader>();
        var sut = CreateSut(
            membership.Object,
            new FakeParticipationService(
                new[]
                {
                    new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly }
                },
                resolveContactId: ContactId),
            standing.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        // Membership term (Read|Write|Create) ∪ ViewOnly grant (Read) = Read|Write|Create.
        Assert.Equal(
            AccessRights.Read | AccessRights.Write | AccessRights.Create,
            set.RightsFor(GrantedProject));
    }

    [Fact]
    public async Task ComposeAsync_SystemUserMembership_ContributesCollaborateEquivalentRights()
    {
        // Status quo preserved: the blanket Collaborate stamp becomes an explicit TERM LEVEL here.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, NoParticipations(), new Mock<IContactStandingGrantReader>().Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        Assert.Equal(
            AccessRights.Read | AccessRights.Write | AccessRights.Create,
            set.RightsFor(MemberRecordA));
    }

    [Fact]
    public async Task RightsFor_RecordContributedByNoTerm_IsNone()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, NoParticipations(), new Mock<IContactStandingGrantReader>().Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        // Absence is None — never a default grant.
        Assert.Equal(AccessRights.None, set.RightsFor(UnrelatedRecord));
        Assert.False(set.Contains(UnrelatedRecord));
    }

    [Theory]
    [InlineData(0)]           // not a member of the enum at all
    [InlineData(99999999)]    // plausible-looking option-set value that is not a level we map
    [InlineData(100000003)]   // one past FullAccess — the shape a NEW choice value would take
    public void ToAccessRights_UnrecognisedLevel_IsNone_FailClosed(int rawLevel)
    {
        // NFR-01: an unmapped level must never widen access. A new sprk_accesslevel option added in
        // Dataverse reaches this code as an unmapped int — it must confer nothing until mapped here.
        Assert.Equal(AccessRights.None, ExternalAccessLevels.ToAccessRights((ExternalAccessLevel)rawLevel));
    }

    [Fact]
    public void ToAccessRights_NullLevel_IsNone_FailClosed()
    {
        // A grant row with no level keeps its id (set membership preserved — deliberately NOT filtered
        // out, which would be a silent revocation) but contributes NO rights.
        Assert.Equal(AccessRights.None, ExternalAccessLevels.ToAccessRights(null));
    }

    [Fact]
    public void AccessibleRecordSet_AVetoedRecordIsABSENT_FromBothRightsAndRecordIds()
    {
        // The veto invariant (ADR-003 as amended by task 030): a veto REMOVES an entry; it never writes
        // a low value. "No Access" is not representable as a level anywhere in the type system —
        // under max() a low value would simply be ignored and an ethical wall would fail silently.
        //
        // This asserts the STRUCTURAL guarantee that makes that safe: RecordIds is a DERIVED VIEW over
        // Rights, so a record removed by a veto cannot linger in the id set and keep granting access.
        // Standing in for a real veto (037/038/039 fill the seam) by composing the post-veto map.
        var survives = MemberRecordA;
        var vetoed = MemberRecordB;

        var set = new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.SystemUser,
            EntityType = MatterEntity,
            Rights = RightsOf(survives),   // `vetoed` was removed by the pipeline
            Sources = new AccessibleRecordSetSources(true, false, false),
        };

        Assert.False(set.Rights.ContainsKey(vetoed));
        Assert.DoesNotContain(vetoed, set.RecordIds);   // the derived view followed — cannot disagree
        Assert.False(set.Contains(vetoed));
        Assert.Equal(AccessRights.None, set.RightsFor(vetoed));

        Assert.Contains(survives, set.RecordIds);
        Assert.Equal(1, set.Count);
    }

    // ─────────────────────────────────────────────────────────────────────
    // IsOperationPermittedAsync — the rights-aware gate (task 033 / FR-19)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task IsOperationPermittedAsync_ContactWithViewOnlyGrant_PermitsReadButDeniesWrite()
    {
        // The FR-19 acceptance in evaluator terms: a ViewOnly grant answers YES to Read and NO to
        // Write on the SAME record. Before task 032/033 the level never reached this layer at all.
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly },
        });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, GrantedProject, AccessRights.Read, CancellationToken.None))
            .Should().BeTrue();

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, GrantedProject, AccessRights.Write, CancellationToken.None))
            .Should().BeFalse("ViewOnly maps to Read alone — it must not confer Write");
    }

    [Fact]
    public async Task IsOperationPermittedAsync_RecordOutsideTheComposedSet_DeniesEveryRight()
    {
        var sut = CreateSut(
            new Mock<IMembershipResolverService>().Object, ParticipationsFor(GrantedProject), NeverStanding());

        foreach (var right in new[] { AccessRights.Read, AccessRights.Write, AccessRights.Create, AccessRights.Delete })
        {
            (await sut.IsOperationPermittedAsync(
                    ContactPrincipal(), ProjectEntity, UnrelatedRecord, right, CancellationToken.None))
                .Should().BeFalse($"a record absent from the rights map must deny {right}");
        }
    }

    [Fact]
    public async Task IsOperationPermittedAsync_EmptyRecordId_DeniesWithoutComposing()
    {
        // Fail-closed on a missing subject: there is no record to evaluate, so nothing can be proven.
        // The membership resolver is strict — composing at all would throw and fail this test.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = CreateSut(membership.Object, NoParticipations(), NeverStanding());

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, Guid.Empty, AccessRights.Read, CancellationToken.None))
            .Should().BeFalse();
    }

    [Fact]
    public async Task IsOperationPermittedAsync_RequiredRightsNone_DeniesInsteadOfPermittingEverything()
    {
        // 🔴 THE FAIL-OPEN THIS GUARD EXISTS FOR.
        //
        // AccessRights is a [Flags] enum, so `anything.HasFlag(None)` is ALWAYS true — zero is a subset
        // of every set. A caller that computed its requirement dynamically and landed on None (an
        // unmapped operation, a defaulted field, a mis-parsed config value) would otherwise be granted
        // permission on ANY record, INCLUDING one it cannot see at all. That is a fail-OPEN reachable
        // purely by caller error, on the one method whose entire job is to deny.
        //
        // Both cases below would return TRUE without the explicit None guard in the implementation.
        var sut = CreateSut(
            new Mock<IMembershipResolverService>().Object, ParticipationsFor(GrantedProject), NeverStanding());

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, GrantedProject, AccessRights.None, CancellationToken.None))
            .Should().BeFalse("asking for no rights is a caller bug, not a free pass");

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, UnrelatedRecord, AccessRights.None, CancellationToken.None))
            .Should().BeFalse("...and it must not grant access to a record outside the set either");
    }

    [Fact]
    public async Task IsOperationPermittedAsync_RequiresALLRequestedRights_NotMerelyOne()
    {
        // Read|Write against a ViewOnly grant must DENY. A caller asking for a compound requirement is
        // asking for the conjunction; satisfying it partially is not satisfying it.
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly },
        });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());

        (await sut.IsOperationPermittedAsync(
                ContactPrincipal(), ProjectEntity, GrantedProject,
                AccessRights.Read | AccessRights.Write, CancellationToken.None))
            .Should().BeFalse("the caller holds Read but not Write — the conjunction is not satisfied");
    }

    // ─────────────────────────────────────────────────────────────────────
    // FR-21 — Restricted post-max veto (task 037)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactWithFullAccessGrant_OnRestrictedRecord_IsDeniedEntirely()
    {
        // FR-21: "denies ALL contact principals regardless of grant source". The STRENGTH of the grant is
        // irrelevant — FullAccess is chosen precisely because it is the strongest thing a contact can hold.
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
        });
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(AccessRights.None);
        set.Contains(GrantedProject).Should().BeFalse(
            "a veto REMOVES the key — it never writes a low value that max() would ignore");
        set.RecordIds.Should().NotContain(GrantedProject,
            "the derived id view must follow, or a vetoed record still reads as 'in the accessible set'");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserOnRestrictedRecord_KeepsMembershipButLosesContactGrant()
    {
        // FR-21's other half: Restricted means "only system users may have access". The systemuser's own
        // ADR-034 membership is Dataverse-governed and survives; the contact-sourced grant does not.
        // A DUAL-IDENTITY user (systemuser + granted contact) is the only shape where both are present.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA, GrantedProject));

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
        });
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(
            AccessibleRecordSetService.MembershipTermRights,
            "the systemuser keeps membership rights but the FullAccess contact grant is vetoed — "
            + "note Delete is ABSENT, proving the grant's contribution did not survive");
        set.RightsFor(MemberRecordA).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "an unrestricted record is untouched");
    }

    // ─────────────────────────────────────────────────────────────────────
    // FR-22 — Secure pre-max suppression (task 037)
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_ContactOrgInheritedGrantOnSecureRecord_IsSuppressed_ButDirectGrantSurvives()
    {
        // The FR-22 survivor pair, on the SAME secure record:
        //   org-inherited only  -> suppressed  -> None
        //   direct personal     -> survives    -> the granted rights
        var orgOnly = GrantedProject;
        var direct = StandingMatter;   // reused as a second project id

        var participations = new FakeParticipationService(new[]
        {
            // Org-inherited: DirectAccessLevel is null — that null IS the provenance marker.
            new ExternalParticipation
            {
                ProjectId = orgOnly,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = null,
            },
            new ExternalParticipation
            {
                ProjectId = direct,
                AccessLevel = ExternalAccessLevel.Collaborate,
                DirectAccessLevel = ExternalAccessLevel.Collaborate,
            },
        });
        participations.Flags[orgOnly] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[direct] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(orgOnly).Should().Be(AccessRights.None,
            "a FullAccess ORG grant confers nothing on a secure record — Secure suppresses org expansion");
        set.RightsFor(direct).Should().Be(
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
            "a DIRECT personal grant survives Secure (FR-22 survivor case)");
    }

    [Fact]
    public async Task ComposeAsync_SecureRecord_SuppressedOrgGrantCannotOutbidSurvivingDirectGrant()
    {
        // ⚠️ THE ORDERING PROOF, and the reason DirectAccessLevel exists.
        //
        // One record, two sources: a ViewOnly DIRECT grant and a Collaborate ORG grant. If suppression ran
        // AFTER the max, the max would already have produced Collaborate and there would be no arithmetic
        // that recovers Read. Getting exactly Read proves the org term never entered the max at all.
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.Collaborate,     // the all-sources max
                DirectAccessLevel = ExternalAccessLevel.ViewOnly,  // ...but only ViewOnly is the caller's own
            },
        });
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(AccessRights.Read,
            "EXACTLY Read — a suppressed Collaborate term cannot outbid the surviving ViewOnly grant");
        set.RightsFor(GrantedProject).HasFlag(AccessRights.Write).Should().BeFalse();
    }

    [Fact]
    public async Task ComposeAsync_ContactStandingMembershipOnSecureRecord_IsSuppressed()
    {
        // The derived-member half of FR-22: standing-grant membership is a DERIVED term, so a secure record
        // never receives it. The same contact still reaches a non-secure record through it (control).
        var secure = MemberRecordA;
        var open = MemberRecordB;

        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, secure, open));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[secure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = CreateSut(membership.Object, participations, AlwaysStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(secure).Should().BeFalse(
            "standing-grant membership is a derived-member term and is suppressed on a secure record");
        set.Contains(open).Should().BeTrue("the non-secure record still comes through the same term");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserWhoseContactHoldsStandingGrant_GetsNoDerivedAccessToSecureRecord()
    {
        // FR-22 acceptance, the Type 1 case (register C-10): a systemuser must not derive access to a secure
        // record through their linked contact.
        //
        // Two independent guarantees, asserted together because either alone would let this regress:
        //   (a) the systemuser plane NEVER consults the standing-grant flag at all, and
        //   (b) even the contact-GRANT term it does consult is Secure-suppressed for org-inherited rows.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity));   // no ADR-034 membership on the secure project

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = null,   // reached only through the contact's organization
            },
        });
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var standing = AlwaysStandingMock();
        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(AccessRights.None,
            "a Type 1 user must not derive access to a secure record via their linked contact — the Secure "
            + "BU covers the Dataverse half, this veto covers the grant half (design §5.1)");
        standing.Verify(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the systemuser plane must never consult the standing-grant flag");
    }

    // ─────────────────────────────────────────────────────────────────────
    // NFR-01 — fail-closed flag read
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_WhenFlagReadFaults_DeniesContactSourcedTerms()
    {
        // An unreadable flag row resolves to Secure AND Restricted, so a contact's grant contributes
        // nothing. The alternative — treating unknown as open — is the failure this whole wave exists to
        // remove: it would grant access to a record nobody could confirm is safe to share.
        var participations = new ThrowingFlagParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
        });

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "an unreadable flag row must deny, not default the record to open (NFR-01)");
    }

    [Fact]
    public async Task ComposeAsync_WhenFlagReadFaults_SystemUserMembershipStillSurvives()
    {
        // The other side of fail-closed: it must not become fail-BROKEN. Restricted denies contacts, not
        // system users, so an unreadable flag must not lock internal staff out of their own records.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var sut = CreateSut(
            membership.Object,
            new ThrowingFlagParticipationService(Array.Empty<ExternalParticipation>()),
            NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(MemberRecordA).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "fail-closed applies to contact-sourced terms; the systemuser's Dataverse-governed membership "
            + "survives, exactly as it does on a genuinely Restricted record");
    }

    private static Mock<IContactStandingGrantReader> AlwaysStandingMock()
    {
        var m = new Mock<IContactStandingGrantReader>();
        m.Setup(s => s.HasStandingGrantAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return m;
    }

    private static AccessibleRecordSetService CreateSut(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        IContactStandingGrantReader standing)
        => new(membership, participations, standing, NullLogger<AccessibleRecordSetService>.Instance);

    /// <summary>
    /// Matches the membership options the composer is now required to pass.
    ///
    /// <para>These setups previously matched the literal argument <c>null</c> — which was pinning
    /// finding A-10 rather than the contract. <c>options: null</c> is exactly what clamped every
    /// composed set to the first 500 rows and discarded the continuation token, so a suite keyed on
    /// it would have gone red the moment that defect was fixed, and green for as long as it stood.
    /// (It did: these eight setups all failed on the task-015 fix.)</para>
    ///
    /// <para>The replacement is deliberately NOT <c>It.IsAny</c>, which would only stop the tests
    /// caring. It asserts the composer passes real paging options at the agreed page size, so the
    /// setups now pin the fix: a regression back to <c>null</c> fails to match and the test dies.</para>
    /// </summary>
    private static MembershipResolveOptions? PagedOptions =>
        It.Is<MembershipResolveOptions?>(o =>
            o != null && o.Limit == AccessibleRecordSetService.MembershipPageSize);

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
    /// Thin test double overriding only the virtual grant loader (<see cref="ExternalParticipationService.GetGrantSetAsync"/>,
    /// task 028 — the base <c>GetParticipationsAsync</c> delegates to it) — avoids driving the real
    /// Dataverse OData path while composing on the real service surface (design §5 reuse). Carries an
    /// optional matter/work-assignment grant set for the polymorphic composition tests.
    /// </summary>
    private sealed class FakeParticipationService : ExternalParticipationService
    {
        private readonly ExternalGrantSet _grantSet;
        private readonly Guid? _resolveContactId;

        /// <param name="matters">Matter grant IDS, for tests that predate levels (task 032). Converted at
        /// Collaborate — the level a bare id effectively resolved to before levels were carried.</param>
        /// <param name="matterGrants">Matter grants WITH levels — use this for rights-fidelity tests.
        /// Takes precedence over <paramref name="matters"/>.</param>
        public FakeParticipationService(
            IReadOnlyList<ExternalParticipation> participations, Guid? resolveContactId = null,
            IReadOnlySet<Guid>? matters = null, IReadOnlySet<Guid>? workAssignments = null,
            IReadOnlyList<ExternalRootGrant>? matterGrants = null,
            IReadOnlyList<ExternalRootGrant>? workAssignmentGrants = null)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
            _grantSet = new ExternalGrantSet
            {
                Projects = participations,
                MatterGrants = matterGrants ?? RootGrants((matters ?? new HashSet<Guid>()).ToArray()),
                WorkAssignmentGrants = workAssignmentGrants ?? RootGrants((workAssignments ?? new HashSet<Guid>()).ToArray()),
            };
            _resolveContactId = resolveContactId;
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_grantSet);

        // Email-fallback resolution (systemuser with no derived contact). Returns the configured
        // contact id regardless of the (oid, email) passed — the test controls whether a match exists.
        public override Task<Guid?> ResolveExternalContactAsync(
            string? oid, string? email, CancellationToken ct = default)
            => Task.FromResult(_resolveContactId);

        // ── Task 037 veto flags ──────────────────────────────────────────────────────────────────
        //
        // Per-record overrides; anything not listed is unflagged. Defaulting to "no vetoes" keeps every
        // pre-037 test meaning what it meant.
        //
        // ⚠️ This override is REQUIRED, not convenience. Without it the base implementation runs, hits
        // `credential: null!` above, throws, and fails CLOSED — so every record would come back secure AND
        // restricted and the whole contact plane would compose to nothing. That is the production fail-closed
        // path behaving correctly; it is exercised deliberately by ThrowingFlagParticipationService below.
        public Dictionary<Guid, RootRecordFlags> Flags { get; } = new();

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            // Distinct(): the candidate set is a concat of the membership and grant terms, so the SAME id
            // legitimately arrives twice. The real implementation dedupes; a fake that throws on it would
            // fail tests for a reason production does not have.
            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds.Distinct().ToDictionary(
                id => id,
                id => Flags.TryGetValue(id, out var f) ? f : RootRecordFlags.None);
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// A participation service whose flag read FAULTS, exercising the NFR-01 fail-closed path end-to-end
    /// (the real implementation catches and returns <see cref="RootRecordFlags.Unreadable"/> for every id;
    /// this reproduces that contract without an HTTP stack).
    /// </summary>
    private sealed class ThrowingFlagParticipationService : ExternalParticipationService
    {
        private readonly ExternalGrantSet _grantSet;

        public ThrowingFlagParticipationService(IReadOnlyList<ExternalParticipation> participations)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
            _grantSet = new ExternalGrantSet
            {
                Projects = participations,
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            };
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_grantSet);

        public override Task<Guid?> ResolveExternalContactAsync(
            string? oid, string? email, CancellationToken ct = default)
            => Task.FromResult<Guid?>(null);

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, RootRecordFlags> result =
                recordIds.Distinct().ToDictionary(id => id, _ => RootRecordFlags.Unreadable);
            return Task.FromResult(result);
        }
    }
}
