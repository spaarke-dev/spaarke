using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Classification;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.AgreementClassify;

/// <summary>
/// ai-advanced-capabilities-agreements-r1 task 020 (FR-07 / design Lens 3d) — the agreement-classify
/// eval family. Mechanically proves the classifier's contract, registry-grounding, and Reasoning-tier
/// config WITHOUT a live model (the Reasoning-tier deployment is env-blocked — nda-r1 task 013; same
/// posture as every sibling eval family in this directory).
/// </summary>
/// <remarks>
/// <para>
/// <b>Merge-gate wiring</b>: <c>[Trait("Category", "GoldenUtteranceEval")]</c> joins this class to the
/// dedicated eval-gate job in <c>.github/workflows/sdap-ci.yml</c> with ZERO CI-YAML change (a net-new
/// family is the established way a project adds gate coverage). It does not touch the shared
/// <c>golden-utterances.json</c>.
/// </para>
/// <para>
/// <b>What is NOT claimed</b>: the model's actual routing + per-candidate confidence VALUES are the
/// live-graded part and are env-blocked; each case records a <c>confidenceBand</c> (the calibration the
/// task-021 &gt;=0.85 gate depends on) as the documented expectation for when the deployment lands.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class AgreementClassifyEvalTests
{
    private readonly ITestOutputHelper _output;

    public AgreementClassifyEvalTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> Families = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "positive-specific", "positive-fallback", "negative-nonagreement", "composite", "low-confidence-ambiguous",
    };

    private static readonly IReadOnlySet<string> Channels =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "chat-upload" };

    private static readonly IReadOnlySet<string> ConfidenceBands =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "high", "medium", "low" };

    private static readonly IReadOnlyDictionary<string, int> FamilyFloors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["positive-specific"] = 1,        // NDA positive
        ["positive-fallback"] = 1,        // lease-like -> lower-confidence or general fallback
        ["negative-nonagreement"] = 1,    // invoice / pleading negatives
        ["composite"] = 1,                // employment + NDA addendum
        ["low-confidence-ambiguous"] = 1, // honest low-confidence
    };

    // ── Inventory integrity ──

    [Fact]
    public void Suite_Loads_WithNamespacedUniqueCaseIds_AndClosedVocabularies()
    {
        var suite = LoadSuite();

        suite.SchemaVersion.Should().Be("agreement-classify-eval@v1");
        suite.ActionCode.Should().Be("agreement-classify");
        suite.Cases.Should().NotBeEmpty();
        suite.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability + CI diffs");
        suite.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("AC-", StringComparison.Ordinal),
            "agreement-classify cases are namespaced AC-### so they never collide with GU-/NDA-/TRIAGE-/AR1- ids");

        foreach (var c in suite.Cases)
        {
            Families.Should().Contain(c.Family, $"case {c.CaseId}: family is a closed vocabulary");
            Channels.Should().Contain(c.Channel, $"case {c.CaseId}: the classifier is invoked on the chat-upload path (task 021)");
            c.Utterance.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: every case carries a document/affordance descriptor");
            foreach (var band in c.Expected.ConfidenceBands.Values)
                ConfidenceBands.Should().Contain(band, $"case {c.CaseId}: confidenceBand is a closed vocabulary (high|medium|low)");
        }
    }

    [Fact]
    public void EveryFamily_HasEvalCoverage_AtItsFloor_IncludingNegativeAndComposite()
    {
        var suite = LoadSuite();

        foreach (var (family, floor) in FamilyFloors)
            suite.Cases.Count(c => string.Equals(c.Family, family, StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterOrEqualTo(floor,
                    $"the '{family}' family owes at least {floor} eval case(s) — negatives + composite authored early (nda-r1 lesson)");
    }

    // ── Gate-relevant contract invariants (the shape task 021's gate + task 023 pin) ──

    [Fact]
    public void NonAgreementCases_FabricateNoCandidates_AndAreNeverComposite()
    {
        var suite = LoadSuite();

        foreach (var c in suite.Cases.Where(c => !c.Expected.IsAgreement))
        {
            c.Expected.CandidateKeys.Should().BeEmpty($"case {c.CaseId}: a non-agreement fabricates NO agreement candidates");
            c.Expected.AcceptableKeys.Should().BeEmpty($"case {c.CaseId}: a non-agreement has no acceptable sub-domain");
            c.Expected.Composite.Should().BeFalse($"case {c.CaseId}: a non-agreement is never composite");
        }
    }

    [Fact]
    public void CompositeCases_NameTwoOrMoreDistinctCandidates()
    {
        var suite = LoadSuite();

        foreach (var c in suite.Cases.Where(c => c.Expected.Composite))
        {
            c.Expected.IsAgreement.Should().BeTrue($"case {c.CaseId}: a composite is an agreement");
            c.Expected.CandidateKeys.Union(c.Expected.AcceptableKeys).Distinct()
                .Should().HaveCountGreaterThanOrEqualTo(2,
                    $"case {c.CaseId}: composite=true MUST name two or more distinct sub-domains (choice-of-lens / 'both')");
        }
    }

    // ── Registry-grounding: no case references a key the classifier cannot emit ──

    [Fact]
    public void EveryExpectedKey_IsARegisteredSprkAgreementTypeKey()
    {
        var suite = LoadSuite();
        var registryKeys = LoadRegistryRows().Select(r => r.Key).ToHashSet(StringComparer.Ordinal);

        registryKeys.Should().NotBeEmpty("the sprk_agreementtype registry mirror must carry rows (task 001)");

        foreach (var c in suite.Cases)
            foreach (var key in c.Expected.CandidateKeys.Union(c.Expected.AcceptableKeys))
                registryKeys.Should().Contain(key,
                    $"case {c.CaseId}: expected subDomainKey '{key}' MUST be a real registry key — an eval must never " +
                    "reference a key the classifier cannot emit (registry-grounded, no invented types)");
    }

    [Fact]
    public void EveryExpectedKey_IsActuallyInjectedIntoTheClassifierPromptBlock()
    {
        // Closes the loop between the eval and the assembly path: the keys each case expects are exactly
        // the keys the AgreementTypeRegistryPromptAssembler puts in front of the model, built from the
        // live registry rows. If a per-type pack registers a new row, its key becomes emittable with zero code.
        var suite = LoadSuite();
        var block = new AgreementTypeRegistryPromptAssembler().BuildRegistryBlock(LoadRegistryRows());

        foreach (var c in suite.Cases)
            foreach (var key in c.Expected.CandidateKeys.Union(c.Expected.AcceptableKeys))
                block.Should().Contain($"\"{key}\"",
                    $"case {c.CaseId}: the classifier prompt block MUST offer the key '{key}' the case expects it to emit");

        _output.WriteLine("agreement-classify eval: all expected keys are registry-grounded + injected into the prompt block.");
    }

    // ── Criterion #5 tie: the classifier Action routes to the Reasoning tier ──

    [Fact]
    public void ClassifierAction_DeclaresTheReasoningTier_TheEvalDependsOn()
    {
        var actionPath = Path.Combine(FindRepoRoot(), "infra", "dataverse", "actions", "agreement-classify.action.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(actionPath));
        doc.RootElement.GetProperty("modelTier").GetString().Should().Be("Reasoning",
            "the classifier's accuracy-first economics (Lens 3d) require the Reasoning tier — these eval expectations " +
            "assume that tier's judgment when the deployment lands");
    }

    // ── Helpers ──

    private static IReadOnlyList<AgreementTypeRow> LoadRegistryRows()
    {
        var path = Path.Combine(FindRepoRoot(), "infra", "dataverse", "sprk_agreementtype-rows.json");
        File.Exists(path).Should().BeTrue("the sprk_agreementtype registry mirror (task 001) must exist");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var rows = new List<AgreementTypeRow>();
        foreach (var r in doc.RootElement.GetProperty("rows").EnumerateArray())
        {
            var key = r.TryGetProperty("sprk_key", out var k) ? k.GetString() : null;
            if (string.IsNullOrWhiteSpace(key)) continue;
            rows.Add(new AgreementTypeRow(
                Key: key!,
                Name: r.TryGetProperty("sprk_name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                ClassificationCue: r.TryGetProperty("sprk_classificationcue", out var cue) && cue.ValueKind == JsonValueKind.String ? cue.GetString() : null,
                IsFallback: r.TryGetProperty("sprk_isfallback", out var f) && f.ValueKind == JsonValueKind.True,
                ConfidenceThreshold: r.TryGetProperty("sprk_confidencethreshold", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetDecimal() : null));
        }
        return rows;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static AgreementClassifyEvalSuite LoadSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "agreement-classify-eval-cases.json");
        File.Exists(path).Should().BeTrue(
            $"agreement-classify-eval-cases.json must be copied to test output at {path} " +
            "(the contract-Eval Content glob in Sprk.Bff.Api.Tests.csproj covers it automatically)");
        var suite = JsonSerializer.Deserialize<AgreementClassifyEvalSuite>(File.ReadAllText(path), JsonOptions);
        suite.Should().NotBeNull("the seed file must deserialize into the agreement-classify case schema");
        return suite!;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Spaarke.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate the repo root (Spaarke.sln).");
    }
}

// ── agreement-classify eval case schema ──

public sealed record AgreementClassifyEvalSuite
{
    [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("actionCode")] public string? ActionCode { get; init; }
    [JsonPropertyName("cases")] public List<AgreementClassifyEvalCase> Cases { get; init; } = new();
}

public sealed record AgreementClassifyEvalCase
{
    [JsonPropertyName("caseId")] public string CaseId { get; init; } = string.Empty;
    [JsonPropertyName("family")] public string Family { get; init; } = string.Empty;
    [JsonPropertyName("channel")] public string Channel { get; init; } = string.Empty;
    [JsonPropertyName("utterance")] public string Utterance { get; init; } = string.Empty;
    [JsonPropertyName("notes")] public string? Notes { get; init; }
    [JsonPropertyName("expected")] public AgreementClassifyExpected Expected { get; init; } = new();
}

public sealed record AgreementClassifyExpected
{
    [JsonPropertyName("isAgreement")] public bool IsAgreement { get; init; }
    [JsonPropertyName("composite")] public bool Composite { get; init; }
    [JsonPropertyName("candidateKeys")] public List<string> CandidateKeys { get; init; } = new();
    [JsonPropertyName("acceptableKeys")] public List<string> AcceptableKeys { get; init; } = new();
    [JsonPropertyName("confidenceBands")] public Dictionary<string, string> ConfidenceBands { get; init; } = new();
}
