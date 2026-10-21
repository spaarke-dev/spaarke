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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;   // AccessRights — task 032 rights-fidelity assertions
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;
using static Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

public class AccessibleRecordSetServiceTests
{
    private const string ProjectEntity = "sprk_project";
    private const string MatterEntity = "sprk_matter";
    private const string WorkAssignmentEntity = "sprk_workassignment";

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

    [Fact]
    public void AccessibleRecordSet_BuiltDirectlyWithANoneRightsEntry_ItsReadViewsExcludeIt()
    {
        // Task 136 (C2), the set's own layer. Every composition removes entries without Read, which would mask
        // these views; a set built some other way (a test double, a future composer) must still answer "holds
        // Read" — so this builds one directly, with the entry the evaluator itself used to leave behind.
        var set = new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = ProjectEntity,
            Rights = new Dictionary<Guid, AccessRights>
            {
                [GrantedProject] = AccessRights.Read,
                [UnrelatedRecord] = AccessRights.None,
            },
            Sources = new AccessibleRecordSetSources(false, true, false),
        };

        set.Contains(GrantedProject).Should().BeTrue("control: a Read-bearing entry is in the set");
        set.Contains(UnrelatedRecord).Should().BeFalse("Contains means \"holds Read\", not \"is a key\"");
        set.RecordIds.Should().BeEquivalentTo(new[] { GrantedProject });
        set.Count.Should().Be(1);
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

    /// <summary>
    /// ✅ FLIPPED BY TASK 141 (defect C7 item 5) — was
    /// <c>ComposeAsync_SystemUserNoLinkedContact_EmailFallbackResolvesContactGrants</c>, which pinned the hole:
    /// a licensed user with no linked contact had a contact resolved by EMAIL ($top=1, no ambiguity check, no
    /// binding) and inherited the grants of any unbound contact that carried the address. A licensed user's
    /// contact now comes ONLY from the systemuser↔contact link.
    /// </summary>
    /// <remarks>
    /// Proven by what the composer ASKS for, not only by what it returns: the participation double holds a
    /// grant that an email resolution would have reached, and counts every grant read. Zero reads is the
    /// property — a composer that resolved any contact would have to read that contact's grants.
    /// </remarks>
    [Fact]
    public async Task ComposeAsync_SystemUserNoLinkedContact_WithAnEmail_NeverResolvesAContactOrReadsGrants()
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity)); // no ADR-034 membership

        // No derived contact (sprk_primarycontact null) — but an email that a contact carries.
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
            new[] { new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.ViewOnly } });

        var sut = CreateSut(membership.Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEmpty(
            "an email is not an identity: with no link there is no contact, so no contact grants");
        set.Sources.ContactGrants.Should().BeFalse();
        participations.GrantSetReads.Should().Be(0,
            "no contact may be resolved from the email — any resolution would have read that contact's grants");
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
                }),
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
        // A grant row with no level is read with a null level, which maps to None here. No level means
        // not granted (owner rule, 2026-09-30): the evaluator's RemoveEntriesWithoutRead takes such a
        // record out of the answer at the end of composition (task 136).
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
        set.Rights.Should().NotContainKey(GrantedProject, "task 136: absent from the answer, not present with no rights");
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
        set.Rights.Should().NotContainKey(orgOnly, "task 136: absent from the answer, not present with no rights");
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
    public async Task ComposeAsync_ContactStandingMembershipOnARecordWhoseSecureFlagReadsEmpty_IsSuppressed()
    {
        // Task 150, round 17 item 3: the flag column is field-secured, so a row read back with sprk_issecure EMPTY has
        // had its true value masked. Mapped by the ONE flag reader, it must suppress the standing-grant term exactly as
        // a secure record does — never read as "not secure". The explicit-No record is the control.
        var masked = MemberRecordA;
        var open = MemberRecordB;

        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, masked, open));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[masked] = ExternalParticipationService.FlagsFrom(isSecure: null, accessPermission: null, stateCode: 0);
        participations.Flags[open] = ExternalParticipationService.FlagsFrom(isSecure: false, accessPermission: null, stateCode: 0);

        var sut = CreateSut(membership.Object, participations, AlwaysStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(masked).Should().BeFalse(
            "a record whose secure flag could not be seen is never opened to a derived-member term");
        set.Contains(open).Should().BeTrue("a record stored as not secure still comes through the same term");
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
        set.Rights.Should().NotContainKey(GrantedProject, "task 136: absent from the answer, not present with no rights");
        standing.Verify(s => s.ReadForContactAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "the systemuser plane must never consult the standing-grant flag");
    }

    // ─────────────────────────────────────────────────────────────────────
    // #2121 — Secure suppresses a systemuser's LOOKUP-derived membership (Term 1) on the SPA/Teams plane.
    // A user whose linked contact is named in a registry "Assigned …" column of a Secure record, and who holds no
    // Dataverse share on it, is denied by the model-driven app (the Secure BU blocks them); the membership term must
    // not open it here either. (This plane does not read shares — K1, task 036.)
    // Ownership-derived membership (the user owns it, or is in the owning team) is Dataverse access and stays.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>The composer's MAIN membership walk: every access-conferring descriptor, no identity-type narrowing.</summary>
    private static MembershipResolveOptions? MainMembershipWalk =>
        It.Is<MembershipResolveOptions?>(o =>
            o != null && o.Limit == AccessibleRecordSetService.MembershipPageSize
            && o.AccessConferringOnly && o.IdentityTypes == null);

    /// <summary>
    /// The OWNERSHIP walk (#2121): the same access-conferring walk narrowed to EXACTLY the real-ownership identity types
    /// {SystemUser, Team} — set equality, so dropping one (an owned or team-owned Secure record would then lose its
    /// membership), adding BusinessUnit back (K6: a BU-only Secure reach would keep MembershipTermRights unconfirmed) or
    /// adding one a registry "Assigned …" column can carry (Contact / Organization) leaves the setup unmatched and the
    /// test red.
    /// </summary>
    private static MembershipResolveOptions? OwnershipMembershipWalk =>
        It.Is<MembershipResolveOptions?>(o =>
            o != null && o.Limit == AccessibleRecordSetService.MembershipPageSize
            && o.AccessConferringOnly
            && o.IdentityTypes != null
            && new HashSet<string>(o.IdentityTypes, StringComparer.OrdinalIgnoreCase)
                .SetEquals(new[] { "SystemUser", "Team" }));

    private static Mock<IMembershipResolverService> MembershipWith(
        string entityType, Guid[] membershipIds, Guid[] ownershipIds)
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, entityType, MainMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, membershipIds));
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, entityType, OwnershipMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, ownershipIds));
        return membership;
    }

    private static readonly RootRecordFlags SecureOnly = new(IsSecure: true, IsRestricted: false);

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRecordReachedOnlyThroughALookupColumn_IsNotInTheSet()
    {
        // Must fire (#2121): the user's linked contact is the record's Assigned Attorney; the user owns nothing.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = NoParticipations();
        ((FakeParticipationService)participations).Flags[secure] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure,
            "FR-22: on a Secure record derived (lookup-column) membership is suppressed for every principal kind — the "
            + "model-driven app denies this user, so the SPA/Teams plane must too");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRecordReachedThroughOwnership_KeepsMembershipRights()
    {
        // Must not fire: the user is a member of the owning team. Ownership is Dataverse access, which the
        // model-driven app grants too, so the membership term keeps it.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: new[] { secure });
        var participations = NoParticipations();
        ((FakeParticipationService)participations).Flags[secure] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(secure).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "ownership-derived membership survives Secure");
        membership.Verify(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()),
            Times.Once, "the ownership half is decided by the resolver's own ownership descriptors");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_LimitedNonSecureRecordReachedThroughALookupColumn_KeepsMembership()
    {
        // Must not fire: Limited governs which CONTACT grant types count, never internal access (task 138).
        var limited = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { limited }, ownershipIds: Array.Empty<Guid>());
        var participations = NoParticipations();
        ((FakeParticipationService)participations).Flags[limited] = LimitedOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(limited).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "Limited does not touch a systemuser's membership");
        membership.Verify(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()),
            Times.Never, "no Secure candidate, so no extra read (NFR-02)");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_NonSecureRecordReachedThroughALookupColumn_KeepsMembership()
    {
        // Must not fire: an ordinary record keeps its lookup-derived membership.
        var open = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { open }, ownershipIds: Array.Empty<Guid>());

        var sut = CreateSut(membership.Object, NoParticipations(), NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(open).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        membership.Verify(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()),
            Times.Never, "no Secure candidate, so no extra read (NFR-02)");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_NonSecureChildFiledUnderASecureMatter_ReachedThroughALookupColumn_IsNotInTheSet()
    {
        // Must fire on the EFFECTIVE flags (task 174): work assignment W is not flagged secure itself but is filed
        // under Secure matter M, so it is Secure. Control: sibling W2, filed under an ordinary matter, keeps its
        // membership — which also proves the filing walk was read as KNOWN (an unknown walk folds to Unreadable,
        // i.e. secure, and would drop W2 as well).
        var w = Guid.Parse("a1000000-0000-0000-0000-000000000001");
        var w2 = Guid.Parse("a1000000-0000-0000-0000-000000000002");
        var secureMatter = Guid.Parse("a2000000-0000-0000-0000-000000000001");
        var openMatter = Guid.Parse("a2000000-0000-0000-0000-000000000002");

        var membership = MembershipWith(WorkAssignmentEntity, membershipIds: new[] { w, w2 }, ownershipIds: Array.Empty<Guid>());
        var participations = NoParticipations();   // W and W2 carry no secure flag of their own

        var filing = FilingWorld(
            FiledWorkAssignment(w, secureMatter),
            FiledWorkAssignment(w2, openMatter),
            MatterRow(secureMatter, isSecure: true),
            MatterRow(openMatter, isSecure: false));

        var sut = CreateSut(membership.Object, participations, NeverStanding(), filing: filing);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), WorkAssignmentEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(w,
            "a child filed under a Secure matter is effectively Secure, so lookup-derived membership is suppressed");
        set.RightsFor(w2).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "control: a child of an ordinary matter keeps its lookup-derived membership");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRecordsAreFilteredPerId_OwnershipOrBothKeep_LookupOnlyDrops()
    {
        // Must not fire for an id reached BOTH ways; per-id, not all-or-nothing. Three Secure records in one composition:
        //   both  — owned by the user's team AND names the user's contact in an Assigned column → kept;
        //   owned — ownership only → kept;
        //   assignedOnly — lookup column only → dropped.
        var both = MemberRecordA;
        var owned = MemberRecordB;
        var assignedOnly = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var membership = MembershipWith(
            MatterEntity,
            membershipIds: new[] { both, owned, assignedOnly },
            ownershipIds: new[] { both, owned });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[both] = participations.Flags[owned] = participations.Flags[assignedOnly] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(both).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "an id matched by ownership AND a lookup column keeps its membership");
        set.RightsFor(owned).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        set.Rights.Should().NotContainKey(assignedOnly);
    }

    private static readonly RootRecordFlags SecureAndRestricted = new(IsSecure: true, IsRestricted: true);

    /// <summary>The linked contact's OWN (direct, by-name) ViewOnly grant on a matter.</summary>
    private static FakeParticipationService DirectViewOnlyMatterGrant(Guid matterId) => new(
        Array.Empty<ExternalParticipation>(),
        matterGrants: new[]
        {
            new ExternalRootGrant
            {
                RecordId = matterId,
                AccessLevel = ExternalAccessLevel.ViewOnly,
                DirectAccessLevel = ExternalAccessLevel.ViewOnly,
            },
        });

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureLookupOnlyRecordWithLinkedContactDirectGrant_CarriesExactlyTheGrantLevel()
    {
        // #2121 × term 2: the lookup-derived membership is suppressed BEFORE the max, so the linked contact's own by-name
        // ViewOnly grant (which FR-22 keeps on a Secure record) is what the user holds — exactly Read. Not absent (the
        // suppression must not remove the key after the max, taking the direct grant with it) and not MembershipTermRights
        // (the lookup-derived membership must not win the max).
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = DirectViewOnlyMatterGrant(secure);
        participations.Flags[secure] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().ContainKey(secure, "the linked contact's own by-name grant survives Secure (FR-22)");
        set.RightsFor(secure).Should().Be(AccessRights.Read,
            "the direct ViewOnly grant is the only contribution left once lookup-derived membership is suppressed");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRestrictedLookupOnlyRecordWithLinkedContactDirectGrant_IsNotInTheSet()
    {
        // #2121 × Restricted: Restricted removes the contact-sourced grant and keeps only the Restricted SURVIVOR, which is
        // the systemuser's membership term. That survivor must be the SUPPRESSED term: the lookup-only Secure id is not in
        // it, so nothing survives and the record goes. Fed the unfiltered walk, the survivor would write
        // MembershipTermRights back onto a record the user reaches only through an Assigned lookup.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = DirectViewOnlyMatterGrant(secure);
        participations.Flags[secure] = SecureAndRestricted;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure,
            "Restricted removes the contact grant, and suppressed lookup-derived membership is not a Restricted survivor");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OwnershipWalkThrows_ComposeThrowsAndReturnsNoSet()
    {
        // Fail closed (ADR-003): a fault in the ownership walk propagates and the caller denies wholesale, as for the
        // main walk. Swallowing it into "nothing to suppress" would keep lookup-derived membership on a Secure record.
        var secure = MemberRecordA;
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, MainMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, secure));
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("ownership walk faulted"));
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var act = () => sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("ownership walk faulted");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OwnershipWalkCapped_LookupOnlySecureIdIsDropped_OwnedIdKept()
    {
        // Fail closed when the ownership walk is INCOMPLETE: the resolver claims a further page but returns nothing new
        // (WalkMembershipPagesAsync's non-advancing-cursor exit), so the walk is capped. An id the walk did return is
        // ownership and keeps its membership; a Secure id it did not return is dropped — a capped walk is never read
        // as "keep everything".
        var owned = MemberRecordA;
        var assignedOnly = MemberRecordB;
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, MainMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, owned, assignedOnly));
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, owned) with { ContinuationToken = "more" });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[owned] = participations.Flags[assignedOnly] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(assignedOnly,
            "a capped ownership walk did not show ownership of this Secure id, so its lookup-derived membership is dropped");
        set.RightsFor(owned).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "an id the (capped) ownership walk did return is ownership");
        membership.Verify(m => m.ResolveAsync(SystemUserId, MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()),
            Times.Exactly(2), "the second page added nothing new, which is what capped the walk");
    }

    // ─────────────────────────────────────────────────────────────────────
    // #2121 option (b) (owner-approved 2026-10-20) — before a lookup-only Secure id is dropped, Dataverse is asked
    // app-only, bound to THIS systemuser (the No Access store's RetrievePrincipalAccess read), whether the user can
    // read it. Kept at exactly Read when it can (a share, a role, a team); dropped on anything else.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The No Access store with its wire read substituted (its <c>internal virtual</c> reads are the test seam, the
    /// <c>FakeEnforcementStore</c> convention; no transport double). Loose, so an un-setup read answers None.
    /// </summary>
    private static Mock<NoAccessEnforcementStore> NewDataverseStore() =>
        new(new HttpClient(), null!, null!, NullLogger<NoAccessEnforcementStore>.Instance);

    /// <summary>A Dataverse that answers "no rights" for every record (the default the SUT is built with).</summary>
    private static Mock<NoAccessEnforcementStore> DataverseGrantsNothing()
    {
        var reader = NewDataverseStore();
        reader
            .Setup(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ReturnsAsync(AccessRights.None);
        return reader;
    }

    /// <summary>A Dataverse that answers <paramref name="rights"/> for <paramref name="recordId"/> (and "no rights" elsewhere).</summary>
    private static Mock<NoAccessEnforcementStore> DataverseAnswers(Guid recordId, AccessRights rights)
    {
        var reader = DataverseGrantsNothing();
        reader
            .Setup(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", recordId, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ReturnsAsync(rights);
        return reader;
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureLookupOnlyRecord_DataverseGrantsRead_IsKeptAtExactlyRead()
    {
        // Must fire (option (b)): the user's linked contact is the Assigned Attorney and the user holds a Dataverse share,
        // so RetrievePrincipalAccess answers Read|Write. The record is kept, at EXACTLY Read — never MembershipTermRights
        // and never the wider Dataverse answer (task 036's round-59 rule for a Dataverse-term record).
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = DataverseAnswers(secure, AccessRights.Read | AccessRights.Write);

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().ContainKey(secure, "Dataverse says this user can read the Secure record (K1 not widened)");
        set.RightsFor(secure).Should().Be(AccessRights.Read, "a Dataverse-confirmed record carries Read only");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "the question is bound to THIS systemuser and the record's entity set");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureLookupOnlyRecord_DataverseGrantsNothing_IsNotInTheSet()
    {
        // Must not fire: Dataverse answers no rights (no share; the Secure BU blocks the user) → dropped.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = DataverseAnswers(secure, AccessRights.None);

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure, "no Dataverse Read, so the lookup-derived membership stays suppressed");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "the drop follows Dataverse's answer, not a skipped question");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureLookupOnlyRecord_DataverseGrantsOnlyWrite_IsNotInTheSet()
    {
        // Must not fire: an answer without Read is not a Read grant (a check of "any rights" would keep it).
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding(),
            principalRights: DataverseAnswers(secure, AccessRights.Write).Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure);
    }

    public static TheoryData<string> DataverseFaults => new() { "500", "timeout", "403" };

    [Theory]
    [MemberData(nameof(DataverseFaults))]
    public async Task ComposeAsync_SystemUser_DataverseFaultsForOneSecureLookupOnlyId_DropsOnlyThatId_LogsAWarning_NeverThrows(
        string fault)
    {
        // Fail closed per id (ADR-003): Dataverse faults for `faulted` (a 500, a timeout, or a 403 privilege /
        // impersonation error); `granted` is answered Read, `open` is non-secure. The composition does not throw, the
        // faulted id is dropped with a Warning naming it, and the other two are unaffected.
        var faulted = MemberRecordA;
        var granted = MemberRecordB;
        var open = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var membership = MembershipWith(
            MatterEntity, membershipIds: new[] { faulted, granted, open }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[faulted] = participations.Flags[granted] = SecureOnly;

        var dataverse = DataverseAnswers(granted, AccessRights.Read);
        Exception error = fault switch
        {
            "500" => new HttpRequestException("Dataverse read failed: 500", null, System.Net.HttpStatusCode.InternalServerError),
            "403" => new HttpRequestException("Dataverse read failed: 403", null, System.Net.HttpStatusCode.Forbidden),
            _ => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"),
        };
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", faulted, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ThrowsAsync(error);
        var log = new CapturingLogger();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, logger: log);
        var act = () => sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        var set = (await act.Should().NotThrowAsync("one id's fault must never deny the whole composition")).Subject;
        set.Rights.Should().NotContainKey(faulted, "a fault is never read as a grant");
        set.RightsFor(granted).Should().Be(AccessRights.Read, "the other Secure id's answer stands");
        set.RightsFor(open).Should().Be(AccessibleRecordSetService.MembershipTermRights, "a non-secure id is untouched");
        var expectedStatus = fault == "timeout" ? "timeout" : fault;
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                                          && e.Message.Contains("FAULTED") && e.Message.Contains(faulted.ToString())
                                          && e.Message.Contains(MatterEntity) && e.Message.Contains($"status: {expectedStatus}"),
            "a fault is logged as a Warning with the id, entity type and status, distinct from a denial");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_CallerCancels_DuringTheRightsCheck_Propagates()
    {
        // Only the CALLER's cancellation propagates (it is not a per-id fault to swallow).
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        using var caller = new CancellationTokenSource();
        var dataverse = NewDataverseStore();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .Returns(async (Guid _, string _, Guid _, CancellationToken token, LogLevel _) =>
            {
                caller.Cancel();
                token.ThrowIfCancellationRequested();
                await Task.Yield();
                return AccessRights.Read;
            });

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var act = () => sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureLookupOnlyRecord_Dataverse404_IsNotInTheSet()
    {
        // 404: the record is gone → dropped (Information, not a Warning — it is not a fault).
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = DataverseGrantsNothing();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ThrowsAsync(new HttpRequestException("Dataverse read failed: 404", null, System.Net.HttpStatusCode.NotFound));
        var log = new CapturingLogger();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, logger: log);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure, "a 404 is never a grant");
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Information
                                          && e.Message.Contains("404") && e.Message.Contains(secure.ToString()));
        log.Entries.Should().NotContain(e => e.Level >= Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("FAULTED"),
            "a gone record is not a Dataverse fault");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_MoreLookupOnlySecureIdsThanTheCeiling_ExcessDroppedAndNeverAsked()
    {
        // NFR-02: at most MaxSecureLookupRightsChecks ids are asked. Dataverse would grant Read on every one, so an id is
        // kept exactly when it was asked; the excess is dropped and the reader is never called for it.
        var max = AccessibleRecordSetService.MaxSecureLookupRightsChecks;
        var ids = Enumerable.Range(1, max + 3).Select(i => Guid.Parse($"e0000000-0000-0000-0000-{i:D12}")).ToArray();
        var membership = MembershipWith(MatterEntity, membershipIds: ids, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        foreach (var id in ids)
        {
            participations.Flags[id] = SecureOnly;
        }

        var dataverse = NewDataverseStore();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ReturnsAsync(AccessRights.Read);
        var log = new CapturingLogger();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, logger: log);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        var asked = ids.OrderBy(id => id).Take(max).ToArray();
        var excess = ids.OrderBy(id => id).Skip(max).ToArray();
        set.RecordIds.Should().BeEquivalentTo(asked, "exactly the ids under the ceiling were asked and confirmed");
        foreach (var id in excess)
        {
            dataverse.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), id, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
                Times.Never, "an id over the ceiling is dropped without a Dataverse call");
        }

        dataverse.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Exactly(max));
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning && e.Message.Contains("ceiling"));
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OwnershipKeptAndNonSecureIds_NeverAskDataverse()
    {
        // NFR-02: only LOOKUP-ONLY Secure ids are asked. An owned Secure id, a non-secure id and a Limited id never reach
        // the reader — and keep MembershipTermRights. The lookup-only Secure id is the one question.
        var owned = MemberRecordA;
        var open = MemberRecordB;
        var limited = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var assignedOnly = Guid.Parse("a0000000-0000-0000-0000-000000000004");
        var membership = MembershipWith(
            MatterEntity, membershipIds: new[] { owned, open, limited, assignedOnly }, ownershipIds: new[] { owned });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[owned] = participations.Flags[assignedOnly] = SecureOnly;
        participations.Flags[limited] = LimitedOnly;
        var dataverse = DataverseGrantsNothing();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(owned).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        set.RightsFor(open).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        set.RightsFor(limited).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        foreach (var id in new[] { owned, open, limited })
        {
            dataverse.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), id, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
                Times.Never, "only lookup-only Secure ids are asked");
        }

        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", assignedOnly, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once);
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_NoSecureCandidate_NeverAsksDataverse()
    {
        // NFR-02: an ordinary composition makes no extra call.
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { MemberRecordA }, ownershipIds: Array.Empty<Guid>());
        var dataverse = DataverseGrantsNothing();

        var sut = CreateSut(membership.Object, NoParticipations(), NeverStanding(), principalRights: dataverse.Object);
        await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        dataverse.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Never);
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRestrictedLookupOnlyRecord_DataverseGrantsRead_SurvivesRestrictedAtRead()
    {
        // Secure + Restricted, Dataverse grants Read. SurvivesRestrictedForSystemUserAsync keeps the systemuser's OWN term
        // ("only system users may have access") and drops only contact-sourced access; a Dataverse-confirmed id is the
        // user's own Dataverse access (task 036 passes its Dataverse term to the same survivor). So an internal user keeps
        // it at Read; the linked contact's direct Collaborate grant (contact-sourced) is removed by Restricted and cannot
        // raise it.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = new FakeParticipationService(
            Array.Empty<ExternalParticipation>(),
            matterGrants: new[]
            {
                new ExternalRootGrant
                {
                    RecordId = secure,
                    AccessLevel = ExternalAccessLevel.Collaborate,
                    DirectAccessLevel = ExternalAccessLevel.Collaborate,
                },
            });
        participations.Flags[secure] = SecureAndRestricted;

        var sut = CreateSut(membership.Object, participations, NeverStanding(),
            principalRights: DataverseAnswers(secure, AccessRights.Read).Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().ContainKey(secure, "the user's own Dataverse access is a Restricted survivor");
        set.RightsFor(secure).Should().Be(AccessRights.Read, "the survivor is the Read-only Dataverse term, not the contact grant");
    }

    [Fact]
    public async Task ComposeAsync_SystemUserFlaggedExternal_SecureRestrictedLookupOnlyRecord_DataverseGrantsRead_IsNotInTheSet()
    {
        // The survivor's own rule still applies (task 114): a systemuser flagged sprk_isexternal keeps nothing on a
        // Restricted record, Dataverse-confirmed or not.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureAndRestricted;

        var sut = CreateSut(membership.Object, participations, NeverStanding(),
            principalRights: DataverseAnswers(secure, AccessRights.Read).Object,
            systemUsers: InternalSystemUsers(SystemUserId));
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure);
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_ComposerBuiltWithoutTheStore_DropsTheLookupOnlySecureId_AndWarns()
    {
        // Fail closed when the Dataverse check is absent (a direct construction that passes no store): nothing is
        // confirmed, the id stays dropped, and the gap is a Warning, not silence.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var log = new CapturingLogger();

        var sut = new AccessibleRecordSetService(
            membership.Object, participations, NeverStanding(), NeverDeniesReader(), UnlinkedIdentityStore(),
            InternalSystemUsers(), NoFilingEntities(), log);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure);
        log.Entries.Should().Contain(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Warning
                                          && e.Message.Contains("No principal-rights reader"));
    }

    [Fact]
    public async Task ComposeAsync_BuiltByTheProductionModule_AsksTheModulesStore_AndCachesItsAnswer()
    {
        // #2121 round 1 (F3): the store and the rights cache are OPTIONAL constructor parameters, so pin that the PRODUCTION
        // registration (ExternalAccessModule.AddExternalAccess) supplies the store, by behaviour only (ADR-038 B3: no
        // service-descriptor assertion): the composer the module registers asks the module's own typed-HttpClient store
        // (only its transport is a stub), keeps the Secure id at Read, and the second composition is answered from the
        // container's cache. If the module stopped registering the store, the container would build the composer with its
        // default (no store), which drops the id and sends no request.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var requests = new System.Collections.Concurrent.ConcurrentQueue<string>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build());
        Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule.AddExternalAccess(services);

        // The composer's upstream readers, substituted at their module boundary (registered after the module, so these win).
        services.AddSingleton(membership.Object);
        services.AddSingleton<ExternalParticipationService>(participations);
        services.AddSingleton(NeverStanding());
        services.AddSingleton(NeverDeniesReader());
        services.AddSingleton(UnlinkedIdentityStore());
        services.AddSingleton(InternalSystemUsers());
        services.AddSingleton(NoFilingEntities());
        // Task 036: the module's FR-20 source (registered unconditionally) reads through CommunicationModule's impersonated
        // query. The flag is off here, so it is never asked; it only has to be constructible.
        services.AddSingleton(Mock.Of<Sprk.Bff.Api.Services.Communication.IImpersonatedCommunicationQuery>(MockBehavior.Strict));
        services.AddSingleton<Azure.Core.TokenCredential>(new FixedTokenCredential());
        services.AddSingleton<Sprk.Bff.Api.Infrastructure.Cache.ITenantCache>(
            new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache());
        // Only the transport is replaced, for EVERY client: this registers no client of its own, so it cannot stand in
        // for the module's store registration.
        services.ConfigureAll<Microsoft.Extensions.Http.HttpClientFactoryOptions>(o =>
            o.HttpMessageHandlerBuilderActions.Add(b => b.PrimaryHandler = new RightsHandler(requests, "ReadAccess")));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<IAccessibleRecordSetService>();
        var first = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);
        var second = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        first.RightsFor(secure).Should().Be(AccessRights.Read, "the module's store answered Read");
        second.RightsFor(secure).Should().Be(AccessRights.Read, "the cached answer is applied the same way");
        requests.Should().ContainSingle("the second composition is answered from the container's rights cache")
            .Which.Should().Contain($"systemusers({SystemUserId})/Microsoft.Dynamics.CRM.RetrievePrincipalAccess");
    }

    // ─────────────────────────────────────────────────────────────────────
    // #2121 round 1 (F1) — each Dataverse answer is cached per (systemuser, entity set, record) for the grant cache's
    // TTL; a 404 or a fault never is.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUser_SecondCompositionWithinTheTtl_MakesNoDataverseCall_ForAReadOrADenial()
    {
        var granted = MemberRecordA;
        var denied = MemberRecordB;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { granted, denied }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[granted] = participations.Flags[denied] = SecureOnly;
        var dataverse = DataverseAnswers(granted, AccessRights.Read);
        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, rightsCache: cache);
        var first = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);
        var second = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        first.RightsFor(granted).Should().Be(AccessRights.Read);
        second.RightsFor(granted).Should().Be(AccessRights.Read, "a cached Read is kept at Read");
        first.Rights.Should().NotContainKey(denied);
        second.Rights.Should().NotContainKey(denied, "a cached denial is still a denial");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", granted, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "the Read answer is cached");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", denied, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "the denial is cached too");
    }

    public static TheoryData<string> UncachedOutcomes => new() { "404", "500" };

    [Theory]
    [MemberData(nameof(UncachedOutcomes))]
    public async Task ComposeAsync_SystemUser_AGoneOrFaultedAnswer_IsNotCached_TheNextCompositionAsksAgain(string outcome)
    {
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = NewDataverseStore();
        var status = outcome == "404" ? System.Net.HttpStatusCode.NotFound : System.Net.HttpStatusCode.InternalServerError;
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ThrowsAsync(new HttpRequestException($"Dataverse read failed: {outcome}", null, status));
        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, rightsCache: cache);
        await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);
        var second = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        second.Rights.Should().NotContainKey(secure);
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Exactly(2), $"a {outcome} is never cached: the next composition asks Dataverse again");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_DifferentUsers_DoNotShareCachedAnswers()
    {
        // Dataverse grants user A Read and user B nothing on the same Secure record; both reach it through a lookup. One
        // shared cache: B must not be served A's answer (the key carries the systemuser id), and each user is asked once.
        var secure = MemberRecordA;
        var otherUser = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(It.IsAny<Guid>(), MatterEntity, MainMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, secure));
        membership
            .Setup(m => m.ResolveAsync(It.IsAny<Guid>(), MatterEntity, OwnershipMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity));
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = DataverseAnswers(secure, AccessRights.Read); // A (SystemUserId) Read; anyone else None
        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();
        var userB = new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = otherUser,
            ContactId = ContactId,
            Oid = Oid.ToString("D"),
            TenantId = Tenant,
        };

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, rightsCache: cache);
        var setA = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);
        var setB = await sut.ComposeAsync(userB, MatterEntity, CancellationToken.None);

        setA.RightsFor(secure).Should().Be(AccessRights.Read);
        setB.Rights.Should().NotContainKey(secure, "user A's cached Read must never serve user B");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(otherUser, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "user B is asked about themselves");
    }

    // ─────────────────────────────────────────────────────────────────────
    // #2121 round 2 (F3) — the bounded concurrency, the cache-fault fallback and the tenant namespace, each proved.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUser_ManySecureLookupOnlyIds_AtMostTheConcurrencyCapAreAskedAtOnce()
    {
        // F3a: every call is held open until released. With the gate, exactly MaxConcurrentRecordLookups start and the
        // rest wait; without it (or with a wider one) more start in the grace period and the high-water mark exceeds it.
        var cap = CallerRecordAccessProbe.MaxConcurrentRecordLookups;
        var ids = Enumerable.Range(1, cap * 3).Select(i => Guid.Parse($"d0000000-0000-0000-0000-{i:D12}")).ToArray();
        var membership = MembershipWith(MatterEntity, membershipIds: ids, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        foreach (var id in ids)
        {
            participations.Flags[id] = SecureOnly;
        }

        var sync = new object();
        var inFlight = 0;
        var highWater = 0;
        var capReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var dataverse = NewDataverseStore();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .Returns(async (Guid _, string _, Guid _, CancellationToken token, LogLevel _) =>
            {
                lock (sync)
                {
                    inFlight++;
                    highWater = Math.Max(highWater, inFlight);
                    if (inFlight >= cap)
                    {
                        capReached.TrySetResult();
                    }
                }

                try
                {
                    await release.Task.WaitAsync(token);
                }
                finally
                {
                    lock (sync)
                    {
                        inFlight--;
                    }
                }

                return AccessRights.Read;
            });

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var compose = sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);
        try
        {
            var started = await Task.WhenAny(capReached.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            started.Should().BeSameAs(capReached.Task, "precondition: the cap's worth of calls started");
            await Task.Delay(TimeSpan.FromMilliseconds(300)); // room for a call past the cap to start, were the gate absent or wider
            lock (sync)
            {
                highWater.Should().Be(cap, $"at most {cap} rights calls are in flight at once (NFR-02)");
            }
        }
        finally
        {
            release.TrySetResult();
        }

        var set = await compose;
        set.RecordIds.Should().BeEquivalentTo(ids, "every waiting id is asked once a slot frees, and Dataverse grants Read");
        highWater.Should().Be(cap);
    }

    /// <summary>Where the rights cache faults, and with what: an ordinary error, or a cancellation the cache raised itself.</summary>
    public static TheoryData<string, string> RightsCacheFaultCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var where in new[] { "read", "write", "both" })
        {
            data.Add(where, "error");
            data.Add(where, "own-cancellation");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RightsCacheFaultCases))]
    public async Task ComposeAsync_SystemUser_TheRightsCacheFaults_AsksDataverseLive_KeepsTheGrantedIdsAtRead_AndWarnsOnce(
        string where, string kind)
    {
        // F3b + K2: a cache fault falls back to the live Dataverse question and Dataverse's Read stands. An
        // OperationCanceledException the cache raised ITSELF (an inner Redis timeout; the caller's token is not cancelled)
        // is a cache fault too, not a Dataverse timeout. The faults are logged ONCE for the composition, not once per id.
        var ids = new[] { MemberRecordA, MemberRecordB, Guid.Parse("a0000000-0000-0000-0000-000000000003") };
        var membership = MembershipWith(MatterEntity, membershipIds: ids, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        foreach (var id in ids)
        {
            participations.Flags[id] = SecureOnly;
        }

        var dataverse = NewDataverseStore();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ReturnsAsync(AccessRights.Read);
        var cache = new FaultingRightsCache(
            faultReads: where != "write",
            faultWrites: where != "read",
            fault: kind == "error"
                ? () => new InvalidOperationException("rights cache unavailable (test double)")
                : () => new OperationCanceledException("inner cache timeout (test double)"));
        var log = new CapturingLogger();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object,
            logger: log, rightsCache: cache);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        cache.Faults.Should().Be(where == "both" ? 2 * ids.Length : ids.Length, "precondition: the cache faulted for every id");
        foreach (var id in ids)
        {
            set.RightsFor(id).Should().Be(AccessRights.Read, $"a cache {where} fault ({kind}) never drops an id Dataverse grants");
            dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", id, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
                Times.Once, "the id was asked of Dataverse live");
        }

        log.Entries.Where(e => e.Level >= LogLevel.Warning).Should().ContainSingle(
                "one Warning for the composition's cache faults, not one per id")
            .Which.Message.Should().Contain("rights cache faulted").And.Contain($"{(where == "both" ? 2 * ids.Length : ids.Length)} time(s)");
        log.Entries.Should().NotContain(e => e.Message.Contains("FAULTED"), "a cache fault is not a Dataverse fault");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_TwoTenants_SameUserSetAndRecord_DoNotShareACachedAnswer()
    {
        // F3c: the cache key carries the REQUEST's tenant (its tid claim, ImpersonatedRootSetSource.CacheTenantFor). One
        // shared cache; Dataverse answers the first question Read and every later one nothing. Tenant B must be asked
        // itself and must not be served tenant A's Read; each tenant's own entry then answers its next composition.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = NewDataverseStore();
        dataverse
            .SetupSequence(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .ReturnsAsync(AccessRights.Read)
            .ReturnsAsync(AccessRights.None)
            .ReturnsAsync(AccessRights.None);
        var cache = new Sprk.Bff.Api.Tests.Infrastructure.Cache.InMemoryTenantCache();
        const string tenantA = "2121a000-aaaa-4aaa-8aaa-00000000000a";
        const string tenantB = "2121b000-bbbb-4bbb-8bbb-00000000000b";

        var sutA = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object,
            rightsCache: cache, httpContextAccessor: TenantRequest(tenantA));
        var sutB = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object,
            rightsCache: cache, httpContextAccessor: TenantRequest(tenantB));

        (await sutA.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None))
            .RightsFor(secure).Should().Be(AccessRights.Read, "precondition: Dataverse grants tenant A's question");
        (await sutB.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None))
            .Rights.Should().NotContainKey(secure, "tenant A's cached Read must never answer tenant B's request");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Exactly(2), "tenant B is asked itself");

        (await sutA.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None))
            .RightsFor(secure).Should().Be(AccessRights.Read, "tenant A's own entry answers its next composition");
        (await sutB.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None))
            .Rights.Should().NotContainKey(secure, "tenant B's own entry answers its next composition");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", secure, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Exactly(2), "both later compositions were answered from their own tenant's entry");
    }

    /// <summary>
    /// A request in <paramref name="tenant"/>: its user carries the <c>tid</c> claim the cache key's namespace reads. A fixed
    /// accessor, not the framework's <c>HttpContextAccessor</c>, whose context lives in one shared <c>AsyncLocal</c> (two
    /// of them in one test would both see the last context set).
    /// </summary>
    private static Microsoft.AspNetCore.Http.IHttpContextAccessor TenantRequest(string tenant) => new FixedRequest
    {
        HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
        {
            User = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim("tid", tenant) }, "test")),
        },
    };

    private sealed class FixedRequest : Microsoft.AspNetCore.Http.IHttpContextAccessor
    {
        public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
    }

    /// <summary>A rights cache whose reads and/or writes fault with <paramref name="fault"/>; otherwise it holds nothing.</summary>
    private sealed class FaultingRightsCache(bool faultReads, bool faultWrites, Func<Exception> fault)
        : Sprk.Bff.Api.Infrastructure.Cache.ITenantCache
    {
        private int _faults;

        public int Faults => Volatile.Read(ref _faults);

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
            => faultReads ? Fail<T?>() : Task.FromResult<T?>(default);

        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null,
            string cacheInstance = "default", CancellationToken ct = default)
            => faultWrites ? Fail<object?>() : Task.CompletedTask;

        public Task RemoveAsync(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version,
            Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default",
            CancellationToken ct = default)
            => throw new NotSupportedException("The composer does not use GetOrCreateAsync.");

        private Task<TResult> Fail<TResult>()
        {
            Interlocked.Increment(ref _faults);
            return Task.FromException<TResult>(fault());
        }
    }

    /// <summary>
    /// For the central eviction tests (<c>AccessCacheInvalidationTests</c>, #2121 round 2, F4): the PRODUCTION composer,
    /// for which every id in <paramref name="secureLookupOnlyIds"/> is a Secure <paramref name="entityType"/> record any
    /// systemuser reaches only through a lookup column. Each composition therefore asks <paramref name="store"/> about
    /// each id through <paramref name="cache"/>, in the tenant <paramref name="request"/> carries.
    /// </summary>
    internal static AccessibleRecordSetService SecureLookupOnlyComposer(
        string entityType,
        IReadOnlyCollection<Guid> secureLookupOnlyIds,
        NoAccessEnforcementStore store,
        Sprk.Bff.Api.Infrastructure.Cache.ITenantCache cache,
        Microsoft.AspNetCore.Http.IHttpContextAccessor request)
    {
        var ids = secureLookupOnlyIds.ToArray();
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(It.IsAny<Guid>(), entityType, MainMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, ids));
        membership
            .Setup(m => m.ResolveAsync(It.IsAny<Guid>(), entityType, OwnershipMembershipWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType));
        var participations = (FakeParticipationService)NoParticipations();
        foreach (var id in ids)
        {
            participations.Flags[id] = SecureOnly;
        }

        return CreateSut(membership.Object, participations, NeverStanding(), principalRights: store, rightsCache: cache,
            httpContextAccessor: request);
    }

    /// <summary>A systemuser principal for <paramref name="systemUserId"/> (no linked contact grants are read).</summary>
    internal static WorkforcePrincipal SystemUserPrincipalFor(Guid systemUserId) => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = systemUserId,
        ContactId = ContactId,
        Oid = systemUserId.ToString("D"),
        TenantId = Tenant,
    };

    // ─────────────────────────────────────────────────────────────────────
    // #2121 round 1 (F2) — the store logs a non-success at Error by default (the enforcer); the composer, which logs every
    // outcome itself, asks it for Debug.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUser_DataverseFault_TheStoreLogsNoError_TheComposerLogsItsWarning()
    {
        // The REAL store, with only its transport stubbed to answer 500.
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var storeLog = new CapturingLogger<NoAccessEnforcementStore>();
        var store = RealStoreAnswering(System.Net.HttpStatusCode.InternalServerError, storeLog);
        var log = new CapturingLogger();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: store, logger: log);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(secure);
        storeLog.Entries.Should().Contain(e => e.Message.Contains("Dataverse read FAILED"),
            "control: the store did see the non-success");
        storeLog.Entries.Should().NotContain(e => e.Level >= LogLevel.Warning,
            "the composer's own Warning is the one entry for this fault; the store's Error would be a second, misleading one");
        log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("FAULTED") && e.Message.Contains("status: 500"));
    }

    /// <summary>The PRODUCTION store over a stub transport answering <paramref name="status"/> to every read.</summary>
    internal static NoAccessEnforcementStore RealStoreAnswering(
        System.Net.HttpStatusCode status, Microsoft.Extensions.Logging.ILogger<NoAccessEnforcementStore> logger)
        => new(
            new HttpClient(new StatusHandler(status)),
            new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new FixedTokenCredential(),
            logger);

    // ─────────────────────────────────────────────────────────────────────
    // #2121 round 1 (K1) — the batch budget, not the caller's token, bounds the Dataverse call.
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ComposeAsync_SystemUser_ARightsCallThatNeverCompletes_IsCancelledByTheBudget_TheIdDropped_TheCallerUntouched()
    {
        var secure = MemberRecordA;
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { secure }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[secure] = SecureOnly;
        var dataverse = NewDataverseStore();
        dataverse
            .Setup(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()))
            .Returns(async (Guid _, string _, Guid _, CancellationToken token, LogLevel _) =>
            {
                await Task.Delay(Timeout.Infinite, token); // completes only when THIS token is cancelled
                return AccessRights.Read;
            });
        var log = new CapturingLogger();
        using var caller = new CancellationTokenSource();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object, logger: log,
            rightsBudget: TimeSpan.FromMilliseconds(200));
        var compose = sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, caller.Token);
        try
        {
            var finished = await Task.WhenAny(compose, Task.Delay(TimeSpan.FromSeconds(10)));
            finished.Should().BeSameAs(compose, "the budget ends the call; the caller never cancels");

            var set = await compose;
            set.Rights.Should().NotContainKey(secure, "an unanswered id is dropped (fail closed)");
            caller.IsCancellationRequested.Should().BeFalse("the budget is its own token, linked to the caller's, never the caller's");
            log.Entries.Should().Contain(e => e.Level == LogLevel.Warning && e.Message.Contains("FAULTED")
                                              && e.Message.Contains(secure.ToString()) && e.Message.Contains("status: timeout"));
        }
        finally
        {
            caller.Cancel(); // releases the call if the budget token was NOT the one passed (the mutation this test kills)
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // #2121 round 1 (K6) — business-unit ownership is not ownership on a Secure record: a Secure id reached ONLY through
    // owningbusinessunit is confirmed against Dataverse and kept at exactly Read; the user's own and their team's
    // ownership are unchanged, and so is a non-Secure business-unit reach.
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A resolver that answers by LEG, as the real one does: the main walk (no identity-type narrowing) returns every id;
    /// a narrowed walk returns the user/team-owned ids when it asks for SystemUser or Team, and the business-unit-owned
    /// ids only when it asks for BusinessUnit.
    /// </summary>
    private static Mock<IMembershipResolverService> MembershipByLeg(
        string entityType, Guid[] lookupIds, Guid[] ownedIds, Guid[] businessUnitIds)
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, entityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, MembershipResolveOptions? options, CancellationToken _) =>
            {
                var types = options?.IdentityTypes;
                var ids = types is null
                    ? lookupIds.Concat(ownedIds).Concat(businessUnitIds)
                    : (types.Contains("SystemUser") || types.Contains("Team") ? ownedIds : Array.Empty<Guid>())
                        .Concat(types.Contains("BusinessUnit") ? businessUnitIds : Array.Empty<Guid>());
                return Response(entityType, ids.Distinct().ToArray());
            });
        return membership;
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRecordReachedOnlyThroughBusinessUnitOwnership_DataverseGrantsRead_IsKeptAtExactlyRead()
    {
        // Must fire: before K6 this id kept MembershipTermRights (Read|Write|Create…) with no role-depth check.
        var buOnly = MemberRecordA;
        var membership = MembershipByLeg(MatterEntity, Array.Empty<Guid>(), Array.Empty<Guid>(), new[] { buOnly });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[buOnly] = SecureOnly;
        var dataverse = DataverseAnswers(buOnly, AccessRights.Read | AccessRights.Write);

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(buOnly).Should().Be(AccessRights.Read, "a business-unit-only Secure reach is kept only as Dataverse-confirmed Read");
        dataverse.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", buOnly, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once, "it is confirmed against Dataverse, like a lookup-only reach");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_SecureRecordReachedOnlyThroughBusinessUnitOwnership_DataverseDenies_IsNotInTheSet()
    {
        // Must fire: the user's role depth does not reach the record, so Dataverse answers no rights → nothing.
        var buOnly = MemberRecordA;
        var membership = MembershipByLeg(MatterEntity, Array.Empty<Guid>(), Array.Empty<Guid>(), new[] { buOnly });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[buOnly] = SecureOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding(),
            principalRights: DataverseAnswers(buOnly, AccessRights.None).Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(buOnly, "business-unit ownership alone confers nothing on a Secure record Dataverse denies");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_DirectOrTeamOwnedSecureRecord_AndANonSecureBusinessUnitReach_AreUnchanged()
    {
        // Must not fire: the user's (or their team's) ownership is real ownership, even when the business-unit leg also
        // matches; a non-Secure business-unit reach is not this rule's concern. All keep MembershipTermRights, none is asked.
        var owned = MemberRecordA;
        var ownedAndBu = MemberRecordB;
        var openBuOnly = Guid.Parse("a0000000-0000-0000-0000-000000000003");
        var membership = MembershipByLeg(
            MatterEntity, Array.Empty<Guid>(), new[] { owned, ownedAndBu }, new[] { ownedAndBu, openBuOnly });
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[owned] = participations.Flags[ownedAndBu] = SecureOnly;
        var dataverse = DataverseGrantsNothing();

        var sut = CreateSut(membership.Object, participations, NeverStanding(), principalRights: dataverse.Object);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.RightsFor(owned).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        set.RightsFor(ownedAndBu).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        set.RightsFor(openBuOnly).Should().Be(AccessibleRecordSetService.MembershipTermRights);
        dataverse.Verify(r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Never, "ownership and non-Secure reaches are never asked about");
    }

    /// <summary>A credential that hands out a fixed token (the store's app-only token read).</summary>
    private sealed class FixedTokenCredential : Azure.Core.TokenCredential
    {
        public override Azure.Core.AccessToken GetToken(Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("test-token", DateTimeOffset.MaxValue);

        public override ValueTask<Azure.Core.AccessToken> GetTokenAsync(
            Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new Azure.Core.AccessToken("test-token", DateTimeOffset.MaxValue));
    }

    /// <summary>Answers every request with <paramref name="status"/> and no body.</summary>
    private sealed class StatusHandler(System.Net.HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    /// <summary>Answers every <c>RetrievePrincipalAccess</c> with <paramref name="rights"/>, recording each request path.</summary>
    private sealed class RightsHandler(System.Collections.Concurrent.ConcurrentQueue<string> requests, string rights) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            requests.Enqueue(Uri.UnescapeDataString(request.RequestUri!.AbsolutePath));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"AccessRights":"{{rights}}"}""", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class CapturingLogger : CapturingLogger<AccessibleRecordSetService>
    {
    }

    internal class CapturingLogger<T> : Microsoft.Extensions.Logging.ILogger<T>
    {
        public System.Collections.Concurrent.ConcurrentQueue<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    /// <summary>
    /// A filing world for the composition's ONE filing walk (task 174): each query is answered from <paramref name="rows"/>
    /// by its table and its primary-id condition (Equal or In), projected to the requested columns — the same shape
    /// <c>ExternalRootEventReadTests.FakeDataverse</c> uses.
    /// </summary>
    private static IGenericEntityService FilingWorld(params Microsoft.Xrm.Sdk.Entity[] rows)
    {
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression query, CancellationToken _) =>
            {
                var ids = query.Criteria.Conditions
                    .Where(c => string.Equals(c.AttributeName, query.EntityName + "id", StringComparison.OrdinalIgnoreCase))
                    .SelectMany(c => c.Values.Select(v => (Guid)v))
                    .ToHashSet();
                return new Microsoft.Xrm.Sdk.EntityCollection(rows
                    .Where(r => string.Equals(r.LogicalName, query.EntityName, StringComparison.OrdinalIgnoreCase) && ids.Contains(r.Id))
                    .Select(r =>
                    {
                        if (query.ColumnSet is null || query.ColumnSet.AllColumns) return r;
                        var projected = new Microsoft.Xrm.Sdk.Entity(r.LogicalName, r.Id);
                        foreach (var column in query.ColumnSet.Columns)
                        {
                            if (r.Attributes.TryGetValue(column, out var value)) projected[column] = value;
                        }
                        return projected;
                    })
                    .ToList());
            });
        return entities.Object;
    }

    private static Microsoft.Xrm.Sdk.Entity FiledWorkAssignment(Guid id, Guid matterId)
    {
        var row = new Microsoft.Xrm.Sdk.Entity(WorkAssignmentEntity, id);
        row["sprk_regardingmatter"] = new Microsoft.Xrm.Sdk.EntityReference(MatterEntity, matterId);
        return row;
    }

    private static Microsoft.Xrm.Sdk.Entity MatterRow(Guid id, bool isSecure)
    {
        var row = new Microsoft.Xrm.Sdk.Entity(MatterEntity, id);
        row["sprk_issecure"] = isSecure;
        row["sprk_accesspermission"] = new Microsoft.Xrm.Sdk.OptionSetValue(100000000);   // Standard
        row["sprk_mattername"] = $"matter {id}";
        return row;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Task 138 — Limited (sprk_accesspermission = Limited) reuses the FR-22 pre-max suppression
    // ─────────────────────────────────────────────────────────────────────

    private static RootRecordFlags LimitedOnly => new(IsSecure: false, IsRestricted: false, IsLimited: true);

    [Fact]
    public async Task ComposeAsync_LimitedRecord_OrgInheritedGrantConfersNothing_DirectGrantKeepsExactlyItsLevel()
    {
        // The same three shapes FR-22 pins for Secure, on a Limited, NON-secure record: an org-only grant
        // (absent), and a ViewOnly direct grant sitting under a Collaborate org grant (EXACTLY Read — the
        // ordering proof: suppression runs before the max, so the org term never enters it).
        var orgOnly = GrantedProject;
        var mixed = StandingMatter; // reused as a second project id

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = orgOnly, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
            new ExternalParticipation { ProjectId = mixed, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
        });
        participations.Flags[orgOnly] = LimitedOnly;
        participations.Flags[mixed] = LimitedOnly;

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(orgOnly, "Limited admits only named, direct grants — an org grant confers nothing");
        set.RightsFor(mixed).Should().Be(AccessRights.Read,
            "EXACTLY Read — the direct ViewOnly grant contributes its own level and the suppressed org Collaborate cannot outbid it");
    }

    [Fact]
    public async Task ComposeAsync_ContactStandingMembershipOnLimitedRecord_IsSuppressed()
    {
        var limited = MemberRecordA;
        var open = MemberRecordB;

        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, MatterEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, limited, open));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[limited] = LimitedOnly;

        var sut = CreateSut(membership.Object, participations, AlwaysStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(limited).Should().BeFalse("standing-grant membership is a derived term; Limited suppresses it exactly as Secure does");
        set.Contains(open).Should().BeTrue("control: the same term still reaches a Standard record");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OnALimitedRecord_KeepsItsMembership_ButNotItsContactsOrgInheritedGrant()
    {
        // Systemuser plane: the contact-grants term is suppressed by Limited (org-inherited → nothing); the
        // ADR-034 membership term is NOT — Limited governs which CONTACT grant types count, never internal access.
        var membershipRecord = MemberRecordA;

        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, membershipRecord));

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
            new ExternalParticipation { ProjectId = membershipRecord, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
        });
        participations.Flags[GrantedProject] = LimitedOnly;
        participations.Flags[membershipRecord] = LimitedOnly;

        var sut = CreateSut(membership.Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(GrantedProject,
            "the linked contact's org-inherited grant is suppressed on a Limited record, on the systemuser plane too");
        set.RightsFor(membershipRecord).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "the systemuser's own membership survives Limited untouched — Delete is absent, proving the suppressed " +
            "org FullAccess grant did not contribute");
    }

    [Fact]
    public async Task ComposeAsync_RecordBothSecureAndLimited_BehavesExactlyAsSecure()
    {
        // Criterion 3: Secure implies Limited for contacts, so the combination is the same predicate — not a
        // stronger or a weaker one.
        ExternalParticipation[] Grants() => new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
            new ExternalParticipation { ProjectId = StandingMatter, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = ExternalAccessLevel.ViewOnly },
        };

        var secure = new FakeParticipationService(Grants());
        secure.Flags[GrantedProject] = secure.Flags[StandingMatter] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var both = new FakeParticipationService(Grants());
        both.Flags[GrantedProject] = both.Flags[StandingMatter] = new RootRecordFlags(IsSecure: true, IsRestricted: false, IsLimited: true);

        var secureSet = await CreateSut(new Mock<IMembershipResolverService>().Object, secure, NeverStanding())
            .ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);
        var bothSet = await CreateSut(new Mock<IMembershipResolverService>().Object, both, NeverStanding())
            .ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        bothSet.Rights.Should().BeEquivalentTo(secureSet.Rights);
        bothSet.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights> { [StandingMatter] = AccessRights.Read });
    }

    [Fact]
    public async Task ComposeAsync_RecordRestrictedAndLimited_GivesTheContactNothing_EvenItsDirectGrant()
    {
        // Criterion 4: Restricted wins. Limited would keep the direct grant; Restricted removes every
        // contact-sourced contribution after the max, direct grants included.
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = ExternalAccessLevel.FullAccess },
        });
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: false, IsRestricted: true, IsLimited: true);

        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Rights.Should().NotContainKey(GrantedProject);
    }

    [Fact]
    public void Unreadable_IsTheMostRestrictiveCombination_AndCarriesTheUnreadableMarker()
    {
        // Criterion 5's read-path half: an unreadable row is Restricted AND direct-only (Secure, Limited), so
        // every contact-sourced contribution is suppressed and vetoed. The marker exists only for the WRITE-time
        // policy, which must not report a fault as "the record is Restricted".
        var unreadable = RootRecordFlags.Unreadable;

        unreadable.IsRestricted.Should().BeTrue();
        unreadable.IsDirectOnly.Should().BeTrue();
        unreadable.IsSecure.Should().BeTrue();
        unreadable.IsLimited.Should().BeTrue();
        unreadable.IsUnreadable.Should().BeTrue();
        RootRecordFlags.None.IsDirectOnly.Should().BeFalse();
        RootRecordFlags.None.IsUnreadable.Should().BeFalse();
    }

    [Fact]
    public async Task ComposeAsync_WhenFlagReadFaults_TheStandingTermIsSuppressedToo()
    {
        // Criterion 5 through ThrowingFlagParticipationService: the unreadable row is direct-only, so even the
        // DERIVED standing term — which Restricted alone would also remove — never contributes. Asserted on a
        // record the contact reaches ONLY through standing membership.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var sut = CreateSut(
            membership.Object,
            new ThrowingFlagParticipationService(Array.Empty<ExternalParticipation>()),
            AlwaysStanding());
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(MemberRecordA).Should().BeFalse("an unreadable flag row must not default the record to open (NFR-01)");
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
        set.Rights.Should().NotContainKey(GrantedProject, "task 136: absent from the answer, not present with no rights");
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
    public async Task ComposeAsync_SystemUser_OnASecureRecord_ALinkedContactEntryRemovesTheWholeRecord_MembershipIncluded()
    {
        // Task 143 (owner Q4 + N2): on a SECURE record the wall binds the internal user — the deny removal is
        // UNCONDITIONAL there, so even the systemuser's own ADR-034 membership (what Restricted would preserve) goes.
        // Before task 143 this held on every record; owner N3 narrowed it to secure records (next test).
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: true, IsRestricted: true);

        var reader = DenyingReader(deniedRecordIds: new[] { GrantedProject });
        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse(
            "on a secure record a linked-contact entry removes the record whole, membership included (Q4, N2)");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OnANonSecureRecord_ALinkedContactEntryRemovesOnlyTheContactsContribution()
    {
        // Task 143 (owner N3, both halves): on a NON-secure record the systemuser keeps its own term at that term's
        // rights — MDA admits it, and C9 says Teams/SPA agrees — while the linked contact's Full Access grant is vetoed.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject));

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation
            {
                ProjectId = GrantedProject,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
            new ExternalParticipation
            {
                ProjectId = StandingMatter,
                AccessLevel = ExternalAccessLevel.FullAccess,
                DirectAccessLevel = ExternalAccessLevel.FullAccess,
            },
        });
        participations.Flags[GrantedProject] = RootRecordFlags.None;
        participations.Flags[StandingMatter] = RootRecordFlags.None;

        var reader = DenyingReader(deniedRecordIds: new[] { GrantedProject, StandingMatter });
        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(MembershipOnlyRights(),
            "the systemuser keeps its own (membership) term on a non-secure record — the contact's Full Access is vetoed");
        set.Rights.Should().NotContainKey(StandingMatter,
            "a record reached ONLY through the linked contact's grant has nothing left once that grant is vetoed");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_ASystemUserSubjectEntry_RemovesASecureRecordButNeverANonSecureOne()
    {
        // Task 143 (owner Q4): a sprk_subjectsystemuser entry binds the internal user on SECURE records only. Driven
        // through the REAL reader (wire seam only), so the subject filter and the subject-kind provenance are real.
        var secure = GrantedProject;
        var open = MemberRecordA;
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, secure, open));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[secure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[open] = RootRecordFlags.None;

        var reader = new SubjectRowReader();
        reader.Add(new NoAccessEntryRow
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_subjectsystemuser_value = SystemUserId,
            _sprk_objectrecordtype_value = Guid.NewGuid(),
            sprk_objectrecordid = secure.ToString(),
        });
        reader.Add(new NoAccessEntryRow
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_subjectsystemuser_value = SystemUserId,
            _sprk_objectrecordtype_value = Guid.NewGuid(),
            sprk_objectrecordid = open.ToString(),
        });

        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(secure).Should().BeFalse("a systemuser-subject entry removes the secure record, membership term included");
        set.RightsFor(open).Should().Be(MembershipOnlyRights(),
            "a systemuser-subject entry never removes a non-secure record (Q4 scope)");
        reader.SubjectFilters.Should().Contain(f => f.Contains($"_sprk_subjectsystemuser_value eq {SystemUserId}"),
            "the systemuser was checked as a subject in its own right");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_WhenTheDenyReadFaults_RemovesEveryCandidate_SecureOrNot()
    {
        // Task 143 criterion 11: a veto-read fault removes the candidate — never "not walled", on any record.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject, MemberRecordA));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[MemberRecordA] = RootRecordFlags.None;

        var reader = new SubjectRowReader { Faults = true };
        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Rights.Should().BeEmpty("a faulted deny read removes every queried candidate (fail closed)");
    }

    // ── Task 143 r1: the systemuser-plane veto's fault branches and its subjects on a SECURE record ──────────

    [Fact]
    public async Task ComposeAsync_SystemUser_WhenTheLinkReadFaults_RemovesEverySecureCandidate_AndLeavesNonSecureOnes()
    {
        // Verifier finding 2 (ADR-003: "link unreadable → removed"). The normalizer that derives principal.ContactId
        // swallows a link-read fault into "no contact", so the principal arrives with NO contact. The veto must not
        // read that as "nothing to check" on a secure record: it reads the link itself, status-first, and a fault
        // removes the secure record whole — membership included. A non-secure record keeps its membership term: there
        // the wall removes only the derived contact's grants, and no contact was derived, so nothing was granted by one.
        var secure = GrantedProject;
        var open = MemberRecordA;
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, secure, open));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[secure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[open] = RootRecordFlags.None;

        var links = new Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InMemoryContactIdentityStore(); // user unreadable
        var sut = CreateSut(membership.Object, participations, NeverStanding(), NeverDeniesReader(), links);

        var set = await sut.ComposeAsync(SystemUserPrincipalWithoutContact(), ProjectEntity, CancellationToken.None);

        set.Contains(secure).Should().BeFalse("a secure record whose wall subjects could not be read is removed (fail closed)");
        set.RightsFor(open).Should().Be(MembershipOnlyRights(), "a non-secure record keeps the systemuser's own term");
        links.Reads.Should().Contain("systemuser", "the veto read the link itself rather than trusting 'no contact'");
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_OnASecureRecord_AnEntryNamingAContactBoundToTheUsersOid_RemovesIt()
    {
        // Verifier finding 3: the veto now asks the write-time guard's question — every contact that represents the
        // user (the primary link AND every contact bound to its oid), not only the one contact the normalizer derived.
        var secure = GrantedProject;
        var userOid = Guid.NewGuid();
        var bound = Guid.NewGuid();
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, secure));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[secure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var links = new Sprk.Bff.Api.Tests.AccessControl.IdentityBinding.InMemoryContactIdentityStore();
        links.AddSystemUser(SystemUserId, userOid, "user@firm.example"); // no primary contact
        links.AddContact(bound, oid: userOid.ToString());

        var reader = new SubjectRowReader();
        reader.Add(new NoAccessEntryRow
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_subjectcontact_value = bound,
            _sprk_objectrecordtype_value = Guid.NewGuid(),
            sprk_objectrecordid = secure.ToString(),
        });

        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader, links);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(secure).Should().BeFalse(
            "a contact bound to the user's oid represents them; its entry walls them off the secure record, as at write time");
        reader.SubjectFilters.Should().Contain(f => f.Contains($"_sprk_subjectcontact_value eq {bound}"));
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_ARecordWhoseFlagsAreUnreadable_IsTreatedAsSecure_AndRemovedWhole()
    {
        // Seed S2: "unreadable flags count as secure" — pinned on the veto's OWN rule, with the unreadable marker set
        // and the secure bit NOT set, so the test does not lean on the factory value (RootRecordFlags.Unreadable sets
        // IsSecure too). Read as non-secure, a linked-contact entry would remove only the contact's contribution and
        // the membership term would survive.
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, GrantedProject, MemberRecordA));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[GrantedProject] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsUnreadable: true);
        participations.Flags[MemberRecordA] = RootRecordFlags.Unreadable; // the production fault value

        var reader = DenyingReader(deniedRecordIds: new[] { GrantedProject, MemberRecordA });
        var sut = CreateSut(membership.Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse("unreadable flags are secure to the veto: the record goes whole");
        set.Contains(MemberRecordA).Should().BeFalse();
    }

    [Fact]
    public async Task ComposeAsync_SystemUser_WhenTheLinkedContactsOrganizationsAreUnreadable_RemovesEveryCandidate()
    {
        // Seed S9: an unreadable membership read removes every candidate — a NON-secure one included, so the test bites
        // on this branch alone (the secure path's own subject read would also catch a secure one).
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, ProjectEntity, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, MemberRecordA));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>()) { ThrowOnActiveOrgIds = true };
        participations.Flags[MemberRecordA] = RootRecordFlags.None;

        var sut = CreateSut(membership.Object, participations, NeverStanding());

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Rights.Should().BeEmpty("the wall's organization axis could not be read: every candidate is removed (fail closed)");
    }

    private static WorkforcePrincipal SystemUserPrincipalWithoutContact() => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = SystemUserId,
        ContactId = null, // the normalizer swallowed a link-read fault into "no contact"
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    /// <summary>The membership term's rights, named for what the assertions mean.</summary>
    private static AccessRights MembershipOnlyRights() => AccessibleRecordSetService.MembershipTermRights;

    /// <summary>
    /// The PRODUCTION <see cref="NoAccessListReader"/> with only its wire seam overridden (task 143): returns every
    /// added row whose populated subject appears in the query's subject filter, on the loop matching its object shape.
    /// </summary>
    private sealed class SubjectRowReader : NoAccessListReader
    {
        private readonly List<NoAccessEntryRow> _rows = new();

        public SubjectRowReader()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
        {
        }

        public bool Faults { get; set; }

        public List<string> SubjectFilters { get; } = new();

        public void Add(NoAccessEntryRow row) => _rows.Add(row);

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(string subjectFilter, string objectFilter, CancellationToken ct)
        {
            SubjectFilters.Add(subjectFilter);
            if (Faults)
            {
                return Task.FromResult<List<NoAccessEntryRow>?>(null);
            }

            var orgLoop = objectFilter.Contains("_sprk_objectorganization_value", StringComparison.Ordinal);
            var matching = _rows
                .Where(r => (r._sprk_subjectsystemuser_value is { } u && subjectFilter.Contains($"_sprk_subjectsystemuser_value eq {u}"))
                            || (r._sprk_subjectcontact_value is { } c && subjectFilter.Contains($"_sprk_subjectcontact_value eq {c}"))
                            || (r._sprk_subjectorganization_value is { } o && subjectFilter.Contains($"_sprk_subjectorganization_value eq {o}")))
                .Where(r => orgLoop ? r._sprk_objectorganization_value.HasValue : !r._sprk_objectorganization_value.HasValue)
                .ToList();
            return Task.FromResult<List<NoAccessEntryRow>?>(matching);
        }
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
        set.Rights.Should().NotContainKey(secureRecord, "task 136: absent from the answer, not present with no rights");
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

    /// <summary>
    /// Task 142 r5 (r4 verifier finding 5, criterion 19). Since r4 the veto answers a provable entry and an unverifiable
    /// candidate in two sets, and the read path removes their UNION. A batch holding ONE OF EACH is the only case in which
    /// that union's third branch runs — every other read-path test has a single-kind batch — so it is pinned here: the
    /// record an entry denies AND the record whose referenced organizations could not be read are both removed, and their
    /// twin (resolvable, no entry) is kept. Dropping either set from the union is fail OPEN on the read path: the record
    /// with an unverifiable wall, or the walled record, would be returned as accessible.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_ABatchWithAnEntryAndAnUnverifiableRecord_RemovesBoth_AndKeepsTheirTwin()
    {
        var walled = GrantedProject;       // an entry on the list names it: a provable denial
        var unresolvable = StandingMatter; // reused as a second project id (file convention): its wall cannot be evaluated
        var twin = MemberRecordA;          // resolvable, named by no entry: the control

        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = walled, AccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = unresolvable, AccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = twin, AccessLevel = ExternalAccessLevel.FullAccess },
        });
        participations.UnreadableOrgReferences.Add(unresolvable);

        // Record-keyed: denies ONLY the walled record. The unresolvable one is never sent to the reader at all (an
        // unreadable record is an unevaluated wall), so its removal can come from the unverifiable set alone.
        var reader = DenyingReader(deniedRecordIds: new[] { walled });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(walled).Should().BeFalse(
            "a provable entry removes the record even when the same batch also holds an unverifiable one");
        set.Contains(unresolvable).Should().BeFalse(
            "an unverifiable record is removed even when the same batch also holds a provable entry (fail closed)");
        set.RightsFor(twin).Should().Be(
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess),
            "the twin — resolvable, named by no entry — keeps exactly its Full Access grant");
    }

    /// <summary>
    /// Task 142 round 18 (R-14): a contact with more active organization memberships than ONE deny-list query holds (30;
    /// the bound is 25) is evaluated on the READ path too. Before round 18 the reader refused that set and failed closed,
    /// so EVERY candidate was removed — the contact saw nothing at all. Now only the record an entry walls off (via one of
    /// the 30 organizations) is removed; its sibling keeps its grant.
    /// </summary>
    [Fact]
    public async Task ComposeAsync_AContactWithMoreOrganizationsThanOneQueryHolds_LosesOnlyTheWalledRecord()
    {
        var walledOrganization = Guid.Parse("a4000000-0000-0000-0000-000000000142");
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.FullAccess },
            new ExternalParticipation { ProjectId = MemberRecordA, AccessLevel = ExternalAccessLevel.FullAccess },
        });
        for (var i = 0; i < 29; i++)
        {
            participations.ActiveOrgIds.Add(Guid.NewGuid());
        }

        participations.ActiveOrgIds.Add(walledOrganization);

        var reader = new SubjectRowReader();
        reader.Add(new NoAccessEntryRow
        {
            sprk_noaccessentryid = Guid.NewGuid(),
            _sprk_subjectorganization_value = walledOrganization,
            _sprk_objectrecordtype_value = Guid.NewGuid(),
            sprk_objectrecordid = GrantedProject.ToString(),
        });
        var sut = CreateSut(new Mock<IMembershipResolverService>().Object, participations, NeverStanding(), reader);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(GrantedProject).Should().BeFalse("an entry names one of the contact's 30 organizations on this record");
        set.RightsFor(MemberRecordA).Should().Be(
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess),
            "the sibling is evaluated and kept — no longer removed because the subject set was 'too large'");
        reader.SubjectFilters.Distinct().Should().HaveCountGreaterThan(1, "the 30 organizations were split across queries");
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
        // Task 143: the systemuser plane's three-subject overload. These canned entries are contact-subject entries,
        // so a denial is reported as CONTACT-sourced — what the systemuser plane splits on (owner N3).
        reader
            .Setup(r => r.GetDeniedRecordsAsync(
                It.IsAny<NoAccessSubjects>(),
                It.IsAny<IReadOnlyCollection<NoAccessCandidateRecord>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((NoAccessSubjects subjects, IReadOnlyCollection<NoAccessCandidateRecord> candidates, CancellationToken _) =>
            {
                var denied = subjects.ContactIds.Count == 0 && subjects.OrganizationIds.Count == 0
                    ? new HashSet<Guid>()
                    : candidates
                        .Where(c => recordIds.Contains(c.RecordId) || c.ReferencedOrganizationIds.Any(orgIds.Contains))
                        .Select(c => c.RecordId)
                        .ToHashSet();
                return new NoAccessListResult
                {
                    DeniedRecordIds = denied,
                    DenyingEntryIds = denied.ToDictionary(id => id, _ => (IReadOnlyList<Guid>)Array.Empty<Guid>()),
                    DenyingSubjectKinds = denied.ToDictionary(id => id, _ => NoAccessSubjectKinds.Contact),
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
        INoAccessListReader? noAccessList = null,
        IContactIdentityStore? identityStore = null,
        IGenericEntityService? filing = null,
        NoAccessEnforcementStore? principalRights = null,
        Microsoft.Extensions.Logging.ILogger<AccessibleRecordSetService>? logger = null,
        Sprk.Bff.Api.Services.Identity.ISystemUserIdentityResolver? systemUsers = null,
        Sprk.Bff.Api.Infrastructure.Cache.ITenantCache? rightsCache = null,
        TimeSpan? rightsBudget = null,
        Microsoft.AspNetCore.Http.IHttpContextAccessor? httpContextAccessor = null)
        => new(membership, participations, standing, noAccessList ?? NeverDeniesReader(),
               identityStore ?? UnlinkedIdentityStore(), systemUsers ?? InternalSystemUsers(),
               filing ?? Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
               logger ?? NullLogger<AccessibleRecordSetService>.Instance,
               principalRights ?? DataverseGrantsNothing().Object,
               rightsCache,
               httpContextAccessor)
        {
            SecureLookupRightsBudget = rightsBudget ?? AccessibleRecordSetService.DefaultSecureLookupRightsBudget,
        };

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
        set.Rights.Should().NotContainKey(StandingProject, "task 136: absent from the answer, not present with no rights");
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
        // nothing". That is decided by the junction read's filter + projection
        // (ExternalParticipationService.BuildOrganizationMembershipFilter / ProjectOrganizationMemberships,
        // task 109), which THIS DOUBLE REPLACES. Asserting it here would assert the fake, not the product,
        // so it is pinned where it lives — OrganizationMembershipReadTests — and deliberately not
        // restated here.
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
        set.Rights.Should().NotContainKey(OrgDerivedRecord, "task 136: absent from the answer, not present with no rights");
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
        set.Rights.Should().NotContainKey(GrantedProject, "task 136: absent from the answer, not present with no rights");
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
    public async Task ComposeAsync_OrgAndContactStandingBothActive_EachTermKeepsItsOwnBaseline()
    {
        // Filling a gap task 043's code-review gate identified, and one this task CREATED: the
        // task-042 test it replaced was the only place the contact's own standing grant and an active
        // organization were configured TOGETHER. Every other org test here sets the contact to NotHeld.
        //
        // This is where a level can be mis-credited: TWO derived terms, at TWO different baselines,
        // both Secure-suppressed, both feeding one AccumulateTerm. The record below is reachable by
        // both, so the union is asserted; the org-only record proves the org term did not simply
        // inherit the contact's (higher) level.
        //
        // Also the only consumer of ContactWalk, whose existence OrgWalkFor's doc relies on to claim
        // the two matchers are mutually exclusive.
        var shared = StandingProject;

        var membership = new Mock<IMembershipResolverService>();
        membership   // the contact's OWN standing walk — binds no organizations
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, ContactWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, shared));
        membership   // the org-expansion walk — bound to OrgA, org-typed descriptors only
            .Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, shared, OrgDerivedRecord));

        var standing = new Mock<ISubjectStandingGrantReader>();
        standing.Setup(s => s.ReadForContactAsync(ContactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.Collaborate));
        standing.Setup(s => s.ReadForOrganizationAsync(OrgA, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StandingGrantState(Held: true, Baseline: ExternalAccessLevel.ViewOnly));

        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.ActiveOrgIds.Add(OrgA);

        var sut = CreateSut(membership.Object, participations, standing.Object);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(shared).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create,
            "reached by BOTH terms, so the union of Collaborate (contact) and Read (org) — the max, not either alone");
        set.RightsFor(OrgDerivedRecord).Should().Be(AccessRights.Read,
            "reachable ONLY through the View Only organization: it must NOT inherit the contact's Collaborate level");
        set.Sources.StandingGrantMembership.Should().BeTrue();
        set.Sources.OrgExpansionMembership.Should().BeTrue("both derived terms ran, so both must be claimed");
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
    public async Task ComposeAsync_WhenTheJunctionEntryThrows_DeniesEveryCandidateAndDoesNotThrow()
    {
        // The TOKEN/API-URL acquisition fault — the one junction failure that reaches the composer as an
        // exception. Task 109 must not weaken it while making the QUERY-level fault report itself; the
        // query-level half is driven through a real transport in OrganizationMembershipReadTests.
        //
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

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 036 / FR-20 — ExternalAccess:ImpersonatedRootSets:Enabled (owner rounds 59 + 75)
    //
    // Flag on, Dataverse's own answer (the impersonated read, substituted here) REPLACES the ADR-034 membership walk
    // for the systemuser's ACCESS, at Read only; the contact-grants union, the deny list and the Restricted veto stay.
    // Every flag-on test pairs with a flag-off twin, so a test that would pass with the flag ignored cannot pass here.
    // The membership mock is STRICT on the flag-on side: consulting it at all fails the test (no fallback, no BU match).
    // ═════════════════════════════════════════════════════════════════════════════

    private static readonly Guid SharedRecord = Guid.Parse("d0360000-0000-4000-8000-000000000001");
    private static readonly Guid BusinessUnitOnlyRecord = Guid.Parse("d0360000-0000-4000-8000-000000000002");
    private static readonly Guid DefaultTeamOwnedRecord = Guid.Parse("d0360000-0000-4000-8000-000000000003");
    private static readonly Guid RoleDepthRecord = Guid.Parse("d0360000-0000-4000-8000-000000000004");
    private static readonly Guid RecordX = Guid.Parse("d0360000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordY = Guid.Parse("d0360000-0000-4000-8000-00000000000b");
    private static readonly Guid RecordZ = Guid.Parse("d0360000-0000-4000-8000-00000000000c");

    private static readonly AccessRights CollaborateRights =
        ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate);

    /// <summary>The impersonated read, substituted: one answer per entity type, or a fault. Records every call.</summary>
    private sealed class FakeImpersonatedRootSetSource : IImpersonatedRootSetSource
    {
        public Dictionary<string, RootIdSet> Answers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Exception? Fault { get; set; }
        public List<(Guid SystemUserId, string EntityType)> Calls { get; } = new();

        public Task<RootIdSet> GetAsync(Guid systemUserId, string entityType, CancellationToken ct = default)
        {
            Calls.Add((systemUserId, entityType));
            if (Fault is not null)
            {
                return Task.FromException<RootIdSet>(Fault);
            }

            return Task.FromResult(Answers.TryGetValue(entityType, out var answer) ? answer : RootIdSet.Empty);
        }

        public FakeImpersonatedRootSetSource Answer(string entityType, params Guid[] ids) =>
            AnswerWith(entityType, truncated: false, ids);

        public FakeImpersonatedRootSetSource AnswerWith(string entityType, bool truncated, params Guid[] ids)
        {
            Answers[entityType] = new RootIdSet(ids.ToHashSet(), truncated);
            return this;
        }
    }

    private static AccessibleRecordSetService CreateFr20Sut(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        IImpersonatedRootSetSource? source,
        bool enabled,
        NoAccessEnforcementStore? principalRights = null)
        => new(membership, participations, NeverStanding(), NeverDeniesReader(),
               UnlinkedIdentityStore(), InternalSystemUsers(),
               Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities(),
               NullLogger<AccessibleRecordSetService>.Instance,
               principalRights: principalRights,
               impersonatedRootSets: source,
               externalAccessOptions: Microsoft.Extensions.Options.Options.Create(new ExternalAccessOptions
               {
                   ImpersonatedRootSets = new ImpersonatedRootSetsOptions { Enabled = enabled },
               }));

    /// <summary>A membership resolver that answers <paramref name="ids"/> for <paramref name="entityType"/>.</summary>
    private static Mock<IMembershipResolverService> MembershipAnswering(string entityType, params Guid[] ids)
    {
        var membership = new Mock<IMembershipResolverService>();
        membership
            .Setup(m => m.ResolveAsync(SystemUserId, entityType, PagedOptions, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, ids));
        return membership;
    }

    /// <summary><see cref="SystemUserPrincipal"/> with the two fields these tests vary.</summary>
    private static WorkforcePrincipal SystemUserPrincipalWith(Guid? systemUserId, bool contactUnreadable = false) => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = systemUserId,
        ContactId = ContactId,
        ContactUnreadable = contactUnreadable,
        Oid = Oid.ToString("D"),
        TenantId = Tenant,
    };

    private static void AssertMembershipNeverConsulted(Mock<IMembershipResolverService> membership) =>
        membership.Verify(
            m => m.ResolveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "flag on, Dataverse's answer replaces the membership walk for access — consulting it is the fallback NFR-01 forbids");

    // ── Task 036 x #2121 (option b, #2122): the Secure lookup-only filter belongs to the flag-OFF path only ─────────
    // Flag off, a Secure id the membership walk reaches only through a lookup is kept only when the app-only
    // principal-rights read confirms Read, then at exactly Read. Flag on, Dataverse's own answer is the whole term: the
    // filter's ownership walk and its principal-rights question are never made, so it can neither drop what Dataverse
    // granted nor add what Dataverse did not.

    private static void AssertPrincipalRightsNeverAsked(Mock<NoAccessEnforcementStore> store) =>
        store.Verify(
            r => r.GetPrincipalRightsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Never,
            "flag on, Dataverse's answer already decides the set; the #2121 probe must not run a second time over it");

    [Fact(DisplayName = "Task 036 x #2121: flag ON, a Secure record in Dataverse's answer is kept at Read and the #2121 probe is never asked")]
    public async Task Fr20_FlagOn_SecureRecordInDataversesAnswer_KeptAtRead_NoPrincipalRightsProbe()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[SharedRecord] = SecureOnly;
        var store = DataverseGrantsNothing();
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled: true, principalRights: store.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights> { [SharedRecord] = AccessRights.Read },
            "Dataverse's own answer covers the Secure BU; the #2121 filter (which would drop it on a no-rights answer) is not re-applied");
        AssertPrincipalRightsNeverAsked(store);
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 x #2121: flag ON, a Secure record absent from Dataverse's answer is not added even when the principal-rights read says Read")]
    public async Task Fr20_FlagOn_SecureRecordAbsentFromDataversesAnswer_NotWidenedByPrincipalRights()
    {
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { MemberRecordA }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[MemberRecordA] = SecureOnly;
        var store = DataverseAnswers(MemberRecordA, AccessRights.Read);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled: true, principalRights: store.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(MemberRecordA).Should().BeFalse("flag on, only Dataverse's impersonated answer confers the systemuser term");
        AssertPrincipalRightsNeverAsked(store);
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 x #2121 twin: flag OFF, a Secure lookup-only id is kept at exactly Read when the principal-rights read confirms Read")]
    public async Task Fr20_FlagOff_SecureLookupOnlyId_ConfirmedByPrincipalRights_KeptAtRead()
    {
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { MemberRecordA }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[MemberRecordA] = SecureOnly;
        var store = DataverseAnswers(MemberRecordA, AccessRights.Read | AccessRights.Write);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled: false, principalRights: store.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights> { [MemberRecordA] = AccessRights.Read },
            "flag off, the #2121 filter applies: Dataverse confirmed Read, so the lookup-only Secure id is kept at Read only");
        store.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", MemberRecordA, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once);
        source.Calls.Should().BeEmpty("flag off, nothing reads the impersonated answer");
    }

    [Fact(DisplayName = "Task 036 x #2121 twin: flag OFF, a Secure lookup-only id is dropped when the principal-rights read gives no Read")]
    public async Task Fr20_FlagOff_SecureLookupOnlyId_NotConfirmed_IsDropped()
    {
        var membership = MembershipWith(MatterEntity, membershipIds: new[] { MemberRecordA }, ownershipIds: Array.Empty<Guid>());
        var participations = (FakeParticipationService)NoParticipations();
        participations.Flags[MemberRecordA] = SecureOnly;
        var store = DataverseGrantsNothing();
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, MemberRecordA);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled: false, principalRights: store.Object);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(MemberRecordA).Should().BeFalse("flag off, the #2121 filter drops an unconfirmed lookup-only Secure id");
        store.Verify(r => r.GetPrincipalRightsAsync(SystemUserId, "sprk_matters", MemberRecordA, It.IsAny<CancellationToken>(), It.IsAny<LogLevel>()),
            Times.Once);
    }

    [Fact(DisplayName = "Task 036: flag OFF (explicit) is the membership walk, unchanged, and never asks the impersonated source")]
    public async Task Fr20_FlagOff_IsTheMembershipWalk_AndNeverAsksTheSource()
    {
        var membership = MembershipAnswering(MatterEntity, MemberRecordA, BusinessUnitOnlyRecord);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: false);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights>
        {
            [MemberRecordA] = AccessibleRecordSetService.MembershipTermRights,
            [BusinessUnitOnlyRecord] = AccessibleRecordSetService.MembershipTermRights,
        });
        set.Sources.SystemUserMembership.Should().BeTrue();
        set.Sources.ImpersonatedRootSet.Should().BeFalse();
        set.CapLimit.Should().Be(MembershipResolveOptions.MaxLimit);
        source.Calls.Should().BeEmpty("flag off, nothing reads Dataverse's answer");
    }

    [Fact(DisplayName = "Task 036: flag ON, a record SHARED with the caller (absent from membership) appears at exactly Read")]
    public async Task Fr20_FlagOn_ASharedRecord_Appears_AtExactlyRead()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights> { [SharedRecord] = AccessRights.Read },
            "the impersonated set confers Read (round 59 item 1); writes are confirmed at the route");
        set.Sources.ImpersonatedRootSet.Should().BeTrue();
        set.Sources.SystemUserMembership.Should().BeFalse();
        source.Calls.Should().ContainSingle().Which.Should().Be((SystemUserId, MatterEntity));
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 twin: flag OFF, the same shared record is ABSENT (the under-grant FR-20 fixes)")]
    public async Task Fr20_FlagOff_ASharedRecord_IsAbsent()
    {
        var membership = MembershipAnswering(MatterEntity);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: false);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(SharedRecord).Should().BeFalse();
    }

    [Fact(DisplayName = "Task 036: flag ON, a record membership matches only by owningbusinessunit, absent from Dataverse's answer, is ABSENT")]
    public async Task Fr20_FlagOn_ABusinessUnitOnlyMatch_IsAbsent()
    {
        var membership = MembershipAnswering(ProjectEntity, BusinessUnitOnlyRecord);
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(BusinessUnitOnlyRecord).Should().BeFalse("the BU pattern match is not consulted for access flag-on");
        set.Contains(SharedRecord).Should().BeTrue();
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 twin: flag OFF, the BU-only match is PRESENT at the membership term (flag-off is unchanged — no under-grant)")]
    public async Task Fr20_FlagOff_ABusinessUnitOnlyMatch_IsPresent()
    {
        var membership = MembershipAnswering(ProjectEntity, BusinessUnitOnlyRecord);
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: false);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(BusinessUnitOnlyRecord).Should().Be(AccessibleRecordSetService.MembershipTermRights);
    }

    [Theory(DisplayName = "Task 036: a record owned by the caller's BU DEFAULT TEAM that Dataverse does not admit — absent flag ON, present flag OFF")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Fr20_ADefaultTeamOwnedRecordDataverseDoesNotAdmit_FollowsTheFlag(bool enabled, bool expectedPresent)
    {
        // The membership walk matches it through owningteam (the BU default team the caller belongs to); Dataverse's
        // answer, read as the caller, does not include it.
        var membership = MembershipAnswering(WorkAssignmentEntity, DefaultTeamOwnedRecord);
        var source = new FakeImpersonatedRootSetSource().Answer(WorkAssignmentEntity, RoleDepthRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), WorkAssignmentEntity, CancellationToken.None);

        set.Contains(DefaultTeamOwnedRecord).Should().Be(expectedPresent);
    }

    [Fact(DisplayName = "Task 036 (owner D1): flag ON, a record reached in Dataverse ONLY through BU/role depth appears at exactly Read")]
    public async Task Fr20_FlagOn_ARoleDepthRecord_Appears()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var source = new FakeImpersonatedRootSetSource().Answer(WorkAssignmentEntity, RoleDepthRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), WorkAssignmentEntity, CancellationToken.None);

        set.RightsFor(RoleDepthRecord).Should().Be(AccessRights.Read);
    }

    /// <summary>
    /// Task 036 fix round 3 (K3), for the #2121 merge. PR #2122 adds, on Term 1, a Secure lookup-only filter: a second,
    /// ownership-only membership walk, then an app-only Read confirmation for each Secure id ownership did not return. That
    /// filter belongs to the FLAG-OFF path only (<c>!dataverseDecides</c>): flag on, Term 1 is Dataverse's own answer, so
    /// there is no lookup to suppress. This test passes on the 036 branch today and fails if the merge runs the filter on
    /// the flag-on path — the strict mock throws on the second walk, and a composer with no confirmation store drops the
    /// Secure id.
    /// </summary>
    [Fact(DisplayName = "Task 036 K3 (#2121 merge): flag ON, a Secure id in Dataverse's answer is kept at Read — no Secure lookup-only filter, no membership walk")]
    public async Task Fr20_FlagOn_ASecureIdInDataversesAnswer_IsDecidedByTheImpersonatedAnswerAlone()
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var participations = new FakeParticipationService(Array.Empty<ExternalParticipation>());
        participations.Flags[SharedRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[RoleDepthRecord] = RootRecordFlags.None;
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, SharedRecord, RoleDepthRecord);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights>
        {
            [SharedRecord] = AccessRights.Read,
            [RoleDepthRecord] = AccessRights.Read,
        }, "flag on, Dataverse's impersonated answer decides the Secure record too; nothing re-asks or suppresses it");
        source.Calls.Should().ContainSingle().Which.Should().Be((SystemUserId, MatterEntity));
        AssertMembershipNeverConsulted(membership);
        membership.VerifyNoOtherCalls();
    }

    [Theory(DisplayName = "Task 036 (B-17): a systemuser who is also a granted contact keeps the grant in BOTH flag states")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fr20_TheContactGrantsUnion_SurvivesInBothStates(bool enabled)
    {
        var membership = enabled
            ? new Mock<IMembershipResolverService>(MockBehavior.Strict)
            : MembershipAnswering(ProjectEntity, MemberRecordA);
        var participations = new FakeParticipationService(new[]
        {
            new ExternalParticipation { ProjectId = GrantedProject, AccessLevel = ExternalAccessLevel.Collaborate },
        });
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(GrantedProject).Should().Be(CollaborateRights,
            "the linked contact's grant is a term of its own; the flag changes only the systemuser's own term");
        set.Sources.ContactGrants.Should().BeTrue();
        set.Contains(enabled ? SharedRecord : MemberRecordA).Should().BeTrue();
    }

    public static TheoryData<string, Exception> SourceFaults => new()
    {
        { "a non-2xx impersonated read", new HttpRequestException("403 prvActOnBehalfOfAnotherUser missing") },
        { "an unsupported entity type", new ArgumentException("entityType is not a root type") },
        { "an empty systemuserid refused by the primitive", new ArgumentException("systemUserId must not be Guid.Empty") },
    };

    [Theory(DisplayName = "Task 036 (NFR-01): flag ON, a source throw FAILS the composition — no fallback to membership")]
    [MemberData(nameof(SourceFaults))]
    public async Task Fr20_FlagOn_ASourceThrow_FailsTheComposition_AndNeverFallsBackToMembership(string _, Exception fault)
    {
        var membership = MembershipAnswering(ProjectEntity, MemberRecordA);
        var source = new FakeImpersonatedRootSetSource { Fault = fault };
        var sut = CreateFr20Sut(membership.Object, ParticipationsFor(GrantedProject), source, enabled: true);

        var act = () => sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fault,
            "the fault propagates, so the caller-principal filter refuses the request — nothing composes around it");
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 (NFR-01): flag ON, an EMPTY SystemUserId fails before anything is read")]
    public async Task Fr20_FlagOn_AnEmptySystemUserId_FailsBeforeAnyRead()
    {
        var membership = MembershipAnswering(ProjectEntity, MemberRecordA);
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);
        var principal = SystemUserPrincipalWith(systemUserId: Guid.Empty);

        var act = () => sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        source.Calls.Should().BeEmpty("Guid.Empty is never impersonated");
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 (NFR-01): flag ON, a MISSING SystemUserId fails — no fallback")]
    public async Task Fr20_FlagOn_AMissingSystemUserId_Fails()
    {
        var membership = MembershipAnswering(ProjectEntity, MemberRecordA);
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);
        var principal = SystemUserPrincipalWith(systemUserId: null);

        var act = () => sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        source.Calls.Should().BeEmpty();
        AssertMembershipNeverConsulted(membership);
    }

    [Fact(DisplayName = "Task 036 (NFR-01): flag ON with no source registered fails closed — never the membership walk")]
    public async Task Fr20_FlagOn_WithNoSource_FailsClosed()
    {
        var membership = MembershipAnswering(ProjectEntity, MemberRecordA);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source: null, enabled: true);

        var act = () => sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        AssertMembershipNeverConsulted(membership);
    }

    [Theory(DisplayName = "Task 036: a Restricted record keeps exactly the systemuser's own term and loses the contact grant")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fr20_ARestrictedRecord_KeepsTheOwnTermAsItsSurvivor_AndLosesTheContactGrant(bool enabled)
    {
        var membership = enabled
            ? new Mock<IMembershipResolverService>(MockBehavior.Strict)
            : MembershipAnswering(ProjectEntity, GrantedProject);
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
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, GrantedProject);
        var sut = CreateFr20Sut(membership.Object, participations, source, enabled);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        // Flag on, the survivor is the DATAVERSE term at Read — not MembershipTermRights (amendment 3 of 2026-09-30:
        // otherwise the constant creeps back through the Restricted path). Flag off, it is the membership term, as before.
        set.RightsFor(GrantedProject).Should().Be(
            enabled ? AccessRights.Read : AccessibleRecordSetService.MembershipTermRights,
            "the FullAccess contact grant is vetoed on a Restricted record; Delete absent proves it did not survive");
    }

    [Theory(DisplayName = "Task 036: the deny-veto pipeline stays in force — an unreadable veto subject removes every candidate, both states")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fr20_TheDenyVeto_StaysInForce(bool enabled)
    {
        var membership = enabled
            ? new Mock<IMembershipResolverService>(MockBehavior.Strict)
            : MembershipAnswering(ProjectEntity, SharedRecord);
        var source = new FakeImpersonatedRootSetSource().Answer(ProjectEntity, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled);
        var principal = SystemUserPrincipalWith(SystemUserId, contactUnreadable: true);

        var set = await sut.ComposeAsync(principal, ProjectEntity, CancellationToken.None);

        set.Rights.Should().BeEmpty("task 132: an unknown veto subject denies every candidate, whichever term found it");
    }

    [Theory(DisplayName = "Task 036 (no constant): flag ON, three records with three different Dataverse answers compose and confirm to exactly those answers")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fr20_FlagOn_NoConstant_ThreeRecordsThreeDataverseAnswers(bool holdsChildCreate)
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var source = new FakeImpersonatedRootSetSource().Answer(MatterEntity, RecordX, RecordY, RecordZ);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), MatterEntity, CancellationToken.None);

        // (1) The composition carries NO constant: every Dataverse-term record is exactly Read. Reintroducing
        //     MembershipTermRights for the Dataverse term turns each of these into Read|Write|Create.
        set.Rights.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights>
        {
            [RecordX] = AccessRights.Read,
            [RecordY] = AccessRights.Read,
            [RecordZ] = AccessRights.Read,
        });

        // (2) A write route's rights then equal Dataverse's own per-record answer — three different answers, three
        //     different results. A constant anywhere on the path makes at least two of them equal.
        var principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.Empty,
            SystemUserId = SystemUserId,
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = CallerPrincipal.FromReadBearing(set.Rights),
            DataverseConfirmsWrites = true,
        };
        var dataverse = new Dictionary<Guid, AccessRights>
        {
            [RecordX] = AccessRights.Read,
            [RecordY] = AccessRights.Read | AccessRights.Write,
            [RecordZ] = AccessRights.Read | AccessRights.Write | AccessRights.AppendTo | AccessRights.Delete,
        };
        Task<AccessRights?> Read(string entitySet, Guid id, CancellationToken _) => Task.FromResult<AccessRights?>(dataverse[id]);
        // A child create (here a to-do under the matter) also needs the child table's Create privilege (fix round 1, F1):
        // AppendTo on Z becomes Create only when the caller holds it; otherwise Z keeps exactly Read|Write|Delete.
        Task<bool?> Holds(string privilege, CancellationToken _) => Task.FromResult<bool?>(holdsChildCreate);

        var confirmed = new Dictionary<Guid, AccessRights>();
        foreach (var id in new[] { RecordX, RecordY, RecordZ })
        {
            confirmed[id] = (await Sprk.Bff.Api.Api.ExternalAccess.ExternalProjectDataEndpoints.WithDataverseRightsAsync(
                principal, principal.GetMatterRights(id), "sprk_matters", id, Read,
                NullLogger.Instance, CancellationToken.None,
                childCreatePrivilege: "prvCreatesprk_Todo", readCallerPrivilege: Holds)).Rights;
        }

        confirmed.Should().BeEquivalentTo(new Dictionary<Guid, AccessRights>
        {
            [RecordX] = AccessRights.Read,
            [RecordY] = AccessRights.Read | AccessRights.Write,
            [RecordZ] = holdsChildCreate
                ? AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete
                : AccessRights.Read | AccessRights.Write | AccessRights.Delete,
        });
    }

    [Theory(DisplayName = "Task 036 (NFR-03): flag ON, the source's Truncated becomes Capped at the source's row cap")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fr20_FlagOn_TruncatedBecomesCapped(bool truncated)
    {
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var source = new FakeImpersonatedRootSetSource().AnswerWith(ProjectEntity, truncated, SharedRecord);
        var sut = CreateFr20Sut(membership.Object, NoParticipations(), source, enabled: true);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Capped.Should().Be(truncated, "a truncated Dataverse answer is never presented as complete (NFR-03)");
        set.CapLimit.Should().Be(ImpersonatedRootSetSource.RowCap);
    }

    /// <summary>
    /// Thin test double overriding only the virtual grant loader (<see cref="ExternalParticipationService.GetGrantSetAsync"/>,
    /// task 028 — the base <c>GetParticipationsAsync</c> delegates to it) — avoids driving the real
    /// Dataverse OData path while composing on the real service surface (design §5 reuse). Carries an
    /// optional matter/work-assignment grant set for the polymorphic composition tests.
    /// </summary>
    private sealed class FakeParticipationService : ExternalParticipationService
    {
        private readonly ExternalGrantSet _grantSet;

        /// <summary>How many times a contact's grants were read (task 141's no-email-resolution proof).</summary>
        public int GrantSetReads { get; private set; }

        /// <param name="matters">Matter grant IDS, for tests that predate levels (task 032). Converted at
        /// Collaborate — the level a bare id effectively resolved to before levels were carried.</param>
        /// <param name="matterGrants">Matter grants WITH levels — use this for rights-fidelity tests.
        /// Takes precedence over <paramref name="matters"/>.</param>
        public FakeParticipationService(
            IReadOnlyList<ExternalParticipation> participations,
            IReadOnlySet<Guid>? matters = null, IReadOnlySet<Guid>? workAssignments = null,
            IReadOnlyList<ExternalRootGrant>? matterGrants = null,
            IReadOnlyList<ExternalRootGrant>? workAssignmentGrants = null)
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance,
                   filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
        {
            _grantSet = new ExternalGrantSet
            {
                Projects = participations,
                MatterGrants = matterGrants ?? RootGrants((matters ?? new HashSet<Guid>()).ToArray()),
                WorkAssignmentGrants = workAssignmentGrants ?? RootGrants((workAssignments ?? new HashSet<Guid>()).ToArray()),
            };
        }

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
        {
            GrantSetReads++;
            return Task.FromResult(_grantSet);
        }

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
        // `null!` here; without an override, ReadOrganizationMembershipsAsync's token acquisition throws
        // into AccessibleRecordSetService's fail-closed handling, which then denies EVERY candidate in the
        // composition — silently failing every pre-039 test that reaches the deny-veto call (i.e. almost
        // all of them, since a resolved contact id is enough to reach it). "References/belongs to
        // nothing" is the deny-veto's honest, inert default for a test that predates it.
        //
        // ActiveOrgIds are CURRENT memberships of ACTIVE organizations, so each one is in BOTH named sets
        // (task 109). Which rows fall into which set is the junction read's projection, pinned on the real
        // code in OrganizationMembershipReadTests — never restated by this double.
        public HashSet<Guid> ActiveOrgIds { get; } = new();
        public Dictionary<Guid, IReadOnlyCollection<Guid>> ReferencedOrgs { get; } = new();
        public HashSet<Guid> UnreadableOrgReferences { get; } = new();

        /// <summary>
        /// Makes the junction ENTRY throw — the token/API-url acquisition fault, the only failure that
        /// reaches the composer as an exception. The composer must map it to
        /// <c>ActiveOrgMemberships.Failed</c>, and that outcome drives TWO decisions in opposite
        /// directions: the additive org-expansion term contributes nothing, while the FR-23 deny veto
        /// denies every queried candidate. (A junction QUERY fault — HTTP 500/403/timeout — is reported by
        /// the real query as an outcome instead, task 109; that path is driven through a real transport in
        /// OrganizationMembershipReadTests, because a double here would assert the fake — the exact error
        /// that let ISS-019 ship green.)
        /// </summary>
        public bool ThrowOnActiveOrgIds { get; set; }

        // Task 137: the contact's live state. Active, so this double's grants compose exactly as before;
        // the inactive-contact guard itself is pinned by UnifiedEvaluatorSeamTests (task 137 section).
        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
            => ThrowOnActiveOrgIds
                ? Task.FromException<ActiveOrgMemberships>(
                    new InvalidOperationException("Dataverse:ServiceUrl is required"))
                : Task.FromResult(new ActiveOrgMemberships(ActiveOrgIds.ToList(), ActiveOrgIds.ToList(), Unreadable: false));

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
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance,
                   filing: Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.NoFilingEntities())
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
        // Task 137: the contact's live state. Active, so this double's grants compose exactly as before;
        // the inactive-contact guard itself is pinned by UnifiedEvaluatorSeamTests (task 137 section).
        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
            => Task.FromResult(ContactRecordState.Active);

        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(ActiveOrgMemberships.None);

        public override Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, ReferencedOrganizations>>(
                recordIds.Distinct().ToDictionary(id => id, _ => ReferencedOrganizations.None));
    }
}
