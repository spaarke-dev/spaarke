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
// Module-boundary substitutes only (IMembershipResolverService, ISubjectStandingGrantReader, and a
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
    private static readonly Guid StandingProject = Guid.Parse("c0000000-0000-0000-0000-000000000002");
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

        var standing = new Mock<ISubjectStandingGrantReader>();
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
        standing.Verify(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
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
        var standing = new Mock<ISubjectStandingGrantReader>();
        standing
            .Setup(s => s.ReadForContactAsync(ContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld); // NO standing grant

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
        var standing = new Mock<ISubjectStandingGrantReader>();
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
        var standing = new Mock<ISubjectStandingGrantReader>();
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

        var standing = new Mock<ISubjectStandingGrantReader>();
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

        var sut = CreateSut(membership.Object, NoParticipations(), new Mock<ISubjectStandingGrantReader>().Object);

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

        var sut = CreateSut(membership.Object, NoParticipations(), new Mock<ISubjectStandingGrantReader>().Object);

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
        standing.Verify(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
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

    // ─────────────────────────────────────────────────────────────────────
    // FR-23 — deny-list veto (task 039). Order: additive max → deny-list → Restricted, with Secure
    // suppression pre-max. These tests are WIRING tests for AccessibleRecordSetService — they do not
    // re-test NoAccessListReader's own query/matching correctness (NoAccessListReaderTests.cs, task 038
    // owns that); DenyingReader below is a canned double that mirrors the reader's two match shapes
    // (record-keyed / org-keyed) just closely enough to prove the composer builds candidates, resolves
    // the subject, and removes denied keys correctly.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_FullAccessGrant_WithMatchingContactOrgDenyEntry_ResolvesToNone_NeverResurrectedByMax()
    {
        // FR-23's load-bearing negative: "No Access" is never a level. A Full Access grant plus a deny
        // entry yields None, because the veto runs AFTER the additive max and max() can therefore never
        // resurrect it.
        var deniedOrg = Guid.Parse("d1000000-0000-0000-0000-000000000001");

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
        });
        participations.ReferencedOrgs[GrantedProject] = new[] { deniedOrg };

        var reader = DenyingReader(deniedOrgIds: new[] { deniedOrg });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(AccessRights.None,
            "a Full Access grant plus a matching deny entry must resolve to None — the veto runs after the max");
        set.Contains(GrantedProject).Should().BeFalse(
            "a veto REMOVES the key; it never writes AccessRights.None (which IsOperationPermittedAsync " +
            "would refuse as a malformed request, not honour as a denial)");
    }

    [Fact]
    public async Task ComposeAsync_ContactRecordDenyEntry_RemovesExactlyThatRecord_SiblingReferencingSameOrgSurvives()
    {
        // Record-keyed specificity: the deny is scoped to the NAMED record, not to every record sharing
        // the same referenced organization — proven by giving both records the SAME org reference.
        var denied = GrantedProject;
        var sibling = StandingMatter; // reused here as a second project id (file convention)
        var sharedOrg = Guid.Parse("f1000000-0000-0000-0000-000000000001");

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = denied, AccessLevel = ExternalAccessLevel.ViewOnly },
            new ExternalParticipation { ProjectId = sibling, AccessLevel = ExternalAccessLevel.ViewOnly },
        });
        participations.ReferencedOrgs[denied] = new[] { sharedOrg };
        participations.ReferencedOrgs[sibling] = new[] { sharedOrg };

        var reader = DenyingReader(deniedRecordIds: new[] { denied }); // record-keyed, NOT org-keyed
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(denied).Should().BeFalse("a contact×record deny entry removes exactly that record");
        set.Contains(sibling).Should().BeTrue(
            "a sibling record referencing the SAME organization is unaffected — the deny was record-keyed");
    }

    [Fact]
    public async Task ComposeAsync_DenyEntryKeyedOnAnOrganizationReferencedOnlyViaTheSecondLawFirmSlot_StillDenies()
    {
        // Over-match (register B-10): the record-side match uses EVERY organization the record
        // references, not narrowed to task 041's access-conferring registry. Framed on the second
        // assigned-law-firm slot — the task brief's own "opposing counsel" example of a reference that
        // need not confer access yet must still be walled.
        var opposingCounselOrg = Guid.Parse("a3000000-0000-0000-0000-000000000001");
        var unrelated = StandingMatter;

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = unrelated, AccessLevel = ExternalAccessLevel.FullAccess },
        });
        participations.ReferencedOrgs[GrantedProject] = new[] { opposingCounselOrg };
        // `unrelated` references nothing — a control proving the match is org-scoped, not blanket.

        var reader = DenyingReader(deniedOrgIds: new[] { opposingCounselOrg });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "a deny entry keyed on an organization the record references still denies it, regardless of " +
            "whether that same lookup would ALSO confer access under a different registry");
        set.Contains(unrelated).Should().BeTrue("a record referencing no denied organization is unaffected");
    }

    [Fact]
    public void BuildOrganizationReferenceSelect_IncludesEveryRegisteredLookup_NoConferralNarrowing()
    {
        // The over-match property pinned at the query-construction level: the $select that drives the
        // deny-list's record-side org match includes EVERY registered org-typed lookup unconditionally
        // — there is no filtering against task 041's access-conferring registry.
        var select = ExternalParticipationService.BuildOrganizationReferenceSelect(
            new[] { "sprk_assignedlawfirm1", "sprk_assignedlawfirm2" });

        select.Should().Be("_sprk_assignedlawfirm1_value,_sprk_assignedlawfirm2_value");
    }

    [Fact]
    public async Task ComposeAsync_QueriesDenyListWithContactsOwnIdAndActiveOrganizationIds()
    {
        // Subject wiring: contact×org / org×record deny entries only fire if the composer actually
        // resolved and passed BOTH the contact's own id AND its active organization membership.
        var activeOrg = Guid.Parse("a2000000-0000-0000-0000-000000000001");
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly },
        });
        participations.ActiveOrgIds.Add(activeOrg);

        var reader = new Mock<INoAccessListReader>();
        reader
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NoAccessListResult.Empty);

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader.Object);
        await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        reader.Verify(r => r.GetDeniedRecordsAsync(
            ContactId,
            It.Is<IReadOnlyCollection<Guid>>(orgs => orgs.Contains(activeOrg)),
            It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(),
            It.IsAny<CancellationToken>()),
            Times.Once,
            "the composer must resolve and pass the contact's own id AND its active organization ids as the deny-list subject");
    }

    [Fact]
    public async Task ComposeAsync_OrganizationSubjectDenyEntry_DeniesEveryRecordForAContactActiveInThatOrg()
    {
        // org×org / org×record: an organization-subject deny entry denies every contact who is an
        // ACTIVE member of that org. The fake reader denies ONLY when the subject organizationIds
        // argument carries the seeded org — a wiring defect (e.g. passing an empty org list) leaves the
        // GENERAL setup in force, which denies nothing, so the assertion below would correctly go red
        // rather than being masked by any fail-closed path.
        var subjectOrg = Guid.Parse("a4000000-0000-0000-0000-000000000001");

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly },
        });
        participations.ActiveOrgIds.Add(subjectOrg);

        var reader = new Mock<INoAccessListReader>();
        reader // general default: nothing denied
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NoAccessListResult.Empty);
        reader // specific: fires only when the subject org is present — Moq prefers the LAST matching setup
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(),
                It.Is<IReadOnlyCollection<Guid>>(orgs => orgs.Contains(subjectOrg)),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? _, IReadOnlyCollection<Guid> __, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ___) =>
                new NoAccessListResult
                {
                    DeniedRecordIds = candidates.Select(c => c.RecordId).ToHashSet(),
                    DenyingEntryIds = candidates.ToDictionary(c => c.RecordId, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                    FailedClosed = false,
                });

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "an organization-subject deny entry denies every record for a contact whose active org set " +
            "contains the subject org");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserRecordThatWouldSurviveRestricted_IsStillRemovedWhenAlsoDenyListed()
    {
        // Ordering: deny is evaluated as an UNCONDITIONAL removal, independent of what Restricted would
        // otherwise have preserved. A record that would normally SURVIVE Restricted (the systemuser's
        // own ADR-034 membership) must still end up ABSENT when it is also on the deny list — proving
        // the deny veto's removal is not something Restricted's survival logic can protect a record
        // from, regardless of which slot's loop runs first.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var reader = DenyingReader(deniedRecordIds: new[] { GrantedProject });
        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "a record that would otherwise survive Restricted (systemuser's own membership) must still " +
            "be removed once it is also deny-listed");
    }

    [Fact]
    public async Task ComposeAsync_FullPipeline_SecureSuppression_DenyVeto_AndRestrictedVeto_EachApplyIndependently()
    {
        // One composition, four records, three mechanisms — Secure suppression (pre-max), the deny veto
        // (post-max slot 1), and Restricted (post-max slot 2) — each applying to its OWN record without
        // leaking into the others or into the untouched control record.
        var secureRecord = GrantedProject;      // org-inherited grant on a Secure record -> suppressed
        var deniedRecord = StandingMatter;      // FullAccess grant + a matching deny entry -> removed
        var restrictedRecord = UnrelatedRecord; // FullAccess grant on a Restricted record -> removed
        var openRecord = MemberRecordA;         // ordinary ViewOnly grant, no vetoes -> Read

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = secureRecord, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
            new ExternalParticipation { ProjectId = deniedRecord, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = restrictedRecord, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = openRecord, AccessLevel = ExternalAccessLevel.ViewOnly, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
        });
        participations.Flags[secureRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[restrictedRecord] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var reader = DenyingReader(deniedRecordIds: new[] { deniedRecord });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(secureRecord).Should().Be(AccessRights.None, "Secure suppresses the org-inherited grant pre-max");
        set.Contains(deniedRecord).Should().BeFalse("the deny veto removes the entry post-max, slot 1");
        set.Contains(restrictedRecord).Should().BeFalse("Restricted removes every contact-sourced entry post-max, slot 2");
        set.RightsFor(openRecord).Should().Be(AccessRights.Read, "an unvetoed record is untouched by any of the three mechanisms");
    }

    [Fact]
    public async Task ComposeAsync_WhenDenyListReaderFaults_DeniesQueriedCandidates_VetoNeverSkipped()
    {
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess },
        });

        var throwingReader = new Mock<INoAccessListReader>();
        throwingReader
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated deny-list outage"));

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), throwingReader.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "a faulting deny-list reader must deny every queried candidate — the veto is never skipped (NFR-01)");
    }

    [Fact]
    public async Task ComposeAsync_WhenDenyListReaderReturnsFailedClosed_RemovesEveryDeniedId()
    {
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess },
        });

        var reader = new Mock<INoAccessListReader>();
        reader
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NoAccessListResult
            {
                DeniedRecordIds = new HashSet<Guid> { GrantedProject },
                DenyingEntryIds = new Dictionary<Guid, IReadOnlyList<Guid>>(),
                FailedClosed = true,
            });

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "the composer must remove every id the reader reports as denied, including a fail-closed deny-all answer");
    }

    [Fact]
    public async Task ComposeAsync_WhenReferencedOrganizationResolutionIsUnreadableForARecord_ThatRecordIsDeniedDirectly()
    {
        // A record whose own referenced organizations could not be resolved must not be silently
        // treated as "references nothing" — that would let it slip past an org-keyed deny entry the
        // read simply could not confirm or rule out. Denied directly, independent of the reader (which
        // is configured here to deny nothing, proving the force-deny does not depend on it).
        var unresolvable = GrantedProject;
        var resolvable = StandingMatter;

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = unresolvable, AccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = resolvable, AccessLevel = ExternalAccessLevel.FullAccess },
        });
        participations.UnreadableOrgReferences.Add(unresolvable);

        var reader = DenyingReader(); // denies nothing — isolates the force-deny path
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(unresolvable).Should().BeFalse(
            "a record whose referenced organizations could not be resolved must be denied directly");
        set.Contains(resolvable).Should().BeTrue("a sibling record whose org references DID resolve is unaffected");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserWithNoResolvableContact_NeverQueriesDenyList()
    {
        // The deny list is keyed on contact/organization identities only (spec FR-23); organization
        // membership is itself read FROM the contact. A systemuser with no linked and no email-resolvable
        // contact has no deny-list-relevant subject on EITHER axis, so the reader must never be asked —
        // pinned with a Strict mock that throws on ANY invocation.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var principal = new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = SystemUserId,
            ContactId = null,
            Oid = Oid.ToString("D"),
            TenantId = Tenant,
            Email = string.Empty,
        };

        var strictReader = new Mock<INoAccessListReader>(MockBehavior.Strict);
        var sut = CreateSut(
            membership.Object, new FakeParticipationService(Array.Empty<ExternalParticipation>()),
            NeverStanding(), strictReader.Object);

        var set = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA });
        strictReader.VerifyNoOtherCalls();
    }

    /// <summary>
    /// A canned <see cref="INoAccessListReader"/> for task 039 WIRING tests — it does NOT re-test
    /// NoAccessListReader's own query/matching correctness (task 038 owns that in
    /// NoAccessListReaderTests.cs). Denies a candidate when its own record id is in
    /// <paramref name="deniedRecordIds"/> OR any of its referenced organizations is in
    /// <paramref name="deniedOrgIds"/> — mirroring the real reader's two match shapes closely enough to
    /// prove the CALLER assembles candidates (record id + referenced orgs) and removes denials correctly.
    /// </summary>
    private static INoAccessListReader DenyingReader(
        IReadOnlyCollection<Guid>? deniedRecordIds = null,
        IReadOnlyCollection<Guid>? deniedOrgIds = null)
    {
        var recordIds = deniedRecordIds ?? Array.Empty<Guid>();
        var orgIds = deniedOrgIds ?? Array.Empty<Guid>();
        var reader = new Mock<INoAccessListReader>();
        reader
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<Guid?>(), It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid? _, IReadOnlyCollection<Guid> __, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken ___) =>
            {
                var denied = candidates
                    .Where(c => recordIds.Contains(c.RecordId) || c.ReferencedOrganizationIds.Any(orgIds.Contains))
                    .Select(c => c.RecordId)
                    .ToHashSet();
                return new NoAccessListResult
                {
                    DeniedRecordIds = denied,
                    DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                    FailedClosed = false,
                };
            });
        return reader.Object;
    }

    private static Mock<ISubjectStandingGrantReader> AlwaysStandingMock()
    {
        var m = new Mock<ISubjectStandingGrantReader>();
        m.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(true, ExternalAccessLevel.Collaborate));
        return m;
    }

    private static AccessibleRecordSetService CreateSut(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        ISubjectStandingGrantReader standing,
        INoAccessListReader? noAccessList = null)
        => new(membership, participations, standing, noAccessList ?? NeverDeniesReader(),
               NullLogger<AccessibleRecordSetService>.Instance);

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

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 042 / FR-25 — the standing term is LEVEL-BEARING, asserted through COMPOSITION.
    //
    // The reader's own tests pin the read; these pin what the EVALUATOR does with it. Before task 042
    // the standing term contributed the constant MembershipTermRights (Collaborate-equivalent) for
    // every subject, so a View Only subject silently received Write and Create.
    // ═════════════════════════════════════════════════════════════════════════════

    public static TheoryData<ExternalAccessLevel, AccessRights> StandingLevelCases => new()
    {
        { ExternalAccessLevel.ViewOnly,    AccessRights.Read },
        { ExternalAccessLevel.Collaborate, AccessRights.Read | AccessRights.Write | AccessRights.Create },
        { ExternalAccessLevel.FullAccess,  AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete },
    };

    /// <summary>
    /// FR-25 acceptance criteria 1 + 2: a standing-derived record carries EXACTLY the subject's
    /// baseline rights — asserted by equality, so a stray bit fails.
    /// </summary>
    [Theory]
    [MemberData(nameof(StandingLevelCases))]
    public async Task ComposeAsync_StandingDerivedRecords_CarryExactlyTheSubjectsBaselineRights(
        ExternalAccessLevel baseline, AccessRights expected)
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, StandingProject));

        var sut = CreateSut(membership.Object, NoParticipations(), StandingAt(baseline));

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(StandingProject).Should().Be(expected,
            "the standing term must contribute the subject's baseline, not a constant");
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    /// <summary>
    /// FR-25 acceptance criterion 3 — the max interaction. A record reached by BOTH a View Only
    /// standing term AND an explicit Collaborate grant resolves to the HIGHER of the two.
    /// </summary>
    /// <remarks>
    /// This is the composition property the whole rights-map design exists for: terms compose by
    /// highest-wins union, so a low-level standing baseline must not cap a deliberately higher explicit
    /// grant — and equally must not be overwritten by it. Both directions matter; asserting equality on
    /// the union catches either mistake.
    /// </remarks>
    [Fact]
    public async Task ComposeAsync_StandingViewOnlyPlusExplicitCollaborateGrant_ResolvesToTheMax()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject)); // SAME record as the grant below

        var sut = CreateSut(
            membership.Object,
            new FakeParticipationService(new[]
            {
                new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.Collaborate }
            }),
            StandingAt(ExternalAccessLevel.ViewOnly));

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create,
            "the explicit Collaborate grant wins the max; a ViewOnly standing baseline must neither cap " +
            "it nor be overwritten by it");
    }

    /// <summary>
    /// 🔴 The owner's option-B decision (2026-09-10) at composition level: a standing grant with NO
    /// baseline yields records that are ABSENT, not present-with-zero-rights.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_StandingGrantWithNoBaseline_ContributesNothingAndSkipsTheMembershipWalk()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var standing = new Mock<ISubjectStandingGrantReader>();
        standing
            .Setup(s => s.ReadForContactAsync(ContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: null));

        var sut = CreateSut(membership.Object, ParticipationsFor(GrantedProject), standing.Object);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject },
            "only the explicit grant survives — an unchosen level confers nothing");
        set.RightsFor(StandingProject).Should().Be(AccessRights.None);
        set.Sources.StandingGrantMembership.Should().BeFalse(
            "provenance must not claim a term that contributed nothing");

        // Strict mock with NO membership setup: the walk was never attempted. A term that can
        // contribute nothing has no records worth enumerating (NFR-02).
        membership.VerifyNoOtherCalls();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Design §4.5 term 4 — ORG EXPANSION (unified-access-control-r2 task 043 / FR-24 + FR-25 + FR-22).
    //
    // A contact derives membership of records that reference — via a REGISTRY-LISTED org-typed lookup —
    // an organization the contact actively belongs to, at THAT ORGANIZATION's standing-grant baseline.
    //
    // ⚠️ This region REPLACES task 042's deliberate scope-boundary test
    // (ComposeAsync_NeverReadsTheOrganizationStandingGrant_OrgTermIsTask043), which asserted
    // Times.Never on ReadForOrganizationAsync with the reason "task 043 owns it". Task 043 is this
    // task; the boundary it marked is the thing now being built, so the assertion is inverted rather
    // than deleted — every test below reads the org baseline that test forbade.
    //
    // The registry filter itself (which org columns may confer) is the RESOLVER's, pinned at the
    // FetchXml-shape level in MembershipResolverServiceTests. These tests pin what the EVALUATOR does
    // with the answer: the level, the suppression, the provenance, and the read accounting.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>A walk that binds NO organizations — the contact's own standing-grant membership.</summary>
    private static MembershipResolveOptions? ContactWalk =>
        It.Is<MembershipResolveOptions?>(o => o != null && o.OrganizationIds == null);

    /// <summary>
    /// A walk bound to <paramref name="orgId"/> — the org-expansion term. Mutually exclusive with
    /// <see cref="ContactWalk"/>, so setup order does not matter.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The IdentityTypes clause is load-bearing, and it is here because perturbation P5 came back
    /// MISSED without it.</b> Removing <c>identityTypes: OrganizationIdentityTypeOnly</c> from the
    /// evaluator's org walk left the whole suite green: a Moq setup is indifferent to option fields it
    /// does not mention, so the stripped call still matched on OrganizationIds alone and still received
    /// the canned response. Requiring the narrowing HERE pins it for every org test at once — drop it in
    /// the evaluator and no setup matches, so the org term contributes nothing and the tests fail.
    /// <para>
    /// The resolver-level test cannot cover this: it passes IdentityTypes itself, so it proves the
    /// resolver HONOURS the narrowing, never that the evaluator SENDS it. What the narrowing prevents is
    /// the always-bound ContactId dragging contact-derived records into a walk whose results are credited
    /// at the ORGANISATION's baseline — undetectable downstream, since the ids are identical and only the
    /// level differs.
    /// </para>
    /// </remarks>
    private static MembershipResolveOptions? OrgWalkFor(Guid orgId) =>
        It.Is<MembershipResolveOptions?>(o =>
            o != null
            && o.OrganizationIds != null && o.OrganizationIds.Contains(orgId)
            && o.IdentityTypes != null
            && o.IdentityTypes.Contains("Organization", StringComparer.OrdinalIgnoreCase));

    private static readonly Guid OrgA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");
    private static readonly Guid OrgB = Guid.Parse("e0000000-0000-0000-0000-00000000000b");
    private static readonly Guid OrgDerivedRecord = Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid OrgDerivedRecordB = Guid.Parse("d0000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task ComposeAsync_ContactInOrgWithViewOnlyStanding_OrgDerivedRecordCarriesExactlyRead()
    {
        // FR-25 acceptance 1: the org-derived contribution enters at the ORGANIZATION's baseline —
        // never the contact's own, never a hardcoded level. Asserted by EQUALITY so a stray bit fails.
        //
        // The contact here holds NO standing grant of their own, which is load-bearing: the org term is
        // the ORGANIZATION's standing arrangement, so it must apply independently. Before task 043 this
        // contact composed to nothing at all.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, OrgDerivedRecord));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.ActiveOrgIds.Add(OrgA);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(OrgDerivedRecord).Should().Be(AccessRights.Read,
            "View Only on the ORGANIZATION means exactly Read on every record derived through it");
        set.Sources.OrgExpansionMembership.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse(
            "the contact holds no standing grant — the org term must not borrow that provenance");
    }

    [Fact]
    public async Task ComposeAsync_ContactInNoOrganizations_NeverReadsAnOrgBaselineAndDerivesNothing()
    {
        // FR-24 negative half: no active organization ⇒ no org-derived entry, and the baseline read is
        // not even attempted (NFR-02 — a term that cannot contribute costs nothing).
        //
        // NOTE on the OTHER half of the acceptance criterion — "an INACTIVE junction row confers
        // nothing". That is enforced by the `statecode eq 0` predicate inside
        // ExternalParticipationService.QueryActiveOrgIdsAsync, which THIS DOUBLE REPLACES. Asserting it
        // here would assert the fake, not the product, so it is pinned where it lives (the junction
        // query) and deliberately not restated here.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);

        var sut = CreateSut(membership.Object, NoParticipations(), standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEmpty();
        set.Sources.OrgExpansionMembership.Should().BeFalse();
        standing.Verify(
            s => s.ReadForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ComposeAsync_SecureRecord_SuppressesTheOrgDerivedContributionForAContactPrincipal()
    {
        // FR-22: org expansion is a DERIVED-MEMBER term, so a secure record never receives the
        // contribution at all. Structural suppression, not post-hoc subtraction — the record is ABSENT,
        // not present at zero rights.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, OrgDerivedRecord, OrgDerivedRecordB));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.FullAccess));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.ActiveOrgIds.Add(OrgA);
        participations.Flags[OrgDerivedRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(OrgDerivedRecord).Should().BeFalse(
            "a Full Access ORG standing grant confers nothing on a secure record");
        set.RightsFor(OrgDerivedRecord).Should().Be(AccessRights.None);
        set.Contains(OrgDerivedRecordB).Should().BeTrue(
            "the non-secure record still comes through the very same term");
    }

    [Fact]
    public async Task ComposeAsync_SecureRecord_Type1SystemUserGetsNoOrgInheritedAccessAndNoOrgExpansionWalk()
    {
        // FR-22, the systemuser half of the criterion: a Type 1 user whose LINKED CONTACT belongs to an
        // organization must not reach a secure record through that organization either.
        //
        // On this plane the org-derived route is the org-INHERITED GRANT (DirectAccessLevel == null is
        // the provenance marker task 037 introduced), and term 2 already suppresses it. Org EXPANSION
        // is NOT applied here — design §5 composes a systemuser as ADR-034 membership ∪ the caller's own
        // contact grants — so the two Times.Never assertions below are the design decision itself,
        // recorded as a test rather than as a comment: no org walk, no org baseline read.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var standing = new Mock<ISubjectStandingGrantReader>();
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = null,   // reached ONLY through the linked contact's organization
            },
        });
        participations.ActiveOrgIds.Add(OrgA);
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(AccessRights.None,
            "org-inherited access is suppressed on a secure record for a systemuser principal too");
        set.Contains(MemberRecordA).Should().BeTrue("the user's own ADR-034 membership is untouched");
        set.Sources.OrgExpansionMembership.Should().BeFalse();
        membership.Verify(
            m => m.ResolveByContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "org expansion is a contact-plane term; running it here would invent access design §5 does not grant");
        standing.Verify(
            s => s.ReadForOrganizationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ComposeAsync_OrgViewOnlyPlusDirectCollaborateGrant_ComposesToTheMax()
    {
        // The additive max across terms: the same record reached by a View Only ORG standing grant and
        // a Collaborate DIRECT grant ends at the union, not at either one alone.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly));

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.Collaborate,
                DirectAccessLevel = ExternalAccessLevel.Collaborate,
            },
        });
        participations.ActiveOrgIds.Add(OrgA);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create,
            "highest-wins is a bitwise union of the org-derived and direct terms");
    }

    [Fact]
    public async Task ComposeAsync_TwoOrgsAtDifferentBaselines_CreditsEachRecordAtItsOwnOrgsLevel()
    {
        // 🔴 THE test for task 043's central design decision. The contact belongs to two organizations
        // with DIFFERENT baselines, so the term is resolved as one walk per DISTINCT baseline.
        //
        // The rejected alternative — ONE walk binding every organization — could credit only a single
        // level, so the record reachable only through the View Only firm would silently inherit the
        // Full Access firm's rights. Nothing downstream could detect that: the record ids are identical
        // either way and only the level differs, which is precisely the class of defect FR-19 exists to
        // remove.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, OrgDerivedRecord));
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgB), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, OrgDerivedRecordB));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly));
        standing.Setup(s => s.ReadForOrganizationAsync(OrgB, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.FullAccess));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.ActiveOrgIds.Add(OrgA);
        participations.ActiveOrgIds.Add(OrgB);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(OrgDerivedRecord).Should().Be(AccessRights.Read,
            "reachable only through the View Only firm — it must NOT inherit the other firm's level");
        set.RightsFor(OrgDerivedRecordB).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete,
            "reachable through the Full Access firm");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserPlane_RequestsAccessConferringColumnsOnly()
    {
        // FR-24 / register A-8 closure. The setup matcher REQUIRES AccessConferringOnly == true, so a
        // regression to the unfiltered call fails to match, the mock returns no response, and the
        // assertion below dies — the test pins the flag rather than merely observing it.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(
                SystemUserId,
                MatterEntity,
                It.Is<MembershipResolveOptions?>(o => o != null && o.AccessConferringOnly),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, MemberRecordA));

        var sut = CreateSut(membership.Object, NoParticipations(), new Mock<ISubjectStandingGrantReader>().Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { MemberRecordA },
            "the composer must ask for registry-listed conferring columns only when deciding access");
        membership.Verify(
            m => m.ResolveAsync(
                SystemUserId,
                MatterEntity,
                It.Is<MembershipResolveOptions?>(o => o != null && o.AccessConferringOnly),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task ComposeAsync_WhenTheJunctionReadFaults_DeniesEveryCandidateAndDoesNotThrow()
    {
        // NFR-01 / NFR-02. The junction read feeds TWO consumers whose safe failure directions are
        // opposite, so one fault has two consequences and the test states both:
        //   • the additive org-expansion term contributes NOTHING (over-inclusion there is an
        //     over-grant), and
        //   • the FR-23 deny veto denies EVERY queried candidate (over-inclusion there is merely a
        //     stricter wall) — which is why the surviving grant disappears too.
        //
        // That second half is STRICTER than the acceptance criterion's "no org-derived contribution",
        // and deliberately so: collapsing both onto an empty org list would have turned the veto's
        // long-standing fail-CLOSED into a fail-OPEN, because an empty subject-org list is
        // indistinguishable from "belongs to no organization". The gate must not 500 either way.
        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.Collaborate,
                DirectAccessLevel = ExternalAccessLevel.Collaborate,
            },
        })
        {
            ThrowOnActiveOrgIds = true,
        };

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Sources.OrgExpansionMembership.Should().BeFalse("a failed junction read contributes nothing");
        set.Contains(GrantedProject).Should().BeFalse(
            "the deny veto cannot evaluate this subject's organizations, so it denies every candidate");
        set.RecordIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ComposeAsync_OrgHoldsStandingGrantWithNoBaseline_ContributesNothingAndGrantsSurvive()
    {
        // The org-side mirror of task 042's owner decision (option B): a standing grant with NO baseline
        // is a MISCONFIGURATION and confers nothing — a level nobody chose must not confer access. The
        // reader already fails closed to this same value on an unreadable or FLS-stripped row, so this
        // one case covers the whole "org-baseline read yields nothing" family.
        //
        // Unlike the junction-fault test above, the deny veto is unaffected here: the subject's
        // organizations were read successfully, so the explicit grant survives.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, OrgDerivedRecord));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StandingGrantState.NotHeld);
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: null));

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.ViewOnly,
                DirectAccessLevel = ExternalAccessLevel.ViewOnly,
            },
        });
        participations.ActiveOrgIds.Add(OrgA);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(OrgDerivedRecord).Should().BeFalse(
            "ABSENT, not present-with-zero-rights — the distinction task 042 §6.2 established");
        set.Sources.OrgExpansionMembership.Should().BeFalse("provenance must not claim a term that gave nothing");
        set.RightsFor(GrantedProject).Should().Be(AccessRights.Read,
            "the explicit grant is untouched — a missing org baseline is not a composition failure");
    }

    private static ISubjectStandingGrantReader StandingAt(ExternalAccessLevel baseline)
    {
        var m = new Mock<ISubjectStandingGrantReader>();
        m.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: baseline));
        return m.Object;
    }

    private static ISubjectStandingGrantReader AlwaysStanding()
    {
        var m = new Mock<ISubjectStandingGrantReader>();
        m.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new StandingGrantState(true, ExternalAccessLevel.Collaborate));
        return m.Object;
    }

    private static ISubjectStandingGrantReader NeverStanding()
    {
        var m = new Mock<ISubjectStandingGrantReader>();
        m.Setup(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(StandingGrantState.NotHeld);
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

        // ── Task 039 deny-veto seams ─────────────────────────────────────────────────────────────
        //
        // Per-record referenced-organization overrides (deny-veto tests seed these); anything not
        // listed references no organization. This contact's own active org memberships (deny-veto
        // subject side); empty by default.
        //
        // ⚠️ Both overrides are REQUIRED, not convenience — exactly like the Flags override above. The
        // base ExternalParticipationService implementations need `credential`/`configuration`, which are
        // `null!` here; without an override, QueryActiveOrgIdsAsync's token acquisition throws
        // uncaught into AccessibleRecordSetService.ResolveDenyVetoAsync's own fail-closed catch, which
        // then denies EVERY candidate in the composition — silently failing every pre-039 test that
        // reaches the deny-veto call (i.e. almost all of them, since a resolved contact id is enough to
        // reach it). "References/belongs to nothing" is the deny-veto's honest, inert default for a
        // test that predates it.
        public HashSet<Guid> ActiveOrgIds { get; } = new();
        public Dictionary<Guid, IReadOnlyCollection<Guid>> ReferencedOrgs { get; } = new();
        public HashSet<Guid> UnreadableOrgReferences { get; } = new();

        /// <summary>
        /// Makes the junction read FAULT (task 043). The real query is wrapped in a try/catch that
        /// returns an empty list, so a fault surfaces to the composer as
        /// <c>ActiveOrgMemberships.Failed</c> rather than as an exception — and that outcome drives TWO
        /// decisions in opposite directions: the additive org-expansion term contributes nothing, while
        /// the FR-23 deny veto denies every queried candidate. A double that could only return an empty
        /// list could not tell those apart, which is the whole point of the distinction.
        /// </summary>
        public bool ThrowOnActiveOrgIds { get; set; }

        public override Task<IReadOnlyList<Guid>> QueryActiveOrgIdsAsync(Guid contactId, CancellationToken ct = default)
            => ThrowOnActiveOrgIds
                ? Task.FromException<IReadOnlyList<Guid>>(
                    new InvalidOperationException("sprk_contactorganization query failed"))
                : Task.FromResult<IReadOnlyList<Guid>>(ActiveOrgIds.ToList());

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, ReferencedOrganizations> result = recordIds.Distinct().ToDictionary(
                id => id,
                id => UnreadableOrgReferences.Contains(id)
                    ? ReferencedOrganizations.Unresolved
                    : ReferencedOrgs.TryGetValue(id, out var orgs)
                        ? new ReferencedOrganizations(orgs, Unreadable: false)
                        : ReferencedOrganizations.None);
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

        // Task 039: this double exists to isolate the FLAG-read fault path (NFR-01) — it must NOT also
        // fault the deny-veto resolution, or ComposeAsync_WhenFlagReadFaults_SystemUserMembershipStillSurvives
        // would see its systemuser membership force-denied by ResolveDenyVetoAsync's own catch-all,
        // for a reason unrelated to what this double is testing. Benign, non-throwing defaults keep the
        // fault surface exactly where this class's name says it is.
        public override Task<IReadOnlyList<Guid>> QueryActiveOrgIdsAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
                recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));
    }
}
