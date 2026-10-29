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

    /// <summary>The pre-task-002 production sprk_document allow-list (ExternalAccessModule, task 134 derivation, plus task 032's
    /// two creator stamps). The production list has since gained sprk_filetype and modifiedon (r3 task 002); these tests
    /// exercise the guard and the registration checks with this fixed list, not the live one.</summary>
    private static readonly IReadOnlySet<string> DocumentColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_documentid", "sprk_documentname", "sprk_documenttype", "createdon",
        "sprk_project", "sprk_matter", "sprk_workassignment",
        "sprk_createdbyperson", "sprk_createdbycontact",
    };

    /// <summary>The live outside-counsel Documents grid FetchXML (sprk_gridconfiguration 3af4102c, 2026-09-30).</summary>
    private const string LiveDocumentsGridFetchXml =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>" +
        "<attribute name='sprk_documenttype'/><attribute name='createdon'/><attribute name='sprk_project'/>" +
        "<attribute name='sprk_matter'/><attribute name='sprk_workassignment'/>" +
        "<order attribute='createdon' descending='true'/></entity></fetch>";

    /// <summary>The documents grid FetchXML as it reads after the r3 task 002 rollout: the live grid plus sprk_filetype and modifiedon.</summary>
    private const string DocumentsGridFetchXmlAfterTask002 =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>" +
        "<attribute name='sprk_documenttype'/><attribute name='createdon'/><attribute name='sprk_project'/>" +
        "<attribute name='sprk_matter'/><attribute name='sprk_workassignment'/>" +
        "<attribute name='sprk_filetype'/><attribute name='modifiedon'/>" +
        "<order attribute='createdon' descending='true'/></entity></fetch>";

    private static ExternalModuleDataEndpoints.FetchXmlGuardResult EvaluateDocuments(string? fetchXml) =>
        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            fetchXml, DocumentEntity, DocumentColumns, new FetchXmlEntityExtractor());

    /// <summary>A documents module identical in scope, and in columns to the pre-task-002 production list.</summary>
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

    /// <summary>
    /// The secure-flag read (owner option B, r3 task 039): these column-guard tests decide nothing on the multi-root rule, so
    /// it answers "none known not secure" (every unreadable root counts as secure, the fail-closed default).
    /// </summary>
    private static readonly NonSecureRootReader NoFlags = (_, _) =>
        Task.FromResult(new NonSecureRootsRead(new HashSet<(ExternalGrantRootType Root, Guid Id)>()));

    /// <summary>
    /// Task 188: the REAL document narrowing. These column-guard rows carry no Access Permission, communication or parent
    /// document, so it decides them on their own columns and makes no read; a read would be a surprise, so it throws.
    /// </summary>
    private static readonly DocumentNarrower Narrow = (_, rows, ct) =>
        ExternalDocumentNarrowing.DecideAsync(
            rows, (entitySet, _, _) => throw new InvalidOperationException($"unexpected narrowing read of {entitySet}"), ct);

    private static Task<IResult> Fetch(ExternalModuleRegistry registry, CallerPrincipal principal, string fetchXml, FetchDouble dataverse) =>
        ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(DocumentEntity, fetchXml, PagingCookie: null),
            principal, registry, new FetchXmlEntityExtractor(), dataverse.Execute, NoFlags, Narrow,
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

    /// <summary>The FetchXML each outside-counsel grid carries once the r3 task 002 grid update is rolled out (owner 2026-10-29),
    /// for project, matter, work assignment, invoice and service request (the others as read live 2026-09-30). The documents
    /// row stays the pre-rollout <see cref="LiveDocumentsGridFetchXml"/>; its post-rollout FetchXML is
    /// <see cref="DocumentsGridFetchXmlAfterTask002"/>.</summary>
    public static TheoryData<string, string> LiveGridFetchXml => new()
    {
        { "sprk_project", "<fetch><entity name='sprk_project'><attribute name='sprk_projectid'/><attribute name='sprk_projectname'/><attribute name='sprk_projectnumber'/><attribute name='statuscode'/><attribute name='modifiedon'/><attribute name='sprk_projecttype_ref'/><order attribute='sprk_projectname' descending='false'/></entity></fetch>" },
        { "sprk_document", LiveDocumentsGridFetchXml },
        { "sprk_invoice", "<fetch><entity name='sprk_invoice'><attribute name='sprk_invoiceid'/><attribute name='sprk_name'/><attribute name='sprk_invoicenumber'/><attribute name='sprk_invoicedate'/><attribute name='statuscode'/><attribute name='sprk_totalamount'/><attribute name='sprk_project'/><attribute name='sprk_matter'/><attribute name='sprk_totalamount_base'/><order attribute='sprk_invoicedate' descending='true'/></entity></fetch>" },
        { "sprk_workassignment", "<fetch><entity name='sprk_workassignment'><attribute name='sprk_workassignmentid'/><attribute name='sprk_name'/><attribute name='sprk_workassignmentnumber'/><attribute name='sprk_priority'/><attribute name='sprk_responseduedate'/><attribute name='statuscode'/><attribute name='sprk_regardingproject'/><attribute name='sprk_mattertype'/><attribute name='modifiedon'/><order attribute='sprk_responseduedate' descending='false'/></entity></fetch>" },
        { "sprk_matter", "<fetch><entity name='sprk_matter'><attribute name='sprk_matterid'/><attribute name='sprk_mattername'/><attribute name='sprk_matternumber'/><attribute name='statuscode'/><attribute name='sprk_mattertype'/><attribute name='modifiedon'/><order attribute='sprk_mattername' descending='false'/></entity></fetch>" },
        { "sprk_servicerequest", "<fetch><entity name='sprk_servicerequest'><attribute name='sprk_servicerequestid'/><attribute name='sprk_servicerequestnumber'/><attribute name='sprk_name'/><attribute name='statuscode'/><attribute name='createdon'/><attribute name='sprk_requestedby'/><attribute name='modifiedon'/><order attribute='createdon' descending='true'/></entity></fetch>" },
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
            $"the {entity} grid FetchXML (post-task-002 rollout) must be admitted in full (violation: {result.ColumnViolation})");
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
        ["sprk_project"] = new[]
        {
            "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "statuscode", "modifiedon",
            // spaarke-SPA-external-access-platform-r3 task 002 (owner 2026-10-29): grid column on grid 61711823.
            "sprk_projecttype_ref",
        },
        ["sprk_document"] = new[]
        {
            "sprk_documentid", "sprk_documentname", "sprk_documenttype", "createdon",
            "sprk_project", "sprk_matter", "sprk_workassignment",
            // spaarke-SPA-external-access-platform-r3 task 032 (FR-26, review 031 K2): the two task-030 creator stamps,
            // NOT grid-derived. The SPA reads them to offer Delete only on the caller's own documents; they name a
            // systemuser or contact id and nothing else (notes/task-032-notes.md).
            "sprk_createdbyperson", "sprk_createdbycontact",
            // r3 task 002 (owner 2026-10-29): grid columns on grid 3af4102c.
            "sprk_filetype", "modifiedon",
        },
        ["sprk_invoice"] = new[]
        {
            "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_invoicedate",
            "sprk_totalamount", "sprk_project", "sprk_matter",
            // spaarke-SPA-external-access-platform-r3 (owner Partner UAT 2026-10-17, ebilling-r1 task 015): NOT grid-derived.
            // The SPA record detail reads the invoice status from statuscode. Appears on both sides of the task 134 shrink
            // comparison, so it drops nothing.
            "statuscode",
            // r3 task 002 (owner 2026-10-29): grid column on grid 3ff4102c.
            "sprk_totalamount_base",
        },
        ["sprk_workassignment"] = new[]
        {
            "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", "sprk_priority",
            "sprk_responseduedate", "statuscode", "sprk_regardingproject",
            // r3 task 002 (owner 2026-10-29): grid columns on grid 42f4102c.
            "sprk_mattertype", "modifiedon",
        },
        ["sprk_matter"] = new[]
        {
            "sprk_matterid", "sprk_mattername", "sprk_matternumber", "statuscode",
            // r3 task 002 (owner 2026-10-29): grid columns on grid 583a2a33.
            "sprk_mattertype", "modifiedon",
        },
        ["sprk_servicerequest"] = new[]
        {
            "sprk_servicerequestid", "sprk_servicerequestnumber", "sprk_name", "statuscode", "createdon",
            "sprk_requestedby",
            // spaarke-SPA-external-access-platform-r3, owner decision 2026-10-12: NOT grid-derived. The SPA reads the open /
            // closed flag to hide Delete on a closed request (the delete route enforces it again). Appears on both sides of
            // the task 134 shrink comparison, so it drops nothing.
            "statecode",
            // r3 task 002 (owner 2026-10-29): grid column on grid 403e5d37 ("Last Activity").
            "modifiedon",
        },
        ["sprk_gridconfiguration"] = new[] { "sprk_gridconfigurationid", "sprk_name", "sprk_configjson" },
    };

    /// <summary>
    /// The lists task 134 shipped (2026-09-30) as the "before" side of the shrink: the new list must be a SUBSET of it
    /// (a shrink never adds a column) and the difference must be exactly the rule-(b)-only columns. Later deliberate
    /// additions (task 032 creator stamps, the 2026-10-12 statecode, the invoice statuscode, the r3 task 002 grid columns)
    /// sit on BOTH sides, so they never show up as a shrink or a growth.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Task134AllowLists = new Dictionary<string, string[]>
    {
        ["sprk_project"] = new[]
        {
            "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "statuscode", "statecode",
            "modifiedon", "createdon", "ownerid", "sprk_practicearea",
            "sprk_projecttype_ref", // task 134 admitted it by rule (b); r3 task 002 (owner 2026-10-29) re-admits it as a grid column, so it is on both sides
        },
        // Task 134's document list was the task-157 derivation (no rule-(b) column); task 032's two creator stamps and the
        // r3 task 002 grid columns (sprk_filetype, modifiedon; owner 2026-10-29) are later, deliberate additions, so they
        // appear on both sides of the shrink comparison and drop nothing.
        ["sprk_document"] = LiveDerivedAllowLists["sprk_document"],
        ["sprk_invoice"] = new[]
        {
            "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_invoicedate", RetiredInvoiceStatusColumn,
            "sprk_totalamount", "sprk_project", "sprk_matter", "sprk_visibilitystate", "modifiedon", "statecode",
            "statuscode", // the 2026-10-17 addition above; on both sides, so the shrink comparison is unchanged
            "sprk_totalamount_base", // r3 task 002 addition (owner 2026-10-29); on both sides so the shrink comparison is unchanged
        },
        ["sprk_workassignment"] = new[]
        {
            "sprk_workassignmentid", "sprk_name", "sprk_workassignmentnumber", "sprk_priority",
            "sprk_responseduedate", "statuscode", "statecode", "sprk_regardingproject", "createdon",
            "ownerid", "sprk_assignedto",
            "sprk_mattertype", "modifiedon", // r3 task 002 addition (owner 2026-10-29); on both sides so the shrink comparison is unchanged
        },
        ["sprk_matter"] = new[]
        {
            "sprk_matterid", "sprk_mattername", "sprk_matternumber", "statuscode", "statecode",
            "createdon", "sprk_mattertype", "sprk_practicearea",
            "modifiedon", // r3 task 002 addition (owner 2026-10-29); on both sides so the shrink comparison is unchanged
        },
        // Task 134's service-request list was the task-157 derivation (no rule-(b) column); the 2026-10-12 statecode and the
        // r3 task 002 modifiedon are later, deliberate additions, so they are listed here on the "before" side too.
        ["sprk_servicerequest"] = new[]
        {
            "sprk_servicerequestid", "sprk_servicerequestnumber", "sprk_name", "statuscode", "createdon",
            "sprk_requestedby",
            "statecode", // the 2026-10-12 addition above; on both sides, so the shrink comparison is unchanged
            "modifiedon", // r3 task 002 addition (owner 2026-10-29); on both sides so the shrink comparison is unchanged
        },
        ["sprk_gridconfiguration"] = LiveDerivedAllowLists["sprk_gridconfiguration"],
    };

    /// <summary>The drop set: columns task 134 admitted ONLY by rule (b). Each must now be unreadable.</summary>
    public static TheoryData<string, string> RuleBOnlyColumns => new()
    {
        { "sprk_project", "statecode" },
        { "sprk_project", "createdon" },
        { "sprk_project", "ownerid" },
        { "sprk_project", "sprk_practicearea" },
        { "sprk_invoice", "sprk_visibilitystate" },
        { "sprk_invoice", "modifiedon" },
        { "sprk_invoice", "statecode" },
        { "sprk_workassignment", "statecode" },
        { "sprk_workassignment", "createdon" },
        { "sprk_workassignment", "ownerid" },
        { "sprk_workassignment", "sprk_assignedto" },
        { "sprk_matter", "statecode" },
        { "sprk_matter", "createdon" },
        { "sprk_matter", "sprk_practicearea" },
    };

    /// <summary>
    /// The drop set that is NOT rule (b): columns the allow-list admitted that the owner later retired once nothing external
    /// read them. sprk_invoice's retired status column (<see cref="RetiredInvoiceStatusColumn"/>): grid 3ff4102c moved to statuscode
    /// on 2026-10-18; removed by r3 task 002 (#2450) with ebilling's OK on 2026-10-29. Task 134 had admitted it as a grid column, so it
    /// is on the "before" side of the shrink.
    /// </summary>
    public static TheoryData<string, string> RetiredAfterTask157 => new()
    {
        { "sprk_invoice", RetiredInvoiceStatusColumn },
    };

    /// <summary>
    /// The retired invoice-status column (ebilling-r1 task 015, D-41; removed from the external allow-list on master in #2450; its dev
    /// column delete waits for the next dev BFF deploy). Spelled in two parts because InvoiceStatusRetirementGuardTests allows the
    /// full name nowhere but in its own file: these tests only prove the column is refused, they never read it.
    /// </summary>
    private const string RetiredInvoiceStatusColumn = "sprk_invoicestatus";

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
    public void LiveDerivation_ComparedWithTask134_OnlyShrinksAndDropsExactlyTheRuleBAndRetiredColumns(string entity)
    {
        var before = Task134AllowLists[entity];
        var after = LiveDerivedAllowLists[entity];
        var expectedDrop = RuleBOnlyColumns.Concat(RetiredAfterTask157)
            .Where(row => (string)row[0] == entity).Select(row => (string)row[1]);

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
            new FetchXmlEntityExtractor(), control.Execute, NoFlags, Narrow, NullLogger.Instance, CancellationToken.None);
        allowed.Should().BeOfType<Ok<FetchResponseDto>>();
        control.Calls.Should().Be(1);

        // The same grid FetchXML with the sibling-view column added — what the ViewSelector used to send.
        var withColumn = gridFetch.Replace("<order", $"<attribute name='{column}'/><order", StringComparison.Ordinal);
        var dataverse = new FetchDouble();

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(entity, withColumn, PagingCookie: null), GrantedEverywhere(id), registry,
            new FetchXmlEntityExtractor(), dataverse.Execute, NoFlags, Narrow, NullLogger.Instance, CancellationToken.None);

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

    // ═════════════════════════════════════════════════════════════════════════════
    // 7. r3 task 002 — owner grid columns (owner decision 2026-10-29)
    // ═════════════════════════════════════════════════════════════════════════════
    //
    // Nine columns became readable so the external grids can show them. Each is a grid column (rule (a)): none is secured,
    // a pointer, or a column the grid does not reference. The two lookups point at reference tables that have no module,
    // so the lookup value is readable and the table behind it is not.

    /// <summary>The nine (entity, column) pairs r3 task 002 admitted.</summary>
    public static TheoryData<string, string> Task002GridColumns => new()
    {
        { "sprk_project", "sprk_projecttype_ref" },
        { "sprk_matter", "sprk_mattertype" },
        { "sprk_matter", "modifiedon" },
        { "sprk_workassignment", "sprk_mattertype" },
        { "sprk_workassignment", "modifiedon" },
        { "sprk_document", "sprk_filetype" },
        { "sprk_document", "modifiedon" },
        { "sprk_invoice", "sprk_totalamount_base" },
        { "sprk_servicerequest", "modifiedon" },
    };

    /// <summary>Columns beside the admitted ones that stay unreadable: (entity, column).</summary>
    public static TheoryData<string, string> Task002SiblingColumnsStillRefused => new()
    {
        { "sprk_matter", "sprk_practicearea" },
        { "sprk_project", "sprk_practicearea" },
        { "sprk_project", "ownerid" },
        { "sprk_workassignment", "sprk_assignedto" },
        { "sprk_document", "sprk_searchindexed" },
        { "sprk_invoice", "sprk_invoiceduedate" },
        // The currency columns Dataverse adds beside a Money column: not grid columns, so not readable.
        { "sprk_invoice", "transactioncurrencyid" },
        { "sprk_invoice", "exchangerate" },
    };

    /// <summary>The grid FetchXML of <paramref name="entity"/> without <paramref name="column"/>, then with it appended.</summary>
    private static string GridFetchXmlPlusColumn(string entity, string column)
    {
        var withoutColumn = LiveGridFetchXmlFor(entity).Replace($"<attribute name='{column}'/>", string.Empty, StringComparison.Ordinal);
        return withoutColumn.Replace("<order", $"<attribute name='{column}'/><order", StringComparison.Ordinal);
    }

    /// <summary>The service-requests module scopes to the caller's OWN contact id, which a partner (CIAM) caller holds only
    /// when the partner switch is on; a workforce caller with a contact always qualifies, so the column tests use one.</summary>
    private static CallerPrincipal WorkforceContact(Guid contactId) => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = contactId,
        Email = "staff@firm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static Task<IResult> FetchGrantedEverywhere(string entity, string fetchXml, Guid id, ExternalModuleRegistry registry, FetchDouble dataverse) =>
        ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto(entity, fetchXml, PagingCookie: null),
            entity == "sprk_servicerequest" ? WorkforceContact(id) : GrantedEverywhere(id), registry,
            new FetchXmlEntityExtractor(), dataverse.Execute, NoFlags, Narrow, NullLogger.Instance, CancellationToken.None,
            // ebilling-r1 task 098: an invoice row goes through the (a′) predicate, which hides every row when no seam is given
            // (fail closed). These column-guard tests decide nothing on it, so the seam hides nothing.
            hideInvoices: InvoiceHidingStub.NoneHidden().Seam);

    [Fact]
    public void ProductionModule_ForTheDocumentsGridAfterTheRollout_AdmitsItsFetchXml()
    {
        var module = ProductionRegistry().FindByEntity(DocumentEntity)!;

        var result = ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            DocumentsGridFetchXmlAfterTask002, module.RecordEntity, module.ReadableColumns, new FetchXmlEntityExtractor());

        result.Verdict.Should().Be(Verdict.Allowed, $"violation: {result.ColumnViolation}");
    }

    [Theory]
    [MemberData(nameof(Task002GridColumns))]
    public async Task ExecuteScopedFetch_WhenAnOwnerGridColumnIsRequested_ReturnsOkAndQueriesOnce(string entity, string column)
    {
        var id = Guid.NewGuid();
        var value = ColumnValue(column);
        var dataverse = new FetchDouble(RowCarrying(entity, id, (column, value)));

        var result = await FetchGrantedEverywhere(entity, GridFetchXmlPlusColumn(entity, column), id, ProductionRegistry(), dataverse);

        dataverse.Calls.Should().Be(1);
        var projected = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities.Should().ContainSingle().Subject;
        projected.Should().ContainKey(column).WhoseValue.Should().Be(value, "the admitted column reaches the caller");
        var formatted = projected["@formattedValues"].Should().BeAssignableTo<IReadOnlyDictionary<string, string>>().Subject;
        formatted.Should().ContainKey(column).WhoseValue.Should().Be(FormattedColumnValue(column));
    }

    /// <summary>A value of the column's own shape: a lookup, a date, a string or Money.</summary>
    private static object ColumnValue(string column) => column switch
    {
        "sprk_projecttype_ref" => new EntityReference("sprk_projecttype_ref", Guid.Parse("11111111-1111-1111-1111-111111111111")),
        "sprk_mattertype" => new EntityReference("sprk_mattertype_ref", Guid.Parse("22222222-2222-2222-2222-222222222222")),
        "modifiedon" => new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc),
        "sprk_filetype" => "pdf",
        "sprk_totalamount_base" => new Money(1250.5m),
        "transactioncurrencyid" => new EntityReference("transactioncurrency", Guid.Parse("33333333-3333-3333-3333-333333333333")),
        "exchangerate" => 1.0m,
        _ => throw new ArgumentOutOfRangeException(nameof(column), column, "no test value"),
    };

    private static string FormattedColumnValue(string column) => $"display of {column}";

    /// <summary>
    /// One in-scope row of <paramref name="entity"/>, carrying <paramref name="extra"/> columns and a display value for each,
    /// scoped to <see cref="GrantedEverywhere"/> (or, for service requests, the workforce contact) by <paramref name="id"/>.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> RowCarrying(string entity, Guid id, params (string Column, object Value)[] extra)
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["@logicalName"] = entity };
        switch (entity)
        {
            case "sprk_project":
            case "sprk_matter":
            case "sprk_workassignment":
                row[$"{entity}id"] = id;
                break;
            case "sprk_document":
                row["sprk_documentid"] = Guid.NewGuid();
                row["sprk_project"] = new EntityReference("sprk_project", id);
                break;
            case "sprk_invoice":
                row["sprk_invoiceid"] = Guid.NewGuid();
                row["sprk_matter"] = new EntityReference("sprk_matter", id);
                break;
            case "sprk_servicerequest":
                row["sprk_servicerequestid"] = Guid.NewGuid();
                row["sprk_requestedby"] = id;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(entity), entity, "no test row");
        }
        var formatted = new Dictionary<string, string>();
        foreach (var (column, value) in extra)
        {
            row[column] = value;
            formatted[column] = FormattedColumnValue(column);
        }
        row["@formattedValues"] = formatted;
        return row;
    }

    public static TheoryData<string, string> Task002RecordSelectLookups => new()
    {
        { "sprk_matter", "sprk_mattertype" },
        { "sprk_project", "sprk_projecttype_ref" },
    };

    [Theory]
    [MemberData(nameof(Task002RecordSelectLookups))]
    public async Task GetScopedRecord_WhenSelectNamesAnOwnerGridLookup_ReturnsTheRecordWithTheKey(string entity, string column)
    {
        var id = Guid.NewGuid();
        var value = ColumnValue(column);
        var dataverse = new RecordDouble(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [$"{entity}id"] = id,
            [column] = value,
        });

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            entity, id, column, GrantedEverywhere(id), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        dataverse.Calls.Should().Be(1);
        record.Should().ContainKey(column).WhoseValue.Should().Be(value);
    }

    [Fact]
    public async Task ExecuteScopedFetch_WhenAnInvoiceRowCarriesTheCurrencySiblingsOfAnAdmittedMoneyColumn_KeepsOnlyTheMoneyColumn()
    {
        var id = Guid.NewGuid();
        var row = RowCarrying("sprk_invoice", id,
            ("sprk_totalamount_base", ColumnValue("sprk_totalamount_base")),
            ("transactioncurrencyid", ColumnValue("transactioncurrencyid")),
            ("exchangerate", ColumnValue("exchangerate")));
        var dataverse = new FetchDouble(row);

        var result = await FetchGrantedEverywhere("sprk_invoice", LiveGridFetchXmlFor("sprk_invoice"), id, ProductionRegistry(), dataverse);

        var projected = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities.Should().ContainSingle().Subject;
        projected.Should().ContainKey("sprk_totalamount_base");
        projected.Should().NotContainKey("transactioncurrencyid");
        projected.Should().NotContainKey("exchangerate");
        var formatted = projected["@formattedValues"].Should().BeAssignableTo<IReadOnlyDictionary<string, string>>().Subject;
        formatted.Should().ContainKey("sprk_totalamount_base");
        formatted.Should().NotContainKey("transactioncurrencyid", "a display value is a column value too");
        formatted.Should().NotContainKey("exchangerate", "a display value is a column value too");
    }

    [Theory]
    [MemberData(nameof(Task002SiblingColumnsStillRefused))]
    public async Task ExecuteScopedFetch_WhenASiblingOfAnOwnerGridColumnIsRequested_Returns400AndNeverQueries(string entity, string column)
    {
        var id = Guid.NewGuid();
        var dataverse = new FetchDouble();

        var result = await FetchGrantedEverywhere(entity, GridFetchXmlPlusColumn(entity, column), id, ProductionRegistry(), dataverse);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteScopedFetch_WhenAMatterRowCarriesTheAdmittedAndAnUnadmittedLookup_KeepsOnlyTheAdmittedOneWithItsDisplayValue()
    {
        var id = Guid.NewGuid();
        var matterType = new EntityReference("sprk_mattertype_ref", Guid.NewGuid());
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matterid"] = id,
            ["sprk_mattername"] = "Alpha matter",
            ["sprk_matternumber"] = "MAT-0001",
            ["sprk_mattertype"] = matterType,
            ["sprk_practicearea"] = new EntityReference("sprk_practicearea_ref", Guid.NewGuid()),
            ["@logicalName"] = "sprk_matter",
            ["@formattedValues"] = new Dictionary<string, string>
            {
                ["sprk_mattertype"] = "Litigation",
                ["sprk_practicearea"] = "Employment",
            },
        };
        var dataverse = new FetchDouble(row);

        var result = await FetchGrantedEverywhere("sprk_matter", LiveGridFetchXmlFor("sprk_matter"), id, ProductionRegistry(), dataverse);

        var projected = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!.Entities.Should().ContainSingle().Subject;
        projected.Should().ContainKey("sprk_mattertype").WhoseValue.Should().Be(matterType);
        projected.Should().NotContainKey("sprk_practicearea");
        var formatted = projected["@formattedValues"].Should().BeAssignableTo<IReadOnlyDictionary<string, string>>().Subject;
        formatted.Should().ContainKey("sprk_mattertype").WhoseValue.Should().Be("Litigation");
        formatted.Should().NotContainKey("sprk_practicearea", "a display value is a column value too");
    }

    [Theory]
    [InlineData("sprk_mattertype_ref")]
    [InlineData("sprk_projecttype_ref")]
    public async Task GetScopedRecord_WhenTheEntityIsALookupTargetTableWithNoModule_IsRefusedWithoutReading(string entity)
    {
        var id = Guid.NewGuid();
        var dataverse = new RecordDouble(new Dictionary<string, object?> { ["sprk_name"] = "Litigation" });

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            entity, id, select: null, GrantedEverywhere(id), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

        ShouldBeNotRegistered(result);
        dataverse.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("sprk_mattertype_ref")]
    [InlineData("sprk_projecttype_ref")]
    public async Task ExecuteScopedFetch_WhenTheEntityIsALookupTargetTableWithNoModule_IsRefusedWithoutQuerying(string entity)
    {
        var id = Guid.NewGuid();
        var dataverse = new FetchDouble();
        var fetchXml = $"<fetch><entity name='{entity}'><attribute name='sprk_name'/></entity></fetch>";

        var result = await FetchGrantedEverywhere(entity, fetchXml, id, ProductionRegistry(), dataverse);

        ShouldBeNotRegistered(result);
        dataverse.Calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(RetiredAfterTask157))]
    public async Task ExecuteScopedFetch_WhenARetiredColumnIsRequested_Returns400AndNeverQueries(string entity, string column)
    {
        var id = Guid.NewGuid();
        var dataverse = new FetchDouble();

        var result = await FetchGrantedEverywhere(entity, GridFetchXmlPlusColumn(entity, column), id, ProductionRegistry(), dataverse);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(RetiredAfterTask157))]
    public async Task GetScopedRecord_WhenSelectNamesARetiredColumn_Returns400AndNeverReads(string entity, string column)
    {
        var id = Guid.NewGuid();
        var dataverse = new RecordDouble(new Dictionary<string, object?> { [column] = "x" });

        var result = await ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            entity, id, column, GrantedEverywhere(id), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

        ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorRecordColumnNotPermitted);
        dataverse.Calls.Should().Be(0);
    }

    // The service-requests module is built by a factory from the partner switch, so the registry enumeration alone does not
    // show that BOTH settings carry the same column list. These run against the factory's descriptor directly.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ServiceRequestsModule_WhateverThePartnerSwitch_AdmitsModifiedOnAndRefusesSiblings(bool partnerSwitch)
    {
        var module = Sprk.Bff.Api.Infrastructure.DI.ExternalAccessModule.ServiceRequestsModule(partnerSwitch);
        var registry = Registry(module);
        var contact = Guid.NewGuid();
        var extractor = new FetchXmlEntityExtractor();
        const string gridFetch =
            "<fetch><entity name='sprk_servicerequest'><attribute name='sprk_servicerequestid'/><attribute name='sprk_name'/>" +
            "<attribute name='sprk_requestedby'/>{0}</entity></fetch>";

        module.ReadableColumns.Should().Contain("modifiedon");
        ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
                string.Format(gridFetch, "<attribute name='modifiedon'/>"), module.RecordEntity, module.ReadableColumns, extractor)
            .Verdict.Should().Be(Verdict.Allowed);

        foreach (var sibling in new[] { "ownerid", "sprk_requesttype" })
        {
            ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
                    string.Format(gridFetch, $"<attribute name='{sibling}'/>"), module.RecordEntity, module.ReadableColumns, extractor)
                .Verdict.Should().Be(Verdict.ColumnNotPermitted, sibling);

            // The column guard runs before scoping, so a partner caller the switch leaves with no rows gets the same refusal.
            var dataverse = new FetchDouble();
            var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
                new FetchRequestDto("sprk_servicerequest", string.Format(gridFetch, $"<attribute name='{sibling}'/>"), PagingCookie: null),
                Ciam(), registry, extractor, dataverse.Execute, NoFlags, Narrow, NullLogger.Instance, CancellationToken.None);
            ShouldBeProblem(result, StatusCodes.Status400BadRequest, ExternalModuleDataEndpoints.ErrorFetchXmlColumnNotPermitted);
            dataverse.Calls.Should().Be(0);
        }

        var allowed = new FetchDouble();
        var ok = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_servicerequest", string.Format(gridFetch, "<attribute name='modifiedon'/>"), PagingCookie: null),
            WorkforceContact(contact), registry, extractor, allowed.Execute, NoFlags, Narrow, NullLogger.Instance, CancellationToken.None);
        ok.Should().BeOfType<Ok<FetchResponseDto>>();
        allowed.Calls.Should().Be(1, "a workforce caller with a contact qualifies whatever the partner switch says");
    }

    private static void ShouldBeNotRegistered(IResult result)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions.Should().ContainKey("reasonCode")
            .WhoseValue.Should().Be(ExternalModuleDataEndpoints.DenyModuleNotRegistered);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 036 (NFR-03): a fetch scoped through an accessible set KNOWN to be incomplete says so
    // ═════════════════════════════════════════════════════════════════════════════

    private static CallerPrincipal CiamWithTruncatedSets(Guid[] projects, params string[] truncatedRootTypes) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@lawfirm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = projects
            .Select(id => CallerProjectAccess.FromLevel(id, ExternalAccessLevel.Collaborate))
            .ToList(),
        TruncatedRootEntityTypes = new HashSet<string>(truncatedRootTypes, StringComparer.OrdinalIgnoreCase),
    };

    private static string WireJson(FetchResponseDto dto) =>
        System.Text.Json.JsonSerializer.Serialize(
            dto, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteScopedFetch_WhenTheCallersSetIsTruncated_SaysSo_AndOtherwiseTheWireIsUnchanged(bool truncated)
    {
        var project = Guid.NewGuid();
        var principal = truncated
            ? CiamWithTruncatedSets(new[] { project }, ProjectEntity)
            : CiamWithTruncatedSets(new[] { project });
        var dataverse = new FetchDouble(DocumentRow(project));

        var result = await Fetch(Registry(DocumentsModule()), principal, LiveDocumentsGridFetchXml, dataverse);

        var body = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!;
        dataverse.Calls.Should().Be(1);
        body.Entities.Should().ContainSingle("truncation flags the response; it never drops a row");
        body.Truncated.Should().Be(truncated);
        var json = WireJson(body);
        if (truncated)
        {
            json.Should().Contain("\"truncated\":true");
        }
        else
        {
            json.Should().NotContain("truncated", "an uncapped response is byte-for-byte what it was before the field existed");
        }
    }

    [Fact]
    public async Task ExecuteScopedFetch_WhenTheAccessibleSetIsEmptyButTruncated_SaysSo_WithoutQuerying()
    {
        // A capped composition can leave a module with no ids at all: "0 rows" is then not the whole truth either.
        var principal = CiamWithTruncatedSets(Array.Empty<Guid>(), "sprk_matter");
        var dataverse = new FetchDouble();

        var result = await Fetch(Registry(DocumentsModule()), principal, LiveDocumentsGridFetchXml, dataverse);

        var body = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!;
        dataverse.Calls.Should().Be(0);
        body.Entities.Should().BeEmpty();
        body.Truncated.Should().BeTrue();
        WireJson(body).Should().Contain("\"truncated\":true");
    }
}
