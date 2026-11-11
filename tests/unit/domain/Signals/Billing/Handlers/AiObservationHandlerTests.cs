using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 11 AiObservation (design.md 6.1; spec I-5, NFR-05). Maintain-class (ADR-038): pure logic, no
/// mocks. A handler only reports the finding; that it never rejects is task 073's guard.
/// </summary>
public class AiObservationHandlerTests
{
    private static readonly BillingRuleDefinition BlockBilling = Rule("""{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}""");
    private static readonly BillingRuleDefinition Vague = Rule("""{"type":"AiObservation","observation":"vagueDescription","minConfidence":0.85}""");

    private static InvoiceLineObservation Observed(InvoiceLineObservationType type, decimal confidence, string quote = "Research, call, draft and review") =>
        new() { Type = type, Confidence = confidence, QuotedText = quote };

    private static BillingFactSet FactsWith(params InvoiceLineObservation[] observations) =>
        Facts(Fee(0) with { Observations = observations });

    [Fact]
    public void MustFire_WhenAnObservationOfTheConfiguredKindIsAtOrAboveTheThreshold()
    {
        var facts = FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.93m));

        var finding = Run(BlockBilling, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.AiBlockBilling);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().BeNull();
        finding.FactValues.Should().Contain("observation", "blockBilling").And.Contain("observationConfidence", "0.93").And.Contain("rule.minConfidence", "0.85");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.AiObservation));
        finding.Value("line.observation").Value.Should().Be("Research, call, draft and review");
    }

    [Fact]
    public void MustFire_AtExactlyTheThreshold_AndNotOneHundredthBelow()
    {
        Run(BlockBilling, FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.85m))).Should().ContainSingle();
        Run(BlockBilling, FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.84m))).Should().BeEmpty();
    }

    [Fact]
    public void MustNotFire_ForAnObservationOfAnotherKind_OrForNone()
    {
        Run(BlockBilling, FactsWith(Observed(InvoiceLineObservationType.VagueDescription, 0.99m))).Should().BeEmpty();
        Run(BlockBilling, FactsWith()).Should().BeEmpty();
    }

    [Fact]
    public void TheVagueDescriptionRule_FiresOnVagueDescription_WithItsOwnReasonCode()
    {
        var facts = FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.99m), Observed(InvoiceLineObservationType.VagueDescription, 0.90m, "Attention to file"));

        var finding = Run(Vague, facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.AiVagueDescription);
        finding.Value("line.observation").Value.Should().Be("Attention to file");
        finding.ReadFields.Should().Equal(new FieldRef(0, BillingFieldNames.ObservationVagueDescription), new FieldRef(0, BillingFieldNames.Date));
    }

    [Fact]
    public void SeveralQualifyingObservations_GiveOneFinding_ForTheMostConfident()
    {
        var facts = FactsWith(
            Observed(InvoiceLineObservationType.BlockBilling, 0.88m, "first"),
            Observed(InvoiceLineObservationType.BlockBilling, 0.97m, "second"),
            Observed(InvoiceLineObservationType.BlockBilling, 0.97m, "third"));

        var finding = Run(BlockBilling, facts).Should().ContainSingle().Subject;

        finding.Value("line.observation").Value.Should().Be("second", "the first of equally confident observations");
    }

    [Fact]
    public void EachLineIsJudgedOnItsOwnObservations()
    {
        var facts = Facts(
            Fee(0) with { Observations = [Observed(InvoiceLineObservationType.BlockBilling, 0.5m)] },
            Fee(1) with { Observations = [Observed(InvoiceLineObservationType.BlockBilling, 0.9m)] },
            Expense(2) with { Observations = [Observed(InvoiceLineObservationType.BlockBilling, 0.95m)] });

        Run(BlockBilling, facts).Select(f => f.Subject.LineIndex).Should().Equal(1, 2);
    }

    // ── NFR-05 and I-5 ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnObservationAboveTheRulesThreshold_ButBelowTheReliabilityThreshold_IsFlagged_NotPresentedAsAFact()
    {
        // 0.86 passes the rule (0.85) but is below the 0.90 reliability threshold: the finding says so.
        var finding = Run(BlockBilling, FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.86m))).Single();

        finding.Value("line.observation").Confidence.Should().Be(0.86m, "the observation's own confidence, not the line's");
        finding.Value("line.observation").BelowReliabilityThreshold.Should().BeTrue();
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void AnObservationAtOrAboveTheReliabilityThreshold_IsNotFlagged()
    {
        var finding = Run(BlockBilling, FactsWith(Observed(InvoiceLineObservationType.BlockBilling, 0.90m))).Single();

        finding.Value("line.observation").BelowReliabilityThreshold.Should().BeFalse();
    }

    [Fact]
    public void TheFindingCarriesNoRejection_OnlyAFlagFact()
    {
        // I-5: a handler reports, it never decides. The finding type has no outcome or consequence member.
        typeof(BillingFinding).GetProperties().Select(p => p.Name).Should().NotContain(
            n => n.Contains("Reject", StringComparison.OrdinalIgnoreCase) || n.Contains("Consequence", StringComparison.OrdinalIgnoreCase)
                 || n.Contains("Outcome", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AMissingOrUnknownObservationKindOnTheRule_IsRefused_NotTreatedAsHolding()
    {
        var rule = Corrupt(Rule("""{"type":"AiObservation","observation":"blockBilling","minConfidence":0.85}"""), nameof(BillingRuleDefinition.Observation), "somethingElse");

        var act = () => BillingRuleHandlers.Evaluate(rule, Origin, FactsWith());

        act.Should().Throw<BillingRuleRefusedException>();
    }
}
