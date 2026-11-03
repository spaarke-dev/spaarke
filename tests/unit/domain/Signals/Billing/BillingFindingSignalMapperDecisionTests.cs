using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 049 part D (072 part W K1; notes 049 section 6 "Primary reason"): <see cref="BillingFindingSignalMapper.MapAll(IReadOnlyList{BillingFinding}, IReadOnlyDictionary{Guid, BillingRuleCandidate}, BillingSignalSubjects, IReadOnlyList{FindingDecision}, SubmissionEvaluationKind?)"/>
/// with 073's decisions. A group holding a deciding finding is titled by the first deciding finding in reason order; a Reject group that 073
/// held throughout is not presented as a rejection and carries a <c>billing.decision</c> Held line; the fingerprint never depends on them.
/// </summary>
public class BillingFindingSignalMapperDecisionTests
{
    private static readonly Guid InvoiceRowId = Guid.Parse("04900000-0000-4000-8000-0000000d00e1");
    private static readonly BillingRuleCandidate ExpenseReject =
        Version("BILL-EXP", RuleType.ExpenseRule, template: "Expense {{line.expenseCode}} is not allowed.") with { Consequence = (int)BillingConsequence.Reject, Severity = SignalWriter.SeverityCritical };

    private static readonly BillingSignalSubjects Subjects = new(InvoiceRowId, "INV-100", new Dictionary<int, Guid> { [0] = Id("line-row:0") });

    private static BillingFinding Finding(string reason, string fact) => new()
    {
        RuleType = RuleType.ExpenseRule,
        PolicyId = ExpenseReject.PolicyId,
        PolicyCode = ExpenseReject.PolicyCode,
        PolicyVersionId = ExpenseReject.PolicyVersionId,
        Subject = BillingSubject.ForLine(0),
        ReasonCode = reason,
        ValuesRead = [new BillingValueRead { Name = "line.expenseCode", Value = fact }],
        FactValues = new Dictionary<string, string> { ["line.expenseCode"] = fact },
    };

    // Code order: amount_over_cap < code_not_reimbursable, so without decisions the cap reason is primary.
    private static readonly BillingFinding OverCap = Finding(BillingReasonCodes.ExpenseAmountOverCap, "E101");
    private static readonly BillingFinding NotReimbursable = Finding(BillingReasonCodes.ExpenseCodeNotReimbursable, "E102");
    private static readonly IReadOnlyList<BillingFinding> Findings = [NotReimbursable, OverCap];
    private static readonly Dictionary<Guid, BillingRuleCandidate> Rules = new() { [ExpenseReject.PolicyVersionId] = ExpenseReject };

    private static FindingDecision Decision(int index, bool rejects, string? heldBecause = null) => new()
    {
        Index = index,
        Finding = Findings[index],
        Consequence = BillingConsequence.Reject,
        Rejects = rejects,
        HeldBecause = rejects ? null : heldBecause,
        ValuesRead = Findings[index].ValuesRead,
    };

    private static string Sentence(BillingSignalMapping mapping) => SignalWriter.RenderSentence(mapping.Request.MessageTemplate, mapping.Request.FactValues);

    [Fact(DisplayName = "049 D: with no decisions the primary reason is the first reject reason in code order (unchanged)")]
    public void NoDecisions_TheFirstRejectReasonByCode_IsPrimary()
    {
        var mapping = BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects).Single();

        Sentence(mapping).Should().Contain("E101");
        mapping.Decision.Should().Be(BillingGroupDecision.NotDecided);
        mapping.Request.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind);
    }

    [Fact(DisplayName = "049 D (072 W K1): the deciding finding titles the group, not the first reject reason by code")]
    public void ADecidingFinding_IsThePrimaryReason()
    {
        var undecided = BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects).Single();

        var mapping = BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects, [Decision(0, rejects: true), Decision(1, rejects: false, "read_field_below_threshold")]).Single();

        Sentence(mapping).Should().Contain("E102", "the reason that actually rejected");
        Sentence(mapping).Should().NotContain("E101");
        mapping.Decision.Should().Be(BillingGroupDecision.Rejects);
        mapping.Fingerprint.Should().Be(undecided.Fingerprint, "the lifecycle fingerprint does not depend on the decision");
        mapping.ReasonCodes.Should().Equal(undecided.ReasonCodes);
        mapping.Request.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind);
    }

    [Fact(DisplayName = "049 D: a Reject group 073 held throughout is not presented as a rejection: policy lane and severity, a Held line with the hold reason")]
    public void AHeldRejectGroup_CarriesTheHeldLine_AndKeepsThePolicysLaneAndSeverity()
    {
        var undecided = BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects).Single();

        var mapping = BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects,
            [Decision(0, rejects: false, "read_field_below_threshold"), Decision(1, rejects: false, "ai_assigned_input")]).Single();

        mapping.Decision.Should().Be(BillingGroupDecision.Held);
        mapping.Request.Lane.Should().Be(undecided.Request.Lane);
        mapping.Request.Severity.Should().Be(SignalWriter.SeverityCritical, "the policy's severity");
        Sentence(mapping).Should().Be(Sentence(undecided), "the sentence is the mapper's primary reason");
        var held = mapping.Request.EvidenceRefs!.Should().ContainSingle(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind).Subject;
        held.Ref.Should().Be(BillingFindingSignalMapper.HeldDecisionRef);
        held.Text.Should().Be("ai_assigned_input", "the primary (cap) reason's hold reason");
        mapping.Fingerprint.Should().Be(undecided.Fingerprint);
    }

    [Fact(DisplayName = "049 D, must-not-fire: a Flag group gets no Held line")]
    public void AFlagGroup_GetsNoHeldLine()
    {
        var flag = ExpenseReject with { Consequence = (int)BillingConsequence.Flag };
        var mapping = BillingFindingSignalMapper.MapAll(Findings, new Dictionary<Guid, BillingRuleCandidate> { [flag.PolicyVersionId] = flag }, Subjects,
            [Decision(0, rejects: false, "consequence_not_reject"), Decision(1, rejects: false, "consequence_not_reject")]).Single();

        mapping.Decision.Should().Be(BillingGroupDecision.NotRejecting);
        mapping.Request.EvidenceRefs!.Should().NotContain(e => e.Kind == BillingFindingSignalMapper.DecisionEvidenceKind);
    }

    [Fact(DisplayName = "049 D: decisions that do not match the findings are refused")]
    public void MismatchedDecisions_AreRefused()
    {
        var tooFew = () => BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects, [Decision(0, rejects: true)]);
        var swapped = () => BillingFindingSignalMapper.MapAll(Findings, Rules, Subjects,
            [Decision(0, rejects: true) with { Index = 1 }, Decision(1, rejects: false) with { Index = 0 }]);

        tooFew.Should().Throw<ArgumentException>();
        swapped.Should().Throw<ArgumentException>();
    }
}
