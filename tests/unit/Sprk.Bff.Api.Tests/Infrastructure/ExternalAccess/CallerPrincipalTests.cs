using FluentAssertions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Domain-logic tests for <see cref="CallerPrincipal"/> — the plane-agnostic collaboration caller
/// (teams-app-r1 task 025 · R2 FR-22). These lock two guarantees:
///
///   (1) CIAM REGRESSION (R2 guardrail #3 / R1 FR-15): the effective-rights + access-level mapping is
///       byte-for-byte identical to the old <see cref="ExternalCallerContext"/> path, so the CIAM /me
///       and per-project authorization responses are unchanged.
///   (2) WORKFORCE TIER-2 SCOPE (R2 NFR-08): a workforce caller's record scope is EXACTLY its accessible
///       project set — <see cref="CallerPrincipal.HasProjectAccess"/> is false for any project outside it.
///   (3) NO FLATTENING (unified-access-control-r2 task 033 / FR-19 / register A-8): rights are per
///       RECORD on all three root types. This clause used to read "every accessible project is
///       surfaced at Collaborate" — that blanket stamp is deleted, and it was the reason a deliberate
///       ViewOnly grant conferred Write.
/// </summary>
public class CallerPrincipalTests
{
    private static CallerPrincipal Ciam(params CallerProjectAccess[] access) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@test.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = access
    };

    // A workforce caller reaching projects through ADR-034 membership. Task 033 deleted the blanket
    // per-plane stamp this helper used to read; membership rights now come from the evaluator's term
    // (AccessibleRecordSetService.MembershipTermRights), which is where a plane-wide default legitimately
    // lives because it composes under max instead of overwriting other terms.
    private static CallerPrincipal Workforce(params Guid[] accessibleProjects) =>
        WorkforceWithRights(accessibleProjects.ToDictionary(
            id => id, _ => AccessibleRecordSetService.MembershipTermRights));

    private static CallerPrincipal WorkforceWithRights(IDictionary<Guid, AccessRights> projectRights) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = Guid.NewGuid(),
        Email = "staff@contoso.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = projectRights
            .Select(kvp => new CallerProjectAccess { ProjectId = kvp.Key, Rights = kvp.Value })
            .ToList()
    };

    // ── CIAM regression: effective-rights mapping unchanged from ExternalCallerContext ────────────

    [Fact]
    public void GetEffectiveRights_CiamViewOnly_GrantsReadOnly()
    {
        var projectId = Guid.NewGuid();
        var caller = Ciam(CallerProjectAccess.FromLevel(projectId, ExternalAccessLevel.ViewOnly));

        var rights = caller.GetEffectiveRights(projectId);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeFalse();
        rights.HasFlag(AccessRights.Create).Should().BeFalse();
        rights.HasFlag(AccessRights.Delete).Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveRights_CiamCollaborate_GrantsReadWriteCreate()
    {
        var projectId = Guid.NewGuid();
        var caller = Ciam(CallerProjectAccess.FromLevel(projectId, ExternalAccessLevel.Collaborate));

        var rights = caller.GetEffectiveRights(projectId);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Create).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeTrue();
        rights.HasFlag(AccessRights.Delete).Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveRights_CiamFullAccess_GrantsAllRights()
    {
        var projectId = Guid.NewGuid();
        var caller = Ciam(CallerProjectAccess.FromLevel(projectId, ExternalAccessLevel.FullAccess));

        var rights = caller.GetEffectiveRights(projectId);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Create).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeTrue();
        rights.HasFlag(AccessRights.Delete).Should().BeTrue();
    }

    [Fact]
    public void GetEffectiveRights_ForUnknownProject_ReturnsNone()
    {
        var caller = Ciam(CallerProjectAccess.FromLevel(Guid.NewGuid(), ExternalAccessLevel.FullAccess));
        caller.GetEffectiveRights(Guid.NewGuid()).Should().Be(AccessRights.None);
    }

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, "ViewOnly")]
    [InlineData(ExternalAccessLevel.Collaborate, "Collaborate")]
    [InlineData(ExternalAccessLevel.FullAccess, "FullAccess")]
    public void MeProjection_CiamAccessLevel_MapsToSameStringAsLegacyHandler(
        ExternalAccessLevel level, string expectedString)
    {
        // The /me handler projects ProjectAccess → ProjectAccessEntry(ProjectId, level-string).
        // Task 033 made the level a DERIVED display projection over stored AccessRights, so this test
        // now also pins that the round-trip level → rights → level is lossless for all three real
        // levels: the CIAM /me payload is byte-identical to what the old handler produced.
        var projectId = Guid.NewGuid();
        var caller = Ciam(CallerProjectAccess.FromLevel(projectId, level));

        var projects = caller.ProjectAccess
            .Select(p => new ProjectAccessEntry(
                p.ProjectId, p.AccessLevel?.ToString() ?? nameof(AccessRights.None)))
            .ToList();

        projects.Should().ContainSingle();
        projects[0].ProjectId.Should().Be(projectId);
        projects[0].AccessLevel.Should().Be(expectedString);
    }

    // ── Workforce Tier-2 record scope (NFR-08): only the accessible set, never all projects ──────

    [Fact]
    public void GetAccessibleProjectIds_Workforce_ReturnsExactlyTheAccessibleSet()
    {
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var caller = Workforce(p1, p2);

        caller.GetAccessibleProjectIds().Should().BeEquivalentTo(new[] { p1, p2 });
    }

    [Fact]
    public void HasProjectAccess_Workforce_TrueForAccessibleProject()
    {
        var p1 = Guid.NewGuid();
        var caller = Workforce(p1);
        caller.HasProjectAccess(p1).Should().BeTrue();
    }

    [Fact]
    public void HasProjectAccess_Workforce_FalseForProjectOutsideAccessibleSet()
    {
        // The NFR-08 crux: a workforce caller must NOT reach a project it was not scoped to — this is
        // what makes the per-project handler gate return 403 (and the list omit it).
        var caller = Workforce(Guid.NewGuid());
        caller.HasProjectAccess(Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void GetEffectiveRights_WorkforceMembershipProject_IsCollaborateEquivalent()
    {
        // STATUS-QUO GUARD for the task-033 stamp deletion. Deleting the blanket stamp must not
        // reduce a membership-derived caller's rights — the membership TERM contributes exactly what
        // the stamp used to (Read|Write|Create), so this flow is unchanged. If this test ever fails,
        // the stamp deletion has become a term-level regression (task 033 escalation trigger 2).
        var p1 = Guid.NewGuid();
        var caller = Workforce(p1);

        var rights = caller.GetEffectiveRights(p1);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Create).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeTrue();
        rights.HasFlag(AccessRights.Delete).Should().BeFalse(
            "membership confers Collaborate-equivalent rights, not Delete");
    }

    // ── FR-19: the workforce plane no longer flattens (task 033, register A-8) ────────────────────

    [Fact]
    public void GetEffectiveRights_WorkforceMixedRights_PreservesPerRecordDifference()
    {
        // THE REGRESSION THIS PROJECT EXISTS TO PREVENT.
        //
        // Until 2026-09-04 WorkforcePrincipalStrategy stamped one level over EVERY accessible project,
        // so these two projects would have come back IDENTICAL and the ViewOnly project would have
        // carried Write. The stamp is gone; rights are per record.
        var viewOnly = Guid.NewGuid();
        var collaborate = Guid.NewGuid();
        var caller = WorkforceWithRights(new Dictionary<Guid, AccessRights>
        {
            [viewOnly] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly),
            [collaborate] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
        });

        caller.GetEffectiveRights(viewOnly).Should().Be(AccessRights.Read);
        caller.GetEffectiveRights(viewOnly).HasFlag(AccessRights.Write).Should().BeFalse(
            "a ViewOnly grant must not permit a write on ANY route (FR-19 acceptance)");

        caller.GetEffectiveRights(collaborate).HasFlag(AccessRights.Write).Should().BeTrue();
        caller.GetEffectiveRights(collaborate).Should().NotBe(caller.GetEffectiveRights(viewOnly),
            "the two projects hold different rights — flattening them is the defect FR-19 removes");
    }

    [Fact]
    public void MatterAndWorkAssignmentAccess_CarryPerRecordRights_NotBareMembership()
    {
        // Matters and work assignments used to be bare id sets, so every consumer treated membership
        // as implying write. They now carry rights like projects do.
        var viewOnlyMatter = Guid.NewGuid();
        var collaborateMatter = Guid.NewGuid();
        var viewOnlyWa = Guid.NewGuid();

        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = Guid.NewGuid(),
            Email = "external@test.com",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>
            {
                [viewOnlyMatter] = AccessRights.Read,
                [collaborateMatter] = AccessRights.Read | AccessRights.Write | AccessRights.Create,
            },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [viewOnlyWa] = AccessRights.Read },
        };

        caller.GetMatterRights(viewOnlyMatter).HasFlag(AccessRights.Write).Should().BeFalse();
        caller.GetMatterRights(collaborateMatter).HasFlag(AccessRights.Write).Should().BeTrue();
        caller.GetWorkAssignmentRights(viewOnlyWa).HasFlag(AccessRights.Write).Should().BeFalse();

        // Fail-closed: a record the caller cannot reach yields None, never a default.
        caller.GetMatterRights(Guid.NewGuid()).Should().Be(AccessRights.None);
        caller.GetWorkAssignmentRights(Guid.NewGuid()).Should().Be(AccessRights.None);
    }

    [Fact]
    public void AccessibleIdSets_AreDerivedFromRights_SoTheyCannotDisagree()
    {
        // The id sets read-scope injection consumes are a VIEW over the rights maps, not a second
        // stored collection. This is what makes "in the id set but with no rights" unrepresentable.
        var matter = Guid.NewGuid();
        var wa = Guid.NewGuid();
        var caller = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = Guid.NewGuid(),
            SystemUserId = Guid.NewGuid(),
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [matter] = AccessRights.Read },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [wa] = AccessRights.Read },
        };

        caller.GetAccessibleMatterIds().Should().BeEquivalentTo(new[] { matter });
        caller.GetAccessibleWorkAssignmentIds().Should().BeEquivalentTo(new[] { wa });
    }

    [Fact]
    public void ToDisplayLevel_IsLossyDownward_NeverOverstatingRights()
    {
        // The /me level string is a DISPLAY projection. It must never claim a level the caller does
        // not fully hold — an off-grid combination degrades DOWNWARD.
        ExternalAccessLevels.ToDisplayLevel(AccessRights.None).Should().BeNull();
        ExternalAccessLevels.ToDisplayLevel(AccessRights.Read).Should().Be(ExternalAccessLevel.ViewOnly);
        ExternalAccessLevels.ToDisplayLevel(AccessRights.Read | AccessRights.Write)
            .Should().Be(ExternalAccessLevel.ViewOnly,
                "Collaborate requires Create too — reporting Collaborate here would overstate the grant");
        ExternalAccessLevels.ToDisplayLevel(
                AccessRights.Read | AccessRights.Write | AccessRights.Create)
            .Should().Be(ExternalAccessLevel.Collaborate);
        ExternalAccessLevels.ToDisplayLevel(
                AccessRights.Read | AccessRights.Write | AccessRights.Create | AccessRights.Delete)
            .Should().Be(ExternalAccessLevel.FullAccess);

        // Round-trip: every level survives level → rights → level unchanged.
        foreach (var level in new[]
                 {
                     ExternalAccessLevel.ViewOnly,
                     ExternalAccessLevel.Collaborate,
                     ExternalAccessLevel.FullAccess,
                 })
        {
            ExternalAccessLevels.ToDisplayLevel(ExternalAccessLevels.ToAccessRights(level))
                .Should().Be(level);
        }
    }
}
