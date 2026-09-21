// unified-access-control-r2 Task 044 — Phases 1-2 closure: the UNIFIED EVALUATOR seam suite.
//
// ADR-038 §2 "vertical-slice-seam" KEEP path (tests/CLAUDE.md): individual tasks (032, 033, 036, 037,
// 039, 041, 042, 043) each carry unit tests that pin ONE term or ONE veto in isolation. This suite
// RESTATES their acceptance criteria as ONE composed contract at the seam — the max→veto pipeline
// across ALL terms — so a future refactor of any single term (e.g. Phase 3 child inheritance touching
// the max) cannot silently break the pipeline's overall shape while every per-term unit test stays
// green. It is also the Phase 3 characterization baseline: child-inheritance tasks extend the same
// world with child records.
//
// ── What "seam" means here, concretely ──────────────────────────────────────────────────────────────
// Unlike AccessibleRecordSetServiceTests.cs (which mocks ISubjectStandingGrantReader and
// INoAccessListReader directly), THIS suite composes PRODUCTION types one layer deeper:
//   • AccessibleRecordSetService — production, unmocked.
//   • SubjectStandingGrantReader — production, unmocked. Its own external boundary (IDataverseService)
//     is substituted, exactly as StandingGrantRuntimeUnionSeamTests.cs (task 051) established.
//   • NoAccessListReader — production, unmocked. Its own external boundary is its internal-virtual
//     QueryChunkAsync wire seam (the same seam NoAccessListReaderTests.cs, task 038, uses) — so the
//     REAL BuildSubjectFilter / BuildOrganizationObjectFilter / BuildRecordObjectFilter / CombineFilter /
//     ProcessRows / fail-closed orchestration all run unmocked here too.
// The remaining external boundaries doubled — IMembershipResolverService and ExternalParticipationService
// (grants / veto flags / active-org membership / referenced-org lookups) — are the SAME boundaries the
// precedent seam test (StandingGrantRuntimeUnionSeamTests.cs) already doubles; this suite generalizes
// that pattern across the FULL evaluator contract instead of one term.
//
// ── Scope note (per this task's own POML) ───────────────────────────────────────────────────────────
// FR-24's REGISTRY-COLUMN filtering itself (a registry-listed column confers; a bare sprk_assigned*
// prefix does not; an opposing-counsel reference never does) lives INSIDE MembershipResolverService's
// FetchXml-shape construction — a layer this seam substitutes away via IMembershipResolverService,
// exactly as the precedent seam does for the standing-grant flag. That behavior is pinned in
// MembershipResolverServiceTests.cs (task 041) and is NOT re-pinned here; see
// projects/unified-access-control-r2/notes/task-044-unified-evaluator-seam-suite.md for the full
// delegation list. This suite instead pins what the EVALUATOR does with the registry's answer (the
// composed rights, the suppression, the provenance) plus the ONE wiring fact that makes the registry
// meaningful at all: the composer requests the registry-filtered view (see FR24_* below).
//
// Impersonation (FR-20) is deliberately excluded — its contract lives in
// tests/integration/auth/ImpersonationNegativeCanaryTests.cs (task 034) against live Dataverse.

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.ExternalAccess;

public sealed class UnifiedEvaluatorSeamTests
{
    // ── the canonical world ─────────────────────────────────────────────────────────────────────────
    private const string ProjectEntity = "sprk_project";
    private const string MatterEntity = "sprk_matter";
    private const string WorkAssignmentEntity = "sprk_workassignment";

    private static readonly Guid SystemUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ContactId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Oid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private const string Tenant = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";

    private static readonly Guid OrgA = Guid.Parse("e0000000-0000-0000-0000-00000000000a");

    public static TheoryData<string> RootEntityTypes => new() { ProjectEntity, MatterEntity, WorkAssignmentEntity };

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 1 — Additive max: multi-term same-record scenarios resolve to the highest mapped
    // rights (grant vs standing vs org-derived), for each of the three root types.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task AdditiveMax_GrantStandingAndOrgExpansion_OnTheSameRecord_ComposeToTheHighestAcrossAllThreeTerms(
        string entityType)
    {
        var recordId = Guid.Parse("10000000-0000-0000-0000-000000000001");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, entityType, ContactWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, recordId));
        membership.Setup(m => m.ResolveByContactAsync(ContactId, entityType, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, recordId));

        var dataverse = BuildDataverse(
            contactHeld: true, contactBaseline: ExternalAccessLevel.Collaborate,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (true, ExternalAccessLevel.FullAccess) });

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(entityType, recordId, ExternalAccessLevel.ViewOnly));
        participations.ActiveOrgIds.Add(OrgA);

        var sut = BuildSut(dataverse, membership.Object, participations);

        var set = await sut.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);

        set.RightsFor(recordId).Should().Be(
            AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete,
            $"on {entityType} the record is reachable via grant (ViewOnly=Read), the contact's own " +
            "standing baseline (Collaborate=Read|Write|Create) and org expansion (FullAccess=RWCD) — " +
            "highest-wins union must resolve to the strongest of the three");
        set.Sources.ContactGrants.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeTrue();
        set.Sources.OrgExpansionMembership.Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 2 — FR-19: ViewOnly-grant-only caller has exactly Read; levels present for matters
    // and work assignments (the pre-FR-19 defect stamped Collaborate over every root type).
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task FR19_ViewOnlyGrantOnly_YieldsExactlyRead_LevelsAreCarriedForAllThreeRootTypes(string entityType)
    {
        var recordId = Guid.Parse("10000000-0000-0000-0000-000000000002");

        // Strict + zero setups: no standing grant, no active orgs, so the walk must never be attempted —
        // proves "grant-only" really means grant-only at the seam, not merely in this test's assertions.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var dataverse = BuildDataverse(contactHeld: false);

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(entityType, recordId, ExternalAccessLevel.ViewOnly));

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);

        set.RightsFor(recordId).Should().Be(AccessRights.Read,
            $"a ViewOnly-only grant on {entityType} must carry EXACTLY Read");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 3 — FR-21: FullAccess-granted contact on a Restricted record ⇒ None; dual-identity
    // systemuser keeps only non-contact-sourced rights there.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FR21_FullAccessGrantedContact_OnARestrictedRecord_IsDeniedEntirely()
    {
        var recordId = Guid.Parse("10000000-0000-0000-0000-000000000003");

        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var dataverse = BuildDataverse(contactHeld: false);

        var participations = new ParticipationWorld();
        participations.SetGrants(new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation
                {
                    ProjectId = recordId,
                    AccessLevel = ExternalAccessLevel.FullAccess,
                    DirectAccessLevel = ExternalAccessLevel.FullAccess,
                },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        });
        participations.Flags[recordId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(recordId).Should().Be(AccessRights.None);
        set.Contains(recordId).Should().BeFalse(
            "FR-21 denies EVERY contact principal regardless of grant strength — FullAccess included");
    }

    [Fact]
    public async Task FR21_DualIdentitySystemUser_OnARestrictedRecord_KeepsOnlyMembershipRights_LosesTheContactGrant()
    {
        var recordId = Guid.Parse("10000000-0000-0000-0000-000000000004");
        var openRecord = Guid.Parse("10000000-0000-0000-0000-000000000005");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, recordId, openRecord));

        var dataverse = BuildDataverse(contactHeld: false);
        var participations = new ParticipationWorld();
        participations.SetGrants(new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation
                {
                    ProjectId = recordId,
                    AccessLevel = ExternalAccessLevel.FullAccess,
                    DirectAccessLevel = ExternalAccessLevel.FullAccess,
                },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        });
        participations.Flags[recordId] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(recordId).Should().Be(
            AccessibleRecordSetService.MembershipTermRights,
            "the systemuser's own ADR-034 membership survives Restricted; the FullAccess contact grant " +
            "does not — note Delete is ABSENT, proving the grant's contribution did not survive");
        set.RightsFor(openRecord).Should().Be(
            AccessibleRecordSetService.MembershipTermRights, "an unrestricted record is untouched");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 4 — FR-22: secure record — standing-derived and org-derived contributions absent for
    // ContactOnly AND SystemUser principals; direct personal grant survives; suppressed-Collaborate-
    // cannot-outbid-surviving-ViewOnly ordering proof.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FR22_StandingDerivedContribution_IsAbsentOnASecureRecord_ForAContactPrincipal()
    {
        var secureRecord = Guid.Parse("10000000-0000-0000-0000-000000000006");
        var openRecord = Guid.Parse("10000000-0000-0000-0000-000000000007");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(
                ContactId, MatterEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(MatterEntity, secureRecord, openRecord));

        var dataverse = BuildDataverse(contactHeld: true, contactBaseline: ExternalAccessLevel.Collaborate);
        var participations = new ParticipationWorld();
        participations.Flags[secureRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None);

        set.Contains(secureRecord).Should().BeFalse(
            "standing-grant membership is a DERIVED-MEMBER term and is structurally suppressed on a secure record");
        set.Contains(openRecord).Should().BeTrue("the same term still reaches the non-secure record");
    }

    [Fact]
    public async Task FR22_OrgDerivedContribution_IsAbsentOnASecureRecord_ForAContactPrincipal()
    {
        var secureRecord = Guid.Parse("10000000-0000-0000-0000-000000000008");
        var openRecord = Guid.Parse("10000000-0000-0000-0000-000000000009");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, secureRecord, openRecord));

        var dataverse = BuildDataverse(
            contactHeld: false,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (true, ExternalAccessLevel.FullAccess) });

        var participations = new ParticipationWorld();
        participations.ActiveOrgIds.Add(OrgA);
        participations.Flags[secureRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(secureRecord).Should().BeFalse("a Full Access ORG standing grant confers nothing on a secure record");
        set.Contains(openRecord).Should().BeTrue("the same term still reaches the non-secure record");
    }

    [Fact]
    public async Task FR22_Type1SystemUser_GetsNoOrgInheritedAccessToASecureRecordViaTheLinkedContact_AndNeverConsultsStanding()
    {
        var secureRecord = Guid.Parse("1000000a-0000-0000-0000-000000000001");
        var membershipRecord = Guid.Parse("1000000a-0000-0000-0000-000000000002");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, membershipRecord));

        // Configured (flag held) but must NEVER be consulted on the systemuser plane — verified below.
        var dataverse = BuildDataverse(contactHeld: true, contactBaseline: ExternalAccessLevel.Collaborate);

        var participations = new ParticipationWorld();
        participations.SetGrants(new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation
                {
                    ProjectId = secureRecord,
                    AccessLevel = ExternalAccessLevel.FullAccess,
                    DirectAccessLevel = null, // reached ONLY through the linked contact's organization
                },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        });
        participations.Flags[secureRecord] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(secureRecord).Should().Be(AccessRights.None,
            "a Type 1 user must not derive access to a secure record via their linked contact's org-inherited grant");
        set.Contains(membershipRecord).Should().BeTrue("the user's own ADR-034 membership is untouched");

        dataverse.Verify(
            d => d.RetrieveAsync("contact", ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the systemuser plane must never consult the standing-grant flag, even though this contact holds one");
    }

    [Fact]
    public async Task FR22_ADirectPersonalGrant_SurvivesSecure_WhileAnOrgInheritedGrantOnTheSameShapeDoesNot()
    {
        var orgOnly = Guid.Parse("1000000b-0000-0000-0000-000000000001");
        var direct = Guid.Parse("1000000b-0000-0000-0000-000000000002");

        var participations = new ParticipationWorld();
        participations.SetGrants(new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation { ProjectId = orgOnly, AccessLevel = ExternalAccessLevel.FullAccess, DirectAccessLevel = null },
                new ExternalParticipation { ProjectId = direct, AccessLevel = ExternalAccessLevel.Collaborate, DirectAccessLevel = ExternalAccessLevel.Collaborate },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        });
        participations.Flags[orgOnly] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[direct] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(orgOnly).Should().Be(AccessRights.None, "a FullAccess ORG grant confers nothing on a secure record");
        set.RightsFor(direct).Should().Be(
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
            "a DIRECT personal grant survives Secure suppression (FR-22 survivor case)");
    }

    [Fact]
    public async Task FR22_SuppressedCollaborateOrgTerm_CannotOutbidTheSurvivingViewOnlyDirectGrant_OrderingProof()
    {
        var recordId = Guid.Parse("1000000c-0000-0000-0000-000000000001");

        var participations = new ParticipationWorld();
        participations.SetGrants(new ExternalGrantSet
        {
            Projects = new[]
            {
                new ExternalParticipation
                {
                    ProjectId = recordId,
                    AccessLevel = ExternalAccessLevel.Collaborate,     // the all-sources max
                    DirectAccessLevel = ExternalAccessLevel.ViewOnly,  // ...but only ViewOnly is the caller's own
                },
            },
            MatterGrants = Array.Empty<ExternalRootGrant>(),
            WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        });
        participations.Flags[recordId] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(recordId).Should().Be(AccessRights.Read,
            "EXACTLY Read — suppression runs BEFORE the additive max, so the suppressed Collaborate term " +
            "never enters it and cannot outbid the surviving ViewOnly grant");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 5 — FR-23: deny entries (all four key shapes) remove records after the max — FullAccess
    // + deny ⇒ None; deny keyed on a non-conferring org reference still denies; deactivated entry
    // denies nothing. Uses the REAL NoAccessListReader (production BuildSubjectFilter /
    // BuildOrganizationObjectFilter / BuildRecordObjectFilter / CombineFilter / ProcessRows), doubled
    // only at its internal-virtual QueryChunkAsync wire seam.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FR23_ContactSubjectRecordObjectDenyEntry_RemovesExactlyThatRecord_NeverResurrectedByTheMax()
    {
        var deniedRecord = Guid.Parse("20000000-0000-0000-0000-000000000001");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, deniedRecord, ExternalAccessLevel.FullAccess));

        var denyReader = new SeamNoAccessListReader();
        denyReader.AddEntry($"sprk_subjectcontact eq {ContactId}", RecordObjectRow(Guid.NewGuid(), deniedRecord));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(deniedRecord).Should().Be(AccessRights.None);
        set.Contains(deniedRecord).Should().BeFalse(
            "a contact-subject × record-object deny entry removes exactly that record — a FullAccess " +
            "grant plus a matching deny entry resolves to None, because the veto runs AFTER the max and " +
            "can therefore never be resurrected");
    }

    [Fact]
    public async Task FR23_ContactSubjectOrgObjectDenyEntry_DeniesARecordReferencingThatOrganization_EvenANonConferringReference()
    {
        // "Opposing counsel" shape: the org reference confers NOTHING via the additive/registry path
        // (FR-24) yet still denies via the deny-list's deliberate over-match (register B-10) — the
        // record-side match uses EVERY organization referenced, not narrowed to the conferring registry.
        var opposingCounselOrg = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var record = Guid.Parse("20000000-0000-0000-0000-000000000003");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));
        participations.ReferencedOrgs[record] = new[] { opposingCounselOrg };

        var denyReader = new SeamNoAccessListReader();
        denyReader.AddEntry($"sprk_subjectcontact eq {ContactId}", OrgObjectRow(Guid.NewGuid(), opposingCounselOrg));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeFalse(
            "a deny entry keyed on an organization the record references still denies it, regardless of " +
            "whether that same reference would ALSO confer access under a different (registry) rule");
    }

    [Fact]
    public async Task FR23_OrganizationSubjectRecordObjectDenyEntry_DeniesEveryActiveMemberOfThatOrganization()
    {
        var record = Guid.Parse("20000000-0000-0000-0000-000000000004");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));
        participations.ActiveOrgIds.Add(OrgA);

        var denyReader = new SeamNoAccessListReader();
        denyReader.AddEntry($"sprk_subjectorganization eq {OrgA}", RecordObjectRow(Guid.NewGuid(), record));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeFalse(
            "an organization-subject deny entry denies every contact who is an ACTIVE member of that " +
            "organization — proven end-to-end: the composer resolved and passed the contact's own active " +
            "org membership into the REAL query, whose subject filter the entry above requires");
    }

    [Fact]
    public async Task FR23_OrganizationSubjectOrgObjectDenyEntry_DeniesRecordsReferencingTheDeniedOrganization()
    {
        var deniedOrg = Guid.Parse("20000000-0000-0000-0000-000000000005");
        var record = Guid.Parse("20000000-0000-0000-0000-000000000006");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));
        participations.ReferencedOrgs[record] = new[] { deniedOrg };
        participations.ActiveOrgIds.Add(OrgA);

        var denyReader = new SeamNoAccessListReader();
        denyReader.AddEntry($"sprk_subjectorganization eq {OrgA}", OrgObjectRow(Guid.NewGuid(), deniedOrg));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeFalse(
            "the org × org shape denies every record the ACTIVE member's own organization references as a wall");
    }

    [Fact]
    public void FR23_TheDenyListQuery_AlwaysScopesToActiveEntries_ServerSide()
    {
        // The ACTIVE-only scoping (statecode eq 0) is enforced in the $filter itself, not by client
        // post-filtering — the pure builder is directly assertable via the internal-virtual test seam's
        // InternalsVisibleTo (mirrors BuildSubjectFilter_ContactAndOrganizations_OrJoinsBothDimensions
        // in NoAccessListReaderTests.cs, task 038's own unit coverage of this exact rule).
        var filter = NoAccessListReader.CombineFilter("subjectFragment", "objectFragment");

        filter.Should().Be("subjectFragment and objectFragment and statecode eq 0");
    }

    [Fact]
    public async Task FR23_WithADeactivatedOrNonexistentDenyEntry_TheGrantIsFullyHonoured()
    {
        // A deactivated entry is never RETURNED by the real server-side statecode-eq-0 query (proven
        // above) — so at this seam, "deactivated" and "configure the double with zero matching entries"
        // are the same observable case. This proves the composer does not spuriously deny in that case.
        var record = Guid.Parse("20000000-0000-0000-0000-000000000007");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));

        var denyReader = new SeamNoAccessListReader(); // zero entries configured

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(record).Should().Be(
            ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.FullAccess),
            "a deactivated (or nonexistent) deny entry denies nothing — the grant is fully honoured");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 6 — FR-24: opposing-counsel org reference confers nothing; registry-listed column
    // confers; non-registry sprk_assigned* column does not.
    //
    // The registry's own column-inclusion rules are resolver-internal (MembershipResolverService's
    // FetchXml-shape construction) — substituted away at this composer-level seam, per the scope note
    // at the top of this file. What IS pinned here is the wiring fact that makes the registry
    // meaningful: the composer must always ASK for the registry-filtered view on the systemuser plane
    // (ResolveByContactAsync already applies it unconditionally and needs no such proof). The
    // "opposing-counsel reference confers nothing via the additive path, but still denies" half of this
    // criterion is pinned above (FR23_ContactSubjectOrgObjectDenyEntry_*).
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FR24_SystemUserMembershipWalk_AlwaysRequestsTheAccessConferringOnlyFilter()
    {
        var record = Guid.Parse("30000000-0000-0000-0000-000000000001");

        // Strict + a matcher that ONLY accepts AccessConferringOnly=true: if the composer ever regressed
        // to the unfiltered (pre-FR-24) call shape, this setup would not match, the mock would throw, and
        // the test would fail loudly rather than silently pass on a stale assumption.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity,
                It.Is<MembershipResolveOptions?>(o => o != null && o.AccessConferringOnly),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, record));

        var dataverse = BuildDataverse(contactHeld: false);
        var participations = new ParticipationWorld();
        var sut = BuildSut(dataverse, membership.Object, participations);

        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeTrue(
            "reaching this point proves the composer always requests FR-24's registry-filtered view on " +
            "the systemuser plane — the registry's own per-column inclusion rules are pinned at the " +
            "FetchXml-shape level in MembershipResolverServiceTests.cs (see file header)");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 7 — FR-25: baseline View Only + standing ⇒ exactly Read; org baseline governs
    // org-derived contributions. Uses the REAL SubjectStandingGrantReader over a substituted
    // IDataverseService, so the reader's own field-read + baseline-mapping logic runs unmocked.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    public static TheoryData<ExternalAccessLevel, AccessRights> StandingBaselineCases => new()
    {
        { ExternalAccessLevel.ViewOnly, AccessRights.Read },
        { ExternalAccessLevel.Collaborate, AccessRights.Read | AccessRights.Write | AccessRights.Create },
        { ExternalAccessLevel.FullAccess, AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete },
    };

    [Theory]
    [MemberData(nameof(StandingBaselineCases))]
    public async Task FR25_ContactStandingBaseline_CarriesExactlyTheSubjectsRights_ThroughTheRealStandingGrantReader(
        ExternalAccessLevel baseline, AccessRights expected)
    {
        var record = Guid.Parse("40000000-0000-0000-0000-000000000001");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, ContactWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, record));

        var dataverse = BuildDataverse(contactHeld: true, contactBaseline: baseline);
        var participations = new ParticipationWorld();
        var sut = BuildSut(dataverse, membership.Object, participations);

        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(record).Should().Be(expected,
            "the REAL SubjectStandingGrantReader reads sprk_accesspermissiongrant and the composer must " +
            "carry that baseline verbatim, not a constant");
        set.Sources.StandingGrantMembership.Should().BeTrue();
    }

    [Fact]
    public async Task FR25_OrganizationBaseline_GovernsOrgDerivedContributions_ThroughTheRealStandingGrantReader()
    {
        var record = Guid.Parse("40000000-0000-0000-0000-000000000002");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, ProjectEntity, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, record));

        var dataverse = BuildDataverse(
            contactHeld: false,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (true, ExternalAccessLevel.ViewOnly) });

        var participations = new ParticipationWorld();
        participations.ActiveOrgIds.Add(OrgA);

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(record).Should().Be(AccessRights.Read,
            "View Only on the ORGANIZATION means exactly Read on every record derived through it");
        set.Sources.OrgExpansionMembership.Should().BeTrue();
        set.Sources.StandingGrantMembership.Should().BeFalse(
            "the contact holds no standing grant of their own — the org term must not borrow that provenance");
    }

    [Fact]
    public async Task FR25_StandingHeldWithNoBaseline_ContributesNothing_RecordIsAbsentNotPresentAtZeroRights()
    {
        var record = Guid.Parse("40000000-0000-0000-0000-000000000003");
        var grantedRecord = Guid.Parse("40000000-0000-0000-0000-000000000004");

        // Strict: the flag is Held but the baseline is UNSET, so per the owner's 2026-09-10 decision the
        // term contributes nothing and the membership walk must never be attempted (NFR-02).
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);

        var dataverse = BuildDataverse(contactHeld: true, contactBaseline: null);
        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, grantedRecord, ExternalAccessLevel.ViewOnly));

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RecordIds.Should().BeEquivalentTo(new[] { grantedRecord },
            "only the explicit grant survives — an unchosen level confers nothing");
        set.RightsFor(record).Should().Be(AccessRights.None);
        set.Sources.StandingGrantMembership.Should().BeFalse("provenance must not claim a term that contributed nothing");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Criterion 8 — fail-closed family: faulted flag read, faulted deny-list read, faulted junction
    // read each yield denial/no-widening as their owning task defined.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task FailClosed_FlagReadUnreadable_DeniesEveryContactSourcedContribution()
    {
        var record = Guid.Parse("50000000-0000-0000-0000-000000000001");

        var participations = new ParticipationWorld { FlagsUnreadable = true };
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeFalse(
            "an unreadable flag row resolves to Secure AND Restricted; Restricted removes every " +
            "contact-sourced contribution — treating unknown as open would grant access to a record " +
            "nobody could confirm is safe to share (NFR-01)");
    }

    [Fact]
    public async Task FailClosed_FlagReadUnreadable_SystemUsersOwnMembershipStillSurvives()
    {
        var record = Guid.Parse("50000000-0000-0000-0000-000000000002");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, record));

        var participations = new ParticipationWorld { FlagsUnreadable = true };
        var dataverse = BuildDataverse(contactHeld: false);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(record).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "fail-closed must not become fail-BROKEN: Restricted denies contacts, not the systemuser's " +
            "own Dataverse-governed membership");
    }

    [Fact]
    public async Task FailClosed_DenyListReadThrows_DeniesEveryQueriedCandidate_VetoNeverSkipped()
    {
        var record = Guid.Parse("50000000-0000-0000-0000-000000000003");

        var participations = new ParticipationWorld();
        participations.SetGrants(SingleGrant(ProjectEntity, record, ExternalAccessLevel.FullAccess));

        var denyReader = new SeamNoAccessListReader { Throws = true };

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Contains(record).Should().BeFalse(
            "a faulting deny-list read must deny every queried candidate — the REAL NoAccessListReader's " +
            "own try/catch turns the fault into a FailedClosed result; the veto is never skipped (NFR-01)");
    }

    [Fact]
    public async Task FailClosed_ActiveOrganizationJunctionReadThrows_OrgExpansionContributesNothing_AndTheDenyVetoDeniesEveryCandidate()
    {
        var grantedRecord = Guid.Parse("50000000-0000-0000-0000-000000000004");

        var participations = new ParticipationWorld { ThrowOnActiveOrgIds = true };
        participations.SetGrants(SingleGrant(ProjectEntity, grantedRecord, ExternalAccessLevel.FullAccess));

        var dataverse = BuildDataverse(contactHeld: false);
        // Strict: with the junction read failed, activeOrgs.OrganizationIds is empty, so the org-expansion
        // loop must never attempt a membership walk.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Sources.OrgExpansionMembership.Should().BeFalse(
            "a faulted junction read must contribute NOTHING to the additive org-expansion term");
        set.Contains(grantedRecord).Should().BeFalse(
            "the SAME faulted junction read must deny every queried candidate on the deny-veto axis — " +
            "the two consumers fail in opposite, deliberate directions (NFR-01)");
    }

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    private static AccessibleRecordSetService BuildSut(
        Mock<IDataverseService> dataverse,
        IMembershipResolverService membership,
        ParticipationWorld participations,
        NoAccessListReader? denyReader = null)
    {
        var standing = new SubjectStandingGrantReader(dataverse.Object, NullLogger<SubjectStandingGrantReader>.Instance);
        return new AccessibleRecordSetService(
            membership,
            participations,
            standing,
            denyReader ?? new SeamNoAccessListReader(),
            NullLogger<AccessibleRecordSetService>.Instance);
    }

    private static WorkforcePrincipal SystemUserPrincipal() => new()
    {
        Kind = WorkforcePrincipalKind.SystemUser,
        SystemUserId = SystemUserId,
        ContactId = ContactId, // derived contact — parallel workforce/contact access (§6.5 Path-B)
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

    /// <summary>A walk that binds NO organizations — the contact's own standing-grant membership.</summary>
    private static MembershipResolveOptions? ContactWalk =>
        It.Is<MembershipResolveOptions?>(o => o != null && o.OrganizationIds == null);

    /// <summary>A walk bound to <paramref name="orgId"/> — the org-expansion term. Mutually exclusive
    /// with <see cref="ContactWalk"/>. Mirrors AccessibleRecordSetServiceTests.cs's OrgWalkFor.</summary>
    private static MembershipResolveOptions? OrgWalkFor(Guid orgId) =>
        It.Is<MembershipResolveOptions?>(o =>
            o != null
            && o.OrganizationIds != null && o.OrganizationIds.Contains(orgId)
            && o.IdentityTypes != null
            && o.IdentityTypes.Contains("Organization", StringComparer.OrdinalIgnoreCase));

    private static ExternalGrantSet SingleGrant(
        string entityType, Guid recordId, ExternalAccessLevel level, ExternalAccessLevel? directLevel = null)
    {
        directLevel ??= level;
        return entityType switch
        {
            ProjectEntity => new ExternalGrantSet
            {
                Projects = new[] { new ExternalParticipation { ProjectId = recordId, AccessLevel = level, DirectAccessLevel = directLevel } },
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            },
            MatterEntity => new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                MatterGrants = new[] { new ExternalRootGrant { RecordId = recordId, AccessLevel = level, DirectAccessLevel = directLevel } },
                WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
            },
            WorkAssignmentEntity => new ExternalGrantSet
            {
                Projects = Array.Empty<ExternalParticipation>(),
                MatterGrants = Array.Empty<ExternalRootGrant>(),
                WorkAssignmentGrants = new[] { new ExternalRootGrant { RecordId = recordId, AccessLevel = level, DirectAccessLevel = directLevel } },
            },
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown root entity type."),
        };
    }

    private static Entity ContactEntityFixture(bool held, ExternalAccessLevel? baseline)
    {
        var entity = new Entity("contact", ContactId);
        entity[SubjectStandingGrantReader.StandingGrantAttribute] = held;
        if (baseline is not null)
        {
            entity[SubjectStandingGrantReader.BaselineAttribute] = new OptionSetValue((int)baseline.Value);
        }

        return entity;
    }

    private static Entity OrganizationEntityFixture(Guid orgId, bool held, ExternalAccessLevel? baseline)
    {
        var entity = new Entity("sprk_organization", orgId);
        entity[SubjectStandingGrantReader.StandingGrantAttribute] = held;
        if (baseline is not null)
        {
            entity[SubjectStandingGrantReader.BaselineAttribute] = new OptionSetValue((int)baseline.Value);
        }

        return entity;
    }

    private static Mock<IDataverseService> BuildDataverse(
        bool contactHeld,
        ExternalAccessLevel? contactBaseline = null,
        IReadOnlyDictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)>? orgs = null)
    {
        var mock = new Mock<IDataverseService>();
        mock.Setup(d => d.RetrieveAsync("contact", ContactId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContactEntityFixture(contactHeld, contactBaseline));

        foreach (var (orgId, state) in orgs ?? new Dictionary<Guid, (bool, ExternalAccessLevel?)>())
        {
            mock.Setup(d => d.RetrieveAsync("sprk_organization", orgId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OrganizationEntityFixture(orgId, state.Held, state.Baseline));
        }

        return mock;
    }

    private static NoAccessEntryRow RecordObjectRow(Guid entryId, Guid recordId) => new()
    {
        sprk_noaccessentryid = entryId,
        _sprk_objectrecordtype_value = Guid.NewGuid(), // populated — only its presence matters to shape detection
        sprk_objectrecordid = recordId.ToString(),
    };

    private static NoAccessEntryRow OrgObjectRow(Guid entryId, Guid objectOrgId) => new()
    {
        sprk_noaccessentryid = entryId,
        _sprk_objectorganization_value = objectOrgId,
    };

    /// <summary>
    /// A subclass of the PRODUCTION <see cref="ExternalParticipationService"/> overriding only its
    /// virtual data-read seam (grants / veto flags / active-org membership / referenced-org lookups) —
    /// the same boundary <c>FakeParticipationService</c> substitutes in
    /// AccessibleRecordSetServiceTests.cs and <c>StandingGrantRuntimeUnionSeamTests.cs</c>'s
    /// <c>FakeParticipationService</c>. The REAL composition, veto pipeline, and grant/rights mapping in
    /// <see cref="AccessibleRecordSetService"/> run unmocked against whatever this double returns.
    /// </summary>
    private sealed class ParticipationWorld : ExternalParticipationService
    {
        private ExternalGrantSet _grantSet = ExternalGrantSet.Empty;

        public Dictionary<Guid, RootRecordFlags> Flags { get; } = new();

        /// <summary>Mirrors ThrowingFlagParticipationService — every candidate resolves to
        /// <see cref="RootRecordFlags.Unreadable"/>, reproducing the real fail-closed contract without HTTP.</summary>
        public bool FlagsUnreadable { get; set; }

        public HashSet<Guid> ActiveOrgIds { get; } = new();
        public bool ThrowOnActiveOrgIds { get; set; }
        public Dictionary<Guid, IReadOnlyCollection<Guid>> ReferencedOrgs { get; } = new();
        public HashSet<Guid> UnreadableOrgReferences { get; } = new();
        public Guid? ResolveContactId { get; set; }

        public ParticipationWorld()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
        }

        public void SetGrants(ExternalGrantSet grantSet) => _grantSet = grantSet;

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_grantSet);

        public override Task<Guid?> ResolveExternalContactAsync(
            string? oid, string? email, CancellationToken ct = default)
            => Task.FromResult(ResolveContactId);

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds.Distinct().ToDictionary(
                id => id,
                id => FlagsUnreadable ? RootRecordFlags.Unreadable : (Flags.TryGetValue(id, out var f) ? f : RootRecordFlags.None));
            return Task.FromResult(result);
        }

        public override Task<IReadOnlyList<Guid>> QueryActiveOrgIdsAsync(Guid contactId, CancellationToken ct = default)
            => ThrowOnActiveOrgIds
                ? Task.FromException<IReadOnlyList<Guid>>(new InvalidOperationException("simulated sprk_contactorganization query failure"))
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
    /// A subclass of the PRODUCTION <see cref="NoAccessListReader"/> overriding only its
    /// internal-virtual <see cref="NoAccessListReader.QueryChunkAsync"/> wire seam — the SAME seam
    /// NoAccessListReaderTests.cs (task 038) uses for its own <c>FakeNoAccessListReader</c>. The REAL
    /// chunking, subject/object filter construction, row-shape matching, dedup, and fail-closed
    /// orchestration in <see cref="NoAccessListReader.GetDeniedRecordsAsync"/> all run unmocked.
    /// <para>
    /// Configured entries are gated on a REQUIRED SUBSTRING of the (server-side) subject filter the
    /// composer built, mirroring what a real Dataverse OData query would have already filtered before
    /// this class ever saw a row — so a contact-subject entry does not leak into an org-subject test and
    /// vice versa, without this double needing to re-implement <c>BuildSubjectFilter</c> itself.
    /// </para>
    /// </summary>
    private sealed class SeamNoAccessListReader : NoAccessListReader
    {
        private sealed record ActiveEntry(string RequiredSubjectSubstring, NoAccessEntryRow Row);

        private readonly List<ActiveEntry> _entries = new();

        public bool Throws { get; set; }

        public SeamNoAccessListReader()
            : base(new HttpClient(), configuration: null!, credential: null!, logger: NullLogger<NoAccessListReader>.Instance)
        {
        }

        public void AddEntry(string requiredSubjectSubstring, NoAccessEntryRow row)
            => _entries.Add(new ActiveEntry(requiredSubjectSubstring, row));

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(
            string subjectFilter, string objectFilter, CancellationToken ct)
        {
            if (Throws)
            {
                throw new InvalidOperationException("simulated sprk_noaccessentry query outage");
            }

            var isOrgLoop = objectFilter.Contains("sprk_objectorganization", StringComparison.Ordinal);
            var matching = _entries
                .Where(e => subjectFilter.Contains(e.RequiredSubjectSubstring, StringComparison.Ordinal))
                .Select(e => e.Row)
                .Where(r => isOrgLoop
                    ? r._sprk_objectorganization_value.HasValue
                    : !r._sprk_objectorganization_value.HasValue)
                .ToList();

            return Task.FromResult<List<NoAccessEntryRow>?>(matching);
        }
    }
}
