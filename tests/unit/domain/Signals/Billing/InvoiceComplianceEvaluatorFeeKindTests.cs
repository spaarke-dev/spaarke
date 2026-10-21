using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 115 follow-up (owner, 2026-10-21): the D-39 replacement key for AddedFee is the rule type plus its fee kind, so a
/// firm cap on one fee kind replaces only the client default's cap on that kind. Every other rule type keeps the rule type alone.
/// ontology-r1 F3 (option a): for the kind <c>other</c> the key also carries the exact fee-code set, so a firm courier cap never
/// displaces the client default's postage cap.
/// </summary>
public class InvoiceComplianceEvaluatorFeeKindTests
{
    private static BillingRuleCandidate Cap(string code, string feeKind, string feeCode, RuleSetScope scope) =>
        Version(code, RuleType.AddedFee,
            $$"""{"type":"AddedFee","feeKind":"{{feeKind}}","allowance":"cap","feeCodes":["{{feeCode}}"],"capAmount":100}""", scope);

    /// <summary>A late fee (E120) and an administrative fee (E130), each 150 against a cap of 100 wherever a rule caps it.</summary>
    private static BillingEvaluationRequest OverCapInvoice() =>
        Request(Fee(0), Expense(1) with { BilledExpenseCode = "E120", Amount = 150m }, Expense(2) with { BilledExpenseCode = "E130", Amount = 150m });

    private static IEnumerable<string> OverCapPolicies(BillingEvaluationResult result) =>
        result.Findings.Where(f => f.ReasonCode == BillingReasonCodes.AddedFeeOverCap).Select(f => f.PolicyCode).Order(StringComparer.Ordinal);

    private static BillingRuleCandidate OtherCap(string policyCode, RuleSetScope scope, params string[] feeCodes) =>
        Version(policyCode, RuleType.AddedFee,
            "{\"type\":\"AddedFee\",\"feeKind\":\"other\",\"allowance\":\"cap\",\"feeCodes\":[" + string.Join(',', feeCodes.Select(c => $"\"{c}\"")) + "],\"capAmount\":100}",
            scope);

    /// <summary>Courier (E107) and postage (E108) lines, each 150, against a cap of 100 wherever a rule caps it.</summary>
    private static BillingEvaluationRequest CourierAndPostageInvoice() =>
        Request(Fee(0), Expense(1) with { BilledExpenseCode = "E107", Amount = 150m }, Expense(2) with { BilledExpenseCode = "E108", Amount = 150m });

    private static (InvoiceComplianceEvaluator Evaluator, List<string> Ran) RecordingEvaluator()
    {
        var ran = new List<string>();
        var evaluator = Evaluator(handlers: (rule, origin, facts) =>
        {
            ran.Add(rule.PolicyCode);
            return BillingRuleHandlers.Evaluate(rule, origin, facts);
        });
        return (evaluator, ran);
    }

    [Fact]
    public void AFirmCapOnOneFeeKind_DoesNotRemoveTheClientDefaultsCapOnAnotherKind_D39FeeKind()
    {
        var clientLate = Cap("CD-LATE", "lateFee", "E120", RuleSetScope.ClientDefault);
        var firmAdmin = Cap("FIRM-ADMIN", "administrativeFee", "E130", RuleSetScope.Firm);

        var result = Evaluator().Evaluate(OverCapInvoice(), Loaded(clientLate, firmAdmin));

        OverCapPolicies(result).Should().Equal("CD-LATE", "FIRM-ADMIN");
    }

    [Fact]
    public void AFirmCapOnAFeeKind_ReplacesOnlyTheClientDefaultsCapOnThatKind_D39FeeKind()
    {
        var clientLate = Cap("CD-LATE", "lateFee", "E120", RuleSetScope.ClientDefault);
        var clientAdmin = Cap("CD-ADMIN", "administrativeFee", "E130", RuleSetScope.ClientDefault);
        var firmLate = Cap("FIRM-LATE", "lateFee", "E120", RuleSetScope.Firm);
        var (evaluator, ran) = RecordingEvaluator();

        var result = evaluator.Evaluate(OverCapInvoice(), Loaded(clientLate, clientAdmin, firmLate));

        ran.Should().BeEquivalentTo(["CD-ADMIN", "FIRM-LATE"], "the client default's late-fee cap is displaced and never judged");
        OverCapPolicies(result).Should().Equal("CD-ADMIN", "FIRM-LATE");
    }

    [Fact]
    public void AFirmCapOnTheSameFeeKind_ReplacesTheClientDefaultsCap_D39FeeKind()
    {
        var clientLate = Cap("CD-LATE", "lateFee", "E120", RuleSetScope.ClientDefault);
        var firmLate = Cap("FIRM-LATE", "lateFee", "E120", RuleSetScope.Firm);

        OverCapPolicies(Evaluator().Evaluate(OverCapInvoice(), Loaded(clientLate))).Should().Equal("CD-LATE");
        OverCapPolicies(Evaluator().Evaluate(OverCapInvoice(), Loaded(clientLate, firmLate))).Should().Equal("FIRM-LATE");
    }

    [Theory]
    [InlineData("\"feeKind\":\"LateFee\",")]
    [InlineData("\"feeKind\":\" lateFee\",")]
    [InlineData("\"feeKind\":\"\",")]
    [InlineData("\"feeKind\":\"  \",")]
    [InlineData("")]
    public void AFeeKindOutsideTheClosedVocabulary_NeverReachesThePrecedenceKey_TheGateRefusesIt_AndTheClientDefaultStays(string feeKindMember)
    {
        // The fee kind is a closed vocabulary matched exactly (schema enum; the handler's FeeKindLabel is an exact switch): there is
        // no trim or case folding to mirror, and a differently-cased, blank or missing kind is refused before a definition exists.
        // So a blank kind cannot displace anything: the client default stays in force.
        var clientLate = Cap("CD-LATE", "lateFee", "E120", RuleSetScope.ClientDefault);
        var firm = Version("FIRM-FEE", RuleType.AddedFee,
            "{\"type\":\"AddedFee\"," + feeKindMember + "\"allowance\":\"cap\",\"feeCodes\":[\"E120\"],\"capAmount\":100}", RuleSetScope.Firm);

        var result = Evaluator().Evaluate(OverCapInvoice(), Loaded(clientLate, firm));

        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            firm.PolicyId, "FIRM-FEE", firm.PolicyVersionId, BillingEvaluationReasons.GateRefused));
        OverCapPolicies(result).Should().Equal("CD-LATE");
    }

    [Fact]
    public void AFirmOtherCapOnCourier_DoesNotDisplaceTheClientDefaultsOtherCapOnPostage_F3()
    {
        var clientPostage = OtherCap("CD-POSTAGE", RuleSetScope.ClientDefault, "E108");
        var firmCourier = OtherCap("FIRM-COURIER", RuleSetScope.Firm, "E107");
        var (evaluator, ran) = RecordingEvaluator();

        var result = evaluator.Evaluate(CourierAndPostageInvoice(), Loaded(clientPostage, firmCourier));

        ran.Should().BeEquivalentTo(["CD-POSTAGE", "FIRM-COURIER"]);
        OverCapPolicies(result).Should().Equal("CD-POSTAGE", "FIRM-COURIER");
    }

    [Theory]
    [InlineData("E107", "E107")]
    [InlineData("E107,E108", "E108,E107")] // order does not count
    [InlineData("E107,E108", "e108,E107")] // the handler matches codes without regard to case
    [InlineData("E107", "e107,E107")] // case-only duplicates pass the schema's uniqueItems and collapse to one code
    public void AFirmOtherCapOnTheSameCodeSet_ReplacesTheClientDefaultsOtherCap_F3(string defaultCodes, string firmCodes)
    {
        var clientDefault = OtherCap("CD-OTHER", RuleSetScope.ClientDefault, defaultCodes.Split(','));
        var firm = OtherCap("FIRM-OTHER", RuleSetScope.Firm, firmCodes.Split(','));
        var (evaluator, ran) = RecordingEvaluator();

        evaluator.Evaluate(CourierAndPostageInvoice(), Loaded(clientDefault, firm));

        ran.Should().Equal("FIRM-OTHER");
    }

    [Theory]
    [InlineData("E107", "E107,E108")]
    [InlineData("E107,E108", "E107")]
    public void AnOverlappingButUnequalCodeSet_IsADifferentKey_BothCapsStayEffective_ExactSetNotSubset_F3(string defaultCodes, string firmCodes)
    {
        // Intended (owner option a): replacement is exact-set, not subset. A cap on {A,B} is a different fee from a cap on {A}.
        var clientDefault = OtherCap("CD-OTHER", RuleSetScope.ClientDefault, defaultCodes.Split(','));
        var firm = OtherCap("FIRM-OTHER", RuleSetScope.Firm, firmCodes.Split(','));
        var (evaluator, ran) = RecordingEvaluator();

        evaluator.Evaluate(CourierAndPostageInvoice(), Loaded(clientDefault, firm));

        ran.Should().BeEquivalentTo(["CD-OTHER", "FIRM-OTHER"]);
    }

    [Fact]
    public void AFirmOtherCapWithNoFeeCodes_IsRefusedByTheGate_AndTheClientDefaultStays_F3()
    {
        // The schema requires at least one fee code on a cap, so an empty set never reaches a definition (and never gets a key).
        var clientPostage = OtherCap("CD-POSTAGE", RuleSetScope.ClientDefault, "E108");
        var firm = Version("FIRM-NOCODES", RuleType.AddedFee,
            """{"type":"AddedFee","feeKind":"other","allowance":"cap","feeCodes":[],"capAmount":100}""", RuleSetScope.Firm);

        var result = Evaluator().Evaluate(CourierAndPostageInvoice(), Loaded(clientPostage, firm));

        result.Refusals.Should().ContainSingle().Which.Should().Be(new BillingRuleRefusal(
            firm.PolicyId, "FIRM-NOCODES", firm.PolicyVersionId, BillingEvaluationReasons.GateRefused));
        OverCapPolicies(result).Should().Equal("CD-POSTAGE");
    }

    [Fact]
    public void ADiscountRule_StillReplacesByRuleTypeAlone_RegressionControl_D39()
    {
        var clientDiscount = Version("CD-DISCOUNT", RuleType.Discount);
        var firmDiscount = Version("FIRM-DISCOUNT", RuleType.Discount, """{"type":"Discount","discountPercent":20}""", RuleSetScope.Firm);
        var (evaluator, ran) = RecordingEvaluator();

        evaluator.Evaluate(OverCapInvoice(), Loaded(clientDiscount, firmDiscount));

        ran.Should().Equal("FIRM-DISCOUNT");
    }

    [Fact]
    public void AnApprovedRateRule_StillReplacesByRuleTypeAlone_RegressionControl_D39()
    {
        var (evaluator, ran) = RecordingEvaluator();

        evaluator.Evaluate(OverCapInvoice(), Loaded(Version("CD-RATE", RuleType.ApprovedRate), Version("FIRM-RATE", RuleType.ApprovedRate, scope: RuleSetScope.Firm)));

        ran.Should().Equal("FIRM-RATE");
    }
}
