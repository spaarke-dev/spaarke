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
using Sprk.Bff.Api.Services.Communication;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.EmailPropose;

/// <summary>
/// email-communication-intelligence-r1 task 030 (NFR-07 blocking merge gate) — the email-propose (Job B)
/// eval family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to every sibling family in this directory):
/// <c>[Trait("Category", "GoldenUtteranceEval")]</c> joins this class to the dedicated <c>eval-gate</c> job
/// (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>, NO <c>continue-on-error</c>) with ZERO
/// CI-YAML change. Does NOT touch the shared <c>golden-utterances.json</c> family coverage — that file only
/// carries the single GU-### case the <see cref="ConsumerTypes.All"/> full-catalog forcing function scans.
/// </para>
/// <para>
/// <b>Not a chat/loop capability.</b> PROPOSE-FIELD-UPDATES is invoked directly off the Communication
/// enrichment path, so every case uses <c>channel: "event"</c>. What IS proven mechanically, no live model:
/// (1) the <c>proposals[]</c> output CONTRACT (via the Action's own worked example, cross-checked against
/// both the action file and this seed), (2) the NFR-06 verify-cited-text gate, (3) the Binding resolves
/// through the REAL <see cref="ConsumerRoutingService"/>, (4) the FR-05/FR-09 no-second-pass structural
/// guarantee.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class ProposeFieldUpdatesEvalTests
{
    private readonly ITestOutputHelper _output;

    public ProposeFieldUpdatesEvalTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "structured-output", "verify-cited-text", "binding-resolution", "no-second-pass",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "event" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["structured-output"] = 1,
        ["verify-cited-text"] = 1,
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

        suite.SchemaVersion.Should().Be("propose-field-updates-eval@v1");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("PROPOSE-", StringComparison.Ordinal),
            "email-propose eval cases are namespaced PROPOSE-### so they never collide with GU-###/TRIAGE-###/NDA-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: PROPOSE-FIELD-UPDATES is event-triggered only — no click/text dispatch is claimed");
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
        var mirrorRow = LoadMirrorRow("email-propose");

        foreach (var c in suite.Cases.Where(c => c.Expected.ConsumerType is not null))
        {
            c.Expected.ConsumerType.Should().Be("email-propose");
            mirrorRow.GetProperty("actionCode").GetString().Should().Be("propose-field-updates",
                $"case {c.CaseId}: the mirror row's actionCode must resolve to the PROPOSE-FIELD-UPDATES Action");
        }
    }

    // -------------------------------------------------------------------------
    // structured-output family — the Action's declared proposals[] contract + worked example
    // -------------------------------------------------------------------------

    [Fact]
    public void Action_DeclaresTheProposalsOutputContract_WithCitationShape()
    {
        var action = LoadActionJson();
        var itemProps = action.GetProperty("outputSchema")
            .GetProperty("properties").GetProperty("proposals")
            .GetProperty("items").GetProperty("properties");

        var names = itemProps.EnumerateObject().Select(p => p.Name).ToList();
        names.Should().BeEquivalentTo(new[] { "field", "newValue", "citation", "reason", "confidence" },
            "FR-09: each proposal MUST declare exactly {field, newValue, citation, reason, confidence}");

        var citationProps = itemProps.GetProperty("citation").GetProperty("properties");
        citationProps.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(new[] { "source", "locator", "quotedText" },
            "NFR-06: the citation carries source + locator + the verbatim quotedText");
        citationProps.GetProperty("source").GetProperty("enum").EnumerateArray().Select(e => e.GetString())
            .Should().BeEquivalentTo(new[] { "subject", "body", "attachment" },
                "citation.source is the closed set of citable email parts");

        itemProps.GetProperty("confidence").GetProperty("type").GetString().Should().Be("number");
    }

    [Fact]
    public void WorkedExample_MatchesBothTheActionFile_AndThisSuitesGroundedExpectation()
    {
        var suite = LoadSuite();
        var structuredCase = suite.Cases.Single(c => c.Family == "structured-output");
        var expected = structuredCase.Expected.ExampleProposal!;

        var action = LoadActionJson();
        var actionProposal = action.GetProperty("examples")[0].GetProperty("output").GetProperty("proposals")[0];

        actionProposal.GetProperty("field").GetString().Should().Be(expected.Field);
        actionProposal.GetProperty("newValue").GetString().Should().Be(expected.NewValue);
        actionProposal.GetProperty("reason").GetString().Should().Be(expected.Reason);
        actionProposal.GetProperty("confidence").GetDouble().Should().Be(expected.Confidence);
        actionProposal.GetProperty("citation").GetProperty("quotedText").GetString()
            .Should().Be(expected.Citation.QuotedText,
                "the eval seed and the Action's own worked example must agree — a drift in either file fails here");
    }

    [Fact]
    public void CommunicationProposeAi_ParsesTheWorkedExampleShape_ThroughTheRealParsingPath()
    {
        // Drives the REAL CommunicationProposeAi output-parsing path (reflection-free construction of the
        // raw JsonElement exactly as ActionRunner.RunAsync would return it, then asserts the public contract).
        var action = LoadActionJson();
        var exampleOutputJson = action.GetProperty("examples")[0].GetProperty("output").GetRawText();
        using var doc = JsonDocument.Parse(exampleOutputJson);

        var candidates = InvokeParseResult(doc.RootElement);

        candidates.Should().ContainSingle();
        var proposal = candidates[0];
        proposal.Field.Should().Be("sprk_closingdate");
        proposal.NewValue.Should().Be("2026-08-15");
        proposal.Citation.QuotedText.Should().Be("the closing has been moved to August 15, 2026");
        proposal.Citation.Source.Should().Be("body");
        proposal.Confidence.Should().Be(0.9);
    }

    // -------------------------------------------------------------------------
    // verify-cited-text family — NFR-06 trust gate (pure, no live model)
    // -------------------------------------------------------------------------

    [Fact]
    public void VerifyCitedText_VerbatimSpanVerifies_FabricatedQuoteDoesNot()
    {
        var source = CitationVerifier.BuildSourceText(
            subject: "Closing moved",
            bodyText: "Counsel, the closing has been moved to August 15, 2026.",
            attachmentText: null);

        CitationVerifier.IsCitedTextPresent(source, "the closing has been moved to August 15, 2026")
            .Should().BeTrue("NFR-06: a verbatim citation verifies and the proposal is stored");
        CitationVerifier.IsCitedTextPresent(source, "the settlement was finalized at $2,000,000")
            .Should().BeFalse("NFR-06 negative: a fabricated citation is DROPPED, never stored");
    }

    // -------------------------------------------------------------------------
    // no-second-pass family — FR-05/FR-09 structural guarantee
    // -------------------------------------------------------------------------

    [Fact]
    public void CommunicationProposeAi_HasNoClassificationDependency_CannotReClassifyEvenByMistake()
    {
        var ctor = typeof(CommunicationProposeAi).GetConstructors().Single();
        var paramTypeNames = ctor.GetParameters().Select(p => p.ParameterType.Name).ToList();

        paramTypeNames.Should().NotContain("ICommunicationClassificationAi",
            "FR-05/FR-09: the facade must be STRUCTURALLY incapable of a second classification call — it has no handle to the classifier");
        paramTypeNames.Should().NotContain("IOpenAiClient",
            "ADR-013: no AI-internal client injected directly — only the Linear AI Consumer primitives");

        _output.WriteLine($"CommunicationProposeAi ctor deps: {string.Join(", ", paramTypeNames)}");
    }

    [Fact]
    public void CommunicationProposeRequest_TriageIsGroundingOnly_NotRequired()
    {
        var triageProp = typeof(CommunicationProposeRequest).GetProperty(nameof(CommunicationProposeRequest.Triage))!;
        // Triage is an OPTIONAL grounding input (nullable, no `required` modifier) — the propose path runs
        // whether or not a triage result exists; it never re-derives classification.
        var isRequired = triageProp.GetCustomAttributesData().Any(a => a.AttributeType.Name == "RequiredMemberAttribute");
        isRequired.Should().BeFalse("the already-produced triage output is grounding only — never a re-classification input");
    }

    // -------------------------------------------------------------------------
    // binding-resolution family — the REAL ConsumerRoutingService over a stubbed
    // Dataverse boundary, shaped EXACTLY like the real mirror row.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EmailProposeBinding_ResolvesThroughTheRealRoutingService_ByConsumerType()
    {
        var mirrorRow = LoadMirrorRow("email-propose");
        var rowId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var stubEntity = BuildRowFromMirror(mirrorRow, rowId, actionId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { stubEntity }));

        var routing = CreateRoutingService(entityService);

        var resolvedActionId = await routing.ResolveActionAsync("email-propose", "default");
        resolvedActionId.Should().Be(actionId,
            "IActionResolver.ResolveAsync -> IConsumerRoutingService.ResolveActionAsync is the exact read " +
            "CommunicationEnrichmentService's email-propose step performs (via ConsumerTypes.EmailPropose)");

        _output.WriteLine("Mechanical routing assertion — email-propose (task 030): resolves via ResolveActionAsync -> propose-field-updates Action.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static IReadOnlyList<ProposedFieldCandidate> InvokeParseResult(JsonElement output)
    {
        var method = typeof(CommunicationProposeAi).GetMethod(
            "ParseResult",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        return (IReadOnlyList<ProposedFieldCandidate>)method.Invoke(null, new object[] { output })!;
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

    /// <summary>Build a stub <c>sprk_playbookconsumer</c> Entity from the email-propose mirror row (a Linear
    /// Consumer — NO disposition/risk/captureMode/toolDescription, exercising the safe-default fallback for
    /// legacy/null columns in <c>ConsumerRoutingService.MapBinding</c>).</summary>
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

    private static ProposeEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "propose-field-updates-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"propose-field-updates-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup glob covers it automatically)");
        var suite = JsonSerializer.Deserialize<ProposeEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the email-propose case schema");
        return suite!;
    }

    private static JsonElement LoadActionJson()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "propose-field-updates.action.json");
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
            "the email-propose mirror-grounding assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// email-propose eval case schema
// -----------------------------------------------------------------------------

public sealed record ProposeEvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<ProposeEvalCase> Cases { get; init; } = new();
}

public sealed record ProposeEvalCase
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
    public ProposeEvalExpected Expected { get; init; } = new();
}

public sealed record ProposeEvalExpected
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

    [JsonPropertyName("exampleProposal")]
    public ProposeEvalExampleProposal? ExampleProposal { get; init; }

    [JsonPropertyName("verifiedVsFabricatedDiffers")]
    public bool? VerifiedVsFabricatedDiffers { get; init; }

    [JsonPropertyName("noSecondClassificationPass")]
    public bool? NoSecondClassificationPass { get; init; }
}

public sealed record ProposeEvalExampleProposal
{
    [JsonPropertyName("field")]
    public string Field { get; init; } = string.Empty;

    [JsonPropertyName("newValue")]
    public string NewValue { get; init; } = string.Empty;

    [JsonPropertyName("citation")]
    public ProposeEvalCitation Citation { get; init; } = new();

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public sealed record ProposeEvalCitation
{
    [JsonPropertyName("source")]
    public string Source { get; init; } = string.Empty;

    [JsonPropertyName("locator")]
    public string Locator { get; init; } = string.Empty;

    [JsonPropertyName("quotedText")]
    public string QuotedText { get; init; } = string.Empty;
}
