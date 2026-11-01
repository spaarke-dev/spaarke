using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 049, Held presentation: <see cref="BillingFindingSignalMapper.MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>
/// with the Blocked outcome. A group holding a deciding finding is presented as held for review: the headline, the sentence "The rejection rule
/// would reject this invoice: &lt;the deciding reason&gt;" and a <c>billing.decision</c> / Held evidence line carrying
/// <see cref="SubmissionReasonCodes.Blocked"/>. The deciding reason that would not fit the writer's sentence limit is left out whole (the
/// no-value wording), never cut.
/// </summary>
public class BillingFindingSignalMapperHeldTests
{
    private const string HeldPrefix = "The rejection rule would reject this invoice: ";
    private const int SentenceLimit = 2000;
    private static readonly Guid InvoiceRowId = Guid.Parse("04900000-0000-4000-8000-0000000d00f1");
    private static readonly BillingSignalSubjects Subjects = new(InvoiceRowId, "INV-100", new Dictionary<int, Guid> { [0] = Id("line-row:held:0") });

    private static BillingRuleCandidate ExpenseReject(string? template = "Expense {{line.expenseCode}} is not allowed.") =>
        Version("BILL-EXP", RuleType.ExpenseRule, template: template) with { Consequence = (int)BillingConsequence.Reject, Severity = SignalWriter.SeverityCritical };

    private static BillingFinding ExpenseFinding(BillingRuleCandidate rule, string reason, string code) => new()
    {
        RuleType = RuleType.ExpenseRule,
        PolicyId = rule.PolicyId,
        PolicyCode = rule.PolicyCode,
        PolicyVersionId = rule.PolicyVersionId,
        Subject = BillingSubject.ForLine(0),
        ReasonCode = reason,
        ValuesRead = [new BillingValueRead { Name = "line.expenseCode", Value = code }],
        FactValues = new Dictionary<string, string> { ["line.expenseCode"] = code },
    };

    private static FindingDecision Decide(int index, BillingFinding finding, bool rejects, string? heldBecause = null) => new()
    {
        Index = index,
        Finding = finding,
        Consequence = BillingConsequence.Reject,
        Rejects = rejects,
        HeldBecause = rejects ? null : heldBecause,
        ValuesRead = finding.ValuesRead,
    };

    private static string Sentence(BillingSignalMapping mapping) => SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);

    /// <summary>One rejecting expense finding; the group's mapping without and with the Blocked outcome.</summary>
    private static (BillingSignalMapping Plain, BillingSignalMapping Held) OneDecidingFinding(string? template = "Expense {{line.expenseCode}} is not allowed.")
    {
        var rule = ExpenseReject(template);
        var finding = ExpenseFinding(rule, BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };
        IReadOnlyList<FindingDecision> decisions = [Decide(0, finding, rejects: true)];
        return (
            BillingFindingSignalMapper.MapAll([finding], rules, Subjects, decisions, SubmissionEvaluationKind.AutoRejected).Single(),
            BillingFindingSignalMapper.MapAll([finding], rules, Subjects, decisions, SubmissionEvaluationKind.Blocked).Single());
    }

    [Fact(DisplayName = "049 Held: a Blocked outcome presents a deciding group as held: headline, the reason in the sentence, a Held line with the blocked code")]
    public void Blocked_DecidingGroup_IsPresentedAsHeld()
    {
        var (plain, held) = OneDecidingFinding();

        held.Request.ShortHeadline.Should().Be("Invoice held for review").And.Be(BillingFindingSignalMapper.HeldHeadline);
        Sentence(held).Should().Be("The rejection rule would reject this invoice: Expense E102 is not allowed.");
        Sentence(held).Should().Be(HeldPrefix + Sentence(plain), "the deciding reason is the existing rendered reason");
        Sentence(held).Should().NotContain(BillingReasonCodes.ExpenseCodeNotReimbursable, "never a raw code").And.NotContain(SubmissionReasonCodes.Blocked);
        held.MessageSource.Should().Be(BillingMessageSource.HeldForReview);
        held.Decision.Should().Be(BillingGroupDecision.Blocked);

        var line = held.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Subject;
        line.Ref.Should().Be(BillingFindingSignalMapper.HeldDecisionRef);
        line.Text.Should().Be("ebilling.submission.blocked").And.Be(SubmissionReasonCodes.Blocked);

        // What does not change: the lifecycle fingerprint, the policy's lane and severity, the reasons and the values read.
        held.Fingerprint.Should().Be(plain.Fingerprint);
        held.Request.Lane.Should().Be(plain.Request.Lane);
        held.Request.Severity.Should().Be(SignalWriter.SeverityCritical);
        held.ReasonCodes.Should().Equal(plain.ReasonCodes);
        held.Request.EvidenceRefs!.Where(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind).Select(e => (e.Ref, e.Text))
            .Should().Equal(plain.Request.EvidenceRefs!.Where(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind).Select(e => (e.Ref, e.Text)));
        SignalWriter.PreflightRefusal(held.Request).Should().BeNull("the persisted plan keeps it");
    }

    [Fact(DisplayName = "049 Held, must-not-fire: an AutoRejected outcome, no outcome and an Accepted outcome keep the rejection presentation")]
    public void OtherOutcomes_KeepTheRejectionPresentation()
    {
        var rule = ExpenseReject();
        var finding = ExpenseFinding(rule, BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };
        IReadOnlyList<FindingDecision> decisions = [Decide(0, finding, rejects: true)];

        foreach (SubmissionEvaluationKind? outcome in new SubmissionEvaluationKind?[] { null, SubmissionEvaluationKind.AutoRejected, SubmissionEvaluationKind.Accepted, SubmissionEvaluationKind.AcceptedUncoded })
        {
            var mapping = BillingFindingSignalMapper.MapAll([finding], rules, Subjects, decisions, outcome).Single();

            mapping.Request.ShortHeadline.Should().NotBe(BillingFindingSignalMapper.HeldHeadline, $"{outcome}");
            Sentence(mapping).Should().Be("Expense E102 is not allowed.", $"{outcome}");
            mapping.Decision.Should().Be(BillingGroupDecision.Rejects, $"{outcome}");
            mapping.Request.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind, $"{outcome}");
        }
    }

    [Fact(DisplayName = "049 Held: under a Blocked outcome a group 073 held keeps its own Held line (the hold reason), and a Flag group is not touched")]
    public void Blocked_HeldAndFlagGroups_AreNotPresentedAsBlocked()
    {
        var rule = ExpenseReject();
        var finding = ExpenseFinding(rule, BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };

        var held = BillingFindingSignalMapper.MapAll([finding], rules, Subjects,
            [Decide(0, finding, rejects: false, "read_field_below_threshold")], SubmissionEvaluationKind.Blocked).Single();

        held.Decision.Should().Be(BillingGroupDecision.Held);
        held.Request.ShortHeadline.Should().NotBe(BillingFindingSignalMapper.HeldHeadline);
        Sentence(held).Should().Be("Expense E102 is not allowed.");
        held.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Which.Text
            .Should().Be("read_field_below_threshold");

        var flag = rule with { Consequence = (int)BillingConsequence.Flag };
        var flagged = BillingFindingSignalMapper.MapAll([finding], new Dictionary<Guid, BillingRuleCandidate> { [flag.PolicyVersionId] = flag }, Subjects,
            [Decide(0, finding, rejects: false, "consequence_not_reject")], SubmissionEvaluationKind.Blocked).Single();

        flagged.Decision.Should().Be(BillingGroupDecision.NotRejecting);
        flagged.Request.ShortHeadline.Should().NotBe(BillingFindingSignalMapper.HeldHeadline);
        flagged.Request.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind);
    }

    [Fact(DisplayName = "049 Held: the deciding finding's reason is the sentence's reason, not another reason of the group")]
    public void Blocked_TheDecidingReason_IsTheOneInTheSentence()
    {
        var rule = ExpenseReject();
        var overCap = ExpenseFinding(rule, BillingReasonCodes.ExpenseAmountOverCap, "E101");
        var notReimbursable = ExpenseFinding(rule, BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");
        var rules = new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule };

        // Code order puts the cap reason first; 073 rejected on the other one.
        var mapping = BillingFindingSignalMapper.MapAll([notReimbursable, overCap], rules, Subjects,
            [Decide(0, notReimbursable, rejects: true), Decide(1, overCap, rejects: false, "read_field_below_threshold")], SubmissionEvaluationKind.Blocked).Single();

        Sentence(mapping).Should().Be(HeldPrefix + "Expense E102 is not allowed.");
        mapping.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Which.Text.Should().Be(SubmissionReasonCodes.Blocked);
    }

    [Fact(DisplayName = "049 Held: a withheld Duplicate decides under a Blocked outcome without any value (D-112)")]
    public void Blocked_WithheldDuplicate_CarriesNoValue()
    {
        var rule = Version("BILL-DUP", RuleType.Duplicate) with { Consequence = (int)BillingConsequence.Reject };
        var finding = new BillingFinding
        {
            RuleType = RuleType.Duplicate,
            PolicyId = rule.PolicyId,
            PolicyCode = rule.PolicyCode,
            PolicyVersionId = rule.PolicyVersionId,
            Subject = BillingSubject.ForLine(0),
            ReasonCode = BillingReasonCodes.DuplicateOfPriorSubmissionLine,
            ValuesRead = [],
            FactValues = new Dictionary<string, string>(),
            DetailWithheld = true,
        };

        var mapping = BillingFindingSignalMapper.MapAll([finding], new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, Subjects,
            [Decide(0, finding, rejects: true)], SubmissionEvaluationKind.Blocked).Single();

        Sentence(mapping).Should().Be(HeldPrefix + BillingRuleCatalogue.WithheldDuplicateTemplate);
        mapping.Request.FactValues.Keys.Should().BeEquivalentTo([BillingFindingSignalMapper.HeldReasonFact]);
        mapping.Decision.Should().Be(BillingGroupDecision.Blocked);
    }

    [Fact(DisplayName = "049 Held: the limits are inclusive: a reason that fills the sentence exactly is cited; one character more falls back to the no-value wording, never cut")]
    public void Blocked_SentenceLimit_FallsBackToNoValue_NeverTruncates()
    {
        var room = SentenceLimit - HeldPrefix.Length;

        var (plainFits, fits) = OneDecidingFinding(new string('a', room));
        Sentence(fits).Should().Be(HeldPrefix + new string('a', room), "exactly 2,000 characters is within the limit");
        Sentence(fits).Length.Should().Be(SentenceLimit);
        fits.MessageSource.Should().Be(BillingMessageSource.HeldForReview);
        SignalWriter.PreflightRefusal(fits.Request).Should().BeNull();
        SignalWriter.PreflightRefusal(plainFits.Request).Should().BeNull();

        var (plainOver, over) = OneDecidingFinding(new string('a', room + 1));
        SignalWriter.PreflightRefusal(plainOver.Request).Should().BeNull("the reason alone fits; only the held sentence would not");
        Sentence(over).Should().Be("The rejection rule would reject this invoice.").And.Be(BillingFindingSignalMapper.HeldSentenceNoValue);
        Sentence(over).Should().NotContain("aaa", "the reason is left out whole, not cut");
        over.Request.ShortHeadline.Should().Be(BillingFindingSignalMapper.HeldHeadline);
        over.Decision.Should().Be(BillingGroupDecision.Blocked);
        over.MessageSource.Should().Be(BillingMessageSource.HeldForReview);
        SignalWriter.PreflightRefusal(over.Request).Should().BeNull("it still passes the writer's checks, so the persisted plan keeps the Signal");
        over.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Which.Text.Should().Be(SubmissionReasonCodes.Blocked);
        over.Request.EvidenceRefs!.Should().Contain(e => e.Kind == BillingFindingSignalMapper.ReasonEvidenceKind && e.Text == new string('a', room + 1),
            "the reason stays in its own evidence line");
    }

    [Fact(DisplayName = "049 Held: the held headline is within the writer's 200-character limit")]
    public void TheHeldHeadline_FitsTheWritersLimit() => BillingFindingSignalMapper.HeldHeadline.Length.Should().BeLessThanOrEqualTo(200);

    [Fact(DisplayName = "049 Held: a Blocked outcome without the rejection rule's decisions is refused, not silently presented as a rejection")]
    public void Blocked_WithoutDecisions_IsRefused()
    {
        var rule = ExpenseReject();
        var finding = ExpenseFinding(rule, BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");

        var act = () => BillingFindingSignalMapper.MapAll([finding], new Dictionary<Guid, BillingRuleCandidate> { [rule.PolicyVersionId] = rule }, Subjects,
            decisions: null, outcome: SubmissionEvaluationKind.Blocked);

        act.Should().Throw<ArgumentException>().WithParameterName("decisions");
    }
}
