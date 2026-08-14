using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.Infrastructure.ExternalAccess;

/// <summary>
/// Domain-logic tests for the FR-22 module-framework registration seam (ADR-028 A3): the
/// <see cref="ExternalModuleRegistry"/> + the per-module Tier-2 predicate on
/// <see cref="ExternalModuleDescriptor"/> consumed by the BffDataverseClient widget-data read group.
///
/// These lock the two load-bearing guarantees the widget-data seam depends on:
///   (1) TIER-2 SCOPE (R2 NFR-08): a module read returns ONLY the caller's accessible records — a
///       non-participant (empty accessible set) receives NOTHING, never the unscoped rows/record.
///   (2) PLANE-AGNOSTIC: the predicate operates on the resolved <see cref="CallerPrincipal"/> only, so
///       a CIAM caller and a workforce caller with the same accessible set are scoped identically —
///       no branch on plane / scheme / iss / tid (ADR-028 A3).
/// Plus the registration contract: an additional module plugs in additively; a duplicate name/entity
/// fails fast (a wiring bug must not silently overwrite a Tier-2 predicate).
/// </summary>
public class ExternalModuleRegistryTests
{
    private const string ProjectEntity = "sprk_project";
    private const string ProjectIdAttr = "sprk_projectid";

    private static ExternalModuleDescriptor CollaborationModule() => new()
    {
        Name = "collaboration",
        RecordEntity = ProjectEntity,
        RecordIdAttribute = ProjectIdAttr,
        AccessibleRecordIds = principal => principal.GetAccessibleProjectIds().ToHashSet(),
    };

    private static CallerPrincipal Ciam(params Guid[] projects) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@test.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = projects
            .Select(id => new CallerProjectAccess { ProjectId = id, AccessLevel = ExternalAccessLevel.Collaborate })
            .ToList(),
    };

    private static CallerPrincipal Workforce(params Guid[] projects) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = Guid.NewGuid(),
        Email = "staff@contoso.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = projects
            .Select(id => new CallerProjectAccess
            {
                ProjectId = id,
                AccessLevel = WorkforcePrincipalStrategy.WorkforceProjectAccessLevel,
            })
            .ToList(),
    };

    private static IReadOnlyDictionary<string, object?> Row(Guid id, string entity = ProjectEntity) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectIdAttr] = id,
            ["@logicalName"] = entity,
            ["sprk_name"] = "Project " + id.ToString("N")[..6],
        };

    // ── Registration contract ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Register_ThenFindByEntity_ResolvesTheModule()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(CollaborationModule());

        registry.FindByEntity(ProjectEntity).Should().NotBeNull();
        registry.FindByEntity(ProjectEntity)!.Name.Should().Be("collaboration");
        registry.FindByName("collaboration").Should().NotBeNull();
        registry.Modules.Should().HaveCount(1);
    }

    [Fact]
    public void FindByEntity_WhenEntityUnregistered_ReturnsNull()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(CollaborationModule());

        // A second module (e.g. task 016's matter widget) plugs in additively; an unregistered entity
        // resolves to null so the read group can fail-closed (deny, not serve unscoped).
        registry.FindByEntity("sprk_matter").Should().BeNull();
    }

    [Fact]
    public void Register_AdditionalDistinctModule_IsPurelyAdditive()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(CollaborationModule());
        registry.Register(new ExternalModuleDescriptor
        {
            Name = "matters",
            RecordEntity = "sprk_matter",
            RecordIdAttribute = "sprk_matterid",
            AccessibleRecordIds = _ => new HashSet<Guid>(),
        });

        registry.Modules.Should().HaveCount(2);
        registry.FindByEntity("sprk_matter").Should().NotBeNull();
        registry.FindByEntity(ProjectEntity).Should().NotBeNull();
    }

    [Fact]
    public void Register_DuplicateModuleName_ThrowsFailFast()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(CollaborationModule());

        var act = () => registry.Register(CollaborationModule());
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Register_DuplicateRecordEntity_ThrowsFailFast()
    {
        var registry = new ExternalModuleRegistry();
        registry.Register(CollaborationModule());

        var act = () => registry.Register(new ExternalModuleDescriptor
        {
            Name = "collaboration-alt",
            RecordEntity = ProjectEntity, // same entity → a Tier-2 predicate collision is a wiring bug
            RecordIdAttribute = ProjectIdAttr,
            AccessibleRecordIds = _ => new HashSet<Guid>(),
        });
        act.Should().Throw<InvalidOperationException>();
    }

    // ── Tier-2 fetch-row scoping (NFR-08) ───────────────────────────────────────────────────────────

    [Fact]
    public void ScopeRows_KeepsOnlyRowsInTheCallersAccessibleSet()
    {
        var accessible = Guid.NewGuid();
        var other = Guid.NewGuid();
        var module = CollaborationModule();
        var principal = Ciam(accessible);

        var scoped = module.ScopeRows(principal, new[] { Row(accessible), Row(other) });

        scoped.Should().HaveCount(1);
        scoped[0][ProjectIdAttr].Should().Be(accessible);
    }

    [Fact]
    public void ScopeRows_NonParticipant_ReturnsEmpty()
    {
        var module = CollaborationModule();
        var principal = Ciam(); // no accessible projects
        var rows = new[] { Row(Guid.NewGuid()), Row(Guid.NewGuid()) };

        // NFR-08 negative case: authenticated but no participation → NOTHING, not the unscoped rows.
        module.ScopeRows(principal, rows).Should().BeEmpty();
    }

    [Fact]
    public void ScopeRows_DropsRowsWhoseEntityDiffersFromTheModule()
    {
        var accessible = Guid.NewGuid();
        var module = CollaborationModule();
        var principal = Ciam(accessible);

        // A row carrying an accessible id BUT a different @logicalName must not leak (defense-in-depth
        // against a FetchXML whose primary entity differs from the claimed module entity).
        var foreignRow = Row(accessible, entity: "sprk_matter");

        module.ScopeRows(principal, new[] { foreignRow }).Should().BeEmpty();
    }

    [Fact]
    public void ScopeRows_IsPlaneAgnostic_SameSetScopesIdenticallyForCiamAndWorkforce()
    {
        var accessible = Guid.NewGuid();
        var other = Guid.NewGuid();
        var module = CollaborationModule();
        var rows = new[] { Row(accessible), Row(other) };

        var ciamScoped = module.ScopeRows(Ciam(accessible), rows);
        var workforceScoped = module.ScopeRows(Workforce(accessible), rows);

        ciamScoped.Should().HaveCount(1);
        workforceScoped.Should().HaveCount(1);
        ciamScoped[0][ProjectIdAttr].Should().Be(accessible);
        workforceScoped[0][ProjectIdAttr].Should().Be(accessible);
    }

    [Fact]
    public void ScopeRows_ParsesStringGuidPrimaryId()
    {
        var accessible = Guid.NewGuid();
        var module = CollaborationModule();
        var principal = Ciam(accessible);

        var stringIdRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [ProjectIdAttr] = accessible.ToString(), // string form (some projections stringify ids)
            ["@logicalName"] = ProjectEntity,
        };

        module.ScopeRows(principal, new[] { stringIdRow }).Should().HaveCount(1);
    }

    // ── Tier-2 single-record gate (NFR-08) ──────────────────────────────────────────────────────────

    [Fact]
    public void IsRecordAccessible_ForAccessibleRecord_ReturnsTrue()
    {
        var accessible = Guid.NewGuid();
        var module = CollaborationModule();

        module.IsRecordAccessible(Ciam(accessible), accessible).Should().BeTrue();
    }

    [Fact]
    public void IsRecordAccessible_ForRecordOutsideSet_ReturnsFalse()
    {
        var accessible = Guid.NewGuid();
        var module = CollaborationModule();

        module.IsRecordAccessible(Ciam(accessible), Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void IsRecordAccessible_ForEmptyGuid_ReturnsFalse()
    {
        var module = CollaborationModule();

        module.IsRecordAccessible(Ciam(Guid.NewGuid()), Guid.Empty).Should().BeFalse();
    }

    // ── task 028: polymorphic multi-dimension (OR) child scoping ──────────────────────────────────

    // Documents module: a row is accessible if it rolls up to an accessible project OR matter OR
    // work assignment (each dimension reads a typed parent lookup on the row).
    private static ExternalModuleDescriptor DocumentsModule() => new()
    {
        Name = "documents",
        RecordEntity = "sprk_document",
        ScopeDimensions = new[]
        {
            new ScopeDimension { Attribute = "sprk_project", AccessibleIds = p => p.GetAccessibleProjectIds().ToHashSet() },
            new ScopeDimension { Attribute = "sprk_matter", AccessibleIds = p => p.GetAccessibleMatterIds() },
            new ScopeDimension { Attribute = "sprk_workassignment", AccessibleIds = p => p.GetAccessibleWorkAssignmentIds() },
        },
    };

    private static CallerPrincipal PolymorphicCiam(
        IEnumerable<Guid>? projects = null, IEnumerable<Guid>? matters = null, IEnumerable<Guid>? was = null) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@test.com",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = (projects ?? Array.Empty<Guid>())
            .Select(id => new CallerProjectAccess { ProjectId = id, AccessLevel = ExternalAccessLevel.Collaborate }).ToList(),
        AccessibleMatterIds = (matters ?? Array.Empty<Guid>()).ToHashSet(),
        AccessibleWorkAssignmentIds = (was ?? Array.Empty<Guid>()).ToHashSet(),
    };

    // A document row projecting its typed parent lookups as EntityReference (the SDK's shape).
    private static IReadOnlyDictionary<string, object?> DocRow(
        Guid? project = null, Guid? matter = null, Guid? wa = null) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_documentid"] = Guid.NewGuid(),
            ["@logicalName"] = "sprk_document",
            ["sprk_project"] = project is { } pid ? new EntityReference("sprk_project", pid) : null,
            ["sprk_matter"] = matter is { } mid ? new EntityReference("sprk_matter", mid) : null,
            ["sprk_workassignment"] = wa is { } wid ? new EntityReference("sprk_workassignment", wid) : null,
        };

    [Fact]
    public void ScopeRows_Documents_KeepsRowMatchingAnyAccessibleParent()
    {
        var project = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var wa = Guid.NewGuid();
        var module = DocumentsModule();
        var principal = PolymorphicCiam(projects: new[] { project }, matters: new[] { matter }, was: new[] { wa });

        var rows = new[]
        {
            DocRow(project: project),            // via project root
            DocRow(matter: matter),              // via matter root
            DocRow(wa: wa),                      // via work-assignment root
            DocRow(project: Guid.NewGuid()),     // unrelated → excluded
        };

        var scoped = module.ScopeRows(principal, rows);

        scoped.Should().HaveCount(3, "documents rolling up to ANY accessible root are kept; the unrelated one is dropped");
    }

    [Fact]
    public void ScopeRows_Documents_MatterLinkedDoc_VisibleToMatterGrantedCaller_SupersedesProjectOnlyHide()
    {
        // The concrete over-hide task 028 fixes: a matter-parented document (no project link) was hidden
        // by the R1 project-only scope. It must now appear for a caller granted its matter.
        var matter = Guid.NewGuid();
        var module = DocumentsModule();
        var principal = PolymorphicCiam(matters: new[] { matter }); // matter grant only, no projects

        var scoped = module.ScopeRows(principal, new[] { DocRow(matter: matter) });

        scoped.Should().HaveCount(1);
    }

    [Fact]
    public void ScopeRows_Documents_NonParticipant_ReturnsEmpty()
    {
        var module = DocumentsModule();
        var principal = PolymorphicCiam(); // no accessible roots of any type

        module.ScopeRows(principal, new[] { DocRow(project: Guid.NewGuid()), DocRow(matter: Guid.NewGuid()) })
            .Should().BeEmpty("fail-closed: every dimension empty → nothing (NFR-08)");
    }
}
