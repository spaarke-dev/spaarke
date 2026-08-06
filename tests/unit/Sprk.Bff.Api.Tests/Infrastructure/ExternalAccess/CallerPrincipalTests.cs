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
///       project set — <see cref="CallerPrincipal.HasProjectAccess"/> is false for any project outside
///       it — and every accessible project is surfaced at Collaborate.
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

    private static CallerPrincipal Workforce(params Guid[] accessibleProjects) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = Guid.NewGuid(),
        Email = "staff@contoso.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = accessibleProjects
            .Select(id => new CallerProjectAccess
            {
                ProjectId = id,
                AccessLevel = WorkforcePrincipalStrategy.WorkforceProjectAccessLevel
            })
            .ToList()
    };

    // ── CIAM regression: effective-rights mapping unchanged from ExternalCallerContext ────────────

    [Fact]
    public void GetEffectiveRights_CiamViewOnly_GrantsReadOnly()
    {
        var projectId = Guid.NewGuid();
        var caller = Ciam(new CallerProjectAccess { ProjectId = projectId, AccessLevel = ExternalAccessLevel.ViewOnly });

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
        var caller = Ciam(new CallerProjectAccess { ProjectId = projectId, AccessLevel = ExternalAccessLevel.Collaborate });

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
        var caller = Ciam(new CallerProjectAccess { ProjectId = projectId, AccessLevel = ExternalAccessLevel.FullAccess });

        var rights = caller.GetEffectiveRights(projectId);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Create).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeTrue();
        rights.HasFlag(AccessRights.Delete).Should().BeTrue();
    }

    [Fact]
    public void GetEffectiveRights_ForUnknownProject_ReturnsNone()
    {
        var caller = Ciam(new CallerProjectAccess { ProjectId = Guid.NewGuid(), AccessLevel = ExternalAccessLevel.FullAccess });
        caller.GetEffectiveRights(Guid.NewGuid()).Should().Be(AccessRights.None);
    }

    [Theory]
    [InlineData(ExternalAccessLevel.ViewOnly, "ViewOnly")]
    [InlineData(ExternalAccessLevel.Collaborate, "Collaborate")]
    [InlineData(ExternalAccessLevel.FullAccess, "FullAccess")]
    public void MeProjection_CiamAccessLevel_MapsToSameStringAsLegacyHandler(
        ExternalAccessLevel level, string expectedString)
    {
        // The /me handler projects ProjectAccess → ProjectAccessEntry(ProjectId, AccessLevel.ToString()).
        // This is the exact mapping the old handler used from ExternalCallerContext.Participations, so
        // the CIAM /me payload is unchanged.
        var projectId = Guid.NewGuid();
        var caller = Ciam(new CallerProjectAccess { ProjectId = projectId, AccessLevel = level });

        var projects = caller.ProjectAccess
            .Select(p => new ProjectAccessEntry(p.ProjectId, p.AccessLevel.ToString()))
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
    public void GetEffectiveRights_WorkforceAccessibleProject_IsCollaborate()
    {
        var p1 = Guid.NewGuid();
        var caller = Workforce(p1);

        var rights = caller.GetEffectiveRights(p1);
        rights.HasFlag(AccessRights.Read).Should().BeTrue();
        rights.HasFlag(AccessRights.Create).Should().BeTrue();
        rights.HasFlag(AccessRights.Write).Should().BeTrue();
        rights.HasFlag(AccessRights.Delete).Should().BeFalse(
            "workforce collaboration grants Collaborate (no Delete) — documented decision for R2 to refine");
    }

    [Fact]
    public void WorkforceProjectAccessLevel_IsCollaborate()
    {
        // Guard the documented decision: changing the workforce within-project level is a deliberate
        // authorization change and must be a conscious edit (R2 coordination note item 3).
        WorkforcePrincipalStrategy.WorkforceProjectAccessLevel
            .Should().Be(ExternalAccessLevel.Collaborate);
    }
}
