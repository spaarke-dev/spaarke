// spaarke-SPA-external-access-platform-r4 task 023b: the "External record documents" sprk_gridconfiguration row.
//
// KEEP-path classification (ADR-038 §2 path #1 security-auth): the `grid-configuration` external module admits a STATIC set of
// grid-configuration ids (ExternalAccessModule.OutsideCounselGridConfigurationIds) and refuses every other id, fail closed. Which
// ids an external caller may read is an AUTHORIZATION decision, so this file lives under tests/integration/auth/**. It compiles
// into Sprk.Bff.Api.Tests through the csproj auth glob.
//
// WHAT IS UNDER TEST.
//   • The REAL production registration (ExternalAccessModule) through the REAL single-record pipeline
//     (ExternalModuleDataEndpoints.GetScopedRecordCoreAsync), with a test double for the one thing that cannot run in-process
//     (the app-only Dataverse read). The double counts its calls, so "Dataverse was not read" is asserted, not inferred.
//   • The committed definition (config/external-record-documents.gridconfiguration.json) against the REAL documents module and
//     the REAL FetchXML guard: the definition's FetchXML must be admitted in full, and the one column the owner wants shown but
//     the documents module does not expose (sprk_summary) must stay out of it.
//
// Must-fire / must-not-fire pairs: the new id is admitted (and the six existing ids still are); an unlisted id, and an id one
// digit off the new one, are refused with the same 403 and no read. The definition's FetchXML is admitted (must-not-fire); the
// same FetchXML with sprk_summary added is refused (must-fire).
//
// Banned-pattern compliance (ADR-038): no Mock<HttpMessageHandler>, no DI-registration assertions, no constructor null-checks.
// Names are {Method}_{Scenario}_{ExpectedResult}.

using System.Text.Json.Nodes;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

using Verdict = ExternalModuleDataEndpoints.FetchXmlGuardVerdict;

public sealed class ExternalRecordDocumentsGridConfigurationTests : IClassFixture<ExternalAccessContractFixture>
{
    private const string GridConfigurationEntity = "sprk_gridconfiguration";
    private const string DocumentEntity = "sprk_document";

    /// <summary>The new row's fixed id (config/external-record-documents.gridconfiguration.json; the SPA's constant in task 023c).</summary>
    private static readonly Guid RecordDocumentsGridId = Guid.Parse("286be8b4-f96f-4d5a-b1d5-82fd83ca9693");

    /// <summary>The six outside-counsel grid configurations admitted before task 023b. They must stay admitted.</summary>
    public static TheoryData<string> ExistingGridConfigurationIds => new()
    {
        { "61711823-1092-f111-b8dc-7ced8ddc4a05" }, // Projects
        { "3af4102c-1092-f111-b8dc-7ced8ddc4a05" }, // Documents
        { "3ff4102c-1092-f111-b8dc-7ced8ddc4a05" }, // Invoices
        { "42f4102c-1092-f111-b8dc-7ced8ddc4a05" }, // Work Assignments
        { "583a2a33-1092-f111-b8dc-7ced8ddc4a05" }, // Matters
        { "403e5d37-cb94-f111-b8db-00224835447a" }, // Service Requests
    };

    private readonly ExternalAccessContractFixture _fixture;

    public ExternalRecordDocumentsGridConfigurationTests(ExternalAccessContractFixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    private ExternalModuleRegistry ProductionRegistry() =>
        _fixture.Services.GetRequiredService<ExternalModuleRegistry>();

    /// <summary>A CIAM contact with NO grants at all: the grid-configuration module's set does not depend on the caller, so any
    /// refusal or admission below comes from the static id set alone.</summary>
    private static CallerPrincipal CallerWithNoGrants() => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external@lawfirm.test",
        Oid = Guid.NewGuid().ToString(),
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    /// <summary>The app-only single-record read, replaced; records every call.</summary>
    private sealed class RecordDouble
    {
        private readonly IReadOnlyDictionary<string, object?> _record;

        public RecordDouble(IReadOnlyDictionary<string, object?> record) => _record = record;

        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, object?>> Read(string entity, Guid id, string[]? columns, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(_record);
        }
    }

    private static RecordDouble ConfigRecord(Guid id) => new(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
    {
        ["sprk_gridconfigurationid"] = id,
        ["sprk_name"] = "External record documents",
        ["sprk_configjson"] = "{\"_version\":\"1.0\"}",
    });

    private Task<IResult> ReadConfiguration(Guid id, RecordDouble dataverse) =>
        ExternalModuleDataEndpoints.GetScopedRecordCoreAsync(
            GridConfigurationEntity, id, "sprk_configjson", CallerWithNoGrants(), ProductionRegistry(), dataverse.Read,
            NullLogger.Instance, CancellationToken.None);

    /// <summary>A refusal from the Tier-2 gate is a 403 whose stable code is in the <c>reasonCode</c> extension
    /// (<c>ProblemDetailsHelper.Forbidden</c>); a column refusal uses <c>errorCode</c>.</summary>
    private static void ShouldBeForbidden(IResult result, string reasonCode)
    {
        var problem = result.Should().BeOfType<ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions.Should().ContainKey("reasonCode").WhoseValue.Should().Be(reasonCode);
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 1. THE ID ADMISSION (grid-configuration module)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetScopedRecord_ForTheExternalRecordDocumentsGridConfiguration_ReadsItsConfigJson()
    {
        var dataverse = ConfigRecord(RecordDocumentsGridId);

        var result = await ReadConfiguration(RecordDocumentsGridId, dataverse);

        var record = result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>().Subject.Value!;
        dataverse.Calls.Should().Be(1);
        record.Should().ContainKey("sprk_configjson", "the shared DataGrid's config load is exactly this read");
    }

    [Theory]
    [MemberData(nameof(ExistingGridConfigurationIds))]
    public async Task GetScopedRecord_ForTheSixExistingGridConfigurations_StillReadsThem(string id)
    {
        var gridId = Guid.Parse(id);
        var dataverse = ConfigRecord(gridId);

        var result = await ReadConfiguration(gridId, dataverse);

        result.Should().BeOfType<Ok<IReadOnlyDictionary<string, object?>>>();
        dataverse.Calls.Should().Be(1);
    }

    [Fact]
    public async Task GetScopedRecord_ForAnUnlistedGridConfigurationId_RefusesWithoutReading()
    {
        var dataverse = ConfigRecord(Guid.NewGuid());

        var result = await ReadConfiguration(Guid.NewGuid(), dataverse);

        ShouldBeForbidden(result, ExternalModuleDataEndpoints.DenyRecordNotAccessible);
        dataverse.Calls.Should().Be(0, "an id outside the static set is refused before any Dataverse read");
    }

    [Fact]
    public async Task GetScopedRecord_ForAnIdOneDigitOffTheNewOne_RefusesWithoutReading()
    {
        // Pins the exact id: a mistyped or neighbouring id must not be admitted by accident.
        var neighbour = Guid.Parse("286be8b4-f96f-4d5a-b1d5-82fd83ca9694");
        var dataverse = ConfigRecord(neighbour);

        var result = await ReadConfiguration(neighbour, dataverse);

        ShouldBeForbidden(result, ExternalModuleDataEndpoints.DenyRecordNotAccessible);
        dataverse.Calls.Should().Be(0);
    }

    [Fact]
    public void ProductionModule_GridConfiguration_AdmitsExactlyTheSevenOutsideCounselIds()
    {
        var module = ProductionRegistry().FindByEntity(GridConfigurationEntity)!;

        var admitted = module.AccessibleRecordIds!(CallerWithNoGrants());

        admitted.Should().BeEquivalentTo(
            ExistingGridConfigurationIds.Select(row => Guid.Parse((string)row[0])).Append(RecordDocumentsGridId),
            "task 023b adds ONE id and no other; a wider set would expose other grid configurations");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // 2. THE COMMITTED DEFINITION ↔ THE DOCUMENTS MODULE
    // ═════════════════════════════════════════════════════════════════════════════

    private static JsonObject LoadDefinition()
    {
        var path = Path.Combine(RepoRoot(), "config", "external-record-documents.gridconfiguration.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            var git = Path.Combine(dir, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                return dir;
            }
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Repository root not found above " + AppContext.BaseDirectory);
    }

    private static string DefinitionFetchXml() =>
        LoadDefinition()["configJson"]!["source"]!["fetchXml"]!.GetValue<string>();

    private ExternalModuleDataEndpoints.FetchXmlGuardResult EvaluateAgainstTheDocumentsModule(string fetchXml)
    {
        var module = ProductionRegistry().FindByEntity(DocumentEntity)!;
        return ExternalModuleDataEndpoints.EvaluateFetchXmlGuard(
            fetchXml, module.RecordEntity, module.ReadableColumns, new FetchXmlEntityExtractor());
    }

    [Fact]
    public void Definition_NamesTheAdmittedIdTheDocumentEntityAndTheOwnersColumns()
    {
        var def = LoadDefinition();

        Guid.Parse(def["id"]!.GetValue<string>()).Should().Be(RecordDocumentsGridId, "the file and the BFF set must name the same id");
        def["name"]!.GetValue<string>().Should().Be("External record documents");
        def["entity"]!.GetValue<string>().Should().Be(DocumentEntity);

        var config = def["configJson"]!.AsObject();
        config["_version"]!.GetValue<string>().Should().Be("1.0", "the shared DataGrid reads the v1.0 schema");
        config["source"]!["type"]!.GetValue<string>().Should().Be("inline");
        config["display"]!["truncateWithTooltip"]!.GetValue<bool>().Should().BeTrue();

        var layout = XDocument.Parse(config["source"]!["layoutXml"]!.GetValue<string>());
        layout.Descendants("cell").Select(c => (string)c.Attribute("name")!)
            .Should().Equal("sprk_documentname", "sprk_documenttype", "createdon", "sprk_summary");
        config["columns"]!["sprk_documentname"]!["label"]!.GetValue<string>().Should().Be("Name");
        config["columns"]!["sprk_documenttype"]!["label"]!.GetValue<string>().Should().Be("Type");
        config["columns"]!["createdon"]!["label"]!.GetValue<string>().Should().Be("Date Added");
        config["columns"]!["sprk_summary"]!["label"]!.GetValue<string>().Should().Be("AI Summary");
    }

    [Fact]
    public void Definition_FetchXml_IsAdmittedInFullByTheDocumentsModule()
    {
        var result = EvaluateAgainstTheDocumentsModule(DefinitionFetchXml());

        result.Verdict.Should().Be(Verdict.Allowed,
            $"the definition's FetchXML must stay inside the documents module's ReadableColumns (violation: {result.ColumnViolation})");
    }

    [Fact]
    public void Definition_FetchXml_WithSprkSummaryAdded_IsRefusedAsColumnNotPermitted()
    {
        // The documents module does not expose sprk_summary (the SPA's rows come from the record route, which does). The
        // definition keeps it as a layout cell only; this proves the reason: selecting it fails the whole read.
        var withSummary = DefinitionFetchXml().Replace("<order", "<attribute name='sprk_summary' /><order", StringComparison.Ordinal);

        var result = EvaluateAgainstTheDocumentsModule(withSummary);

        result.Verdict.Should().Be(Verdict.ColumnNotPermitted);
    }
}
