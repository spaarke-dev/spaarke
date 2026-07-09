using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.Chat.Gate;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Eval.GoldenUtterances;
using Sprk.Bff.Api.Tests.Eval.Resourcefulness;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Eval.OriginClassification;

/// <summary>
/// Origin-classification eval family — generated from the RULED E-1..E-6 rows in
/// <c>notes/policy-v2-origin-classification-decision-tree.md</c> §4 (design.md D-F1 adopted
/// them). Spaarke-ai-architecture-redesign-r2 task 033, spec FR-A1-04.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this family is a HARD-EQUALITY gate, not an LLM judge</b>: the request-origin
/// classifier (<see cref="RequestOriginClassifier"/>, task 032) and the Confirmation Policy v2
/// engine (<see cref="ConfirmationPolicyEngine"/> / <see cref="GateDecisionProjector"/>) are
/// DETERMINISTIC production code — pure functions over structural DATA, never a model opinion
/// (ADR-039 one-intent boundary). Every case below invokes those REAL production types directly
/// (not a reimplementation or mock oracle) and asserts BOTH the classified
/// <see cref="GateRequestOrigin"/> AND the resulting <see cref="GateOutcome"/> by hard equality.
/// </para>
/// <para>
/// <b>Positive vs negative per row</b>: each of E-1..E-6 gets a POSITIVE case (the primary ruled
/// branch — e.g. a single complete proposal binds an affirmation to explicit) and a NEGATIVE case
/// (the edge/overlay branch within the SAME row that flips the outcome — e.g. two proposals, or
/// an injection-suspect overlay). The negative case is the discriminating one: perturbing the
/// classifier or engine to the WRONG branch turns it red. This mirrors the acceptance criterion's
/// two named examples verbatim (E-1 two-proposals negative; E-6 dispatchUncertain negative).
/// </para>
/// <para>
/// <b>Merge-gate wiring</b> (same pattern as <see cref="GoldenUtteranceEvalSuiteTests"/> and
/// <see cref="ResourcefulnessEvalSuiteTests"/>): this class carries the
/// <c>Category=GoldenUtteranceEval</c> trait, so the dedicated <c>eval-gate</c> job in
/// <c>.github/workflows/sdap-ci.yml</c> (<c>dotnet test --filter "Category=GoldenUtteranceEval"</c>,
/// NO <c>continue-on-error</c>) runs it as a merge-blocking gate with ZERO CI-YAML change — the
/// trait IS the registration.
/// </para>
/// </remarks>
[Trait("Category", "GoldenUtteranceEval")]
public class OriginClassificationEvalSuiteTests
{
    private readonly ITestOutputHelper _output;

    public OriginClassificationEvalSuiteTests(ITestOutputHelper output) => _output = output;

    private static readonly IReadOnlySet<string> EdgeCaseKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "E-1", "E-2", "E-3", "E-4", "E-5", "E-6",
    };

    private static readonly IReadOnlySet<string> CaseTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "positive", "negative",
    };

    // =========================================================================
    // 1. Inventory integrity (mechanical — CI)
    // =========================================================================

    [Fact]
    public void Family_Loads_WithAtLeast12Cases_UniqueIds_ValidTaxonomy()
    {
        var family = LoadFamily();

        family.SchemaVersion.Should().NotBeNullOrWhiteSpace();
        family.FamilyKey.Should().Be("origin-classification");
        family.Cases.Should().HaveCountGreaterOrEqualTo(12,
            "spec FR-A1-04: >=1 positive + >=1 negative per E-1..E-6 row = >=12 cases minimum");
        family.Cases.Select(c => c.CaseId).Should().OnlyHaveUniqueItems("case ids anchor traceability");
        family.Cases.Should().OnlyContain(c => c.CaseId.StartsWith("OC-"),
            "origin-classification case ids use the OC- namespace (dedupe against GU- golden and RF- resourcefulness ids)");

        foreach (var c in family.Cases)
        {
            EdgeCaseKeys.Should().Contain(c.EdgeCase, $"case {c.CaseId}: edgeCase must be one of E-1..E-6");
            CaseTypes.Should().Contain(c.CaseType, $"case {c.CaseId}: caseType must be positive or negative");
        }

        _output.WriteLine("ORIGIN-CLASSIFICATION FAMILY COVERAGE (E-1..E-6 x positive/negative):");
        _output.WriteLine($"{"edgeCase",-8} {"positive",8} {"negative",8}");
        _output.WriteLine(new string('-', 30));
        foreach (var edgeCase in EdgeCaseKeys.OrderBy(k => k))
        {
            var pos = family.Cases.Count(c => c.EdgeCase == edgeCase && string.Equals(c.CaseType, "positive", StringComparison.OrdinalIgnoreCase));
            var neg = family.Cases.Count(c => c.EdgeCase == edgeCase && string.Equals(c.CaseType, "negative", StringComparison.OrdinalIgnoreCase));
            _output.WriteLine($"{edgeCase,-8} {pos,8} {neg,8}");
        }
        _output.WriteLine(new string('-', 30));
        _output.WriteLine($"total cases: {family.Cases.Count}");
    }

    [Fact]
    public void EveryEdgeCase_HasAtLeastOnePositive_AndOneNegative()
    {
        var family = LoadFamily();

        foreach (var edgeCase in EdgeCaseKeys)
        {
            family.Cases.Should().Contain(
                c => c.EdgeCase == edgeCase && string.Equals(c.CaseType, "positive", StringComparison.OrdinalIgnoreCase),
                $"row {edgeCase} must have >=1 positive case (spec FR-A1-04)");
            family.Cases.Should().Contain(
                c => c.EdgeCase == edgeCase && string.Equals(c.CaseType, "negative", StringComparison.OrdinalIgnoreCase),
                $"row {edgeCase} must have >=1 negative case (spec FR-A1-04)");
        }
    }

    [Fact]
    public void EveryCase_DeclaresProvenance_AndExpectedOriginPlusOutcome()
    {
        var family = LoadFamily();

        foreach (var c in family.Cases)
        {
            c.Provenance.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: anchors in the ruled E-1..E-6 table + worked traces");
            c.Description.Should().NotBeNullOrWhiteSpace($"case {c.CaseId}: carries a human-readable description");
            c.Expected.Should().NotBeNull($"case {c.CaseId}: carries the expected origin + gate outcome");
            c.Expected!.Origin.Should().BeOneOf("Explicit", "Inferred", $"case {c.CaseId}: expected origin is one of the two closed values");
        }
    }

    /// <summary>
    /// Guards against a vacuously-green family: within each E-1..E-6 row, the positive and
    /// negative case must produce a DIFFERENT (origin, outcome, decisive-overlay) triple
    /// (mirrors the resourcefulness family's "gate is not vacuous" guard). Most rows (E-1,
    /// E-2, E-4, E-5) discriminate on ORIGIN flipping (explicit vs inferred); E-3 and E-6
    /// discriminate on the OVERLAY firing while origin stays explicit (layering — an overlay
    /// overrides the outcome, never the origin classification itself). Either discriminator is
    /// acceptable, but at least one MUST differ — if a row's pos/neg pair ever resolved to an
    /// IDENTICAL triple, the "negative" case would not actually discriminate anything: it would
    /// pass regardless of whether the classifier/engine is correct.
    /// </summary>
    [Fact]
    public void EveryEdgeCase_PositiveAndNegativeCases_ResolveToDifferentClassifierOrGateResults()
    {
        var family = LoadFamily();

        foreach (var edgeCase in EdgeCaseKeys)
        {
            var results = family.Cases
                .Where(c => c.EdgeCase == edgeCase)
                .Select(c => $"{c.Expected!.Origin}|{c.Expected.Outcome}|{c.Expected.OverlayDecisive}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            results.Should().HaveCountGreaterOrEqualTo(2,
                $"row {edgeCase}: the positive and negative cases must resolve to a DIFFERENT " +
                "(origin, outcome, decisive-overlay) result, otherwise the negative case cannot " +
                "discriminate a perturbed classifier/engine from a correct one");
        }
    }

    // =========================================================================
    // 2. Mechanical deterministic assertions against the REAL production classifier
    //    + Confirmation Policy v2 engine (no live LLM — hard equality only).
    // =========================================================================

    [Fact]
    public void EveryCase_ClassifierAndEngine_ProduceTheRuledOriginAndGateOutcome()
    {
        var family = LoadFamily();

        _output.WriteLine("ORIGIN-CLASSIFICATION MECHANICAL ASSERTIONS (production RequestOriginClassifier + ConfirmationPolicyEngine):");
        foreach (var c in family.Cases)
        {
            var originRequest = ToGateOriginRequest(c.OriginRequest!);
            var actualOrigin = RequestOriginClassifier.Classify(originRequest);

            var context = ToPolicyEvaluationContext(originRequest, c.PolicyContext!);
            var decision = ConfirmationPolicyEngine.Evaluate(context);

            var expectedOrigin = Enum.Parse<GateRequestOrigin>(c.Expected!.Origin, ignoreCase: true);
            var expectedOutcome = Enum.Parse<GateOutcome>(c.Expected.Outcome, ignoreCase: true);

            actualOrigin.Should().Be(expectedOrigin,
                $"case {c.CaseId} ({c.EdgeCase}, {c.CaseType}): {c.Description}");
            decision.Outcome.Should().Be(expectedOutcome,
                $"case {c.CaseId} ({c.EdgeCase}, {c.CaseType}): {c.Description}");

            if (c.Expected.OverlayDecisive is { } overlayName)
            {
                var expectedOverlay = Enum.Parse<GateOverlay>(overlayName, ignoreCase: true);
                decision.Overlays.Decisive.Should().Be(expectedOverlay,
                    $"case {c.CaseId}: the decisive overlay must be {overlayName} (layering — overlays override the outcome, never the origin classification itself)");
            }
            else
            {
                decision.Overlays.Decisive.Should().BeNull($"case {c.CaseId}: no overlay expected to fire");
            }

            _output.WriteLine($"  {c.CaseId,-10} {c.EdgeCase,-5} {c.CaseType,-9} origin={actualOrigin,-9} outcome={decision.Outcome,-16} overlay={decision.Overlays.Decisive}");
        }
    }

    /// <summary>
    /// The E-1 negative case, isolated and named exactly per the acceptance criterion: a bare
    /// affirmation after TWO model-proposed options does not bind to a single proposal, so it is
    /// inferred (confirm-once). This assertion FAILS if <see cref="RequestOriginClassifier.ClassifyAffirmation"/>
    /// is perturbed to return <see cref="GateRequestOrigin.Explicit"/> for a two-proposal context.
    /// </summary>
    [Fact]
    public void E1Negative_AffirmationAfterTwoProposals_ClassifiesInferred_NeverExplicit()
    {
        var twoProposals = new GateProposalContext { ConcreteActionCount = 2, ArgsComplete = true };

        var origin = RequestOriginClassifier.ClassifyAffirmation(twoProposals);

        origin.Should().Be(GateRequestOrigin.Inferred,
            "E-1: two proposals never bind a bare affirmation to a single proposal — must be inferred");
        origin.Should().NotBe(GateRequestOrigin.Explicit,
            "E-1 negative control: a classifier that returns explicit here would re-open the R3-1 confirm-loop hazard");
    }

    /// <summary>
    /// The E-6 negative case, isolated and named exactly per the acceptance criterion:
    /// <c>dispatchUncertain</c> forces a confirm-dialog-with-suspicion even over an otherwise
    /// explicit, complete Tier-2b request. FAILS if the engine is perturbed to auto-execute.
    /// </summary>
    [Fact]
    public void E6Negative_DispatchUncertainOverExplicitRequest_ForcesDialogWithSuspicion_NeverAutoExecutes()
    {
        var originRequest = new GateOriginRequest
        {
            Source = GateRequestSource.UserUtterance,
            UtteranceEnumeratesCapability = true,
        };

        var context = new PolicyEvaluationContext
        {
            RiskProfile = new GateRiskProfile
            {
                DeclaredTier = GateRiskTier.Tier2b,
                Reversible = true,
                RecordOfTruthImpact = true,
            },
            Origin = originRequest,
            ArgsComplete = true,
            DispatchUncertain = true,
        };

        var decision = ConfirmationPolicyEngine.Evaluate(context);

        decision.Origin.Should().Be(GateRequestOrigin.Explicit,
            "E-6: origin classification is untouched by dispatchUncertain (layering — it is not an input to RequestOriginClassifier)");
        decision.Overlays.Decisive.Should().Be(GateOverlay.InjectionSuspect,
            "E-6: dispatchUncertain fires the injection-suspect overlay (overlay 1)");
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog,
            "E-6: suspicion wins — dialog is required even on an otherwise-explicit, complete request");
        decision.Outcome.Should().NotBe(GateOutcome.ExecuteWithUndo,
            "E-6 negative control: an engine that auto-executes here would silently drop the suspicion signal");
    }

    // =========================================================================
    // 2b. Task 044 (gate G-R2-A) — the newly-OBSERVABLE gate outcomes, asserted on the REAL engine.
    //     These document the operator-ratified behaviors the live gate now drives from
    //     (tier × completeness × confidence): execute-no-dialog / draft-no-dialog / elicit / confirm.
    //     Deterministic hard-equality on ConfirmationPolicyEngine.Evaluate (never an LLM judge).
    // =========================================================================

    /// <summary>
    /// The happy path (task 044): a confident + complete Tier-2b create resolves to
    /// <see cref="GateOutcome.ExecuteWithUndo"/> — NO confirmation dialog. FAILS if the engine is
    /// perturbed to confirm a clear, complete low-risk request.
    /// </summary>
    [Fact]
    public void PolicyV2_ConfidentCompleteTier2bCreate_ExecutesWithUndo_NoDialog()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = new GateRiskProfile { DeclaredTier = GateRiskTier.Tier2b, Reversible = true, RecordOfTruthImpact = true },
            Origin = new GateOriginRequest { Source = GateRequestSource.UserUtterance, UtteranceEnumeratesCapability = true },
            ArgsComplete = true,
        });

        decision.Outcome.Should().Be(GateOutcome.ExecuteWithUndo,
            "a clear+complete low-risk (2b) request executes with an Undo affordance — no dialog (operator primary goal)");
    }

    /// <summary>
    /// Task 044: a Tier-1 email DRAFT executes (drafts) with no dialog — the review-and-send handoff
    /// is the gate, never an auto-send. FAILS if a Tier-1 draft is confirmed or escalated.
    /// </summary>
    [Fact]
    public void PolicyV2_EmailDraftTier1_Executes_NoDialog()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = new GateRiskProfile { DeclaredTier = GateRiskTier.Tier1, Reversible = true },
            Origin = new GateOriginRequest { Source = GateRequestSource.UserUtterance, UtteranceEnumeratesCapability = true },
            ArgsComplete = true,
        });

        decision.Outcome.Should().Be(GateOutcome.Execute,
            "an email draft is Tier 1 — it executes (drafts); the human's review-and-send is the gate, not a dialog");
    }

    /// <summary>
    /// Task 044: incomplete required args on a Tier-2b create resolve to <see cref="GateOutcome.Elicit"/>
    /// — the agent asks one natural question, it does NOT execute or suspend. FAILS if the engine
    /// confirms or executes an incomplete request.
    /// </summary>
    [Fact]
    public void PolicyV2_IncompleteArgsTier2b_Elicits()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            RiskProfile = new GateRiskProfile { DeclaredTier = GateRiskTier.Tier2b, Reversible = true, RecordOfTruthImpact = true },
            Origin = new GateOriginRequest { Source = GateRequestSource.UserUtterance, UtteranceEnumeratesCapability = true },
            ArgsComplete = false,
        });

        decision.Outcome.Should().Be(GateOutcome.Elicit,
            "incomplete args ⇒ elicit (a natural question), then re-evaluate — never a scary confirm and never execution");
        decision.Overlays.Decisive.Should().Be(GateOverlay.IncompleteArgs);
    }

    /// <summary>
    /// Task 044: an irreversible record-of-truth mutation (delete/supersede) is ESCALATED by
    /// <see cref="RiskTierResolver"/> from a declared 2b to Tier 4 and ALWAYS confirms — regardless
    /// of a confident, complete request. FAILS if authoring under-declaration lets it auto-execute.
    /// </summary>
    [Fact]
    public void PolicyV2_IrreversibleRecordOfTruth_EscalatesToTier4_ConfirmDialog()
    {
        var decision = ConfirmationPolicyEngine.Evaluate(new PolicyEvaluationContext
        {
            // Declared 2b, but irreversible + record-of-truth ⇒ factor floor escalates to Tier 4.
            RiskProfile = new GateRiskProfile { DeclaredTier = GateRiskTier.Tier2b, Reversible = false, RecordOfTruthImpact = true },
            Origin = new GateOriginRequest { Source = GateRequestSource.UserUtterance, UtteranceEnumeratesCapability = true },
            ArgsComplete = true,
        });

        decision.Tier.Should().Be(GateRiskTier.Tier4, "irreversible + record-of-truth escalates 2b → Tier 4 (fail-closed)");
        decision.Outcome.Should().Be(GateOutcome.ConfirmDialog,
            "an irreversible in-system mutation always confirms, even on a confident, complete request");
    }

    // =========================================================================
    // 3. Net-new-coverage dedupe vs the golden-utterance + resourcefulness suites
    // =========================================================================

    [Fact]
    public void OriginClassificationFamily_IsNetNewCoverage_NotDuplicatingGoldenOrResourcefulnessSuites()
    {
        var family = LoadFamily();
        var golden = LoadGoldenSuite();
        var resourcefulness = LoadResourcefulnessFamily();

        var goldenIds = golden.Cases.Select(c => c.CaseId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourcefulnessIds = resourcefulness.Cases.Select(c => c.CaseId).ToHashSet(StringComparer.OrdinalIgnoreCase);

        family.Cases.Select(c => c.CaseId).Should().OnlyContain(id => !goldenIds.Contains(id),
            "OC- ids must not collide with GU- golden ids");
        family.Cases.Select(c => c.CaseId).Should().OnlyContain(id => !resourcefulnessIds.Contains(id),
            "OC- ids must not collide with RF- resourcefulness ids");

        var goldenFamilies = golden.Cases.Select(c => c.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resourcefulnessFamilies = resourcefulness.Cases.Select(c => c.Family).ToHashSet(StringComparer.OrdinalIgnoreCase);

        EdgeCaseKeys.Should().OnlyContain(e => !goldenFamilies.Contains(e) && !resourcefulnessFamilies.Contains(e),
            "the E-1..E-6 edge-case keys are net-new — they are not golden-utterance or resourcefulness family taxonomies");
    }

    // =========================================================================
    // Helpers — JSON case -> production GateOriginRequest / PolicyEvaluationContext
    // =========================================================================

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static GateOriginRequest ToGateOriginRequest(OriginRequestSpec spec) => new()
    {
        Source = Enum.Parse<GateRequestSource>(spec.Source, ignoreCase: true),
        UtteranceEnumeratesCapability = spec.UtteranceEnumeratesCapability,
        IsBareAffirmation = spec.IsBareAffirmation,
        IsElicitationAnswer = spec.IsElicitationAnswer,
        PrecedingProposal = spec.PrecedingProposal is { } p
            ? new GateProposalContext { ConcreteActionCount = p.ConcreteActionCount, ArgsComplete = p.ArgsComplete }
            : null,
        OriginalRequestOrigin = spec.OriginalRequestOrigin is { } o ? Enum.Parse<GateRequestOrigin>(o, ignoreCase: true) : null,
        PriorExplicitForSameCapabilityArgs = spec.PriorExplicitForSameCapabilityArgs is { } pe ? Enum.Parse<GateRequestOrigin>(pe, ignoreCase: true) : null,
        UserTurnSincePriorClassification = spec.UserTurnSincePriorClassification,
    };

    private static PolicyEvaluationContext ToPolicyEvaluationContext(GateOriginRequest originRequest, PolicyContextSpec spec) => new()
    {
        RiskProfile = new GateRiskProfile
        {
            DeclaredTier = GateRiskProfile.ParseTier(spec.RiskProfile!.DeclaredTier),
            Reversible = spec.RiskProfile.Reversible,
            ExternallyVisible = spec.RiskProfile.ExternallyVisible,
            DeadlineImpact = spec.RiskProfile.DeadlineImpact,
            ConfidentialityImpact = spec.RiskProfile.ConfidentialityImpact,
            RecordOfTruthImpact = spec.RiskProfile.RecordOfTruthImpact,
        },
        Origin = originRequest,
        ArgsComplete = spec.ArgsComplete,
        DispatchUncertain = spec.DispatchUncertain,
        ContentSafetyFlagged = spec.ContentSafetyFlagged,
        SafetyPerimeterDegraded = spec.SafetyPerimeterDegraded,
    };

    private static OriginClassificationFamily LoadFamily()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "origin-classification-eval-family.json");
        File.Exists(path).Should().BeTrue(
            $"origin-classification-eval-family.json must be copied to test output at {path} " +
            "(Content include in Sprk.Bff.Api.Tests.csproj — the contract-Eval **/*.json glob)");

        var family = JsonSerializer.Deserialize<OriginClassificationFamily>(File.ReadAllText(path), JsonOptions);
        family.Should().NotBeNull("the seed file must deserialize into the family schema");
        return family!;
    }

    private static GoldenUtteranceSuite LoadGoldenSuite()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "golden-utterances.json");
        File.Exists(path).Should().BeTrue($"golden-utterances.json must be copied to test output at {path}");
        var suite = JsonSerializer.Deserialize<GoldenUtteranceSuite>(File.ReadAllText(path), JsonOptions);
        return suite!;
    }

    private static ResourcefulnessFamily LoadResourcefulnessFamily()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ContractTests", "Eval", "resourcefulness-eval-family.json");
        File.Exists(path).Should().BeTrue($"resourcefulness-eval-family.json must be copied to test output at {path}");
        var family = JsonSerializer.Deserialize<ResourcefulnessFamily>(File.ReadAllText(path), JsonOptions);
        return family!;
    }
}

// -----------------------------------------------------------------------------
// Origin-classification family case schema (net-new; distinct from the golden-utterance
// dispatch/clarify/refuse schema and the resourcefulness partial-value/honesty schema —
// this scores the deterministic origin classifier + Confirmation Policy v2 gate engine).
// -----------------------------------------------------------------------------

public sealed record OriginClassificationFamily
{
    [JsonPropertyName("schemaVersion")] public string? SchemaVersion { get; init; }
    [JsonPropertyName("family")] public string FamilyKey { get; init; } = string.Empty;
    [JsonPropertyName("provenance")] public string? Provenance { get; init; }
    [JsonPropertyName("cases")] public List<OriginClassificationCase> Cases { get; init; } = new();
}

public sealed record OriginClassificationCase
{
    [JsonPropertyName("caseId")] public string CaseId { get; init; } = string.Empty;
    [JsonPropertyName("edgeCase")] public string EdgeCase { get; init; } = string.Empty;
    [JsonPropertyName("caseType")] public string CaseType { get; init; } = string.Empty;
    [JsonPropertyName("provenance")] public string Provenance { get; init; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("originRequest")] public OriginRequestSpec? OriginRequest { get; init; }
    [JsonPropertyName("policyContext")] public PolicyContextSpec? PolicyContext { get; init; }
    [JsonPropertyName("expected")] public ExpectedSpec? Expected { get; init; }
}

public sealed record OriginRequestSpec
{
    [JsonPropertyName("source")] public string Source { get; init; } = string.Empty;
    [JsonPropertyName("utteranceEnumeratesCapability")] public bool UtteranceEnumeratesCapability { get; init; }
    [JsonPropertyName("isBareAffirmation")] public bool IsBareAffirmation { get; init; }
    [JsonPropertyName("isElicitationAnswer")] public bool IsElicitationAnswer { get; init; }
    [JsonPropertyName("precedingProposal")] public ProposalSpec? PrecedingProposal { get; init; }
    [JsonPropertyName("originalRequestOrigin")] public string? OriginalRequestOrigin { get; init; }
    [JsonPropertyName("priorExplicitForSameCapabilityArgs")] public string? PriorExplicitForSameCapabilityArgs { get; init; }
    [JsonPropertyName("userTurnSincePriorClassification")] public bool UserTurnSincePriorClassification { get; init; }
}

public sealed record ProposalSpec
{
    [JsonPropertyName("concreteActionCount")] public int ConcreteActionCount { get; init; }
    [JsonPropertyName("argsComplete")] public bool ArgsComplete { get; init; }
}

public sealed record PolicyContextSpec
{
    [JsonPropertyName("argsComplete")] public bool ArgsComplete { get; init; } = true;
    [JsonPropertyName("dispatchUncertain")] public bool DispatchUncertain { get; init; }
    [JsonPropertyName("contentSafetyFlagged")] public bool ContentSafetyFlagged { get; init; }
    [JsonPropertyName("safetyPerimeterDegraded")] public bool SafetyPerimeterDegraded { get; init; }
    [JsonPropertyName("riskProfile")] public RiskProfileSpec? RiskProfile { get; init; }
}

public sealed record RiskProfileSpec
{
    [JsonPropertyName("declaredTier")] public string DeclaredTier { get; init; } = "0";
    [JsonPropertyName("reversible")] public bool Reversible { get; init; }
    [JsonPropertyName("externallyVisible")] public bool ExternallyVisible { get; init; }
    [JsonPropertyName("deadlineImpact")] public bool DeadlineImpact { get; init; }
    [JsonPropertyName("confidentialityImpact")] public bool ConfidentialityImpact { get; init; }
    [JsonPropertyName("recordOfTruthImpact")] public bool RecordOfTruthImpact { get; init; }
}

public sealed record ExpectedSpec
{
    [JsonPropertyName("origin")] public string Origin { get; init; } = string.Empty;
    [JsonPropertyName("outcome")] public string Outcome { get; init; } = string.Empty;
    [JsonPropertyName("overlayDecisive")] public string? OverlayDecisive { get; init; }
}
