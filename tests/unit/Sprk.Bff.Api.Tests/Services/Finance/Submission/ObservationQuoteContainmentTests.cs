using System.Text.Json.Nodes;
using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// Ebilling-r1 task 095d-2a-1 (uac-r2 ruling B, condition C1): an AI observation's quote is citable, so it reaches the Signal evidence
/// (<c>signalPlan[].evidence</c>, which <see cref="SignalWriter"/> writes to <c>sprk_signal.sprk_evidencerefs</c>) and the stored findings'
/// <c>valuesRead</c> (Invoice Review). That is allowed only for text of the line's OWN narrative, which <see cref="LineCodingGuard"/>
/// (its quote containment check) keeps. End to end: the model's answer through the guard, the evaluation and the persister's stored copy.
/// </summary>
/// <remarks>The guard's own keep / drop rule is covered by <c>LineCodingGuardTests</c> (domain) and
/// <c>LineCodingSeamTests.AnObservationWhoseQuotedTextIsNotInTheNarrative_IsDropped</c> (seam); these tests pin where the quote goes.</remarks>
public sealed class ObservationQuoteContainmentTests
{
    private const string Narrative = "Drafted the motion; called the client about the motion.";

    private static readonly Guid InvoiceId = Guid.Parse("095d2a10-0000-4000-8000-00000000c1a1");

    /// <summary>The model returned one block-billing observation quoting <paramref name="quote"/>; the coding step keeps what the guard keeps.</summary>
    private static async Task<(LineCodingResult Guarded, SubmissionEvaluationOutcome Outcome, JsonObject Stored)> RunAsync(string quote)
    {
        var line = new InvoiceLineToCode { LineKey = "0", LineType = InvoiceLineType.Fee, Narrative = Narrative };
        var set = new ResolvedCodeSet(Guid.Parse("04900000-0000-4000-8000-0000000000e1"), "UTBMS", CodeSetSource.FirmCodes, [], [], []);
        var returned = new InvoiceLineCoding
        {
            LineKey = "0",
            Confidence = 0.9m,
            Observations = [new InvoiceLineObservation { Type = InvoiceLineObservationType.BlockBilling, Confidence = 0.95m, QuotedText = quote }],
        };
        var guarded = LineCodingGuard.Apply(line, returned, set);

        var harness = new InvoiceSubmissionProcessorTests.Harness
        {
            Rules = [Version("BILL-AI", RuleType.AiObservation)],
            Lines = [InvoiceSubmissionProcessorTests.FeeLine() with { Narrative = InvoiceSubmissionProcessorTests.F<string?>(Narrative) }],
        };
        harness.Coding.Observations = guarded.Observations;
        var outcome = await harness.RunAsync();
        return (guarded, outcome, JsonNode.Parse(InvoiceSubmissionPersister.BuildFindingsJson(outcome, InvoiceId))!.AsObject());
    }

    private static IEnumerable<string?> EvidenceTexts(JsonObject stored) =>
        (stored["signalPlan"]?.AsArray() ?? []).SelectMany(p => p!["evidence"]!.AsArray()).Select(e => e!["text"]?.GetValue<string>());

    private static IEnumerable<string?> ValuesRead(JsonObject stored) =>
        (stored["findings"]?.AsArray() ?? []).SelectMany(f => f!["valuesRead"]?.AsArray() ?? []).Select(v => v!["value"]?.GetValue<string>());

    [Theory(DisplayName = "095d-2a-1 (C1) SEEDED must-fire: a quote that is not text of the line's own narrative never reaches the Signal evidence or the stored valuesRead")]
    [InlineData("FOREIGNSENTINEL reviewed the opposing brief")]
    [InlineData("FOREIGNSENTINEL; called the client")]
    public async Task AQuoteNotInTheNarrative_NeverReachesTheEvidence(string quote)
    {
        var (guarded, outcome, stored) = await RunAsync(quote);

        guarded.Observations.Should().BeEmpty("the containment check drops it");
        guarded.DroppedObservations.Should().Be(1);
        outcome.Evaluation!.Findings.Should().NotContain(f => f.ValuesRead.Any(v => v.Value != null && v.Value.Contains("FOREIGNSENTINEL")));
        EvidenceTexts(stored).Should().NotContain(t => t != null && t.Contains("FOREIGNSENTINEL"));
        ValuesRead(stored).Should().NotContain(v => v != null && v.Contains("FOREIGNSENTINEL"));
        stored.ToJsonString().Should().NotContain("FOREIGNSENTINEL");
    }

    [Fact(DisplayName = "095d-2a-1 (C1) must-not-fire: a quote that is text of the line's own narrative is kept, and is the cited evidence and the value read")]
    public async Task AQuoteInTheNarrative_IsKept_AndCited()
    {
        const string quote = "Drafted the motion; called the client";

        var (guarded, _, stored) = await RunAsync(quote);

        guarded.Observations.Should().ContainSingle().Which.QuotedText.Should().Be(quote);
        EvidenceTexts(stored).Should().Contain(quote, "the AiObservation finding cites the observation");
        ValuesRead(stored).Should().Contain(quote);
    }
}
