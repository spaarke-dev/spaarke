// unified-access-control-r2 task 157, finding F1 — the external savedquery routes returned the INTERNAL MDA views of
// every registered entity (names, FetchXML, layoutXml) to any external caller, app-only.
//
// KEEP-path classification (ADR-038 §2 path #1 security-auth): every test here asserts an AUTHORIZATION decision on
// GET /api/v1/external/api/dataverse/savedquery/{id} or …/savedqueries/{entity} — which view definitions an external
// (CIAM contact / Teams workforce) caller may read. It lives under tests/integration/auth/** and compiles into
// Sprk.Bff.Api.Tests via the csproj auth glob, so InternalsVisibleTo reaches the two pipeline cores.
//
// THE RULE (owner decisions C6 / C9: a contact gets only what it is granted). The routes return ONLY the views an
// external module's grid is registered to use (ExternalModuleDescriptor.SavedQueryIds, on the same module registry as
// the column allow-lists), and the same 404 for everything else: an internal view of a registered entity, a view of
// another entity, an unknown id, and an entity with no module. Every production module registers NO view today (all
// six grid configurations are inline, read live 2026-10-02), so both routes answer 404 for every view.
//
// WHAT IS UNDER TEST: the REAL pipelines (ExternalModuleDataEndpoints.GetSavedQueryCoreAsync / GetSavedQueriesCoreAsync)
// and the REAL production registrations, with a test double for the ONE thing that cannot run in-process: the app-only
// Dataverse read (SavedQueryService needs a live ServiceClient). The doubles count their calls, so "Dataverse was not
// queried" is asserted, not inferred.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration assertions, no constructor
// null-checks. Names are {Method}_{Scenario}_{ExpectedResult}.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

public sealed class ExternalModuleSavedQueryScopeTests : IClassFixture<ExternalAccessContractFixture>
{
    private readonly ExternalAccessContractFixture _fixture;

    public ExternalModuleSavedQueryScopeTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Shared test data
    // ─────────────────────────────────────────────────────────────────────────────

    private const string ProjectEntity = "sprk_project";

    /// <summary>A view the test module's grid is registered to use.</summary>
    private static readonly Guid RegisteredView = Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001");

    /// <summary>An INTERNAL MDA view of the same entity: what the old view picker offered outside counsel.</summary>
    private static readonly Guid InternalProjectView = Guid.Parse("b2b2b2b2-0000-4000-8000-000000000002");

    /// <summary>A view of an entity no external module owns.</summary>
    private static readonly Guid AccountView = Guid.Parse("c3c3c3c3-0000-4000-8000-000000000003");

    private static SavedQueryDto View(string entity, string name) =>
        new(entity, $"<fetch><entity name='{entity}'><attribute name='ownerid'/></entity></fetch>", "<grid/>", name);

    private static ExternalModuleDescriptor ProjectsModule(params Guid[] views) => new()
    {
        Name = "collaboration",
        RecordEntity = ProjectEntity,
        RecordIdAttribute = "sprk_projectid",
        AccessibleRecordIds = p => p.GetAccessibleProjectIds().ToHashSet(),
        PrimaryNameAttribute = "sprk_projectnumber",
        ReadableColumns = new HashSet<string> { "sprk_projectid", "sprk_projectnumber" },
        SavedQueryIds = views.ToHashSet(),
    };

    private static ExternalModuleDescriptor MattersModule(params Guid[] views) => new()
    {
        Name = "matters",
        RecordEntity = "sprk_matter",
        RecordIdAttribute = "sprk_matterid",
        AccessibleRecordIds = p => p.GetAccessibleMatterIds(),
        PrimaryNameAttribute = "sprk_matternumber",
        ReadableColumns = new HashSet<string> { "sprk_matterid", "sprk_matternumber" },
        SavedQueryIds = views.ToHashSet(),
    };

    private static ExternalModuleRegistry Registry(params ExternalModuleDescriptor[] modules)
    {
        var registry = new ExternalModuleRegistry();
        foreach (var module in modules)
        {
            registry.Register(module);
        }
        return registry;
    }

    private ExternalModuleRegistry ProductionRegistry() =>
        _fixture.Services.GetRequiredService<ExternalModuleRegistry>();

    /// <summary>The app-only by-id view read, replaced. Counts its calls; returns the scripted view, or null.</summary>
    private sealed class ViewReadDouble
    {
        private readonly IReadOnlyDictionary<Guid, SavedQueryDto> _views;

        public ViewReadDouble(IReadOnlyDictionary<Guid, SavedQueryDto>? views = null) =>
            _views = views ?? new Dictionary<Guid, SavedQueryDto>();

        public int Calls { get; private set; }

        public Task<SavedQueryDto?> Load(Guid id, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_views.TryGetValue(id, out var view) ? view : null);
        }
    }

    /// <summary>The app-only per-entity view list, replaced. Same contract.</summary>
    private sealed class ViewListDouble
    {
        private readonly IReadOnlyList<SavedQuerySummaryDto> _views;

        public ViewListDouble(params SavedQuerySummaryDto[] views) => _views = views;

        public int Calls { get; private set; }

        public Task<IReadOnlyList<SavedQuerySummaryDto>> List(string entity, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_views);
        }
    }

    private static Task<IResult> GetView(ExternalModuleRegistry registry, Guid id, ViewReadDouble dataverse) =>
        ExternalModuleDataEndpoints.GetSavedQueryCoreAsync(id, registry, dataverse.Load, NullLogger.Instance, CancellationToken.None);

    private static Task<IResult> ListViews(ExternalModuleRegistry registry, string entity, ViewListDouble dataverse) =>
        ExternalModuleDataEndpoints.GetSavedQueriesCoreAsync(entity, registry, dataverse.List, NullLogger.Instance, CancellationToken.None);

    private static void ShouldBeNotFound(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode")
            .WhoseValue.Should().Be(ExternalModuleDataEndpoints.ErrorSavedQueryNotFound);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 1. GET /savedquery/{id}
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSavedQuery_WhenTheIdIsRegistered_Returns200WithTheView()
    {
        // Positive control: a registered view still loads, so the refusals below are the allow-list.
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [RegisteredView] = View(ProjectEntity, "Outside counsel projects") });

        var result = await GetView(Registry(ProjectsModule(RegisteredView)), RegisteredView, dataverse);

        result.Should().BeOfType<Ok<SavedQueryDto>>().Subject.Value!.Name.Should().Be("Outside counsel projects");
        dataverse.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetSavedQuery_WhenTheIdIsAnInternalViewOfARegisteredEntity_Returns404AndNeverReads()
    {
        // F1 verbatim: the entity HAS a module, but its grid is not registered to use this view.
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [InternalProjectView] = View(ProjectEntity, "All Projects (internal)") });

        var result = await GetView(Registry(ProjectsModule(RegisteredView)), InternalProjectView, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0, "the allow-list is checked before any Dataverse read");
    }

    [Fact]
    public async Task GetSavedQuery_WhenTheViewBelongsToAnotherEntity_Returns404AndNeverReads()
    {
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [AccountView] = View("account", "Active Accounts") });

        var result = await GetView(Registry(ProjectsModule(RegisteredView)), AccountView, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetSavedQuery_WhenARegisteredIdLoadsAViewOfAnotherEntity_Returns404()
    {
        // Defence in depth: a mis-registered id must not serve another entity's view definition.
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [RegisteredView] = View("account", "Active Accounts") });

        var result = await GetView(Registry(ProjectsModule(RegisteredView)), RegisteredView, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetSavedQuery_WhenTheRegisteredViewIsAbsent_ReturnsTheSame404()
    {
        var result = await GetView(Registry(ProjectsModule(RegisteredView)), RegisteredView, new ViewReadDouble());

        ShouldBeNotFound(result);
    }

    [Fact]
    public async Task GetSavedQuery_WhenAViewRegisteredByTheMattersModuleTargetsProjects_Returns404()
    {
        // The entity check is against the REGISTERING module: a matters registration cannot serve a project view.
        var matterView = Guid.Parse("d4d4d4d4-0000-4000-8000-000000000004");
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [matterView] = View(ProjectEntity, "Mislabelled") });

        var result = await GetView(Registry(ProjectsModule(RegisteredView), MattersModule(matterView)), matterView, dataverse);

        ShouldBeNotFound(result);
    }

    [Fact]
    public async Task GetSavedQuery_OnTheProductionRegistry_RefusesAnyViewWithoutReading()
    {
        // Every production grid is inline, so no view is registered: an internal view id gets 404 and no read.
        var dataverse = new ViewReadDouble(new Dictionary<Guid, SavedQueryDto> { [InternalProjectView] = View(ProjectEntity, "Active Projects") });

        var result = await GetView(ProductionRegistry(), InternalProjectView, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 2. GET /savedqueries/{entity}
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSavedQueries_WhenTheModuleRegistersViews_ReturnsOnlyThose()
    {
        var dataverse = new ViewListDouble(
            new SavedQuerySummaryDto(RegisteredView, "Outside counsel projects", IsDefault: false, QueryType: 0),
            new SavedQuerySummaryDto(InternalProjectView, "All Projects (internal)", IsDefault: true, QueryType: 0));

        var result = await ListViews(Registry(ProjectsModule(RegisteredView)), ProjectEntity, dataverse);

        var views = result.Should().BeOfType<Ok<IReadOnlyList<SavedQuerySummaryDto>>>().Subject.Value!;
        views.Select(v => v.Id).Should().Equal(RegisteredView);
        dataverse.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetSavedQueries_WhenTheEntityHasNoModule_Returns404AndNeverReads()
    {
        var dataverse = new ViewListDouble(new SavedQuerySummaryDto(AccountView, "Active Accounts", IsDefault: true, QueryType: 0));

        var result = await ListViews(Registry(ProjectsModule(RegisteredView)), "account", dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetSavedQueries_WhenTheModuleRegistersNoView_Returns404AndNeverReads()
    {
        var dataverse = new ViewListDouble(new SavedQuerySummaryDto(InternalProjectView, "All Projects (internal)", IsDefault: true, QueryType: 0));

        var result = await ListViews(Registry(ProjectsModule()), ProjectEntity, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0);
    }

    public static TheoryData<string> ProductionEntities => new()
    {
        "sprk_project", "sprk_document", "sprk_invoice", "sprk_workassignment", "sprk_matter", "sprk_servicerequest",
        "sprk_gridconfiguration",
    };

    [Theory]
    [MemberData(nameof(ProductionEntities))]
    public async Task GetSavedQueries_OnTheProductionRegistry_Returns404ForEveryModuleEntityWithoutReading(string entity)
    {
        var dataverse = new ViewListDouble(new SavedQuerySummaryDto(InternalProjectView, "Active", IsDefault: true, QueryType: 0));

        var result = await ListViews(ProductionRegistry(), entity, dataverse);

        ShouldBeNotFound(result);
        dataverse.Calls.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 3. PRODUCTION REGISTRATIONS ↔ LIVE DATA (dev, read 2026-10-02)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Each module's registered views as derived from LIVE data (2026-10-02, read-only): the sprk_gridconfiguration
    /// source of its grid. All six grids are source.type = "inline" (no savedQueryId); grid-configuration has no grid.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Guid[]> LiveDerivedSavedQueryIds = new Dictionary<string, Guid[]>
    {
        ["sprk_project"] = Array.Empty<Guid>(),
        ["sprk_document"] = Array.Empty<Guid>(),
        ["sprk_invoice"] = Array.Empty<Guid>(),
        ["sprk_workassignment"] = Array.Empty<Guid>(),
        ["sprk_matter"] = Array.Empty<Guid>(),
        ["sprk_servicerequest"] = Array.Empty<Guid>(),
        ["sprk_gridconfiguration"] = Array.Empty<Guid>(),
    };

    [Theory]
    [MemberData(nameof(ProductionEntities))]
    public void ProductionModule_ForEachRegisteredEntity_RegistersExactlyTheLiveDerivedViews(string entity)
    {
        var module = ProductionRegistry().FindByEntity(entity);

        module.Should().NotBeNull($"'{entity}' is a registered external module");
        module!.SavedQueryIds.Should().BeEquivalentTo(LiveDerivedSavedQueryIds[entity],
            "a module registers only the views its own grid configuration names (inline grids name none)");
    }

    [Fact]
    public void ProductionRegistry_EveryRegisteredModule_HasALiveViewDerivation()
    {
        // A module added later must record its view derivation here, or the pin above proves nothing for it.
        ProductionRegistry().Modules.Select(m => m.RecordEntity).Should().BeSubsetOf(LiveDerivedSavedQueryIds.Keys);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4. REGISTRATION — refuses an unsafe view list at startup
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Register_WhenAViewIdIsEmpty_Throws()
    {
        var act = () => Registry(ProjectsModule(Guid.Empty));

        act.Should().Throw<InvalidOperationException>().WithMessage("*SavedQueryIds contains Guid.Empty*");
    }

    [Fact]
    public void Register_WhenTwoModulesRegisterTheSameView_Throws()
    {
        var act = () => Registry(ProjectsModule(RegisteredView), MattersModule(RegisteredView));

        act.Should().Throw<InvalidOperationException>().WithMessage("*already registered by external module 'collaboration'*");
    }

    [Fact]
    public void Register_WhenNoViewIsDeclared_RegistersNone()
    {
        // The default is fail-closed: no list means no view, never "every view of the entity".
        var module = new ExternalModuleDescriptor
        {
            Name = "collaboration",
            RecordEntity = ProjectEntity,
            RecordIdAttribute = "sprk_projectid",
            AccessibleRecordIds = p => p.GetAccessibleProjectIds().ToHashSet(),
            PrimaryNameAttribute = "sprk_projectnumber",
            ReadableColumns = new HashSet<string> { "sprk_projectid", "sprk_projectnumber" },
        };

        var registry = Registry(module);

        module.SavedQueryIds.Should().BeEmpty();
        registry.FindBySavedQueryId(InternalProjectView).Should().BeNull();
    }
}
