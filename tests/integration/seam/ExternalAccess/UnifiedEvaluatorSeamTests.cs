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

using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.AccessControl.IdentityBinding;
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
        set.Rights.Should().NotContainKey(recordId, "task 136: absent from the answer, not present with no rights");
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
        // Task 136 (C2): None alone is also what a present-but-powerless key reads as. Absence is the claim.
        set.Contains(secureRecord).Should().BeFalse("no Read, so not in the set");
        set.Rights.Should().NotContainKey(secureRecord, "absent from the answer, not present with no rights");
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
        set.Contains(orgOnly).Should().BeFalse("task 136 (C2): no Read, so not in the set");
        set.Rights.Should().NotContainKey(orgOnly, "absent from the answer, not present with no rights");
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
        denyReader.AddEntry($"_sprk_subjectcontact_value eq {ContactId}", RecordObjectRow(Guid.NewGuid(), deniedRecord));

        var dataverse = BuildDataverse(contactHeld: false);
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations, denyReader);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(deniedRecord).Should().Be(AccessRights.None);
        set.Rights.Should().NotContainKey(deniedRecord, "task 136: absent from the answer, not present with no rights");
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
        denyReader.AddEntry($"_sprk_subjectcontact_value eq {ContactId}", OrgObjectRow(Guid.NewGuid(), opposingCounselOrg));

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
        denyReader.AddEntry($"_sprk_subjectorganization_value eq {OrgA}", RecordObjectRow(Guid.NewGuid(), record));

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
        denyReader.AddEntry($"_sprk_subjectorganization_value eq {OrgA}", OrgObjectRow(Guid.NewGuid(), deniedOrg));

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
        set.Rights.Should().NotContainKey(record, "task 136: absent from the answer, not present with no rights");
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
        // Strict: with the junction read failed, activeOrgs.ConferringOrganizationIds is empty, so the
        // org-expansion loop must never attempt a membership walk. (This double throws — the token/API-url
        // fault. The QUERY-level fault, which the real read now reports as an outcome, is driven through a
        // real transport in OrganizationMembershipReadTests, task 109.)
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);

        set.Sources.OrgExpansionMembership.Should().BeFalse(
            "a faulted junction read must contribute NOTHING to the additive org-expansion term");
        set.Contains(grantedRecord).Should().BeFalse(
            "the SAME faulted junction read must deny every queried candidate on the deny-veto axis — " +
            "the two consumers fail in opposite, deliberate directions (NFR-01)");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Task 135 (defect C1) — the CIAM plane. Every test below drives CiamContactPrincipalStrategy, the
    // external SPA's real entry, over this suite's REAL evaluator, standing-grant reader and No Access
    // reader, and asserts the CallerPrincipal it produces — the object every /api/v1/external handler
    // authorizes on. Before task 135 that strategy built the principal from the grant rows alone, so none
    // of FR-21/22/23 reached a ciamlogin.com token.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Ciam_FR21_DirectFullAccessGrantOnARestrictedRoot_IsAbsentFromTheCiamPrincipal(string entityType)
    {
        var restricted = RootId(entityType, 1);
        var open = RootId(entityType, 2);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(entityType, Direct(restricted, ExternalAccessLevel.FullAccess), Direct(open, ExternalAccessLevel.FullAccess)));
        participations.Flags[restricted] = new RootRecordFlags(IsSecure: false, IsRestricted: true);

        var principal = await ResolveCiamAsync(GrantOnlySut(participations));
        var scope = ScopeOf(principal, entityType);

        scope.Should().NotContainKey(restricted,
            $"FR-21 on CIAM: a Restricted {entityType} admits NO contact-based access, an explicit FullAccess grant included");
        scope.Should().ContainKey(open).WhoseValue.Should().Be(Rights(ExternalAccessLevel.FullAccess),
            "control: the same grant on an unrestricted record is untouched");
    }

    [Theory]
    [InlineData("contact x record")]
    [InlineData("contact x organization")]
    [InlineData("organization x record")]
    [InlineData("organization x organization")]
    public async Task Ciam_FR23_NoAccessEntryOfEachKeyShape_RemovesTheFullAccessRecordFromTheCiamPrincipal(string shape)
    {
        var denied = RootId(ProjectEntity, 11);
        var open = RootId(ProjectEntity, 12);
        var referencedOrg = Guid.Parse("20000000-0000-0000-0000-0000000000f1");

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(denied, ExternalAccessLevel.FullAccess), Direct(open, ExternalAccessLevel.FullAccess)));
        participations.ActiveOrgIds.Add(OrgA);
        participations.ReferencedOrgs[denied] = new[] { referencedOrg };

        var denyReader = new SeamNoAccessListReader();
        var (subject, row) = shape switch
        {
            "contact x record" => ($"_sprk_subjectcontact_value eq {ContactId}", RecordObjectRow(Guid.NewGuid(), denied)),
            "contact x organization" => ($"_sprk_subjectcontact_value eq {ContactId}", OrgObjectRow(Guid.NewGuid(), referencedOrg)),
            "organization x record" => ($"_sprk_subjectorganization_value eq {OrgA}", RecordObjectRow(Guid.NewGuid(), denied)),
            "organization x organization" => ($"_sprk_subjectorganization_value eq {OrgA}", OrgObjectRow(Guid.NewGuid(), referencedOrg)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unknown key shape."),
        };
        denyReader.AddEntry(subject, row);

        var principal = await ResolveCiamAsync(GrantOnlySut(participations, denyReader));

        principal.HasProjectAccess(denied).Should().BeFalse(
            $"FR-23 on CIAM: a {shape} No Access entry vetoes the record after the max, FullAccess grant included");
        principal.GetEffectiveRights(open).Should().Be(Rights(ExternalAccessLevel.FullAccess),
            "control: the entry names only the denied record (or an organization only it references)");
    }

    [Fact]
    public async Task Ciam_FR23_AContactByRecordEntryNamingOneContact_LeavesAnotherContactOfTheSameOrganizationUnaffected()
    {
        // Task 039's amended negative control: the veto is targeted, not a blanket deny of the organization.
        var record = RootId(ProjectEntity, 13);
        var colleague = Guid.Parse("44444444-0000-0000-0000-0000000000c2");

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(record, ExternalAccessLevel.FullAccess)));
        participations.ActiveOrgIds.Add(OrgA); // both contacts belong to OrgA

        var denyReader = new SeamNoAccessListReader();
        denyReader.AddEntry($"_sprk_subjectcontact_value eq {ContactId}", RecordObjectRow(Guid.NewGuid(), record));
        var evaluator = GrantOnlySut(participations, denyReader);

        var named = await ResolveCiamAsync(evaluator);
        named.HasProjectAccess(record).Should().BeFalse("precondition: the entry denies the contact it names");

        var strategy = CiamStrategy(evaluator, colleague);
        var resolution = await strategy.ResolveAsync(CiamRequest(), CancellationToken.None);

        resolution.Principal!.ContactId.Should().Be(colleague);
        resolution.Principal.GetEffectiveRights(record).Should().Be(Rights(ExternalAccessLevel.FullAccess),
            "a contact-by-record entry binds the contact it names, not every member of that contact's organization");
    }

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Ciam_FR22_OnASecureRoot_AnOrgOnlyGrantConfersNothing_AndDirectViewOnlyPlusOrgCollaborateIsExactlyRead(
        string entityType)
    {
        var orgOnly = RootId(entityType, 21);
        var mixed = RootId(entityType, 22);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType,
            OrgOnly(orgOnly, ExternalAccessLevel.FullAccess),
            new GrantRow(mixed, ExternalAccessLevel.Collaborate, ExternalAccessLevel.ViewOnly)));
        participations.Flags[orgOnly] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        participations.Flags[mixed] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var principal = await ResolveCiamAsync(GrantOnlySut(participations));
        var scope = ScopeOf(principal, entityType);

        RightsIn(scope, orgOnly).Should().Be(AccessRights.None,
            "FR-22 on CIAM: on a Secure root an organization-inherited grant confers nothing");
        scope.Should().NotContainKey(orgOnly,
            "task 136 (C2): the record is ABSENT from the CIAM principal — before 136 a None-rights key remained " +
            "here and every presence-gated read admitted it");
        RightsIn(scope, mixed).Should().Be(AccessRights.Read,
            "EXACTLY Read — suppression runs before the max, so the org Collaborate never enters it");
    }

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Ciam_FR22_ADirectCollaborateGrantOnASecureRoot_ConfersCollaborate(string entityType)
    {
        var secure = RootId(entityType, 23);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(entityType, Direct(secure, ExternalAccessLevel.Collaborate)));
        participations.Flags[secure] = new RootRecordFlags(IsSecure: true, IsRestricted: false);

        var principal = await ResolveCiamAsync(GrantOnlySut(participations));

        RightsIn(ScopeOf(principal, entityType), secure).Should().Be(Rights(ExternalAccessLevel.Collaborate),
            "a named DIRECT grant is the one thing Secure keeps for a contact (owner round 2 item 3)");
    }

    [Fact]
    public async Task Ciam_OwnerModel_StandingGrantAndOrgExpansionContributeNothing_WhileTheWorkforceContactPlaneKeepsThem()
    {
        var granted = RootId(ProjectEntity, 31);
        var viaStanding = RootId(ProjectEntity, 32);
        var viaOrganization = RootId(ProjectEntity, 33);

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, It.IsAny<string>(), ContactWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string entity, MembershipResolveOptions? _, CancellationToken _) =>
                entity == ProjectEntity ? Response(entity, viaStanding) : Response(entity));
        membership.Setup(m => m.ResolveByContactAsync(ContactId, It.IsAny<string>(), OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string entity, MembershipResolveOptions? _, CancellationToken _) =>
                entity == ProjectEntity ? Response(entity, viaOrganization) : Response(entity));

        // The contact AND its organization both hold a Full Access standing grant.
        var dataverse = BuildDataverse(
            contactHeld: true, contactBaseline: ExternalAccessLevel.FullAccess,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (true, ExternalAccessLevel.FullAccess) });

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(granted, ExternalAccessLevel.ViewOnly)));
        participations.ActiveOrgIds.Add(OrgA);

        var evaluator = BuildSut(dataverse, membership.Object, participations);

        var ciam = await ResolveCiamAsync(evaluator);

        ciam.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { granted },
            "owner model (C9): a CIAM contact gets ONLY what it is granted — no standing-grant or " +
            "organization-expansion membership");
        ciam.MatterAccess.Should().BeEmpty();
        ciam.WorkAssignmentAccess.Should().BeEmpty();
        membership.Verify(
            m => m.ResolveByContactAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()),
            Times.Never, "no derived-member walk runs on the CIAM plane");
        dataverse.Verify(d => d.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "neither the contact's nor the organization's standing grant is even read on the CIAM plane");

        // The one plane difference, owner-signed (A2, round 3): the SAME world on the workforce contact plane
        // still derives both. This is what proves the world above COULD have widened CIAM.
        var workforce = await ResolveWorkforceContactAsync(evaluator);
        workforce.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { granted, viaStanding, viaOrganization },
            "owner A2: standing grants and organization access stay, as they work today, on the workforce sign-in");
    }

    public static TheoryData<string> PlaneParityScenarios => new()
    {
        "organization grant on an open root",
        "restricted",
        "secure",
        "limited",
        "no access entry",
    };

    [Theory]
    [MemberData(nameof(PlaneParityScenarios))]
    public async Task PlaneParity_SameGrantsFlagsAndNoAccessEntries_CiamAndWorkforceContactPrincipalsCarryIdenticalRights(
        string scenario)
    {
        var participations = new ParticipationWorld();
        var denyReader = new SeamNoAccessListReader();

        // The contact belongs to OrgA, but OrgA (and the contact) hold NO standing grant — so there is no
        // derived-member data, and the workforce plane's derived terms have nothing to contribute.
        participations.ActiveOrgIds.Add(OrgA);

        var rows = new List<(string EntityType, GrantRow Row)>();
        var expected = new Dictionary<string, Dictionary<Guid, AccessRights>>();
        foreach (var entityType in new[] { ProjectEntity, MatterEntity, WorkAssignmentEntity })
        {
            var first = RootId(entityType, 41);
            var second = RootId(entityType, 42);
            var answer = new Dictionary<Guid, AccessRights>();

            switch (scenario)
            {
                case "organization grant on an open root":
                    rows.Add((entityType, OrgOnly(first, ExternalAccessLevel.Collaborate)));
                    rows.Add((entityType, new GrantRow(second, ExternalAccessLevel.Collaborate, ExternalAccessLevel.ViewOnly)));
                    answer[first] = Rights(ExternalAccessLevel.Collaborate);  // org access stays (A2) off Secure
                    answer[second] = Rights(ExternalAccessLevel.Collaborate); // all-sources level off Secure
                    break;
                case "restricted":
                    rows.Add((entityType, Direct(first, ExternalAccessLevel.FullAccess)));
                    rows.Add((entityType, Direct(second, ExternalAccessLevel.Collaborate)));
                    participations.Flags[first] = new RootRecordFlags(IsSecure: false, IsRestricted: true);
                    answer[second] = Rights(ExternalAccessLevel.Collaborate);
                    break;
                case "secure":
                    rows.Add((entityType, OrgOnly(first, ExternalAccessLevel.FullAccess)));
                    rows.Add((entityType, new GrantRow(second, ExternalAccessLevel.Collaborate, ExternalAccessLevel.ViewOnly)));
                    participations.Flags[first] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
                    participations.Flags[second] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
                    answer[second] = AccessRights.Read;
                    break;
                case "limited":
                    // Task 138: the same shape as "secure", on a Limited non-secure root — one predicate.
                    rows.Add((entityType, OrgOnly(first, ExternalAccessLevel.FullAccess)));
                    rows.Add((entityType, new GrantRow(second, ExternalAccessLevel.Collaborate, ExternalAccessLevel.ViewOnly)));
                    participations.Flags[first] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true);
                    participations.Flags[second] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsLimited: true);
                    answer[second] = AccessRights.Read;
                    break;
                case "no access entry":
                    rows.Add((entityType, Direct(first, ExternalAccessLevel.FullAccess)));
                    rows.Add((entityType, Direct(second, ExternalAccessLevel.Collaborate)));
                    denyReader.AddEntry($"_sprk_subjectorganization_value eq {OrgA}", RecordObjectRow(Guid.NewGuid(), first));
                    answer[second] = Rights(ExternalAccessLevel.Collaborate);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario.");
            }

            expected[entityType] = answer;
        }

        participations.SetGrants(GrantsAcross(rows));

        // Strict + no setups: with no derived-member data, neither plane may walk membership.
        var membership = new Mock<IMembershipResolverService>(MockBehavior.Strict);
        var dataverse = BuildDataverse(
            contactHeld: false,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (false, null) });
        var evaluator = BuildSut(dataverse, membership.Object, participations, denyReader);

        var ciam = await ResolveCiamAsync(evaluator);
        var workforce = await ResolveWorkforceContactAsync(evaluator);

        foreach (var entityType in new[] { ProjectEntity, MatterEntity, WorkAssignmentEntity })
        {
            ScopeOf(ciam, entityType).Should().BeEquivalentTo(ScopeOf(workforce, entityType),
                $"one rule set for every contact sign-in: {scenario} on {entityType} must answer the same on CIAM " +
                "as on the workforce contact plane");
            // Exact, unfiltered (task 136): the answer carries no None-rights entry to filter out any more.
            ScopeOf(ciam, entityType)
                .Should().BeEquivalentTo(expected[entityType],
                    $"…and the shared answer for {scenario} on {entityType} is the right one, not merely the same one");
        }
    }

    public static TheoryData<string> CiamFaults => new()
    {
        "root-flag read non-success",
        "No Access list reader throws",
        "subject organizations unreadable",
    };

    [Theory]
    [MemberData(nameof(CiamFaults))]
    public async Task Ciam_FailClosed_EachFault_RemovesEveryCandidateFromTheCiamPrincipal(string fault)
    {
        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsAcross(new[]
        {
            (ProjectEntity, Direct(RootId(ProjectEntity, 51), ExternalAccessLevel.FullAccess)),
            (MatterEntity, Direct(RootId(MatterEntity, 51), ExternalAccessLevel.FullAccess)),
            (WorkAssignmentEntity, Direct(RootId(WorkAssignmentEntity, 51), ExternalAccessLevel.FullAccess)),
        }));
        var denyReader = new SeamNoAccessListReader();
        var evaluator = GrantOnlySut(participations, denyReader);

        var healthy = await ResolveCiamAsync(evaluator);
        healthy.ProjectAccess.Should().NotBeEmpty("control: the healthy world grants a project");
        healthy.MatterAccess.Should().NotBeEmpty("control: …a matter");
        healthy.WorkAssignmentAccess.Should().NotBeEmpty("control: …and a work assignment");

        switch (fault)
        {
            // What the REAL flag read returns for a non-success status (task 037): every id Unreadable.
            case "root-flag read non-success": participations.FlagsUnreadable = true; break;
            // The REAL NoAccessListReader catches its query fault and fails closed (task 038).
            case "No Access list reader throws": denyReader.Throws = true; break;
            // What the REAL junction query returns for a non-success status since task 109 (#998).
            case "subject organizations unreadable": participations.JunctionUnreadable = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(fault), fault, "Unknown fault.");
        }

        var faulted = await ResolveCiamAsync(evaluator);

        faulted.ProjectAccess.Should().BeEmpty($"{fault}: fail closed on the CIAM plane — never the grants-only answer");
        faulted.MatterAccess.Should().BeEmpty($"{fault}: fail closed on matters too");
        faulted.WorkAssignmentAccess.Should().BeEmpty($"{fault}: fail closed on work assignments too");
    }

    [Fact]
    public async Task Ciam_FailClosed_RootFlagReadThrows_TheCallerIsNotResolved_NeverTheGrantsOnlyAnswer()
    {
        var participations = new ParticipationWorld { ThrowOnFlagRead = true };
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(RootId(ProjectEntity, 52), ExternalAccessLevel.FullAccess)));

        var strategy = CiamStrategy(GrantOnlySut(participations), ContactId);

        var act = () => strategy.ResolveAsync(CiamRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "a fault escaping the flag read fails the request (ADR-003 fail-closed); the strategy must not catch it " +
            "and answer from the grant rows, which is the pre-C1 answer with no veto applied");
    }

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Ciam_Regression_PlainDirectGrantsOnOpenRoots_KeepExactlyTheirGrantedRights(string entityType)
    {
        var viewOnly = RootId(entityType, 61);
        var collaborate = RootId(entityType, 62);
        var fullAccess = RootId(entityType, 63);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType,
            Direct(viewOnly, ExternalAccessLevel.ViewOnly),
            Direct(collaborate, ExternalAccessLevel.Collaborate),
            Direct(fullAccess, ExternalAccessLevel.FullAccess)));

        var principal = await ResolveCiamAsync(GrantOnlySut(participations));

        ScopeOf(principal, entityType).Should().BeEquivalentTo(new Dictionary<Guid, AccessRights>
        {
            [viewOnly] = Rights(ExternalAccessLevel.ViewOnly),
            [collaborate] = Rights(ExternalAccessLevel.Collaborate),
            [fullAccess] = Rights(ExternalAccessLevel.FullAccess),
        }, $"a CIAM contact with plain grants on open {entityType} records keeps exactly what it was granted");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Task 136 · defect C2 — a record reaches the answer and the principal ONLY with Read, on both contact
    // sign-ins. Before 136 the two cases below each left a None-rights KEY, and every presence-gated read
    // (project routes, module /fetch and /record, /me) admitted it. Route-level 403s are asserted through the
    // real handlers in tests/integration/contract/Api/ExternalAccess/.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task C2_SecureRootReachedOnlyThroughAnOrganizationGrant_IsAbsentFromTheSetAndFromBothPrincipals(
        string entityType)
    {
        var secureOrgOnly = RootId(entityType, 71);
        var openDirect = RootId(entityType, 72);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType,
            OrgOnly(secureOrgOnly, ExternalAccessLevel.FullAccess),
            Direct(openDirect, ExternalAccessLevel.ViewOnly)));
        participations.Flags[secureOrgOnly] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var evaluator = GrantOnlySut(participations);

        var set = await evaluator.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);
        set.Rights.Should().NotContainKey(secureOrgOnly, "absent from the composed answer, not present with None");
        set.Contains(secureOrgOnly).Should().BeFalse();
        (await evaluator.IsRecordAccessibleAsync(ContactPrincipal(), entityType, secureOrgOnly, CancellationToken.None))
            .Should().BeFalse("IsRecordAccessibleAsync means \"holds Read\"");
        set.Contains(openDirect).Should().BeTrue("control: the open record's direct ViewOnly grant still reads");

        var workforce = await ResolveWorkforceContactAsync(evaluator);
        var ciam = await ResolveCiamAsync(evaluator);
        foreach (var (plane, principal) in new[] { ("workforce", workforce), ("CIAM", ciam) })
        {
            ScopeOf(principal, entityType).Should().Equal(
                new Dictionary<Guid, AccessRights> { [openDirect] = AccessRights.Read },
                $"{plane}: the Secure record is not in the principal at all — only the readable control is");
        }
    }

    public static TheoryData<string> NullableLevelRootEntityTypes => new() { MatterEntity, WorkAssignmentEntity };

    [Theory]
    [MemberData(nameof(NullableLevelRootEntityTypes))]
    public async Task C2_GrantRowWithNoLevel_ConfersNothing_OnBothContactSignIns(string entityType)
    {
        var noLevel = RootId(entityType, 73);
        var levelled = RootId(entityType, 74);

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(entityType, NoLevel(noLevel), Direct(levelled, ExternalAccessLevel.Collaborate)));
        var evaluator = GrantOnlySut(participations);

        var set = await evaluator.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);
        set.Rights.Should().NotContainKey(noLevel, "owner 2026-09-30: no level = not granted");
        set.Contains(noLevel).Should().BeFalse();

        var workforce = await ResolveWorkforceContactAsync(evaluator);
        var ciam = await ResolveCiamAsync(evaluator);
        foreach (var (plane, principal) in new[] { ("workforce", workforce), ("CIAM", ciam) })
        {
            ScopeOf(principal, entityType).Should().Equal(
                new Dictionary<Guid, AccessRights> { [levelled] = Rights(ExternalAccessLevel.Collaborate) },
                $"{plane}: the level-less {entityType} is absent; the levelled control keeps exactly its level");
            (entityType == MatterEntity ? principal.GetAccessibleMatterIds() : principal.GetAccessibleWorkAssignmentIds())
                .Should().BeEquivalentTo(new[] { levelled }, $"{plane}: the module scope dimension never sees the level-less record");
        }
    }

    [Fact]
    public async Task C2_NoComposedSetCarriesAnEntryWithoutRead_OnTheSystemUserOrTheContactPlane()
    {
        // The end-of-composition step, pinned on its own: whatever the terms entered at None is GONE from
        // AccessibleRecordSet.Rights — the raw answer, which the Read-gated views would otherwise mask.
        var secureOrgOnly = Guid.Parse("13600000-0000-0000-0000-000000000001");
        var membershipRecord = Guid.Parse("13600000-0000-0000-0000-000000000002");
        var noLevelMatter = Guid.Parse("13600000-0000-0000-0000-000000000003");

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, membershipRecord));

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsAcross(new[]
        {
            (ProjectEntity, OrgOnly(secureOrgOnly, ExternalAccessLevel.FullAccess)),
            (MatterEntity, NoLevel(noLevelMatter)),
        }));
        participations.Flags[secureOrgOnly] = new RootRecordFlags(IsSecure: true, IsRestricted: false);
        var evaluator = BuildSut(BuildDataverse(contactHeld: false), membership.Object, participations);

        var compositions = new[]
        {
            ("systemuser / project", await evaluator.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None)),
            ("contact / project", await evaluator.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None)),
            ("contact / matter", await evaluator.ComposeAsync(ContactPrincipal(), MatterEntity, CancellationToken.None)),
            ("CIAM / project", await evaluator.ComposeForCiamContactAsync(ContactId, ProjectEntity, CancellationToken.None)),
            ("CIAM / matter", await evaluator.ComposeForCiamContactAsync(ContactId, MatterEntity, CancellationToken.None)),
        };

        foreach (var (name, set) in compositions)
        {
            set.Rights.Values.Should().NotContain(r => !r.HasFlag(AccessRights.Read),
                $"{name}: no composed set carries an entry without Read after composition");
            set.Rights.Should().NotContainKeys(new[] { secureOrgOnly, noLevelMatter }, $"{name}: absent, not None");
        }

        compositions[0].Item2.Contains(membershipRecord).Should().BeTrue(
            "control: the systemuser's own membership is untouched by the step");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Task 138 (#1061) — Limited (sprk_accesspermission = Limited) is real. It reuses the ONE FR-22 pre-max
    // suppression predicate (Secure OR Limited), so every term that consulted "Secure" now consults it, on
    // every plane: the workforce contact plane (grant + standing + org expansion), the systemuser plane's
    // contact-grants term, and — through task 135's shared contact-plane composition — CIAM, with no
    // CIAM-specific code. The systemuser's ADR-034 membership term is never suppressed.
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    private static RootRecordFlags Limited => new(IsSecure: false, IsRestricted: false, IsLimited: true);

    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Limited_OnTheWorkforceContactPlane_OrgGrantStandingAndOrgExpansionConferNothing_TheDirectGrantKeepsItsLevel(
        string entityType)
    {
        var limitedMixed = RootId(entityType, 51);      // direct ViewOnly under an org Collaborate grant
        var limitedOrgOnly = RootId(entityType, 52);    // org-inherited grant only
        var limitedStanding = RootId(entityType, 53);   // reached only by the contact's standing membership
        var limitedOrgWalk = RootId(entityType, 54);    // reached only by organization expansion
        var openStanding = RootId(entityType, 55);      // control: Standard, standing membership
        var openOrgWalk = RootId(entityType, 56);       // control: Standard, organization expansion

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveByContactAsync(ContactId, entityType, ContactWalk, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, limitedStanding, openStanding));
        membership.Setup(m => m.ResolveByContactAsync(ContactId, entityType, OrgWalkFor(OrgA), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, limitedOrgWalk, openOrgWalk));

        var dataverse = BuildDataverse(
            contactHeld: true, contactBaseline: ExternalAccessLevel.Collaborate,
            orgs: new Dictionary<Guid, (bool Held, ExternalAccessLevel? Baseline)> { [OrgA] = (true, ExternalAccessLevel.FullAccess) });

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType,
            new GrantRow(limitedMixed, ExternalAccessLevel.Collaborate, ExternalAccessLevel.ViewOnly),
            OrgOnly(limitedOrgOnly, ExternalAccessLevel.FullAccess)));
        participations.ActiveOrgIds.Add(OrgA);
        foreach (var id in new[] { limitedMixed, limitedOrgOnly, limitedStanding, limitedOrgWalk })
        {
            participations.Flags[id] = Limited;
        }

        var sut = BuildSut(dataverse, membership.Object, participations);
        var set = await sut.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);

        set.RightsFor(limitedMixed).Should().Be(AccessRights.Read,
            $"on a Limited {entityType} the DIRECT grant contributes exactly its own level (ViewOnly), and the " +
            "org Collaborate never enters the max");
        set.Rights.Should().NotContainKeys(new[] { limitedOrgOnly, limitedStanding, limitedOrgWalk },
            "org-inherited grants, standing membership and organization expansion contribute nothing on a Limited record");
        set.RightsFor(openStanding).Should().Be(Rights(ExternalAccessLevel.Collaborate), "control: standing still reaches a Standard record");
        set.RightsFor(openOrgWalk).Should().Be(Rights(ExternalAccessLevel.FullAccess), "control: org expansion still reaches a Standard record");
    }

    [Fact]
    public async Task Limited_OnTheSystemUserPlane_TheContactGrantsTermIsSuppressed_TheMembershipTermIsNot()
    {
        var limitedMember = RootId(ProjectEntity, 61);   // the systemuser's own ADR-034 membership
        var limitedOrgOnly = RootId(ProjectEntity, 62);  // only the linked contact's organization grant
        var limitedMixed = RootId(ProjectEntity, 63);    // linked contact: direct ViewOnly + org FullAccess

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, limitedMember));

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            ProjectEntity,
            OrgOnly(limitedOrgOnly, ExternalAccessLevel.FullAccess),
            new GrantRow(limitedMixed, ExternalAccessLevel.FullAccess, ExternalAccessLevel.ViewOnly)));
        participations.Flags[limitedMember] = Limited;
        participations.Flags[limitedOrgOnly] = Limited;
        participations.Flags[limitedMixed] = Limited;

        var sut = BuildSut(BuildDataverse(contactHeld: false), membership.Object, participations);
        var set = await sut.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        set.RightsFor(limitedMember).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "internal access is unaffected by Limited — the membership term never consults the predicate");
        set.Rights.Should().NotContainKey(limitedOrgOnly,
            "the linked contact's org-inherited grant confers nothing on a Limited record");
        set.RightsFor(limitedMixed).Should().Be(AccessRights.Read,
            "the linked contact's own direct ViewOnly grant survives, at exactly its level");
    }

    /// <summary>
    /// Criterion 2: the CIAM plane, through task 135's pipeline, gives the SAME answer on a Limited root for a
    /// contact holding a direct grant, an org-inherited grant, or both — with no CIAM-specific Limited code.
    /// </summary>
    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task Ciam_Limited_DirectOrgOrBoth_TheDirectGrantAloneCounts(string entityType)
    {
        var directOnly = RootId(entityType, 71);
        var orgOnly = RootId(entityType, 72);
        var both = RootId(entityType, 73);
        var openOrgOnly = RootId(entityType, 74); // control: an organization grant on a Standard root

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType,
            Direct(directOnly, ExternalAccessLevel.Collaborate),
            OrgOnly(orgOnly, ExternalAccessLevel.FullAccess),
            new GrantRow(both, ExternalAccessLevel.FullAccess, ExternalAccessLevel.Collaborate),
            OrgOnly(openOrgOnly, ExternalAccessLevel.Collaborate)));
        participations.Flags[directOnly] = Limited;
        participations.Flags[orgOnly] = Limited;
        participations.Flags[both] = Limited;

        var principal = await ResolveCiamAsync(GrantOnlySut(participations));
        var scope = ScopeOf(principal, entityType);

        RightsIn(scope, directOnly).Should().Be(Rights(ExternalAccessLevel.Collaborate));
        scope.Should().NotContainKey(orgOnly, "an org-inherited grant confers nothing on a Limited root, on CIAM too");
        RightsIn(scope, both).Should().Be(Rights(ExternalAccessLevel.Collaborate),
            "direct Collaborate under an org FullAccess is EXACTLY Collaborate — the org term is suppressed before the max");
        RightsIn(scope, openOrgOnly).Should().Be(Rights(ExternalAccessLevel.Collaborate),
            "control: organization access stays on a Standard root (owner A2)");
    }

    /// <summary>
    /// Criterion 4: Restricted wins over Limited (and Secure) on every plane — CIAM, the workforce contact plane
    /// and the systemuser plane's contact-grants term — while the systemuser's membership term survives.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task RestrictedWins_WithOrWithoutSecureOrLimited_NoContactPrincipalKeepsAnything_MembershipSurvives(
        bool secure, bool limited)
    {
        var restricted = RootId(ProjectEntity, 81);

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, restricted));

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(restricted, ExternalAccessLevel.FullAccess)));
        participations.Flags[restricted] = new RootRecordFlags(IsSecure: secure, IsRestricted: true, IsLimited: limited);

        var evaluator = BuildSut(BuildDataverse(contactHeld: false), membership.Object, participations);

        var ciam = await ResolveCiamAsync(evaluator);
        var workforceContact = await evaluator.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);
        var systemUser = await evaluator.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        ScopeOf(ciam, ProjectEntity).Should().NotContainKey(restricted, "CIAM: Restricted admits no contact access");
        workforceContact.Rights.Should().NotContainKey(restricted, "workforce contact: the same");
        systemUser.RightsFor(restricted).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "systemuser plane: the direct FullAccess contact grant is vetoed (Delete absent), the membership survives");
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════════
    // Task 137 (#1060, defect C5) — inactive contacts and inactive roots confer nothing contact-sourced
    // (the transport-level twins — the live reads over a real HTTP server, warm caches — are in
    // OrganizationMembershipReadTests' task 137 section)
    // ═════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// C5: an INACTIVE root confers nothing to a contact on any plane — CIAM, the workforce contact plane, and the
    /// systemuser plane's contact-grants term — while the systemuser's OWN membership on that root survives (the
    /// Restricted slot's survivor rule). One case per root type. The positive twin is the active control below.
    /// </summary>
    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task InactiveRoot_ConfersNothingContactSourced_OnEveryPlane_TheSystemusersMembershipSurvives(string entityType)
    {
        var inactive = RootId(entityType, 91);
        var active = RootId(entityType, 92);

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, entityType, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(entityType, inactive));

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(
            entityType, Direct(inactive, ExternalAccessLevel.FullAccess), Direct(active, ExternalAccessLevel.Collaborate)));
        participations.Flags[inactive] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsInactive: true);

        var evaluator = BuildSut(BuildDataverse(contactHeld: false), membership.Object, participations);

        var ciam = await ResolveCiamAsync(evaluator);
        var workforceContact = await evaluator.ComposeAsync(ContactPrincipal(), entityType, CancellationToken.None);
        var systemUser = await evaluator.ComposeAsync(SystemUserPrincipal(), entityType, CancellationToken.None);

        ScopeOf(ciam, entityType).Should().NotContainKey(inactive, "CIAM: an inactive root confers nothing");
        workforceContact.Rights.Should().NotContainKey(inactive, "workforce contact: the same");
        systemUser.RightsFor(inactive).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "systemuser: the FullAccess contact grant is removed (no Delete), the systemuser's own membership survives");

        RightsIn(ScopeOf(ciam, entityType), active).Should().Be(Rights(ExternalAccessLevel.Collaborate),
            "control: an ACTIVE root's grant is untouched");
        workforceContact.RightsFor(active).Should().Be(Rights(ExternalAccessLevel.Collaborate));
    }

    /// <summary>C5: reactivating the root restores the contact's access with no other change (read-time, not a write).</summary>
    [Theory]
    [MemberData(nameof(RootEntityTypes))]
    public async Task InactiveRoot_Reactivated_RestoresTheContactsAccess(string entityType)
    {
        var root = RootId(entityType, 93);
        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(entityType, Direct(root, ExternalAccessLevel.Collaborate)));
        participations.Flags[root] = new RootRecordFlags(IsSecure: false, IsRestricted: false, IsInactive: true);

        ScopeOf(await ResolveCiamAsync(GrantOnlySut(participations)), entityType).Should().NotContainKey(root);

        participations.Flags[root] = RootRecordFlags.None; // reactivated — the grant row never changed

        ScopeOf(await ResolveCiamAsync(GrantOnlySut(participations)), entityType).Should().ContainKey(root,
            "the same grant confers again once the root is active");
    }

    /// <summary>C5: an UNREADABLE root (the existing fail-closed value) is inactive too, and still keeps the survivor.</summary>
    [Fact]
    public void UnreadableRoot_IsInactive_AndRemovesContactSourcedAccess()
    {
        RootRecordFlags.Unreadable.IsInactive.Should().BeTrue();
        RootRecordFlags.Unreadable.RemovesContactSourcedAccess.Should().BeTrue();
        RootRecordFlags.None.RemovesContactSourcedAccess.Should().BeFalse("control: a readable Standard active root");
    }

    public static TheoryData<string> InactiveContactStates => new() { "inactive", "unreadable", "read-throws" };

    /// <summary>
    /// C5: an inactive (or unreadable) contact confers nothing on either contact plane — every term there is
    /// contact-sourced — and nothing else is even read for it. The control is the same world with an active contact.
    /// </summary>
    [Theory]
    [MemberData(nameof(InactiveContactStates))]
    public async Task InactiveOrUnreadableContact_ComposesToNothing_OnBothContactPlanes(string state)
    {
        var root = RootId(ProjectEntity, 94);
        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(root, ExternalAccessLevel.FullAccess)));
        var evaluator = GrantOnlySut(participations);

        (await evaluator.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None))
            .Rights.Should().ContainKey(root, "control: the active contact holds its grant");

        participations.ContactState = state == "inactive" ? ContactRecordState.Inactive : ContactRecordState.Unreadable;
        participations.ThrowOnContactStateRead = state == "read-throws";

        var workforce = await evaluator.ComposeAsync(ContactPrincipal(), ProjectEntity, CancellationToken.None);
        var ciam = await evaluator.ComposeForCiamContactAsync(ContactId, ProjectEntity, CancellationToken.None);

        workforce.Rights.Should().BeEmpty($"workforce contact: a contact whose state is {state} confers nothing");
        ciam.Rights.Should().BeEmpty($"CIAM: the same ({state})");
    }

    /// <summary>
    /// C5: a systemuser's INACTIVE linked contact contributes no grant, while the systemuser's own membership is
    /// unchanged — internal access is Dataverse's answer, never the contact's. Positive twin: the active contact's
    /// grant on a record the membership does not reach.
    /// </summary>
    [Fact]
    public async Task InactiveLinkedContact_OfASystemUser_ContributesNoGrant_TheMembershipIsUnchanged()
    {
        var memberRecord = RootId(ProjectEntity, 95);
        var grantOnlyRecord = RootId(ProjectEntity, 96);

        var membership = new Mock<IMembershipResolverService>();
        membership.Setup(m => m.ResolveAsync(
                SystemUserId, ProjectEntity, It.IsAny<MembershipResolveOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response(ProjectEntity, memberRecord));

        var participations = new ParticipationWorld();
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(grantOnlyRecord, ExternalAccessLevel.FullAccess)));
        var evaluator = BuildSut(BuildDataverse(contactHeld: false), membership.Object, participations);

        var active = await evaluator.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);
        active.Rights.Should().ContainKey(grantOnlyRecord, "control: the active linked contact's grant applies");

        participations.ContactState = ContactRecordState.Inactive;
        var inactive = await evaluator.ComposeAsync(SystemUserPrincipal(), ProjectEntity, CancellationToken.None);

        inactive.Rights.Should().NotContainKey(grantOnlyRecord, "the inactive linked contact's grant confers nothing");
        inactive.RightsFor(memberRecord).Should().Be(AccessibleRecordSetService.MembershipTermRights,
            "the systemuser's own membership is unchanged");
        inactive.Sources.ContactGrants.Should().BeFalse("no contact grant term was applied");
    }

    /// <summary>
    /// C5 constraint: the contact-state check must not depend on the standing-grant reader (task 142 escalation (d)
    /// may retire that term, and CIAM never calls it). With the standing reader Strict and unset — any call throws —
    /// the CIAM composition still denies an inactive contact, and reads the contact state itself.
    /// </summary>
    [Fact]
    public async Task InactiveContactCheck_DoesNotRideTheStandingGrantReader()
    {
        var root = RootId(ProjectEntity, 97);
        var participations = new ParticipationWorld { ContactState = ContactRecordState.Inactive };
        participations.SetGrants(GrantsOn(ProjectEntity, Direct(root, ExternalAccessLevel.FullAccess)));
        var evaluator = new AccessibleRecordSetService(
            new Mock<IMembershipResolverService>(MockBehavior.Strict).Object,
            participations,
            new Mock<ISubjectStandingGrantReader>(MockBehavior.Strict).Object,
            new SeamNoAccessListReader(),
            // Merge of 137-b2 with 143-r2 (task 142 base): 143 r1 added the identity store to the evaluator; the inert
            // unlinked default every other construction site uses.
            Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
            NullLogger<AccessibleRecordSetService>.Instance);

        var ciam = await evaluator.ComposeForCiamContactAsync(ContactId, ProjectEntity, CancellationToken.None);

        ciam.Rights.Should().BeEmpty();
        participations.ContactStateReads.Should().Be(1, "the evaluator reads the contact's state itself");
    }

    // ── CIAM harness (task 135) ──────────────────────────────────────────────────────────────────

    private const string CiamOid = "c1a00000-0000-0000-0000-0000000000c1";

    /// <summary>One grant row as the grant set carries it: the all-sources level and the DIRECT level
    /// (null when only an organization grant reaches the record). The all-sources level is null only for a
    /// matter / work-assignment row written with no level (task 136; the real read drops such PROJECT rows).</summary>
    private sealed record GrantRow(Guid RecordId, ExternalAccessLevel? Level, ExternalAccessLevel? DirectLevel);

    private static GrantRow Direct(Guid recordId, ExternalAccessLevel level) => new(recordId, level, level);

    private static GrantRow OrgOnly(Guid recordId, ExternalAccessLevel level) => new(recordId, level, null);

    /// <summary>A contact's own grant row with no <c>sprk_accesslevel</c> (matter / work assignment only).</summary>
    private static GrantRow NoLevel(Guid recordId) => new(recordId, null, null);

    private static AccessRights Rights(ExternalAccessLevel level) => ExternalAccessLevels.ToAccessRights(level);

    private static AccessRights RightsIn(IReadOnlyDictionary<Guid, AccessRights> scope, Guid recordId) =>
        scope.TryGetValue(recordId, out var rights) ? rights : AccessRights.None;

    /// <summary>A distinct, deterministic root id per entity type, so one world can carry all three.</summary>
    private static Guid RootId(string entityType, int n) => Guid.Parse(
        (entityType switch
        {
            ProjectEntity => "7e000001",
            MatterEntity => "7e000002",
            WorkAssignmentEntity => "7e000003",
            _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown root entity type."),
        }) + $"-0000-0000-0000-{n:D12}");

    private static ExternalGrantSet GrantsOn(string entityType, params GrantRow[] rows) =>
        GrantsAcross(rows.Select(r => (entityType, r)));

    private static ExternalGrantSet GrantsAcross(IEnumerable<(string EntityType, GrantRow Row)> rows)
    {
        var all = rows.ToList();
        IReadOnlyList<ExternalRootGrant> RootGrantsOf(string entityType) => all
            .Where(x => x.EntityType == entityType)
            .Select(x => new ExternalRootGrant { RecordId = x.Row.RecordId, AccessLevel = x.Row.Level, DirectAccessLevel = x.Row.DirectLevel })
            .ToList();

        return new ExternalGrantSet
        {
            Projects = all
                .Where(x => x.EntityType == ProjectEntity)
                .Select(x => new ExternalParticipation
                {
                    ProjectId = x.Row.RecordId,
                    AccessLevel = x.Row.Level ?? throw new ArgumentException("A project grant row always carries a level."),
                    DirectAccessLevel = x.Row.DirectLevel,
                })
                .ToList(),
            MatterGrants = RootGrantsOf(MatterEntity),
            WorkAssignmentGrants = RootGrantsOf(WorkAssignmentEntity),
        };
    }

    /// <summary>An evaluator over a world with no standing grants and a membership resolver that must never be
    /// called (Strict, no setups) — the explicit-grant world most CIAM tests need.</summary>
    private static AccessibleRecordSetService GrantOnlySut(ParticipationWorld participations, NoAccessListReader? denyReader = null) =>
        BuildSut(BuildDataverse(contactHeld: false), new Mock<IMembershipResolverService>(MockBehavior.Strict).Object, participations, denyReader);

    private static DefaultHttpContext CiamRequest() => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", CiamOid), new Claim("iss", "https://spaarketest.ciamlogin.com/tid/v2.0") },
            "Ciam")),
    };

    /// <summary>
    /// The REAL CIAM strategy over a world where <see cref="CiamOid"/> is bound to <paramref name="contactId"/>.
    /// Task 141 moved identity resolution to the shared <see cref="ContactIdentityBinder"/>, so the contact is
    /// named by the identity store, not by the participation service.
    /// </summary>
    private static CiamContactPrincipalStrategy CiamStrategy(AccessibleRecordSetService evaluator, Guid contactId)
    {
        var identities = new InMemoryContactIdentityStore();
        identities.AddContact(contactId, oid: CiamOid, plane: IdentityPlaneMarker.External);
        return new CiamContactPrincipalStrategy(
            IdentityBindingTestKit.Binder(identities), evaluator, NullLogger<CiamContactPrincipalStrategy>.Instance);
    }

    private static async Task<CallerPrincipal> ResolveCiamAsync(AccessibleRecordSetService evaluator)
    {
        var strategy = CiamStrategy(evaluator, ContactId);

        var resolution = await strategy.ResolveAsync(CiamRequest(), CancellationToken.None);

        resolution.IsResolved.Should().BeTrue("precondition: the CIAM contact resolves");
        resolution.Principal!.Plane.Should().Be(CallerPrincipalPlane.CiamContact);
        return resolution.Principal;
    }

    /// <summary>The SAME contact, signed in on a workforce token (a contact-only principal).</summary>
    private static async Task<CallerPrincipal> ResolveWorkforceContactAsync(AccessibleRecordSetService evaluator)
    {
        var resolver = new Mock<IWorkforcePrincipalResolver>();
        resolver.Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(WorkforcePrincipalResolution.ForContact(ContactId, Oid.ToString("D"), Tenant));
        var strategy = new WorkforcePrincipalStrategy(
            resolver.Object, evaluator, NullLogger<WorkforcePrincipalStrategy>.Instance);

        var resolution = await strategy.ResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        resolution.IsResolved.Should().BeTrue("precondition: the workforce contact resolves");
        return resolution.Principal!;
    }

    private static IReadOnlyDictionary<Guid, AccessRights> ScopeOf(CallerPrincipal principal, string entityType) => entityType switch
    {
        ProjectEntity => principal.ProjectAccess.ToDictionary(p => p.ProjectId, p => p.Rights),
        MatterEntity => principal.MatterAccess,
        WorkAssignmentEntity => principal.WorkAssignmentAccess,
        _ => throw new ArgumentOutOfRangeException(nameof(entityType), entityType, "Unknown root entity type."),
    };

    // ── harness ──────────────────────────────────────────────────────────────────────────────────

    private static AccessibleRecordSetService BuildSut(
        Mock<IDataverseService> dataverse,
        IMembershipResolverService membership,
        ParticipationWorld participations,
        NoAccessListReader? denyReader = null,
        IContactIdentityStore? identityStore = null)
    {
        var standing = new SubjectStandingGrantReader(dataverse.Object, NullLogger<SubjectStandingGrantReader>.Instance);
        return new AccessibleRecordSetService(
            membership,
            participations,
            standing,
            denyReader ?? new SeamNoAccessListReader(),
            identityStore ?? Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess.AccessibleRecordSetTestFactory.UnlinkedIdentityStore(),
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

        /// <summary>Task 135: the flag read itself THROWS (an exception escaping the read, not the
        /// Unreadable outcome the real read returns for a non-success status).</summary>
        public bool ThrowOnFlagRead { get; set; }

        public HashSet<Guid> ActiveOrgIds { get; } = new();
        public bool ThrowOnActiveOrgIds { get; set; }

        /// <summary>Task 135: the junction read returns <see cref="ActiveOrgMemberships.Failed"/> — exactly what
        /// the real query returns for a non-success status since task 109 (pinned on the wire by
        /// OrganizationMembershipReadTests).</summary>
        public bool JunctionUnreadable { get; set; }
        public Dictionary<Guid, IReadOnlyCollection<Guid>> ReferencedOrgs { get; } = new();
        public HashSet<Guid> UnreadableOrgReferences { get; } = new();

        public ParticipationWorld()
            : base(new HttpClient(), cache: null!, configuration: null!, credential: null!,
                   httpContextAccessor: null!, logger: NullLogger<ExternalParticipationService>.Instance)
        {
        }

        public void SetGrants(ExternalGrantSet grantSet) => _grantSet = grantSet;

        public override Task<ExternalGrantSet> GetGrantSetAsync(Guid contactId, CancellationToken ct = default)
            => Task.FromResult(_grantSet);

        // (Task 141 removed the participation service's contact resolution — there is no email fallback left
        // in the evaluator for this world to stub.)

        public override Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
            string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            if (ThrowOnFlagRead)
            {
                return Task.FromException<IReadOnlyDictionary<Guid, RootRecordFlags>>(
                    new InvalidOperationException("simulated root-flag read fault escaping the read"));
            }

            IReadOnlyDictionary<Guid, RootRecordFlags> result = recordIds.Distinct().ToDictionary(
                id => id,
                id => FlagsUnreadable ? RootRecordFlags.Unreadable : (Flags.TryGetValue(id, out var f) ? f : RootRecordFlags.None));
            return Task.FromResult(result);
        }

        /// <summary>Task 137: the contact's live state (Active unless a test says otherwise).</summary>
        public ContactRecordState ContactState { get; set; } = ContactRecordState.Active;

        /// <summary>Task 137: the live state read THROWS (the production wrapper turns that into Unreadable).</summary>
        public bool ThrowOnContactStateRead { get; set; }

        /// <summary>Task 137: how many times the live contact-state read ran.</summary>
        public int ContactStateReads { get; private set; }

        internal override Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
        {
            ContactStateReads++;
            return ThrowOnContactStateRead
                ? Task.FromException<ContactRecordState>(new HttpRequestException("simulated contact-state read fault"))
                : Task.FromResult(ContactState);
        }

        // ActiveOrgIds are current memberships of active organizations — in BOTH named sets (task 109).
        // ThrowOnActiveOrgIds models the entry's token/API-url fault, the one that arrives as an exception.
        internal override Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(Guid contactId, CancellationToken ct = default)
            => ThrowOnActiveOrgIds
                ? Task.FromException<ActiveOrgMemberships>(new InvalidOperationException("simulated token/API-url acquisition failure"))
                : JunctionUnreadable
                    ? Task.FromResult(ActiveOrgMemberships.Failed)
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

        /// <summary>
        /// Adds an active entry. Its SUBJECT column is set from <paramref name="requiredSubjectSubstring"/>
        /// (<c>"_sprk_subjectcontact_value eq {id}"</c> / <c>"_sprk_subjectorganization_value eq {id}"</c>) — a real row carries the
        /// subject the query matched it on, and since task 143 the reader treats a row with no subject as malformed.
        /// </summary>
        public void AddEntry(string requiredSubjectSubstring, NoAccessEntryRow row)
        {
            var parts = requiredSubjectSubstring.Split(" eq ", 2);
            if (parts.Length == 2 && Guid.TryParse(parts[1], out var subjectId))
            {
                switch (parts[0])
                {
                    case "_sprk_subjectcontact_value":
                        row._sprk_subjectcontact_value ??= subjectId;
                        break;
                    case "_sprk_subjectorganization_value":
                        row._sprk_subjectorganization_value ??= subjectId;
                        break;
                    case "_sprk_subjectsystemuser_value":
                        row._sprk_subjectsystemuser_value ??= subjectId;
                        break;
                }
            }

            _entries.Add(new ActiveEntry(requiredSubjectSubstring, row));
        }

        internal override Task<List<NoAccessEntryRow>?> QueryChunkAsync(
            string subjectFilter, string objectFilter, CancellationToken ct)
        {
            if (Throws)
            {
                throw new InvalidOperationException("simulated sprk_noaccessentry query outage");
            }

            var isOrgLoop = objectFilter.Contains("_sprk_objectorganization_value", StringComparison.Ordinal);
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
