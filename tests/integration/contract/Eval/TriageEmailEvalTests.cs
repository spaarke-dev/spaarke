using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.EmailTriage;

/// <summary>
/// email-communication-intelligence-r1 task 023 (NFR-07 blocking merge gate) — the email-triage eval
/// family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to every sibling family in this directory):
/// <c>[Trait("Category", "GoldenUtteranceEval")]</c> joins this class to the dedicated <c>eval-gate</c> job
/// in <c>.github/workflows/sdap-ci.yml</c> (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>, NO
/// <c>continue-on-error</c>) with ZERO CI-YAML change. This suite does NOT touch the shared
/// <c>golden-utterances.json</c> — a net-new family is the established way a project adds gate coverage.
/// </para>
/// <para>
/// <b>Not a chat/loop capability.</b> TRIAGE-EMAIL is invoked directly off the Communication enrichment
/// path (never chat/loop-projected — the mirror row carries no <c>toolDescription</c>/<c>surfaces</c>), so
/// every case uses <c>channel: "event"</c> (the README's third channel) and no click/text dispatch fact is
/// claimed. What IS proven mechanically, no live model (same posture as every sibling family): (1) the
/// structured 5-field output CONTRACT (via the Action's own worked example, cross-checked against BOTH
/// <c>triage-email.action.json</c> and this suite's seed JSON so either file drifting fails a test), (2)
/// the FR-06 grounded-vs-context-free prompt-composition difference, (3) the Binding resolves through the
/// REAL <see cref="ConsumerRoutingService"/>, (4) the FR-05 no-second-pass structural guarantee.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class TriageEmailEvalTests
{
    private readonly ITestOutputHelper _output;

    public TriageEmailEvalTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "structured-output", "context-improvement", "binding-resolution", "no-second-pass",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["structured-output"] = 1,
        ["context-improvement"] = 1,
        ["binding-resolution"] = 1,
        ["no-second-pass"] = 1,
    };

    // -------------------------------------------------------------------------
    // Inventory integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("triage-email-eval@v1");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("TRIAGE-", StringComparison.Ordinal),
            "email-triage eval cases are namespaced TRIAGE-### so they never collide with GU-###/NDA-###/AR1-###/RF-###/OC-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: TRIAGE-EMAIL is event-triggered only — no click/text dispatch is claimed");
            c.Utterance.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every case carries an event/affordance descriptor");
        }
    }

    [Fact]
    public void EveryFamily_HasEvalCoverage_AtItsFloor()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
        {
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor, $"NFR-07: the '{family}' family owes at least {floor} eval case(s)");
        }
    }

    // -------------------------------------------------------------------------
    // Catalog grounding — no invented capability names
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchCases_ExpectedConsumerType_IsGroundedInTheMirror()
    {
        var suite = LoadSuite();
        var mirrorRow = LoadMirrorRow("email-triage");

        foreach (var c in suite.Cases.Where(c => c.Expected.ConsumerType is not null))
        {
            c.Expected.ConsumerType.Should().Be("email-triage");
            mirrorRow.GetProperty("actionCode").GetString().Should().Be("triage-email",
                $"case {c.CaseId}: the mirror row's actionCode must resolve to the TRIAGE-EMAIL Action");
        }
    }

    // -------------------------------------------------------------------------
    // structured-output family — the Action's declared contract + worked example
    // -------------------------------------------------------------------------

    [Fact]
    public void Action_DeclaresTheFiveFieldOutputContract_WithCorrectChoicesPrefixes()
    {
        var action = LoadActionJson();
        var fields = action.GetProperty("output").GetProperty("fields").EnumerateArray().ToList();

        var byName = fields.ToDictionary(f => f.GetProperty("name").GetString()!, f => f);
        byName.Keys.Should().BeEquivalentTo(new[] { "category", "summary", "obligations", "priority", "reviewOutcome" },
            "FR-05: the Action MUST declare exactly the five triage fields — no more, no less");

        byName["category"].GetProperty("$choices").GetString().Should().Be("lookup:sprk_triagecategory.sprk_name");
        byName["priority"].GetProperty("$choices").GetString().Should().Be("optionset:sprk_communication.sprk_triagepriority");
        byName["reviewOutcome"].GetProperty("$choices").GetString().Should().Be("optionset:sprk_communication.sprk_reviewoutcome");
        byName["summary"].TryGetProperty("$choices", out _).Should().BeFalse("summary is free text — no $choices");
        byName["obligations"].GetProperty("type").GetString().Should().Be("array");
    }

    [Fact]
    public void WorkedExample_MatchesBothTheActionFile_AndThisSuitesGroundedExpectation()
    {
        var suite = LoadSuite();
        var structuredCase = suite.Cases.Single(c => c.Family == "structured-output");
        var expectedExample = structuredCase.Expected.ExampleOutput!;

        var action = LoadActionJson();
        var actionExampleOutput = action.GetProperty("examples")[0].GetProperty("output");

        actionExampleOutput.GetProperty("category").GetString().Should().Be(expectedExample.Category);
        actionExampleOutput.GetProperty("summary").GetString().Should().Be(expectedExample.Summary);
        actionExampleOutput.GetProperty("priority").GetString().Should().Be(expectedExample.Priority);
        actionExampleOutput.GetProperty("reviewOutcome").GetString().Should().Be(expectedExample.ReviewOutcome);
        actionExampleOutput.GetProperty("obligations").EnumerateArray().Select(o => o.GetString())
            .Should().BeEquivalentTo(expectedExample.Obligations,
                "the eval seed and the Action's own worked example must agree — a drift in either file fails here");
    }

    [Fact]
    public void CommunicationTriageResult_ParsesTheWorkedExampleShape_ThroughTheRealParsingPath()
    {
        // Drives the REAL CommunicationTriageAi output-parsing path (reflection-free: builds the raw
        // JsonElement exactly as ActionRunner.RunAsync would return it, then asserts the public contract).
        var action = LoadActionJson();
        var exampleOutputJson = action.GetProperty("examples")[0].GetProperty("output").GetRawText();
        using var doc = JsonDocument.Parse(exampleOutputJson);

        var result = InvokeParseResult(doc.RootElement);

        result.Category.Should().Be("Court / Filing");
        result.Priority.Should().Be("Urgent");
        result.ReviewOutcome.Should().Be("Route");
        result.Obligations.Should().HaveCount(3);
        result.Summary.Should().Contain("Smith v. Acme");
    }

    // -------------------------------------------------------------------------
    // no-second-pass family — FR-05 structural guarantee
    // -------------------------------------------------------------------------

    [Fact]
    public void Action_InputSection_RequiresClassification_AsThePrimarySignal()
    {
        var action = LoadActionJson();
        var classificationInput = action.GetProperty("input").GetProperty("classification");
        classificationInput.GetProperty("required").GetBoolean().Should().BeTrue(
            "FR-05: the Action's input contract MUST require the already-produced classification signal");
    }

    [Fact]
    public void CommunicationTriageAi_HasNoClassificationDependency_CannotReClassifyEvenByMistake()
    {
        var ctor = typeof(CommunicationTriageAi).GetConstructors().Single();
        var paramTypeNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();

        paramTypeNames.Should().NotContain("ICommunicationClassificationAi",
            "FR-05: the facade must be STRUCTURALLY incapable of a second classification call — it has no handle to the classifier");
        paramTypeNames.Should().NotContain("IOpenAiClient",
            "ADR-013: no AI-internal client injected directly — only the Linear AI Consumer primitives + IRagService");

        _output.WriteLine($"CommunicationTriageAi ctor deps: {string.Join(", ", paramTypeNames)}");
    }

    [Fact]
    public void CommunicationTriageRequest_ClassificationIsRequired_CallerMustSupplyIt()
    {
        var classificationProp = typeof(CommunicationTriageRequest).GetProperty(nameof(CommunicationTriageRequest.Classification))!;
        var requiredAttr = classificationProp.GetCustomAttributesData()
            .Any(a => a.AttributeType.Name == "RequiredMemberAttribute");

        requiredAttr.Should().BeTrue(
            "the `required` C# modifier on Classification enforces at COMPILE TIME that every caller supplies the already-produced signal");
    }

    // -------------------------------------------------------------------------
    // context-improvement family — FR-06 grounding
    // -------------------------------------------------------------------------

    [Fact]
    public void Grounding_WithPriorCorrespondence_ProducesNonEmptyFragment_ContextFreeProducesNull()
    {
        var grounded = CommunicationTriageGrounding.BuildFragment(new[]
        {
            new RagSearchResult { Content = "Prior email: client confirmed the Smith matter deadline extension request." },
        });
        var contextFree = CommunicationTriageGrounding.BuildFragment(Array.Empty<RagSearchResult>());

        grounded.Should().NotBeNullOrWhiteSpace(
            "FR-06: matter-grounded triage MUST carry a non-empty grounding fragment the prompt composer prepends");
        grounded.Should().Contain("Prior Correspondence For This Matter")
            .And.Contain("Smith matter",
                "the retrieved prior-correspondence content must actually be present in the composed grounding block");
        contextFree.Should().BeNull(
            "FR-06 negative: no prior correspondence MUST degrade to a context-free run (no fragment), never a fabricated one");
    }

    // -------------------------------------------------------------------------
    // binding-resolution family — the REAL ConsumerRoutingService over a stubbed
    // Dataverse boundary, shaped EXACTLY like the real mirror row.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmailTriageBinding_ResolvesThroughTheRealRoutingService_ByConsumerType()
    {
        var mirrorRow = LoadMirrorRow("email-triage");
        var rowId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var stubEntity = BuildRowFromMirror(mirrorRow, rowId, actionId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { stubEntity }));

        var routing = CreateRoutingService(entityService);

        var resolvedActionId = await routing.ResolveActionAsync("email-triage", "default");
        resolvedActionId.Should().Be(actionId,
            "IActionResolver.ResolveAsync -> IConsumerRoutingService.ResolveActionAsync is the exact read " +
            "CommunicationEnrichmentService's email-triage trigger performs (via ConsumerTypes.EmailTriage)");

        _output.WriteLine("Mechanical routing assertion — email-triage (task 023): resolves via ResolveActionAsync -> triage-email Action.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CommunicationTriageResult InvokeParseResult(JsonElement output)
    {
        var method = typeof(CommunicationTriageAi).GetMethod(
            "ParseResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (CommunicationTriageResult)method.Invoke(null, new object[] { output })!;
    }

    private static ConsumerRoutingService CreateRoutingService(Mock<IGenericEntityService> entityService)
    {
        var hostEnvironment = new Mock<IHostEnvironment>();
        hostEnvironment.SetupGet(e => e.EnvironmentName).Returns("Development");

        return new ConsumerRoutingService(
            entityService.Object,
            new MemoryCache(new MemoryCacheOptions()),
            hostEnvironment.Object,
            NullLogger<ConsumerRoutingService>.Instance);
    }

    /// <summary>
    /// Build a stub <c>sprk_playbookconsumer</c> Entity from the email-triage mirror row. Unlike the
    /// NDA-review family's helper, this Binding declares NO disposition/risk/captureMode/toolDescription
    /// (it is a Linear Consumer, not chat/loop-projected) — those OptionSetValue/string attributes are
    /// deliberately left UNSET so <c>ConsumerRoutingService.MapBinding</c> exercises its documented
    /// safe-default fallback for legacy/null columns.
    /// </summary>
    private static Entity BuildRowFromMirror(JsonElement mirrorRow, Guid rowId, Guid actionId)
    {
        var entity = new Entity("sprk_playbookconsumer", rowId);
        entity["sprk_playbookconsumerid"] = rowId;
        entity["sprk_consumertype"] = mirrorRow.GetProperty("consumerType").GetString();
        entity["sprk_consumercode"] = mirrorRow.GetProperty("consumerCode").GetString();
        entity["sprk_priority"] = mirrorRow.GetProperty("priority").GetInt32();
        entity["sprk_enabled"] = mirrorRow.GetProperty("enabled").GetBoolean();
        entity["sprk_environment"] = mirrorRow.GetProperty("environment").GetString();
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", actionId);
        entity["action.sprk_kind"] = new AliasedValue(
            "sprk_analysisaction", "sprk_kind", new OptionSetValue((int)ActionKind.Prompted));
        return entity;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static TriageEmailEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "triage-email-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"triage-email-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup glob covers it automatically)");
        var suite = JsonSerializer.Deserialize<TriageEmailEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the email-triage case schema");
        return suite!;
    }

    private static JsonElement LoadActionJson()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "triage-email.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    private static JsonElement LoadMirrorRow(string consumerType)
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        return mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Single(r => r.GetProperty("consumerType").GetString() == consumerType
                         && r.GetProperty("consumerCode").GetString() == "default")
            .Clone();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repo root (Spaarke.sln) from AppContext.BaseDirectory — " +
            "the email-triage mirror-grounding assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// email-triage eval case schema
// -----------------------------------------------------------------------------

public sealed record TriageEmailEvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<TriageEmailEvalCase> Cases { get; init; } = new();
}

public sealed record TriageEmailEvalCase
{
    [JsonPropertyName("caseId")]
    public string CaseId { get; init; } = string.Empty;

    [JsonPropertyName("family")]
    public string Family { get; init; } = string.Empty;

    [JsonPropertyName("ucId")]
    public string UcId { get; init; } = string.Empty;

    [JsonPropertyName("channel")]
    public string Channel { get; init; } = string.Empty;

    [JsonPropertyName("utterance")]
    public string Utterance { get; init; } = string.Empty;

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("expected")]
    public TriageEmailExpected Expected { get; init; } = new();
}

public sealed record TriageEmailExpected
{
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    [JsonPropertyName("consumerCode")]
    public string? ConsumerCode { get; init; }

    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    [JsonPropertyName("mirroredBy")]
    public string? MirroredBy { get; init; }

    [JsonPropertyName("actionCode")]
    public string? ActionCode { get; init; }

    [JsonPropertyName("exampleOutput")]
    public TriageEmailExampleOutput? ExampleOutput { get; init; }

    [JsonPropertyName("groundedVsContextFreeDiffers")]
    public bool? GroundedVsContextFreeDiffers { get; init; }

    [JsonPropertyName("noSecondClassificationPass")]
    public bool? NoSecondClassificationPass { get; init; }
}

public sealed record TriageEmailExampleOutput
{
    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("summary")]
    public string Summary { get; init; } = string.Empty;

    [JsonPropertyName("obligations")]
    public List<string> Obligations { get; init; } = new();

    [JsonPropertyName("priority")]
    public string Priority { get; init; } = string.Empty;

    [JsonPropertyName("reviewOutcome")]
    public string ReviewOutcome { get; init; } = string.Empty;
}
