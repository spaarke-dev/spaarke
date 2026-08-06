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
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.NdaReview;

/// <summary>
/// <c>ai-advanced-capabilities-nda-r1</c> task 051 (ADR-039 golden-utterance dispatch gate) —
/// the NDA-review dispatch-eval family.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is</b>: the dispatch-side companion to task 050's output-quality eval. ADR-039
/// requires golden-utterance coverage for every new entry path, including a negative case. Task 022
/// wired the "Review an NDA" Assistant card (Click path) + the nda-review/nda-standard-summary
/// Bindings' <c>sprk_tooldescription</c> (Text path); this suite proves both routes land on
/// <c>nda-review/default</c>, and that an off-target utterance plus a same-domain sibling utterance do
/// NOT.
/// </para>
/// <para>
/// <b>Merge-gate wiring</b> (identical pattern to <c>AssistantEnhancementsR1EvalTests</c> /
/// <c>ResourcefulnessEvalSuiteTests</c> / <c>OriginClassificationEvalSuiteTests</c> in this same
/// directory): the <c>[Trait("Category", "GoldenUtteranceEval")]</c> attribute joins this class to the
/// dedicated <c>eval-gate</c> job in <c>.github/workflows/sdap-ci.yml</c>
/// (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>, NO <c>continue-on-error</c>) with ZERO
/// CI-YAML change — the trait IS the registration. This suite deliberately does NOT touch the shared
/// <c>golden-utterances.json</c> (owned by the ai-architecture-redesign project) — a net-new family is
/// the established way a project adds gate coverage.
/// </para>
/// <para>
/// <b>Mechanical-vs-live boundary (same posture as every other family in this directory)</b>: nothing
/// in this suite — or in any sibling family here — invokes a live Azure OpenAI model. "Live dispatch
/// assertion" in this codebase's established vocabulary means "asserted against the REAL production
/// routing code" (<see cref="ConsumerRoutingService.ResolveBindingAsync"/>,
/// <see cref="ConsumerRoutingService.GetBindingByIdAsync"/>,
/// <see cref="ConsumerRoutingService.ListTextProjectableBindingsAsync"/>) with the Dataverse boundary
/// stubbed — the exact reads the Click path (card → <c>chips.dispatchBinding</c> →
/// <c>GetBindingByIdAsync</c>) and the Text-path loop projection (<c>SprkChatAgentFactory</c> →
/// <c>ListTextProjectableBindingsAsync</c>) perform. The bounded function-calling agent turn's actual
/// selection AMONG competing capability tools for a live NL utterance is exercised by manual/browser
/// UAT against a deployed org (same posture as the golden-utterance suite's own P1/P2/P3 "live"
/// facts) — it is out of scope for <c>dotnet test</c> in this repo and is NOT claimed here.
/// </para>
/// <para>
/// <b>catalogStatus grounding</b> (honest — no invented capability names): both <c>nda-review</c> and
/// <c>nda-standard-summary</c> are <c>mirrored</c> (rows in
/// <c>infra/dataverse/sprk_playbookconsumer-rows.json</c>, task 022) but NOT YET live-seeded on
/// spaarkedev1 in this session (<c>Seed-PlaybookConsumers.ps1</c> is env-blocked per task 022's notes)
/// and NOT <see cref="ConsumerTypes"/> constants (routing keys on the Dataverse row, not a code
/// constant, per ADR-039 — the Binding table is the ONLY routing surface).
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class NdaReviewDispatchEvalTests
{
    private readonly ITestOutputHelper _output;

    public NdaReviewDispatchEvalTests(ITestOutputHelper output) => _output = output;

    // -------------------------------------------------------------------------
    // Closed vocabularies (this family's case-schema contract)
    // -------------------------------------------------------------------------

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "nda-review-click", "nda-review-text", "off-target", "nda-review-vs-standard-summary",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "text", "click" };

    private static readonly IReadOnlySet<string> OutcomeClasses =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "dispatch", "excluded" };

    /// <summary>The single §3 UC trigger family this suite derives from — see class remarks.</summary>
    private static readonly IReadOnlySet<string> NdaUcIds =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UC-A-1" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["nda-review-click"] = 1,
        ["nda-review-text"] = 2,
        ["off-target"] = 1,
        ["nda-review-vs-standard-summary"] = 1,
    };

    // -------------------------------------------------------------------------
    // Inventory integrity
    // -------------------------------------------------------------------------

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("nda-review-eval@v1", "the family declares its schema version");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("NDA-", StringComparison.Ordinal),
            "NDA-review dispatch cases are namespaced NDA-### so they never collide with GU-###/AR1-###/RF-###/OC-### ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: channel is text|click");
            OutcomeClasses.Should().Contain(c.Expected.OutcomeClass, $"case {c.CaseId}: outcome class is closed (dispatch|excluded)");
            NdaUcIds.Should().Contain(c.UcId, $"case {c.CaseId}: ucId must trace to UC-A-1 (see class remarks)");
            c.Utterance.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every case carries an utterance/affordance descriptor");
        }
    }

    [Fact]
    public void EveryFamily_HasEvalCoverage_AtItsFloor()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
        {
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor,
                    $"ADR-039: the '{family}' family owes at least {floor} eval case(s)");
        }

        suite.Cases.Should().Contain(c => c.Channel.Equals("click", StringComparison.OrdinalIgnoreCase),
            "ADR-039 requires the Click path be covered (the 'Review an NDA' Assistant card)");
        suite.Cases.Should().Contain(
            c => c.Channel.Equals("text", StringComparison.OrdinalIgnoreCase)
                 && c.Expected.OutcomeClass.Equals("dispatch", StringComparison.OrdinalIgnoreCase)
                 && c.Expected.ConsumerType == "nda-review",
            "ADR-039 requires at least one Text-path case dispatching to nda-review/default");
    }

    // -------------------------------------------------------------------------
    // Catalog grounding — no invented capability names
    // -------------------------------------------------------------------------

    [Fact]
    public void DispatchCases_ExpectedConsumerType_IsGroundedInTheMirror_AndNotYetACodeConstant()
    {
        var suite = LoadSuite();
        var mirrorConsumerTypes = LoadMirrorConsumerTypes();

        foreach (var c in suite.Cases.Where(c => c.Expected.ConsumerType is not null))
        {
            var consumerType = c.Expected.ConsumerType!;
            c.Expected.CatalogStatus.Should().Be("mirrored",
                $"case {c.CaseId}: nda-review/nda-standard-summary are mirrored (row in the mirror file), not yet a ConsumerTypes constant");
            mirrorConsumerTypes.Should().Contain(consumerType,
                $"case {c.CaseId}: '{consumerType}' must be a row in infra/dataverse/sprk_playbookconsumer-rows.json");
            ConsumerTypes.All.Should().NotContain(consumerType,
                $"case {c.CaseId}: '{consumerType}' has no server-side ConsumerTypes dependency yet — flip catalogStatus " +
                "to existing only if a constant is added");
        }
    }

    [Fact]
    public void NegativeCases_DeclareNotConsumerTypeNdaReview_AndDifferFromAnyPositiveConsumerType()
    {
        var suite = LoadSuite();
        var negativeCases = suite.Cases
            .Where(c => c.Expected.NotConsumerType is not null)
            .ToList();

        negativeCases.Should().NotBeEmpty("ADR-039 requires at least one negative dispatch case");
        negativeCases.Should().OnlyContain(c => c.Expected.NotConsumerType == "nda-review",
            "every negative case in this family asserts exclusion FROM nda-review specifically");

        foreach (var c in negativeCases.Where(c => c.Expected.ConsumerType is not null))
        {
            c.Expected.ConsumerType.Should().NotBe(c.Expected.NotConsumerType,
                $"case {c.CaseId}: the disambiguation target and the excluded capability must differ (non-vacuous)");
        }

        // The pure off-target case (no other capability claimed) declares outcomeClass=excluded
        // and a null consumerType — mirrors the golden-utterances.json refusal-family convention.
        suite.Cases.Should().Contain(
            c => string.Equals(c.Family, "off-target", StringComparison.OrdinalIgnoreCase)
                 && c.Expected.OutcomeClass == "excluded" && c.Expected.ConsumerType == null,
            "the off-target case makes no positive claim about where the utterance DOES route — only that it excludes nda-review");
    }

    // -------------------------------------------------------------------------
    // Mechanical routing facts — the REAL ConsumerRoutingService over a stubbed
    // Dataverse boundary, shaped EXACTLY like the real mirror row (read from
    // disk, never hand-duplicated, so a drift in the mirror fails here).
    // -------------------------------------------------------------------------

    [Fact]
    public async Task NdaReviewBinding_ResolvesThroughTheRealRoutingService_ForBothClickAndTextPathCases()
    {
        var suite = LoadSuite();
        var ndaReviewCases = suite.Cases
            .Where(c => c.Expected.ConsumerType == "nda-review")
            .ToList();
        ndaReviewCases.Should().NotBeEmpty();
        ndaReviewCases.Should().Contain(c => c.Channel == "click");
        ndaReviewCases.Should().Contain(c => c.Channel == "text");

        var mirrorRow = LoadMirrorRow("nda-review");
        var rowId = Guid.NewGuid();
        var stubEntity = BuildRowFromMirror(mirrorRow, rowId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression query, CancellationToken _) =>
            {
                var byId = query.Criteria.Conditions
                    .FirstOrDefault(cond => cond.AttributeName == "sprk_playbookconsumerid");
                if (byId is not null)
                {
                    var requestedId = (Guid)byId.Values[0];
                    return new EntityCollection(requestedId == rowId
                        ? new List<Entity> { stubEntity }
                        : new List<Entity>());
                }
                return new EntityCollection(new List<Entity> { stubEntity });
            });

        var routing = CreateRoutingService(entityService);

        // ── Text-path leg (the loop's read + the summarize-precedent read): resolve BY consumerType.
        var byType = await routing.ResolveBindingAsync("nda-review", "default");
        byType.Should().NotBeNull(
            "the nda-review Binding must resolve through the real FR-1R-03 selection algorithm — " +
            "the exact read the Text-path capability-discovery/dispatch seam performs");
        byType!.ConsumerType.Should().Be("nda-review");
        byType.Disposition.Should().Be(BindingDisposition.Compose,
            "the mirror row declares disposition=100000006 (Compose) — findings are durable-by-disposition " +
            "since agreements-r1 task 030 (FR-16a): the review output lands on the document session's ledger " +
            "and re-materializes on reopen; still advisory (no edit side effect — apply-leg gated by FR-16d)");
        byType.Risk.Should().Be(BindingRisk.None,
            "the mirror row declares risk=100000000 (None) — no confirmation turn for a read-only review");
        byType.ActionKind.Should().Be(ActionKind.Prompted);
        byType.ToolDescription.Should().NotBeNullOrWhiteSpace(
            "non-empty sprk_tooldescription is REQUIRED for the Text-path capability-discovery projection");
        byType.Surfaces.Should().Contain("assistant").And.Contain("compose",
            "the mirror row declares surfaces='assistant,compose'");

        // ── Click-path leg (task 022): chips.dispatchBinding resolves BY the Binding id — the
        // exact read SessionDispatchOrchestrator performs (mirrors GU-038's chip-target resolution).
        var byId = await routing.GetBindingByIdAsync(rowId);
        byId.Should().NotBeNull(
            "the Assistant card's chips.dispatchBinding(ndaReviewBindingId, ...) call resolves the Binding BY ID — " +
            "the exact read this proves");
        byId!.ConsumerType.Should().Be("nda-review");
        byId.BindingId.Should().Be(rowId);

        _output.WriteLine("Mechanical routing assertions — nda-review (task 051):");
        foreach (var c in ndaReviewCases)
        {
            _output.WriteLine($"  {c.CaseId}  \"{c.Utterance}\"  ({c.Channel})  ->  nda-review/default (resolved)");
        }
    }

    [Fact]
    public async Task NdaReviewBinding_IsTextProjectable_ThroughTheRealListTextProjectableBindingsRead()
    {
        var mirrorRow = LoadMirrorRow("nda-review");
        var rowId = Guid.NewGuid();
        var stubEntity = BuildRowFromMirror(mirrorRow, rowId);

        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<Entity> { stubEntity }));

        var routing = CreateRoutingService(entityService);

        // The EXACT read SprkChatAgentFactory performs to put nda-review in the loop's tool list
        // (the Text-path projection precondition for "review this NDA" et al.).
        var projectable = await routing.ListTextProjectableBindingsAsync();

        projectable.Should().ContainSingle(b => b.ConsumerType == "nda-review",
            "the nda-review Binding is text-projectable — 'review this NDA' dispatches through the closed catalog " +
            "(ADR-039: routing config lives only in the Binding table)");
    }

    // -------------------------------------------------------------------------
    // Disambiguation + off-target grounding — the authored catalog cues that
    // make the negative/disambiguation cases correct, read from the REAL
    // mirror file (not restated, so a cue removed from the mirror fails here).
    // -------------------------------------------------------------------------

    [Fact]
    public void NdaReviewToolDescription_ContainsTheAuthoredTextPathParaphrases()
    {
        var suite = LoadSuite();
        var textCases = suite.Cases
            .Where(c => string.Equals(c.Family, "nda-review-text", StringComparison.OrdinalIgnoreCase))
            .ToList();
        textCases.Should().NotBeEmpty();

        var toolDescription = LoadMirrorRow("nda-review").GetProperty("toolDescription").GetString();
        toolDescription.Should().NotBeNullOrWhiteSpace();

        foreach (var c in textCases)
        {
            toolDescription!.Should().Contain(c.Utterance,
                $"case {c.CaseId}: the nda-review Binding's own sprk_tooldescription must quote this NL paraphrase " +
                "verbatim — the projection opt-in AND the loop's intent surface (ADR-039: no second intent-detection mechanism)");
        }
    }

    [Fact]
    public void NdaStandardSummaryDisambiguationCue_RoutesTheStandardQuestionAwayFromNdaReview()
    {
        var ndaReviewDescription = LoadMirrorRow("nda-review").GetProperty("toolDescription").GetString();
        var standardSummaryDescription = LoadMirrorRow("nda-standard-summary").GetProperty("toolDescription").GetString();

        ndaReviewDescription.Should().NotBeNullOrWhiteSpace();
        standardSummaryDescription.Should().NotBeNullOrWhiteSpace();

        ndaReviewDescription!.Should().Contain("uploaded or attached",
            "nda-review's authored cue requires an uploaded/attached NDA — grounds why a bare 'what does our " +
            "standard say' question (no attached document) must NOT select it");
        standardSummaryDescription!.Should().ContainAll(new[] { "NDA standard", "Does not review" },
            "nda-standard-summary's authored cue explicitly scopes to the firm's OWN standard and explicitly " +
            "excludes reviewing any uploaded document — the ADR-039 ambiguity-in-descriptions disambiguation, " +
            "not a second classifier");
        standardSummaryDescription.Should().Contain("use the NDA Review capability instead",
            "the sibling Binding's own cue names nda-review as the correct capability for an actual uploaded " +
            "agreement — the two descriptions are mutually disambiguating, authored as catalog data");
    }

    [Fact]
    public void OffTargetUtterance_HasNoLexicalOverlapWithNdaReviewToolDescription()
    {
        var suite = LoadSuite();
        var offTargetCase = suite.Cases.Single(c => string.Equals(c.Family, "off-target", StringComparison.OrdinalIgnoreCase));

        var ndaReviewDescription = LoadMirrorRow("nda-review").GetProperty("toolDescription").GetString();
        ndaReviewDescription.Should().NotBeNullOrWhiteSpace();

        ndaReviewDescription!.Should().NotContain("email",
            $"case {offTargetCase.CaseId} (\"{offTargetCase.Utterance}\"): nda-review's authored intent surface " +
            "carries no cue that could plausibly attract an email-summarization ask — cheap lexical-exclusion " +
            "grounding for the required negative case (the live agent-loop selection itself is out of this " +
            "mechanical suite's scope — see class remarks)");
        ndaReviewDescription.Should().Contain("NDA",
            "nda-review's cue is scoped to NDAs specifically, not general documents — the positive half of the same grounding");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

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
    /// Build a stub <c>sprk_playbookconsumer</c> Entity carrying EVERY field the real mirror row
    /// declares (disposition, risk, capture mode, surfaces, tool description), targeting an
    /// arbitrary local <c>sprk_action</c> id — the actual nda-review Action GUID is assigned live at
    /// Dataverse seed time (task 020's mirror-first author surface has no fixed GUID pre-seed), so a
    /// synthetic id proves the SAME routing/projection mechanics without depending on a live env.
    /// </summary>
    private static Entity BuildRowFromMirror(JsonElement mirrorRow, Guid rowId)
    {
        var entity = new Entity("sprk_playbookconsumer", rowId);
        entity["sprk_playbookconsumerid"] = rowId;
        entity["sprk_consumertype"] = mirrorRow.GetProperty("consumerType").GetString();
        entity["sprk_consumercode"] = mirrorRow.GetProperty("consumerCode").GetString();
        entity["sprk_priority"] = mirrorRow.GetProperty("priority").GetInt32();
        entity["sprk_enabled"] = mirrorRow.GetProperty("enabled").GetBoolean();
        entity["sprk_environment"] = mirrorRow.GetProperty("environment").GetString();
        entity["sprk_disposition"] = new OptionSetValue(mirrorRow.GetProperty("disposition").GetInt32());
        entity["sprk_risk"] = new OptionSetValue(mirrorRow.GetProperty("risk").GetInt32());
        entity["sprk_capturemode"] = new OptionSetValue(mirrorRow.GetProperty("captureMode").GetInt32());
        entity["sprk_surfaces"] = mirrorRow.GetProperty("surfaces").GetString();
        entity["sprk_tooldescription"] = mirrorRow.GetProperty("toolDescription").GetString();
        entity["sprk_action"] = new EntityReference("sprk_analysisaction", Guid.NewGuid());
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

    private static NdaReviewEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "nda-review-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"nda-review-eval-cases.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval ItemGroup glob covers it automatically)");
        var suite = JsonSerializer.Deserialize<NdaReviewEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the NDA-review case schema");
        return suite!;
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

    private static IReadOnlyCollection<string> LoadMirrorConsumerTypes()
    {
        var mirrorPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_playbookconsumer-rows.json");
        using var mirror = JsonDocument.Parse(File.ReadAllText(mirrorPath));
        return mirror.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("consumerType").GetString()!)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
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
            "the NDA-review mirror-grounding assertions require an in-repo test run.");
    }
}

// -----------------------------------------------------------------------------
// NDA-review eval case schema
// -----------------------------------------------------------------------------

public sealed record NdaReviewEvalSuite
{
    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("cases")]
    public List<NdaReviewEvalCase> Cases { get; init; } = new();
}

public sealed record NdaReviewEvalCase
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
    public NdaReviewExpected Expected { get; init; } = new();
}

public sealed record NdaReviewExpected
{
    /// <summary>dispatch | excluded (closed set).</summary>
    [JsonPropertyName("outcomeClass")]
    public string OutcomeClass { get; init; } = string.Empty;

    [JsonPropertyName("consumerType")]
    public string? ConsumerType { get; init; }

    [JsonPropertyName("consumerCode")]
    public string? ConsumerCode { get; init; }

    /// <summary>The capability this utterance must NOT select (negative/disambiguation cases).</summary>
    [JsonPropertyName("notConsumerType")]
    public string? NotConsumerType { get; init; }

    /// <summary>mirrored (row in sprk_playbookconsumer-rows.json) — null for the pure off-target case.</summary>
    [JsonPropertyName("catalogStatus")]
    public string? CatalogStatus { get; init; }

    [JsonPropertyName("mirroredBy")]
    public string? MirroredBy { get; init; }
}
