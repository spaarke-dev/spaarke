using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 1 ApprovedTimekeeper (spec FR-23, D-89). Maintain-class (ADR-038, <c>tests/unit/domain/**</c>):
/// pure logic over an in-memory fact set, no mocks. The roster match itself is task 046's; these tests pin what the rule does with
/// its outcome.
/// </summary>
public class ApprovedTimekeeperHandlerTests
{
    private static readonly BillingRuleDefinition Approved = Rule("""{"type":"ApprovedTimekeeper"}""");

    [Fact]
    public void MustNotFire_WhenTheTimekeeperIsMatchedToTheRoster()
    {
        Run(Approved, Facts(Fee(0), Fee(1))).Should().BeEmpty();
    }

    [Fact]
    public void MustFire_ForAnUnmatchedTimekeeper_OnThatLineOnly()
    {
        var facts = Facts(
            Fee(0),
            Fee(1) with { TimekeeperName = "J. Smythe", Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster) },
            Fee(2));

        var finding = Run(Approved, facts).Should().ContainSingle().Subject;

        finding.Subject.Should().Be(BillingSubject.ForLine(1));
        finding.ReasonCode.Should().Be(BillingReasonCodes.TimekeeperNotOnRoster);
        finding.SuggestedAdjustment.Should().BeNull("design 6.1 allows Reject or Flag only, no adjustment");
        finding.Value("line.timekeeperName").Value.Should().Be("J. Smythe");
        finding.FactValues.Should().Contain("line.timekeeperName", "J. Smythe").And.Contain("line.date", "2026-03-02");
    }

    [Theory]
    [InlineData(TimekeeperUnmatchedReason.NameMissing, BillingReasonCodes.TimekeeperNameMissing)]
    [InlineData(TimekeeperUnmatchedReason.LineDateMissing, BillingReasonCodes.TimekeeperLineDateMissing)]
    [InlineData(TimekeeperUnmatchedReason.NameIncomplete, BillingReasonCodes.TimekeeperNameIncomplete)]
    [InlineData(TimekeeperUnmatchedReason.NotOnRoster, BillingReasonCodes.TimekeeperNotOnRoster)]
    [InlineData(TimekeeperUnmatchedReason.NotApprovedOnLineDate, BillingReasonCodes.TimekeeperNotApprovedOnLineDate)]
    [InlineData(TimekeeperUnmatchedReason.Ambiguous, BillingReasonCodes.TimekeeperAmbiguous)]
    [InlineData(TimekeeperUnmatchedReason.AiBelowThreshold, BillingReasonCodes.TimekeeperAiBelowThreshold)]
    [InlineData(TimekeeperUnmatchedReason.AiNotClearlyAhead, BillingReasonCodes.TimekeeperAiNotClearlyAhead)]
    [InlineData(TimekeeperUnmatchedReason.AiUnavailable, BillingReasonCodes.TimekeeperAiUnavailable)]
    [InlineData(TimekeeperUnmatchedReason.None, BillingReasonCodes.TimekeeperUnmatched)]
    public void EveryUnmatchedReasonOf046_FiresWithItsOwnReasonCode(TimekeeperUnmatchedReason reason, string expectedCode)
    {
        var facts = Facts(Fee(0) with { Timekeeper = Unmatched(reason) });

        Run(Approved, facts).Should().ContainSingle().Which.ReasonCode.Should().Be(expectedCode);
    }

    [Fact]
    public void EveryReasonOf046_IsMappedExplicitly_SoANewReasonIsNotSilentlyGeneric()
    {
        var generic = Enum.GetValues<TimekeeperUnmatchedReason>()
            .Where(r => r != TimekeeperUnmatchedReason.None)
            .Where(r => Run(Approved, Facts(Fee(0) with { Timekeeper = Unmatched(r) })).Single().ReasonCode == BillingReasonCodes.TimekeeperUnmatched)
            .ToList();

        generic.Should().BeEmpty("a reason added to 046 needs its own code here, not the fallback");
    }

    [Fact]
    public void MustFire_ForAFeeLineWithNoResolutionAtAll_FailsClosed()
    {
        var facts = Facts(Fee(0) with { Timekeeper = null });

        Run(Approved, facts).Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.TimekeeperUnmatched);
    }

    [Fact]
    public void ExpenseLines_HaveNoTimekeeper_AndAreNotJudged()
    {
        Run(Approved, Facts(Expense(0))).Should().BeEmpty();
    }

    [Fact]
    public void AnUnmatchedTimekeeper_PassesThrough046sCappedConfidence_AndIsMarkedUnreliable_ThisIsNotTheD89Enforcement()
    {
        // What this proves, and what it does not. It proves the handler passes 046's 0.80 reference-missing cap through unchanged
        // and marks a value below the 0.90 reliability threshold as unreliable (D-47). It does NOT prove D-89 ("ApprovedTimekeeper
        // holds, never auto-rejects"): the handler decides no rejection (I-5), and D-89 holds today only because the cap
        // (0.80) happens to be below the threshold (0.90). Both are settings. Enforcing D-89 independent of that coincidence is
        // task 073: it refuses a Reject consequence for ApprovedTimekeeper at publish, treats ApprovedTimekeeper findings as
        // hold-only in the rejection rule, and validates ReferenceMissingCap < ReliabilityThreshold when the settings are saved.
        var capped = Fee(0) with
        {
            Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster),
            FieldConfidence = Confidence().With(BillingFieldNames.TimekeeperAsBilled, 0.80m),
        };

        var finding = Run(Approved, Facts(capped)).Should().ContainSingle().Subject;

        var read = finding.Value("line.timekeeperName");
        read.Confidence.Should().Be(0.80m, "the handler reports the confidence it was given, it does not recompute it");
        read.BelowReliabilityThreshold.Should().BeTrue();
        finding.ReadsLowConfidenceValue.Should().BeTrue("task 073 will therefore hold the invoice for review instead of rejecting it");
    }

    [Fact]
    public void ReadFields_AreTheTimekeeperAsBilledAndTheLineDate()
    {
        var facts = Facts(Fee(3) with { Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster) });

        Run(Approved, facts).Single().ReadFields.Should().Equal(
            new FieldRef(3, BillingFieldNames.TimekeeperAsBilled), new FieldRef(3, BillingFieldNames.Date));
    }

    [Fact]
    public void FactValues_AreExactlyTheTemplateFieldsOfTheType()
    {
        var facts = Facts(Fee(0) with { Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster) });

        Run(Approved, facts).Single().FactValues.Keys.Should().BeEquivalentTo(
            BillingRuleCatalogue.TemplateFactFields(RuleType.ApprovedTimekeeper));
    }

    [Fact]
    public void FindingsComeInInvoiceOrder()
    {
        var facts = Facts(
            Fee(0) with { Timekeeper = Unmatched(TimekeeperUnmatchedReason.NotOnRoster) },
            Fee(1),
            Fee(2) with { Timekeeper = Unmatched(TimekeeperUnmatchedReason.Ambiguous) });

        Run(Approved, facts).Select(f => f.Subject.LineIndex).Should().Equal(0, 2);
    }
}
