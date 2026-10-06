// unified-access-control-r2 task 134 — defect C6 (Medium): the external module read seam had ROW scope but
// no COLUMN scope.
//
// KEEP-path classification (ADR-038 §2 path #1 security-auth): every test here asserts an AUTHORIZATION
// decision on POST /api/v1/external/api/dataverse/fetch or GET …/record — what an external caller may READ —
// so the file lives under tests/integration/auth/**. It compiles into Sprk.Bff.Api.Tests via the csproj auth
// glob, so InternalsVisibleTo reaches the guard and the two pipeline cores.
//
// THE DEFECT. Both routes execute app-only, which bypasses Dataverse field-level security, and both let the
// caller choose the columns (FetchXML / $select). Tier-2 scoping filtered ROWS only. So a contact granted on
// one project could read sprk_graphdriveid / sprk_graphitemid / sprk_filepath — the SPE drive id, item id and
// Graph webUrl — of every document on it, plus any internal column of an in-scope row.
//
// WHAT IS UNDER TEST, AND HOW.
//   • The REAL guard (ExternalModuleDataEndpoints.EvaluateFetchXmlGuard) with the REAL entity extractor —
//     never a transcription of it (see FetchXmlGuardSelfJoinTests' header for why that matters here).
//   • The REAL fetch / record pipelines (…CoreAsync), driven with a test double for the ONE thing that
//     cannot run in-process: the app-only Dataverse read. The double counts its calls, so "Dataverse was not
//     queried" is asserted, not inferred, and it can return attributes nobody asked for, which is the only
//     way to exercise the post-execution strip.
//   • The REAL production registrations (ExternalAccessModule), checked against the LIVE grid FetchXML and
//     the LIVE primary-name metadata read on 2026-09-30 (notes/task-134-external-module-column-allow-list.md).
//   • Task 157 (section 6): the lists shrank once the external grids lost their view selector; each list is
//     pinned to its live re-derivation (2026-10-01) and every dropped column is proven unreadable on both
//     routes (notes/task-157-external-grid-columns.md).
//
// Every assertion on a refusal pins the exact verdict / errorCode. The guard has three ordered signals and
// the pipelines have two defences; asserting only "refused" would stay green if one of them were deleted.
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration assertions, no
// constructor null-checks. Names are {Method}_{Scenario}_{ExpectedResult}.

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

using Verdict = ExternalModuleDataEndpoints.FetchXmlGuardVerdict;

public sealed class ExternalModuleColumnAllowListTests : IClassFixture<ExternalAccessContractFixture>
{
    private readonly ExternalAccessContractFixture _fixture;

    public ExternalModuleColumnAllowListTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Shared test data
    // ─────────────────────────────────────────────────────────────────────────────

    private const string DocumentEntity = "sprk_document";
    private const string ProjectEntity = "sprk_project";

    /// <summary>The production sprk_document allow-list (ExternalAccessModule, task 134 derivation).</summary>
    private static readonly IReadOnlySet<string> DocumentColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_documentid", "sprk_documentname", "sprk_documenttype", "createdon",
        "sprk_project", "sprk_matter", "sprk_workassignment",
    };

    /// <summary>The live outside-counsel Documents grid FetchXML (sprk_gridconfiguration 3af4102c, 2026-09-30).</summary>
    private const string LiveDocumentsGridFetchXml =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>" +
        "<attribute name='sprk_documenttype'/><attribute name='createdon'/><attribute name='sprk_project'/>" +
        "<attribute name='sprk_matter'/><attribute name='sprk_workassignment'/>" +
        "<order attribute='createdon' descending='true'/></entity></fetch>";

    private static ExternalModuleDataEndpoints.FetchXmlGuardResult EvaluateDocuments(string? fetchXml) =>
        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            fetchXml, DocumentEntity, DocumentColumns, new FetchXmlEntityExtractor());

    /// <summary>A documents module identical in scope and columns to the production one.</summary>
    private static ExternalModuleDescriptor DocumentsModule() => DocumentsModuleWithColumns(DocumentColumns);

    /// <summary>The same module with an arbitrary column list — <c>null</c> is passed through as-is — and,
    /// optionally, an arbitrary primary-name declaration (default: the live <c>sprk_documentname</c>).</summary>
    private static ExternalModuleDescriptor DocumentsModuleWithColumns(
        IReadOnlySet<string>? columns, string primaryName = "sprk_documentname") => new()
    {
        Name = "documents",
        RecordEntity = DocumentEntity,
        PrimaryNameAttribute = primaryName,
        ScopeDimensions = new[]
        {
            new ScopeDimension { Attribute = "sprk_project", AccessibleIds = p => p.GetAccessibleProjectIds().ToHashSet() },
            new ScopeDimension { Attribute = "sprk_matter", AccessibleIds = p => p.GetAccessibleMatterIds() },
            new ScopeDimension { Attribute = "sprk_workassignment", AccessibleIds = p => p.GetAccessibleWorkAssignmentIds() },
        },
        ReadableColumns = columns!,
    };

    private static ExternalModuleDescriptor ProjectsModule() => new()
    {
        Name = "collaboration",
        RecordEntity = ProjectEntity,
        RecordIdAttribute = "sprk_projectid",
        AccessibleRecordIds = p => p.GetAccessibleProjectIds().ToHashSet(),
        PrimaryNameAttribute = "sprk_projectnumber",
        ReadableColumns = new HashSet<string> { "sprk_projectid", "sprk_projectname", "sprk_projectnumber" },
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

    private static CallerPrincipal Ciam(params Guid[] projects) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@lawfirm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = projects
            .Select(id => CallerProjectAccess.FromLevel(id, ExternalAccessLevel.Collaborate))
            .ToList(),
    };

    /// <summary>
    /// The app-only fetch, replaced. Records every call and the FetchXML it was handed; returns a scripted
    /// result. A pipeline that refuses must leave <see cref="Calls"/> at zero.
    /// </summary>
    private sealed class FetchDouble
    {
        private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _rows;

        public FetchDouble(params IReadOnlyDictionary<string, object?>[] rows) => _rows = rows;

        public int Calls { get; private set; }
        public string? ExecutedFetchXml { get; private set; }

        public Task<FetchResponseDto> Execute(FetchRequestDto request, CancellationToken ct)
        {
            Calls++;
            ExecutedFetchXml = request.FetchXml;
            return Task.FromResult(new FetchResponseDto(_rows, MoreRecords: false, PagingCookie: null));
        }
    }

    /// <summary>The app-only single-record read, replaced. Same contract as <see cref="FetchDouble"/>.</summary>
    private sealed class RecordDouble
    {
        private readonly IReadOnlyDictionary<string, object?> _record;

        public RecordDouble(IReadOnlyDictionary<string, object?> record) => _record = record;

        public int Calls { get; private set; }
        public string[]? RequestedColumns { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>> Read(string entity, Guid id, string[]? columns, CancellationToken ct)
        {
            Calls++;
            RequestedColumns = columns;
            return Task.FromResult(_record);
        }
    }

    private static IReadOnlyDictionary<string, object?> DocumentRow(Guid project, params (string Key, object? Value)[] extra)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_documentid"] = Guid.NewGuid(),
            ["sprk_documentname"] = "Engagement letter.docx",
            ["sprk_project"] = new EntityReference("sprk_project", project),
            ["@logicalName"] = DocumentEntity,
        };
        foreach (var (key, value) in extra)
        {
            row[key] = value;
        }
        return row;
    }

    private static Task<IResult> Fetch(ExternalModuleRegistry registry, CallerPrincipal principal, string fetchXml, FetchDouble dataverse) =>
        ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(DocumentEntity, fetchXml, PagingCookie: null),
            principal, registry, new FetchXmlEntityExtractor(), dataverse.Execute,
            NullLogger.Instance, CancellationToken.None);

    private static void ShouldBeProblem(IResult result, int status, string errorCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Extensions.Should().ContainKey("errorCode")
            .WhoseValue.Should().Be(errorCode);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 1. THE GUARD'S COLUMN SIGNAL — every FetchXML position
    // ═════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("sprk_graphdriveid")]
    [InlineData("sprk_graphitemid")]
    [InlineData("sprk_filepath")]
    public void EvaluateFetchXmlGuard_WhenFetchSelectsAnSpePointer_RefusesAsColumnNotPermitted(string pointer)
    {
        // C6 verbatim: the scope column is projected (trivial for the caller) so the row would survive
        // ScopeRows and carry the pointer out.
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>" +
                       $"<attribute name='sprk_project'/><attribute name='{pointer}'/></entity></fetch>";

        var result = EvaluateDocuments(fetchXml);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
        result.ColumnViolation.Should().Contain(pointer);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenFetchUsesAllAttributes_RefusesAsColumnNotPermitted()
    {
        var result = EvaluateDocuments("<fetch><entity name='sprk_document'><all-attributes/></entity></fetch>");

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted, "all-attributes is every column, pointers included");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenHiddenColumnIsUsedOnlyInACondition_RefusesAsColumnNotPermitted()
    {
        // Selects only readable columns, but FILTERS on a hidden one — a value oracle: the row count answers
        // "does this document's Graph item id start with X?".
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>" +
                       "<filter><condition attribute='sprk_graphitemid' operator='like' value='01A%'/></filter>" +
                       "</entity></fetch>";

        var result = EvaluateDocuments(fetchXml);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
        result.ColumnViolation.Should().Contain("condition").And.Contain("sprk_graphitemid");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenHiddenColumnIsUsedOnlyAsAConditionValueOf_RefusesAsColumnNotPermitted()
    {
        // Column-comparison condition: the hidden column hides in valueof=, not attribute=.
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>" +
                       "<filter><condition attribute='sprk_documentname' operator='eq' valueof='sprk_filepath'/></filter>" +
                       "</entity></fetch>";

        var result = EvaluateDocuments(fetchXml);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
        result.ColumnViolation.Should().Contain("valueof").And.Contain("sprk_filepath");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenHiddenColumnIsUsedOnlyInAnOrder_RefusesAsColumnNotPermitted()
    {
        // Sorting by a hidden column leaks its ordering (a binary-search oracle across pages).
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>" +
                       "<order attribute='sprk_graphdriveid' descending='false'/></entity></fetch>";

        var result = EvaluateDocuments(fetchXml);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
        result.ColumnViolation.Should().Contain("order").And.Contain("sprk_graphdriveid");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenHiddenColumnIsUsedOnlyAsAnAliasedAttribute_RefusesAsColumnNotPermitted()
    {
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/>" +
                       "<attribute name='sprk_graphitemid' alias='documentname'/></entity></fetch>";

        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }

    /// <summary>
    /// Isolates the ALIAS check: the aliased column IS readable, so only the alias rule can refuse. An alias
    /// would put a column's value under a key the strip does not recognise — the reject-alias rule is what
    /// stops the alias being used to rename anything, and removing it turns this test red.
    /// </summary>
    [Fact]
    public void EvaluateFetchXmlGuard_WhenAReadableColumnIsAliased_RefusesOnTheAliasRuleAlone()
    {
        var fetchXml = "<fetch><entity name='sprk_document'>" +
                       "<attribute name='sprk_documentname' alias='sprk_project'/></entity></fetch>";

        var result = EvaluateDocuments(fetchXml);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
        result.ColumnViolation.Should().StartWith("aliased attribute");
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenHiddenColumnIsUsedOnlyInAnAggregate_RefusesAsColumnNotPermitted()
    {
        // The shape Dataverse requires for an aggregate (aggregate fetch + aliased aggregate attribute).
        var fetchXml = "<fetch aggregate='true'><entity name='sprk_document'>" +
                       "<attribute name='sprk_graphitemid' aggregate='countcolumn' alias='n'/></entity></fetch>";

        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }

    [Theory]
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid' groupby='true'/></entity></fetch>")]
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid' aggregate='count'/></entity></fetch>")]
    [InlineData("<fetch aggregate='true'><entity name='sprk_document'><attribute name='sprk_documentid'/></entity></fetch>")]
    public void EvaluateFetchXmlGuard_WhenAReadableColumnIsAggregatedOrGrouped_Refuses(string fetchXml)
    {
        // Each aggregate marker is refused on its own, even without an alias and on a readable column.
        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }

    [Theory]
    // Casing / namespace variants of a column-bearing element must be inspected, not skipped.
    [InlineData("<fetch><entity name='sprk_document'><ATTRIBUTE name='sprk_graphitemid'/></entity></fetch>")]
    [InlineData("<fetch xmlns:x='urn:t'><entity name='sprk_document'><x:attribute name='sprk_graphitemid'/></entity></fetch>")]
    [InlineData("<fetch><entity name='sprk_document'><All-Attributes/></entity></fetch>")]
    // An element the guard does not model is refused, not assumed harmless (ADR-003).
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><projection name='sprk_filepath'/></entity></fetch>")]
    // A column-bearing element that names no column cannot be proven readable.
    [InlineData("<fetch><entity name='sprk_document'><attribute/></entity></fetch>")]
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><filter><condition operator='not-null'/></filter></entity></fetch>")]
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><order descending='true'/></entity></fetch>")]
    // A condition or order that points at a link alias — there are none on this surface.
    [InlineData("<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><filter><condition entityname='x' attribute='sprk_documentid' operator='not-null'/></filter></entity></fetch>")]
    public void EvaluateFetchXmlGuard_WhenAColumnCannotBeProvenReadable_FailsClosed(string fetchXml)
    {
        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenTheModuleHasNoAllowList_RefusesEvenAPlainRead()
    {
        // Registration forbids this; the guard must not be the place a missing list becomes "all columns".
        const string plain = "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/></entity></fetch>";
        var extractor = new FetchXmlEntityExtractor();

        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(plain, DocumentEntity, null, extractor)
            .Verdict.Should().Be(Verdict.ColumnNotPermitted);
        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(plain, DocumentEntity, new HashSet<string>(), extractor)
            .Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenTheLiveDocumentsGridFetchIsSubmitted_Allows()
    {
        // Regression: the grid an outside-counsel user actually loads must still pass, filters and sort included.
        EvaluateDocuments(LiveDocumentsGridFetchXml).Verdict.Should().Be(Verdict.Allowed);

        var withCondition = LiveDocumentsGridFetchXml.Replace(
            "<order", "<filter><condition attribute='sprk_documenttype' operator='eq' value='100000000'/></filter><order",
            StringComparison.Ordinal);
        EvaluateDocuments(withCondition).Verdict.Should().Be(Verdict.Allowed,
            "a filter on a readable column (a grid header filter) is a legitimate read");
    }

    // ── Guard ORDER: the column signal is third and must not mask the first two ──

    [Fact]
    public void EvaluateFetchXmlGuard_WhenCrossEntityAndAForbiddenColumn_StillReportsEntityMismatch()
    {
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_graphitemid'/>" +
                       "<link-entity name='sprk_matter' from='sprk_matterid' to='sprk_matter' alias='m'>" +
                       "<attribute name='sprk_containerid'/></link-entity></entity></fetch>";

        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.EntityMismatch);
    }

    [Fact]
    public void EvaluateFetchXmlGuard_WhenSelfJoinAndAForbiddenColumn_StillReportsLinkEntityNotPermitted()
    {
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_graphitemid'/>" +
                       "<link-entity name='sprk_document' from='statecode' to='statecode' alias='leak'>" +
                       "<attribute name='sprk_filepath'/></link-entity></entity></fetch>";

        EvaluateDocuments(fetchXml).Verdict.Should().Be(Verdict.LinkEntityNotPermitted);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 2. THE FETCH PIPELINE — refusal before execution, regression, strip after
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExecuteScopedFetch_WhenGrantedCallerRequestsSpePointers_Returns400AndNeverQueriesDataverse()
    {
        var project = Guid.NewGuid();
        var dataverse = new FetchDouble(DocumentRow(project, ("sprk_graphitemid", "01ABCDEF")));
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_project'/>" +
                       "<attribute name='sprk_graphdriveid'/><attribute name='sprk_graphitemid'/>" +
                       "<attribute name='sprk_filepath'/></entity></fetch>";

        var result = await Fetch(Registry(DocumentsModule()), Ciam(project), fetchXml, dataverse);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
        dataverse.Calls.Should().Be(0, "the column guard runs before the app-only read");
    }

    [Fact]
    public async Task ExecuteScopedFetch_WhenOnlyReadableColumnsAreRequested_ReturnsTheSameScopedRowsAsBefore()
    {
        var inScope = Guid.NewGuid();
        var outOfScope = Guid.NewGuid();
        var dataverse = new FetchDouble(DocumentRow(inScope), DocumentRow(outOfScope));

        var result = await Fetch(Registry(DocumentsModule()), Ciam(inScope), LiveDocumentsGridFetchXml, dataverse);

        var rows = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities;
        rows.Should().ContainSingle("Tier-2 scoping still drops the out-of-scope row");
        ((EntityReference)rows[0]["sprk_project"]!).Id.Should().Be(inScope);
        rows[0]["sprk_documentname"].Should().Be("Engagement letter.docx");
        dataverse.Calls.Should().Be(1);
        dataverse.ExecutedFetchXml.Should().Contain(inScope.ToString("D"),
            "the server-side Tier-2 filter is still injected before execution");
    }

    /// <summary>
    /// Defence in depth. The guard admitted the request, but the read returned more than was asked for —
    /// a pointer column and its display value, and an aliased key. Both are stripped; the synthetic
    /// <c>@logicalName</c> and the readable columns (with their display values) survive.
    /// </summary>
    [Fact]
    public async Task ExecuteScopedFetch_WhenTheResultCarriesNonReadableKeys_StripsThemAndKeepsLogicalName()
    {
        var project = Guid.NewGuid();
        var row = DocumentRow(project,
            ("sprk_graphitemid", "01ABCDEF"),
            ("leak.sprk_filepath", new AliasedValue("sprk_document", "sprk_filepath", "https://contoso.sharepoint/x")),
            ("@formattedValues", new Dictionary<string, string>
            {
                ["sprk_project"] = "Project Alpha",
                ["sprk_graphitemid"] = "01ABCDEF",
            }));
        var dataverse = new FetchDouble(row);

        var result = await Fetch(Registry(DocumentsModule()), Ciam(project), LiveDocumentsGridFetchXml, dataverse);

        var projected = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities.Should().ContainSingle().Subject;
        projected.Should().NotContainKey("sprk_graphitemid");
        projected.Should().NotContainKey("leak.sprk_filepath");
        projected.Should().ContainKey("@logicalName").WhoseValue.Should().Be(DocumentEntity);
        projected.Should().ContainKey("sprk_documentname");
        var formatted = projected["@formattedValues"].Should().BeAssignableTo<IReadOnlyDictionary<string, string>>().Subject;
        formatted.Should().ContainKey("sprk_project");
        formatted.Should().NotContainKey("sprk_graphitemid", "a display value is a column value too");
    }

    // ── Authorization negative: empty accessible set ──

    [Fact]
    public async Task ExecuteScopedFetch_WhenCallerHasNoAccessAndRequestsReadableColumns_Returns0RowsWithoutQuerying()
    {
        var dataverse = new FetchDouble(DocumentRow(Guid.NewGuid()));

        var result = await Fetch(Registry(DocumentsModule()), Ciam(), LiveDocumentsGridFetchXml, dataverse);

        result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities.Should().BeEmpty();
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteScopedFetch_WhenCallerHasNoAccessAndRequestsAPointer_ReturnsTheSame400WithoutQuerying()
    {
        var dataverse = new FetchDouble(DocumentRow(Guid.NewGuid()));
        var fetchXml = "<fetch><entity name='sprk_document'><attribute name='sprk_graphitemid'/></entity></fetch>";

        var result = await Fetch(Registry(DocumentsModule()), Ciam(), fetchXml, dataverse);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
        dataverse.Calls.Should().Be(0, "the guard runs before scoping, so an empty set changes nothing about the refusal");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 3. THE RECORD PIPELINE — $select refused, default projection, strip
    // ═════════════════════════════════════════════════════════════════════════════

    private static Task<IResult> Record(Guid project, string? select, RecordDouble dataverse, params Guid[] accessible) =>
        ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            ProjectEntity, project, select, Ciam(accessible), Registry(ProjectsModule()), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

    private static IReadOnlyDictionary<string, object?> ProjectRecord(Guid id) =>
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = id,
            ["sprk_projectid"] = id,
            ["sprk_projectnumber"] = "PRJ-0042",
            ["sprk_projectname"] = "Alpha diligence",
            ["sprk_containerid"] = "b!container-pointer",
        };

    [Fact]
    public async Task GetScopedRecord_WhenSelectNamesANonReadableColumn_Returns400AndNeverReads()
    {
        var project = Guid.NewGuid();
        var dataverse = new RecordDouble(ProjectRecord(project));

        var result = await Record(project, "sprk_projectname,sprk_containerid", dataverse, project);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorRecordColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public async Task GetScopedRecord_WhenSelectIsReadable_Returns200WithOnlyReadableKeys()
    {
        var project = Guid.NewGuid();
        var dataverse = new RecordDouble(ProjectRecord(project)); // returns more than was asked for

        var result = await Record(project, "sprk_projectname", dataverse, project);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        record.Keys.Should().BeSubsetOf(ProjectsModule().ReadableColumns);
        record.Should().NotContainKey("sprk_containerid");
        dataverse.RequestedColumns.Should().Equal("sprk_projectname");
    }

    [Fact]
    public async Task GetScopedRecord_WhenNoSelect_ReturnsTheDefaultProjectionStrippedToReadableKeys()
    {
        var project = Guid.NewGuid();
        var dataverse = new RecordDouble(ProjectRecord(project));

        var result = await Record(project, select: null, dataverse, project);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        dataverse.RequestedColumns.Should().BeNull("no $select ⇒ the read service's primary id + name default");
        record.Should().ContainKey("sprk_projectid");
        record.Should().ContainKey("sprk_projectnumber", "the primary name is on the allow-list");
        record.Keys.Should().BeSubsetOf(ProjectsModule().ReadableColumns);
    }

    [Fact]
    public async Task GetScopedRecord_WhenRecordIsOutsideTheCallersSet_StillDeniesWithoutReading()
    {
        var dataverse = new RecordDouble(ProjectRecord(Guid.NewGuid()));

        var result = await Record(Guid.NewGuid(), "sprk_projectname", dataverse /* no accessible projects */);

        result.Should().BeOfType<ProblemHttpResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        dataverse.Calls.Should().Be(0);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 4. REGISTRATION — refuses an unsafe or missing list at startup
    // ═════════════════════════════════════════════════════════════════════════════

    public static TheoryData<string, IReadOnlySet<string>?> UnsafeDocumentLists => new()
    {
        { "no list", null },
        { "empty list", new HashSet<string>() },
        { "blank entry", new HashSet<string>(DocumentColumns) { " " } },
        { "missing scope attribute sprk_matter", new HashSet<string>(DocumentColumns.Where(c => c != "sprk_matter")) },
        { "missing primary id", new HashSet<string>(DocumentColumns.Where(c => c != "sprk_documentid")) },
        { "missing primary name", new HashSet<string>(DocumentColumns.Where(c => c != "sprk_documentname")) },
    };

    [Theory]
    [MemberData(nameof(UnsafeDocumentLists))]
    public void Register_WhenTheColumnListIsMissingOrIncomplete_Throws(string reason, IReadOnlySet<string>? columns)
    {
        var act = () => new ExternalModuleRegistry().Register(DocumentsModuleWithColumns(columns));

        act.Should().Throw<InvalidOperationException>(reason).WithMessage("*ReadableColumns*");
    }

    /// <summary>
    /// The verifier's partial on criterion 8: a list holding the primary id but NOT the primary name would
    /// strip the no-<c>$select</c> /record projection to an id-only record. Pins the exact refusal (and the
    /// column it names), so the generic "*ReadableColumns*" match above cannot be satisfied by another rule.
    /// </summary>
    [Fact]
    public void Register_WhenTheListLacksTheDeclaredPrimaryName_ThrowsNamingThePrimaryName()
    {
        var withoutName = new HashSet<string>(DocumentColumns.Where(c => c != "sprk_documentname"));

        var act = () => new ExternalModuleRegistry().Register(DocumentsModuleWithColumns(withoutName));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*primary-name attribute 'sprk_documentname'*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Register_WhenThePrimaryNameIsNotDeclared_Throws(string? primaryName)
    {
        // Fail-closed: an undeclared primary name cannot be checked against the list, so it is refused,
        // never read as "no name column needed".
        var act = () => new ExternalModuleRegistry().Register(DocumentsModuleWithColumns(DocumentColumns, primaryName!));

        act.Should().Throw<InvalidOperationException>().WithMessage("*must declare its PrimaryNameAttribute*");
    }

    [Fact]
    public void Register_WhenTheDeclaredPrimaryNameIsOnTheListInAnotherCase_Accepts()
    {
        // The list is case-insensitive (Dataverse logical names are lower-case; a declaration's casing
        // must not turn a correct list into a startup failure).
        var act = () => new ExternalModuleRegistry().Register(DocumentsModuleWithColumns(DocumentColumns, "SPRK_DocumentName"));

        act.Should().NotThrow();
    }

    public static TheoryData<string> PointerColumns
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var pointer in ExternalModuleRegistry.PointerColumns)
            {
                data.Add(pointer);
            }
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(PointerColumns))]
    public void Register_WhenTheColumnListContainsAPointerColumn_Throws(string pointer)
    {
        var columns = new HashSet<string>(DocumentColumns) { pointer.ToUpperInvariant() };

        var act = () => new ExternalModuleRegistry().Register(DocumentsModuleWithColumns(columns));

        act.Should().Throw<InvalidOperationException>().WithMessage("*pointer*");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 5. PRODUCTION REGISTRATIONS ↔ LIVE DATA (dev, read 2026-09-30)
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The live primary-name attribute of every module entity (EntityDefinitions, 2026-09-30; re-read
    /// read-only and unchanged on the criterion-8 follow-up). The no-$select /record projection is primary
    /// id + primary name; registration checks the list holds the id and the DECLARED name, this pins each
    /// declaration to the live metadata (a wrong declaration would make the registration check vacuous).
    /// </summary>
    public static TheoryData<string, string> LivePrimaryNames => new()
    {
        { "sprk_project", "sprk_projectnumber" },
        { "sprk_document", "sprk_documentname" },
        { "sprk_invoice", "sprk_name" },
        { "sprk_workassignment", "sprk_name" },
        { "sprk_matter", "sprk_matternumber" },
        { "sprk_servicerequest", "sprk_name" },
        { "sprk_gridconfiguration", "sprk_name" },
    };

    /// <summary>The live FetchXML of each outside-counsel grid (sprk_gridconfiguration.sprk_configjson, 2026-09-30).</summary>
    public static TheoryData<string, string> LiveGridFetchXml => new()
    {
        { "sprk_project", "<fetch><entity name='sprk_project'><attribute name='sprk_projectid'/><attribute name='sprk_projectname'/><attribute name='sprk_projectnumber'/><attribute name='statuscode'/><attribute name='modifiedon'/><order attribute='sprk_projectname' descending='false'/></entity></fetch>" },
        { "sprk_document", LiveDocumentsGridFetchXml },
        { "sprk_invoice", "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_invoicenumber'/><attribute name='sprk_invoicedate'/><attribute name='sprk_invoicestatus'/><attribute name='sprk_totalamount'/><attribute name='sprk_project'/><attribute name='sprk_matter'/><order attribute='sprk_invoicedate' descending='true'/></entity></fetch>" },
        { "sprk_workassignment", "<fetch><entity name='sprk_workassignment'><attribute name='sprk_workassignmentid'/><attribute name='sprk_workassignmentnumber'/><attribute name='sprk_priority'/><attribute name='sprk_responseduedate'/><attribute name='statuscode'/><attribute name='sprk_regardingproject'/><order attribute='sprk_responseduedate' descending='false'/></entity></fetch>" },
        { "sprk_matter", "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid'/><attribute name='sprk_mattername'/><attribute name='sprk_matternumber'/><attribute name='statuscode'/><order attribute='sprk_mattername' descending='false'/></entity></fetch>" },
        { "sprk_servicerequest", "<fetch><entity name='sprk_servicerequest'><attribute name='sprk_servicerequestid'/><attribute name='sprk_servicerequestnumber'/><attribute name='sprk_name'/><attribute name='statuscode'/><attribute name='createdon'/><attribute name='sprk_requestedby'/><order attribute='createdon' descending='true'/></entity></fetch>" },
    };

    private ExternalModuleRegistry ProductionRegistry() =>
        _fixture.Services.GetRequiredService<ExternalModuleRegistry>();

    [Theory]
    [MemberData(nameof(LivePrimaryNames))]
    public void ProductionModule_ForEachRegisteredEntity_ReadsTheLivePrimaryNameAndNoPointer(string entity, string primaryName)
    {
        var module = ProductionRegistry().FindByEntity(entity);

        module.Should().NotBeNull($"'{entity}' is a registered external module");
        module!.PrimaryNameAttribute.Should().Be(primaryName, "the declaration must match live EntityDefinitions");
        module.ReadableColumns.Should().Contain(primaryName, "the no-$select /record projection must not be blanked");
        module.ReadableColumns.Should().NotIntersectWith(ExternalModuleRegistry.PointerColumns);
    }

    [Fact]
    public void ProductionRegistry_EveryRegisteredModule_HasItsPrimaryNamePinnedToLiveMetadata()
    {
        // A module added later must join LivePrimaryNames (read from EntityDefinitions), or its declared
        // primary name is unverified and the Register check above proves nothing for it.
        var pinned = LivePrimaryNames.Select(row => (string)row[0]).ToHashSet(StringComparer.OrdinalIgnoreCase);

        ProductionRegistry().Modules.Select(m => m.RecordEntity).Should().BeSubsetOf(pinned);
    }

    [Theory]
    [MemberData(nameof(LiveGridFetchXml))]
    public void ProductionModule_ForEachLiveOutsideCounselGrid_AdmitsTheGridsFetchXml(string entity, string fetchXml)
    {
        var module = ProductionRegistry().FindByEntity(entity)!;

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            fetchXml, module.RecordEntity, module.ReadableColumns, new FetchXmlEntityExtractor());

        result.Verdict.Should().Be(Verdict.Allowed,
            $"the live {entity} grid must render unchanged after task 134 (violation: {result.ColumnViolation})");
    }

    [Fact]
    public void ProductionModule_GridConfiguration_ReadsTheConfigJsonTheSharedGridSelects()
    {
        // DataGrid.tsx fetchConfigRecord: retrieveRecord('sprk_gridconfiguration', id, ['sprk_configjson']).
        ProductionRegistry().FindByEntity("sprk_gridconfiguration")!
            .ReadableColumns.Should().Contain("sprk_configjson", "otherwise no external grid could load its configuration");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 6. TASK 157 — the shrink: rule (b) (sibling-view columns) retired
    // ═════════════════════════════════════════════════════════════════════════════
    //
    // Task 134 admitted, besides the grid's own columns, every column of an internal MDA main view the grid's
    // ViewSelector offered (rule (b)). Owner round 4 item 7 (2026-10-01) turned the selector off on every
    // external grid (GridWidgetBody.tsx showViewSelector={false}, pinned by the arch guard
    // ExternalSpaGridViewSelectorGuardTests), so rule (b) no longer applies. Each list is now exactly
    // (a) the grid configuration's columns + (c) the scope attributes + (d) the /record default projection,
    // re-derived READ-ONLY from live dev data on 2026-10-01 (notes/task-157-external-grid-columns.md).

    /// <summary>
    /// Each module's list as task 157 derived it from LIVE data (2026-10-01): (a) sprk_gridconfiguration
    /// columns (FetchXML attributes/conditions/orders, layoutXml cells, configjson column keys), (c) scope
    /// attributes, (d) EntityDefinitions primary id + primary name. Nothing else.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> LiveDerivedAllowLists = new Dictionary<string, string[]>
    {
        ["sprk_project"] = new[] { "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "statuscode", "modifiedon" },
        ["sprk_document"] = new[]
        {
            "sprk_documentid", "sprk_documentname", "sprk_documenttype", "createdon",
            "sprk_project", "sprk_matter", "sprk_workassignment",
        },
        ["sprk_invoice"] = new[]
        {
            "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_invoicedate", "sprk_invoicestatus",
            "sprk_totalamount", "sprk_project", "sprk_matter",
        },
        ["sprk_workassignment"] = new[]
        {
            "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", "sprk_priority",
            "sprk_responseduedate", "statuscode", "sprk_regardingproject",
        },
        ["sprk_matter"] = new[] { "sprk_matterid", "sprk_mattername", "sprk_matternumber", "statuscode" },
        ["sprk_servicerequest"] = new[]
        {
            "sprk_servicerequestid", "sprk_servicerequestnumber", "sprk_name", "statuscode", "createdon",
            "sprk_requestedby",
        },
        ["sprk_gridconfiguration"] = new[] { "sprk_gridconfigurationid", "sprk_name", "sprk_configjson" },
    };

    /// <summary>
    /// The lists task 134 shipped (2026-09-30), kept verbatim as the "before" side of the shrink: the new
    /// list must be a SUBSET of it (a shrink never adds a column) and the difference must be exactly the
    /// rule-(b)-only columns.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Task134AllowLists = new Dictionary<string, string[]>
    {
        ["sprk_project"] = new[]
        {
            "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "statuscode", "statecode",
            "modifiedon", "createdon", "ownerid", "sprk_practicearea", "sprk_projecttype_ref",
        },
        ["sprk_document"] = LiveDerivedAllowLists["sprk_document"],
        ["sprk_invoice"] = new[]
        {
            "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_invoicedate", "sprk_invoicestatus",
            "sprk_totalamount", "sprk_project", "sprk_matter", "sprk_visibilitystate", "modifiedon", "statecode",
        },
        ["sprk_workassignment"] = new[]
        {
            "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", "sprk_priority",
            "sprk_responseduedate", "statuscode", "statecode", "sprk_regardingproject", "createdon",
            "ownerid", "sprk_assignedto",
        },
        ["sprk_matter"] = new[]
        {
            "sprk_matterid", "sprk_mattername", "sprk_matternumber", "statuscode", "statecode",
            "createdon", "sprk_mattertype", "sprk_practicearea",
        },
        ["sprk_servicerequest"] = LiveDerivedAllowLists["sprk_servicerequest"],
        ["sprk_gridconfiguration"] = LiveDerivedAllowLists["sprk_gridconfiguration"],
    };

    /// <summary>The drop set: columns task 134 admitted ONLY by rule (b). Each must now be unreadable.</summary>
    public static TheoryData<string, string> RuleBOnlyColumns => new()
    {
        { "sprk_project", "statecode" },
        { "sprk_project", "createdon" },
        { "sprk_project", "ownerid" },
        { "sprk_project", "sprk_practicearea" },
        { "sprk_project", "sprk_projecttype_ref" },
        { "sprk_invoice", "sprk_visibilitystate" },
        { "sprk_invoice", "modifiedon" },
        { "sprk_invoice", "statecode" },
        { "sprk_workassignment", "statecode" },
        { "sprk_workassignment", "createdon" },
        { "sprk_workassignment", "ownerid" },
        { "sprk_workassignment", "sprk_assignedto" },
        { "sprk_matter", "statecode" },
        { "sprk_matter", "createdon" },
        { "sprk_matter", "sprk_mattertype" },
        { "sprk_matter", "sprk_practicearea" },
    };

    public static TheoryData<string> DerivedEntities
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var entity in LiveDerivedAllowLists.Keys)
            {
                data.Add(entity);
            }
            return data;
        }
    }

    private static string LiveGridFetchXmlFor(string entity) =>
        (string)LiveGridFetchXml.Single(row => (string)row[0] == entity)[1];

    /// <summary>
    /// A caller for whom the Tier-2 gate passes on <paramref name="id"/> in EVERY module: the id is a
    /// readable project, matter and work assignment at once. Used so a refusal or strip below can only come
    /// from the COLUMN scope, never from row scope.
    /// </summary>
    private static CallerPrincipal GrantedEverywhere(Guid id) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@lawfirm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(id, ExternalAccessLevel.Collaborate) },
        MatterAccess = new Dictionary<Guid, AccessRights> { [id] = AccessRights.Read },
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [id] = AccessRights.Read },
    };

    [Theory]
    [MemberData(nameof(DerivedEntities))]
    public void ProductionModule_ForEachRegisteredEntity_ReadableColumnsEqualTheLiveDerivation(string entity)
    {
        var module = ProductionRegistry().FindByEntity(entity);

        module.Should().NotBeNull($"'{entity}' is a registered external module");
        module!.ReadableColumns.Should().BeEquivalentTo(LiveDerivedAllowLists[entity],
            "each list is exactly grid config + scope + /record default (task 157), nothing from a sibling view");
    }

    [Fact]
    public void ProductionRegistry_EveryRegisteredModule_HasALiveDerivation()
    {
        // A module added later must record its derivation here, or the equality pin above proves nothing for it.
        ProductionRegistry().Modules.Select(m => m.RecordEntity)
            .Should().BeSubsetOf(LiveDerivedAllowLists.Keys);
    }

    [Theory]
    [MemberData(nameof(DerivedEntities))]
    public void LiveDerivation_ComparedWithTask134_OnlyShrinksAndDropsExactlyTheRuleBColumns(string entity)
    {
        var before = Task134AllowLists[entity];
        var after = LiveDerivedAllowLists[entity];
        var expectedDrop = RuleBOnlyColumns.Where(row => (string)row[0] == entity).Select(row => (string)row[1]);

        after.Should().BeSubsetOf(before, "a shrink never adds a column");
        before.Except(after).Should().BeEquivalentTo(expectedDrop);
    }

    [Theory]
    [MemberData(nameof(RuleBOnlyColumns))]
    public async Task ExecuteScopedFetch_WhenAColumnAdmittedOnlyByTheRetiredViewRuleIsRequested_Returns400AndNeverQueries(
        string entity, string column)
    {
        var id = Guid.NewGuid();
        var registry = ProductionRegistry();
        var gridFetch = LiveGridFetchXmlFor(entity);

        // Positive control: the grid's own FetchXML still runs for this caller, so the 400 below is the column.
        var control = new FetchDouble();
        var allowed = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(entity, gridFetch, PagingCookie: null), GrantedEverywhere(id), registry,
            new FetchXmlEntityExtractor(), control.Execute, NullLogger.Instance, CancellationToken.None);
        allowed.Should().BeOfType<Ok<FetchResponseDto>>();
        control.Calls.Should().Be(1);

        // The same grid FetchXML with the sibling-view column added — what the ViewSelector used to send.
        var withColumn = gridFetch.Replace("<order", $"<attribute name='{column}'/><order", StringComparison.Ordinal);
        var dataverse = new FetchDouble();

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(entity, withColumn, PagingCookie: null), GrantedEverywhere(id), registry,
            new FetchXmlEntityExtractor(), dataverse.Execute, NullLogger.Instance, CancellationToken.None);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(RuleBOnlyColumns))]
    public async Task GetScopedRecord_WhenSelectNamesAColumnAdmittedOnlyByTheRetiredViewRule_Returns400AndNeverReads(
        string entity, string column)
    {
        var id = Guid.NewGuid();
        var dataverse = new RecordDouble(new Dictionary<string, object?> { [column] = "x" });

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            entity, id, column, GrantedEverywhere(id), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorRecordColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(RuleBOnlyColumns))]
    public async Task GetScopedRecord_WhenTheReadReturnsAColumnAdmittedOnlyByTheRetiredViewRule_StripsIt(
        string entity, string column)
    {
        var id = Guid.NewGuid();
        var module = ProductionRegistry().FindByEntity(entity)!;
        var primaryId = $"{entity}id";
        var dataverse = new RecordDouble(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [primaryId] = id,
            [module.PrimaryNameAttribute] = "REC-0001",
            [column] = "was readable through a sibling view",
            ["@formattedValues"] = new Dictionary<string, string> { [column] = "display value" },
        });

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            entity, id, select: null, GrantedEverywhere(id), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        dataverse.Calls.Should().Be(1);
        record.Should().NotContainKey(column);
        record.Should().NotContainKey("@formattedValues", "its only entry was the dropped column's display value");
        record.Should().ContainKey(primaryId).And.ContainKey(module.PrimaryNameAttribute,
            "the /record default projection survives the shrink");
    }
}
