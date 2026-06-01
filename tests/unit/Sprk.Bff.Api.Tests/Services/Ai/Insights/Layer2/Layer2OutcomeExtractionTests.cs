using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Extraction;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Insights.Layer2;

/// <summary>
/// Integration tests for the D-P6 Layer 2 outcome-extraction pipeline:
/// prompt+schema (<c>outcome-extraction@v1</c>) -> validator (D-P6 schema gate) ->
/// projection (typed -> <see cref="ExtractionResult"/>) -> downstream <see cref="IObservationEmitter"/>
/// (D-P10 confidence gating + emission). LLM is mocked; everything else is real.
/// <para>
/// Acceptance criteria covered (per <c>tasks/031-layer2-outcome-extraction-prompt.poml</c>):
/// </para>
/// <list type="bullet">
///   <item>Prompt response matching <c>SPEC-phase-1-minimum.md §3.4</c> schema validates cleanly.</item>
///   <item>Closing-letter fixture extracts outcomeCategory + settlementAmount + outcomeDate with verbatim quotes.</item>
///   <item>Settlement-agreement fixture extracts settlementAmount + keyTerms[].</item>
///   <item>Decision-memo fixture (mixed/unclear outcome) returns nulls with confidence 0 + explanations.</item>
///   <item>Verbatim quotes are byte-identical substrings of the source fixture.</item>
///   <item><c>producedBy="outcome-extraction@v1"</c> stamped on every emitted Observation.</item>
///   <item>Malformed LLM output is rejected by the validator before downstream gates run.</item>
/// </list>
/// </summary>
public class Layer2OutcomeExtractionTests
{
    // ─── Fixture loading ──────────────────────────────────────────────────────

    /// <summary>
    /// Locates the <c>tests/Insights/fixtures/</c> directory relative to the test assembly.
    /// Test fixtures live OUTSIDE the .Tests project so the D-P7 universal ingest playbook can
    /// reuse them at integration time (per task POML output spec).
    /// </summary>
    private static string FixturesDirectory
    {
        get
        {
            var assemblyDir = Path.GetDirectoryName(typeof(Layer2OutcomeExtractionTests).Assembly.Location)
                ?? AppContext.BaseDirectory;
            // Walk up from bin/Debug/net8.0 to repo root, then down into tests/Insights/fixtures.
            var dir = new DirectoryInfo(assemblyDir);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "Insights", "fixtures")))
            {
                dir = dir.Parent;
            }
            if (dir is null)
            {
                throw new DirectoryNotFoundException(
                    $"Could not locate tests/Insights/fixtures starting from {assemblyDir}.");
            }
            return Path.Combine(dir.FullName, "tests", "Insights", "fixtures");
        }
    }

    private static string LoadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixturesDirectory, fileName));

    // ─── Test helpers ─────────────────────────────────────────────────────────

    private static readonly DateTimeOffset FixedAsOf = new(2026, 4, 12, 8, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Permissive thresholds so every above-zero-confidence field emits (we are testing the
    /// prompt + projection plumbing here; D-P10 threshold behavior is covered separately by
    /// <c>ObservationEmitterTests</c>).
    /// </summary>
    private static IObservationEmitter NewEmitterWithPermissiveThresholds()
    {
        var permissive = new ConfidenceThresholdOptions
        {
            DefaultThreshold = 0.10,
            PerField = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            {
                ["outcomeCategory"] = 0.10,
                ["settlementAmount"] = 0.10,
                ["outcomeDate"] = 0.10,
                ["matterDurationDays"] = 0.10
            }
        };
        var monitor = new TestOptionsMonitor<ConfidenceThresholdOptions>(permissive);
        return new ObservationEmitter(monitor, NullLogger<ObservationEmitter>.Instance);
    }

    /// <summary>Runs the full Layer 2 pipeline: validate -> project -> emit.</summary>
    private static async Task<IReadOnlyList<ObservationArtifact>> RunPipelineAsync(
        string mockedLlmJson,
        string subject,
        string documentRef,
        string tenantId,
        ExtractionScope? scope = null)
    {
        // Step 1: validator — same gate that D-P6 runs before any downstream gate sees the output.
        var validation = OutcomeExtractionResponseValidator.Validate(mockedLlmJson);
        validation.IsValid.Should().BeTrue(
            "the mocked LLM response must match the published schema — violations: " +
            string.Join("; ", validation.Errors));

        // Step 2: projection — typed response into ExtractionResult.
        var extraction = OutcomeExtractionProjection.Build(
            validation.Response!,
            subject: subject,
            documentRef: documentRef,
            tenantId: tenantId,
            asOf: FixedAsOf,
            scope: scope);

        // Step 3: D-P10 emission — applies the confidence threshold gate + stamps producedBy.
        var emitter = NewEmitterWithPermissiveThresholds();
        return await emitter.EmitFromExtractionAsync(
            extraction,
            upsertAsync: null,
            ct: CancellationToken.None);
    }

    // ─── Acceptance #1 — closing letter: outcomeCategory + settlementAmount + outcomeDate ───

    [Fact(Skip = "RB-T028-02: HOLD per task 008 Insights sibling sign-off. Layer2 outcome-extraction LLM-mock fixture text drifted from the test's documentText assertion. Awaiting `ai-spaarke-insights-engine-r1` owner sign-off before production fix or test re-baseline. See real-bug-ledger.md.")]
    [Trait("status", "real-bug-pending-fix")]
    public async Task ClosingLetterFixture_ExtractsOutcomeAndSettlementAndDate_WithVerbatimQuotes()
    {
        // Arrange — load the closing-letter fixture and a mocked LLM response that matches it.
        var documentText = LoadFixture("closing-letter-M-2024-0341.txt");
        const string mockedLlmJson = """
        {
          "outcomeCategory": "favorable_to_client",
          "settlementAmount": 310000,
          "settlementCurrency": "USD",
          "outcomeDate": "2024-08-15",
          "matterDurationDays": 213,
          "keyTerms": [
            { "term": "cure-period clause", "description": "30-day cure-period clause preserved unchanged from the original license agreement." },
            { "term": "exclusive licensing territory", "description": "Acme retained sole licensee in North America." },
            { "term": "non-disparagement covenant", "description": "Mutual non-disparagement with 5-year survival term." }
          ],
          "evidence": {
            "outcomeCategory": "The matter concluded with terms favorable to our client, securing all material\nrights sought in the original complaint.",
            "settlementAmount": "Settlement total: $310,000 USD payable within 30 days of execution of the\nSettlement and Release Agreement.",
            "outcomeDate": "Final closing on August 15, 2024.",
            "matterDurationDays": "Matter open from January 14, 2024 through closing — 213 days total."
          },
          "confidence": {
            "outcomeCategory": 0.95,
            "settlementAmount": 0.97,
            "outcomeDate": 0.99,
            "matterDurationDays": 0.92
          }
        }
        """;

        // Manual acceptance check: every evidence quote MUST be a verbatim substring of the
        // source fixture. This is the load-bearing prompt-engineering invariant that
        // GroundingVerifier (D-P9) mechanically enforces in production.
        var validation = OutcomeExtractionResponseValidator.Validate(mockedLlmJson);
        validation.IsValid.Should().BeTrue("validator must accept a schema-conforming response");
        documentText.Should().Contain(validation.Response!.Evidence.OutcomeCategory!,
            "outcomeCategory quote MUST be verbatim from the closing letter (GroundingVerifier check)");
        documentText.Should().Contain(validation.Response.Evidence.SettlementAmount!,
            "settlementAmount quote MUST be verbatim from the closing letter");
        documentText.Should().Contain(validation.Response.Evidence.OutcomeDate!,
            "outcomeDate quote MUST be verbatim from the closing letter");
        documentText.Should().Contain(validation.Response.Evidence.MatterDurationDays!,
            "matterDurationDays quote MUST be verbatim from the closing letter");

        // Act — run the full pipeline.
        var emitted = await RunPipelineAsync(
            mockedLlmJson,
            subject: "matter:M-2024-0341",
            documentRef: "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
            tenantId: "tenant-acme",
            scope: new ExtractionScope { MatterId = "M-2024-0341", PracticeArea = "ip-licensing" });

        // Assert — all 4 fields produce Observations with the correct shape.
        emitted.Should().HaveCount(4);
        emitted.Select(o => o.Predicate).Should().BeEquivalentTo(new[]
        {
            "outcomeCategory", "settlementAmount", "outcomeDate", "matterDurationDays"
        });

        var outcomeCat = emitted.Single(o => o.Predicate == "outcomeCategory");
        outcomeCat.Subject.Should().Be("matter:M-2024-0341");
        outcomeCat.Value.Raw.GetString().Should().Be("favorable_to_client");
        outcomeCat.Confidence.Should().Be(0.95);
        outcomeCat.Evidence[0].Quote.Should().Be(
            "The matter concluded with terms favorable to our client, securing all material\nrights sought in the original complaint.");

        var settlement = emitted.Single(o => o.Predicate == "settlementAmount");
        settlement.Value.Raw.GetDecimal().Should().Be(310000m);
        settlement.Value.DisplayHint.Should().Be("currency-usd");
        settlement.Evidence[0].Quote.Should().Be(
            "Settlement total: $310,000 USD payable within 30 days of execution of the\nSettlement and Release Agreement.");

        var date = emitted.Single(o => o.Predicate == "outcomeDate");
        date.Value.Raw.GetString().Should().Be("2024-08-15");
        date.Value.DisplayHint.Should().Be("date");

        var duration = emitted.Single(o => o.Predicate == "matterDurationDays");
        duration.Value.Raw.GetInt32().Should().Be(213);
        duration.Value.DisplayHint.Should().Be("duration-days");
    }

    // ─── Acceptance #2 — settlement agreement: settlementAmount + keyTerms[] ───

    [Fact(Skip = "RB-T028-02: HOLD per task 008 Insights sibling sign-off (see RB-T028-02 entry). Awaiting `ai-spaarke-insights-engine-r1` owner sign-off.")]
    [Trait("status", "real-bug-pending-fix")]
    public async Task SettlementAgreementFixture_ExtractsSettlementAmount_AndKeyTermsPopulated()
    {
        // Arrange — settlement-agreement fixture; the document is dispositive on amount + key
        // commercial terms but ambiguous on a clean outcomeCategory (it's a mutual release).
        var documentText = LoadFixture("settlement-agreement-M-2024-0188.txt");
        const string mockedLlmJson = """
        {
          "outcomeCategory": "neutral",
          "settlementAmount": 245000,
          "settlementCurrency": "EUR",
          "outcomeDate": "2024-05-22",
          "matterDurationDays": null,
          "keyTerms": [
            { "term": "exclusivity", "description": "3-year supply exclusivity against named competitors per Schedule A." },
            { "term": "indemnification cap", "description": "Aggregate indemnification capped at 2x trailing-twelve-month volume up to EUR 1,000,000." },
            { "term": "cure-period clause", "description": "45-day cure period in any successor supply arrangement (replaces prior 30-day cure)." },
            { "term": "governing law and venue", "description": "Swiss law; ICC arbitration in Geneva." }
          ],
          "evidence": {
            "outcomeCategory": "Mutual releases. Effective upon Claimant's receipt of the Settlement",
            "settlementAmount": "Respondent shall pay Claimant the sum of EUR 245,000",
            "outcomeDate": "is entered into as of\nMay 22, 2024",
            "matterDurationDays": null
          },
          "confidence": {
            "outcomeCategory": 0.55,
            "settlementAmount": 0.98,
            "outcomeDate": 0.90,
            "matterDurationDays": 0.0
          },
          "explanations": {
            "matterDurationDays": "Agreement does not state the matter open date; only the effective date of settlement is stated."
          }
        }
        """;

        // Manual verbatim-substring check against the fixture.
        var validation = OutcomeExtractionResponseValidator.Validate(mockedLlmJson);
        validation.IsValid.Should().BeTrue("validator must accept a schema-conforming response");
        documentText.Should().Contain(validation.Response!.Evidence.SettlementAmount!,
            "settlementAmount quote MUST be verbatim from the settlement agreement");
        documentText.Should().Contain(validation.Response.Evidence.OutcomeDate!,
            "outcomeDate quote MUST be verbatim from the settlement agreement");

        // Act
        var emitted = await RunPipelineAsync(
            mockedLlmJson,
            subject: "matter:M-2024-0188",
            documentRef: "spe://drive/hawthorne-matters/item/settlement-agreement-M-2024-0188.docx",
            tenantId: "tenant-hawthorne",
            scope: new ExtractionScope { MatterId = "M-2024-0188", PracticeArea = "supply-chain" });

        // Assert — settlementAmount emits; non-USD currency reflected in DisplayHint; matterDurationDays
        // is omitted from emitted Observations (it was null, so projection drops it per SPEC §3.4).
        emitted.Should().HaveCount(3, "matterDurationDays was null; projection omits null fields");
        emitted.Select(o => o.Predicate).Should().Contain("settlementAmount");
        emitted.Select(o => o.Predicate).Should().NotContain("matterDurationDays");

        var settlement = emitted.Single(o => o.Predicate == "settlementAmount");
        settlement.Value.Raw.GetDecimal().Should().Be(245000m);
        settlement.Value.DisplayHint.Should().Be("currency-eur",
            "settlement currency was EUR; projection mirrors the ISO code into the display hint");
        settlement.Evidence[0].Quote.Should().Be("Respondent shall pay Claimant the sum of EUR 245,000");

        // keyTerms[] populated — these surface on the D-P11 review row but are not gated fields,
        // so we assert via the validated response rather than via emitted Observations.
        validation.Response.KeyTerms.Should().HaveCount(4);
        validation.Response.KeyTerms.Select(k => k.Term).Should().Contain(new[]
        {
            "exclusivity", "indemnification cap", "cure-period clause", "governing law and venue"
        });
    }

    // ─── Acceptance #3 — missing field returns null + confidence 0 + explanation ───

    [Fact(Skip = "RB-T028-02: HOLD per task 008 Insights sibling sign-off (see RB-T028-02 entry). Awaiting `ai-spaarke-insights-engine-r1` owner sign-off.")]
    [Trait("status", "real-bug-pending-fix")]
    public async Task DecisionMemoFixture_MixedOutcome_ReturnsNullsWithConfidenceZeroAndExplanations()
    {
        // Arrange — decision-memo fixture; this is intentionally mixed/unclear (records a
        // committee disposition mid-case, not a final outcome). The model should NOT fabricate
        // a settlement amount or a final outcome date; it should return nulls with confidence
        // 0 and per-field explanations.
        var documentText = LoadFixture("decision-memo-M-2024-0512.txt");
        const string mockedLlmJson = """
        {
          "outcomeCategory": "mixed",
          "settlementAmount": null,
          "settlementCurrency": "USD",
          "outcomeDate": null,
          "matterDurationDays": null,
          "keyTerms": [
            { "term": "mediated settlement", "description": "Committee chose mediated settlement on surviving counts over appeal or trial." },
            { "term": "summary judgment grant", "description": "Court granted summary judgment for Vista on Count III; denied on Counts I and II." }
          ],
          "evidence": {
            "outcomeCategory": "This is a mixed outcome — neither a clear favorable nor unfavorable\ndisposition for Coastal at the current procedural stage.",
            "settlementAmount": null,
            "outcomeDate": null,
            "matterDurationDays": null
          },
          "confidence": {
            "outcomeCategory": 0.78,
            "settlementAmount": 0.0,
            "outcomeDate": 0.0,
            "matterDurationDays": 0.0
          },
          "explanations": {
            "settlementAmount": "Memo records committee disposition mid-case; no settlement amount is stated.",
            "outcomeDate": "Matter has not reached final disposition; only the strategy-decision date is recorded.",
            "matterDurationDays": "Matter open date is not stated in the memo."
          }
        }
        """;

        // Validator should accept the response: null fields have null evidence + 0.0 confidence,
        // which is the "honest abstention" shape SPEC §3.4 mandates.
        var validation = OutcomeExtractionResponseValidator.Validate(mockedLlmJson);
        validation.IsValid.Should().BeTrue(
            "validator must accept honest abstention shape (null + null + 0.0). Errors: " +
            string.Join("; ", validation.Errors));

        // Manual verbatim-substring check on the one non-null evidence quote.
        documentText.Should().Contain(validation.Response!.Evidence.OutcomeCategory!,
            "outcomeCategory quote MUST be verbatim from the decision memo");

        // Act
        var emitted = await RunPipelineAsync(
            mockedLlmJson,
            subject: "matter:M-2024-0512",
            documentRef: "spe://drive/coastal-matters/item/decision-memo-M-2024-0512.docx",
            tenantId: "tenant-coastal");

        // Assert — only outcomeCategory emits; the other three fields were null so projection
        // omits them entirely (the LLM honestly abstained instead of fabricating).
        emitted.Should().HaveCount(1, "honest abstention: 3 fields null -> 3 omitted by projection; only outcomeCategory survives");
        emitted.Single().Predicate.Should().Be("outcomeCategory");
        emitted.Single().Value.Raw.GetString().Should().Be("mixed");

        // Explanations were captured for the D-P11 review surface (Phase 1.5+).
        validation.Response.Explanations.Should().NotBeNull();
        validation.Response.Explanations!.Should().ContainKeys("settlementAmount", "outcomeDate", "matterDurationDays");
        validation.Response.Explanations!["settlementAmount"].Should().Contain("no settlement amount is stated");
    }

    // ─── Acceptance #4 — producedBy="outcome-extraction@v1" on every emitted Observation ───

    [Fact]
    public async Task EveryEmittedObservation_StampsProducedByOutcomeExtractionV1()
    {
        const string mockedLlmJson = """
        {
          "outcomeCategory": "favorable_to_client",
          "settlementAmount": 310000,
          "settlementCurrency": "USD",
          "outcomeDate": "2024-08-15",
          "matterDurationDays": 213,
          "keyTerms": [],
          "evidence": {
            "outcomeCategory": "The matter concluded with terms favorable to our client, securing all material\nrights sought in the original complaint.",
            "settlementAmount": "Settlement total: $310,000 USD payable within 30 days",
            "outcomeDate": "Final closing on August 15, 2024.",
            "matterDurationDays": "Matter open from January 14, 2024 through closing — 213 days total."
          },
          "confidence": {
            "outcomeCategory": 0.95,
            "settlementAmount": 0.97,
            "outcomeDate": 0.99,
            "matterDurationDays": 0.92
          }
        }
        """;

        var emitted = await RunPipelineAsync(
            mockedLlmJson,
            subject: "matter:M-2024-0341",
            documentRef: "spe://drive/acme-matters/item/closing-letter-M-2024-0341.docx",
            tenantId: "tenant-acme");

        emitted.Should().HaveCount(4);
        foreach (var obs in emitted)
        {
            obs.ProducedBy.Kind.Should().Be("playbook");
            obs.ProducedBy.Id.Should().Be("playbook://outcome-extraction@v1",
                "D-62 prompt versioning — producedBy.Id is the canonical producer identifier for re-extraction targeting");
            obs.ProducedBy.Version.Should().Be("v1",
                "D-05 mandates Version on Observations; v1 propagates from the prompt template to every emitted Observation");

            // playbook-run evidence ref is the second entry per ObservationEmitter contract.
            obs.Evidence.Should().HaveCountGreaterOrEqualTo(2);
            obs.Evidence.Last().RefType.Should().Be("playbook-run");
            obs.Evidence.Last().Ref.Should().StartWith("playbook://outcome-extraction@v1/run-");
        }
    }

    // ─── Validator — schema failure ───────────────────────────────────────────

    [Fact]
    public void Validator_RejectsMalformedJson_BeforeDownstreamGatesRun()
    {
        // Not even JSON.
        var bareString = OutcomeExtractionResponseValidator.Validate("not json at all");
        bareString.IsValid.Should().BeFalse();
        bareString.Errors.Should().ContainSingle().Which.Should().Contain("not valid JSON");
    }

    [Fact]
    public void Validator_RejectsNonNullFieldWithMissingEvidence()
    {
        // settlementAmount is non-null but its evidence quote is null — hallucination shape.
        const string malformed = """
        {
          "outcomeCategory": null,
          "settlementAmount": 100000,
          "settlementCurrency": "USD",
          "outcomeDate": null,
          "matterDurationDays": null,
          "keyTerms": [],
          "evidence": {
            "outcomeCategory": null,
            "settlementAmount": null,
            "outcomeDate": null,
            "matterDurationDays": null
          },
          "confidence": {
            "outcomeCategory": 0.0,
            "settlementAmount": 0.95,
            "outcomeDate": 0.0,
            "matterDurationDays": 0.0
          }
        }
        """;
        var result = OutcomeExtractionResponseValidator.Validate(malformed);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("settlementAmount") && e.Contains("evidence quote is missing"));
    }

    [Fact]
    public void Validator_RejectsNullFieldWithSuppliedEvidence()
    {
        // outcomeCategory is null but a quote was supplied — hallucinated quote for absent field.
        const string malformed = """
        {
          "outcomeCategory": null,
          "settlementAmount": null,
          "settlementCurrency": "USD",
          "outcomeDate": null,
          "matterDurationDays": null,
          "keyTerms": [],
          "evidence": {
            "outcomeCategory": "the agreement concluded favorably",
            "settlementAmount": null,
            "outcomeDate": null,
            "matterDurationDays": null
          },
          "confidence": {
            "outcomeCategory": 0.0,
            "settlementAmount": 0.0,
            "outcomeDate": 0.0,
            "matterDurationDays": 0.0
          }
        }
        """;
        var result = OutcomeExtractionResponseValidator.Validate(malformed);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("outcomeCategory") && e.Contains("hallucinated quote"));
    }

    [Fact]
    public void Validator_RejectsUnknownOutcomeCategoryEnumValue()
    {
        // "won_big" is not in the allowed enum.
        const string malformed = """
        {
          "outcomeCategory": "won_big",
          "settlementAmount": null,
          "settlementCurrency": "USD",
          "outcomeDate": null,
          "matterDurationDays": null,
          "keyTerms": [],
          "evidence": { "outcomeCategory": "we won big", "settlementAmount": null, "outcomeDate": null, "matterDurationDays": null },
          "confidence": { "outcomeCategory": 0.9, "settlementAmount": 0.0, "outcomeDate": 0.0, "matterDurationDays": 0.0 }
        }
        """;
        var result = OutcomeExtractionResponseValidator.Validate(malformed);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("won_big") && e.Contains("not in the allowed enum"));
    }

    [Fact]
    public void Validator_RejectsConfidenceOutsideUnitInterval()
    {
        const string malformed = """
        {
          "outcomeCategory": "favorable_to_client",
          "settlementAmount": null,
          "settlementCurrency": "USD",
          "outcomeDate": null,
          "matterDurationDays": null,
          "keyTerms": [],
          "evidence": { "outcomeCategory": "favorable outcome here", "settlementAmount": null, "outcomeDate": null, "matterDurationDays": null },
          "confidence": { "outcomeCategory": 1.5, "settlementAmount": 0.0, "outcomeDate": 0.0, "matterDurationDays": 0.0 }
        }
        """;
        var result = OutcomeExtractionResponseValidator.Validate(malformed);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("confidence 1.50 is outside [0.0, 1.0]"));
    }

    [Fact]
    public void Validator_RejectsInvalidIsoDate()
    {
        // outcomeDate is non-null but not yyyy-MM-dd.
        const string malformed = """
        {
          "outcomeCategory": null,
          "settlementAmount": null,
          "settlementCurrency": "USD",
          "outcomeDate": "August 15, 2024",
          "matterDurationDays": null,
          "keyTerms": [],
          "evidence": { "outcomeCategory": null, "settlementAmount": null, "outcomeDate": "Final closing on August 15, 2024.", "matterDurationDays": null },
          "confidence": { "outcomeCategory": 0.0, "settlementAmount": 0.0, "outcomeDate": 0.95, "matterDurationDays": 0.0 }
        }
        """;
        var result = OutcomeExtractionResponseValidator.Validate(malformed);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("August 15, 2024") && e.Contains("not a valid ISO 8601 date"));
    }

    // ─── Projection — null field omission ────────────────────────────────────

    [Fact]
    public void Projection_OmitsNullFieldsFromExtractionResult()
    {
        // All fields null — projection produces empty Fields dictionary.
        var allNull = new OutcomeExtractionResponse
        {
            OutcomeCategory = null,
            SettlementAmount = null,
            SettlementCurrency = "USD",
            OutcomeDate = null,
            MatterDurationDays = null,
            Evidence = new OutcomeExtractionEvidence(),
            Confidence = new OutcomeExtractionConfidence()
        };

        var extraction = OutcomeExtractionProjection.Build(
            allNull,
            subject: "matter:M-X",
            documentRef: "spe://drive/x/item/doc.docx",
            tenantId: "tenant-x",
            asOf: FixedAsOf);

        extraction.Fields.Should().BeEmpty(
            "null fields are 'not attempted' per SPEC §3.4 and MUST be omitted upstream of D-P10");
    }

    [Fact]
    public void Projection_PreservesProducedByOutcomeExtractionV1()
    {
        var minimal = new OutcomeExtractionResponse
        {
            OutcomeCategory = "favorable_to_client",
            SettlementAmount = null,
            SettlementCurrency = "USD",
            OutcomeDate = null,
            MatterDurationDays = null,
            Evidence = new OutcomeExtractionEvidence { OutcomeCategory = "favorable to client outcome" },
            Confidence = new OutcomeExtractionConfidence { OutcomeCategory = 0.9 }
        };

        var extraction = OutcomeExtractionProjection.Build(
            minimal,
            subject: "matter:M-X",
            documentRef: "spe://drive/x/item/doc.docx",
            tenantId: "tenant-x",
            asOf: FixedAsOf);

        extraction.ProducedBy.Kind.Should().Be("playbook");
        extraction.ProducedBy.Id.Should().Be("playbook://outcome-extraction@v1");
        extraction.ProducedBy.Version.Should().Be("v1");
    }

    // ─── Prompt + schema content sanity ──────────────────────────────────────

    [Fact]
    public void PromptTemplateFile_IsPresentInOutputDirectory()
    {
        // The csproj copies Services/Ai/Insights/Prompts/*.txt to the output directory so the
        // ingest playbook can load it at runtime. This asserts the copy actually happened.
        var assemblyDir = Path.GetDirectoryName(typeof(Layer2OutcomeExtractionTests).Assembly.Location)
            ?? AppContext.BaseDirectory;
        // The .Tests assembly references the BFF API; its output directory should contain the
        // copied content. Walk down into Services/Ai/Insights/Prompts/.
        var promptPath = Path.Combine(
            assemblyDir, "Services", "Ai", "Insights", "Prompts", "outcome-extraction.v1.txt");
        File.Exists(promptPath).Should().BeTrue(
            $"outcome-extraction.v1.txt MUST be copied to the output directory so the D-P7 ingest playbook can load it at runtime. Expected: {promptPath}");

        var content = File.ReadAllText(promptPath);
        content.Should().Contain("outcomeCategory");
        content.Should().Contain("settlementAmount");
        content.Should().Contain("verbatim");
    }

    [Fact]
    public void SchemaFile_IsPresentAndParsesAsValidJson()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(Layer2OutcomeExtractionTests).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var schemaPath = Path.Combine(
            assemblyDir, "Services", "Ai", "Insights", "Prompts", "outcome-extraction.v1.schema.json");
        File.Exists(schemaPath).Should().BeTrue($"schema sibling must be copied. Expected: {schemaPath}");

        var json = File.ReadAllText(schemaPath);
        Action parseAct = () => JsonDocument.Parse(json);
        parseAct.Should().NotThrow("the schema must be valid JSON");
    }

    [Fact]
    public void PlaybookNodeConfig_IsPresentAndReferencesPromptAndExecutor()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(Layer2OutcomeExtractionTests).Assembly.Location)
            ?? AppContext.BaseDirectory;
        var configPath = Path.Combine(
            assemblyDir, "Services", "Ai", "Insights", "Playbooks", "layer2-outcome-extraction.node.json");
        File.Exists(configPath).Should().BeTrue($"playbook node config must be copied. Expected: {configPath}");

        var json = File.ReadAllText(configPath);
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("executor").GetString().Should().Be("AiAnalysisNodeExecutor");
        doc.RootElement.GetProperty("promptTemplate").GetString().Should().Be("outcome-extraction.v1.txt");
        doc.RootElement.GetProperty("producedBy").GetString().Should().Be("playbook://outcome-extraction@v1");
    }

    // ─── Test helper: in-process IOptionsMonitor implementation ──────────────

    /// <summary>
    /// Minimal <see cref="IOptionsMonitor{TOptions}"/> shim — mirrors the test helper from
    /// <c>ObservationEmitterTests</c> so this test file does not depend on test-internal types.
    /// </summary>
    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
    {
        private TOptions _current;

        public TestOptionsMonitor(TOptions initial) => _current = initial;

        public TOptions CurrentValue => _current;
        public TOptions Get(string? name) => _current;

        public IDisposable OnChange(Action<TOptions, string?> listener) => new Noop();

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }
}
