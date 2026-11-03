using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Sprk.Bff.Api.Services.Signals.Billing.Handlers;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 10 RequiredData (design.md 6.1; spec FR-21 currency). Maintain-class (ADR-038): pure logic, no
/// mocks. The default fact set is complete and in USD.
/// </summary>
public class RequiredDataHandlerTests
{
    private static BillingRuleDefinition Required(params string[] fields) =>
        Rule($$"""{"type":"RequiredData","fields":[{{string.Join(",", fields.Select(f => $"\"{f}\""))}}]}""");

    private static readonly string[] AllFields =
        ["firmInvoiceNumber", "invoiceDate", "billingPeriodStart", "billingPeriodEnd", "timekeeper", "lineDate", "narrative", "taskCode", "activityCode", "expenseCode"];

    [Fact]
    public void MustNotFire_WhenEveryListedFieldIsPresent_OnACompleteInvoice()
    {
        Run(Required(AllFields), Facts(Fee(0), Expense(1))).Should().BeEmpty();
    }

    // ── Header ───────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("firmInvoiceNumber")]
    [InlineData("invoiceDate")]
    [InlineData("billingPeriodStart")]
    [InlineData("billingPeriodEnd")]
    public void MustFire_OnTheInvoice_ForAMissingHeaderField(string field)
    {
        var invoice = field switch
        {
            "firmInvoiceNumber" => Invoice() with { FirmInvoiceNumber = null },
            "invoiceDate" => Invoice() with { InvoiceDate = null },
            "billingPeriodStart" => Invoice() with { BillingPeriodStart = null },
            _ => Invoice() with { BillingPeriodEnd = null },
        };

        var finding = Run(Required(field), Facts(Fee(0)) with { Invoice = invoice }).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.RequiredFieldMissing);
        finding.Subject.Should().Be(BillingSubject.ForInvoice());
        finding.FactValues.Should().BeEquivalentTo(new Dictionary<string, string> { ["missingField"] = field });
        finding.SuggestedAdjustment.Should().BeNull();
    }

    [Fact]
    public void AHeaderFieldTheRuleDoesNotList_IsNotChecked()
    {
        var facts = Facts(Fee(0)) with { Invoice = Invoice() with { BillingPeriodStart = null, InvoiceDate = null } };

        Run(Required("firmInvoiceNumber"), facts).Should().BeEmpty();
    }

    [Fact]
    public void ABlankInvoiceNumber_IsMissing()
    {
        Run(Required("firmInvoiceNumber"), Facts(Fee(0)) with { Invoice = Invoice() with { FirmInvoiceNumber = "   " } }).Should().ContainSingle();
    }

    // ── Lines ────────────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("timekeeper")]
    [InlineData("lineDate")]
    [InlineData("narrative")]
    [InlineData("taskCode")]
    [InlineData("activityCode")]
    public void MustFire_OnTheLine_ForAMissingFeeLineField(string field)
    {
        var line = field switch
        {
            "timekeeper" => Fee(3) with { TimekeeperName = null },
            "lineDate" => Fee(3) with { Date = null },
            "narrative" => Fee(3) with { Narrative = "  " },
            "taskCode" => Fee(3) with { BilledTaskCode = null },
            _ => Fee(3) with { BilledActivityCode = "" },
        };

        var finding = Run(Required(field), Facts(Fee(0), Fee(1), Fee(2), line)).Should().ContainSingle().Subject;

        finding.Subject.Should().Be(BillingSubject.ForLine(3));
        finding.FactValues["missingField"].Should().Be(field);
    }

    [Fact]
    public void MustFire_ForAnExpenseLineWithNoExpenseCode_AndNoOtherLineType()
    {
        var facts = Facts(Expense(0) with { BilledExpenseCode = null }, Fee(1) with { BilledExpenseCode = null });

        var finding = Run(Required("expenseCode"), facts).Should().ContainSingle().Subject;

        finding.Subject.Should().Be(BillingSubject.ForLine(0));
    }

    [Fact]
    public void FieldsThatOnlyApplyToFeeLines_AreNotRequiredOfExpenseLines()
    {
        var expense = Expense(0) with { TimekeeperName = null, BilledTaskCode = null, BilledActivityCode = null };

        Run(Required("timekeeper", "taskCode", "activityCode"), Facts(expense)).Should().BeEmpty();
    }

    [Fact]
    public void TheLineDateAndNarrative_AreRequiredOfEveryLineType()
    {
        var facts = Facts(Expense(0) with { Date = null, Narrative = null });

        Run(Required("lineDate", "narrative"), facts).Select(f => f.FactValues["missingField"]).Should().Equal("lineDate", "narrative");
    }

    [Fact]
    public void TheFirmsOwnCode_IsWhatIsRequired_NotTheAiAssignedOne()
    {
        // The rule asks what the invoice carries; an AI code does not make the firm's omission go away.
        var line = Fee(0) with { BilledTaskCode = null, TaskCode = "L110" };

        Run(Required("taskCode"), Facts(line)).Should().ContainSingle();
    }

    [Fact]
    public void SeveralMissingFields_GiveOneFindingEach_InAFixedOrder_WhateverTheOrderOfTheBody()
    {
        var facts = Facts(Fee(0) with { Date = null, Narrative = null, TimekeeperName = null }) with { Invoice = Invoice() with { InvoiceDate = null } };

        var findings = Run(Required("narrative", "timekeeper", "invoiceDate", "lineDate"), facts);

        findings.Select(f => f.FactValues["missingField"]).Should().Equal("invoiceDate", "timekeeper", "lineDate", "narrative");
    }

    [Fact]
    public void AMissingField_ReadsOnlyThatField_AndItsConfidenceIsWhateverWasKnown()
    {
        var line = Fee(2) with { Narrative = null, FieldConfidence = Confidence().With(BillingFieldNames.Narrative, null) };

        var finding = Run(Required("narrative"), Facts(line)).Single();

        finding.ReadFields.Should().Equal(new FieldRef(2, BillingFieldNames.Narrative));
        finding.ReadsLowConfidenceValue.Should().BeTrue("an absent value with no known confidence is not reliably absent (NFR-06)");
    }

    // ── FR-21: a currency other than USD is a finding, not a crash ──────────────────────────────────────────────

    [Theory]
    [InlineData("EUR")]
    [InlineData("GBP")]
    [InlineData("cad")]
    [InlineData("CA$")] // a dollar of another country: not USD
    public void MustFire_ForACurrencyOtherThanUsd_AsAFindingOnTheInvoice_FR21(string currency)
    {
        var facts = Facts(Fee(0)) with { Invoice = Invoice() with { Currency = currency } };

        var finding = Run(Required("firmInvoiceNumber"), facts).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.CurrencyNotUsd);
        finding.Subject.Should().Be(BillingSubject.ForInvoice());
        finding.FactValues["missingField"].Should().Be("currency");
        finding.ReadFields.Should().Equal(new FieldRef(null, BillingFieldNames.Currency));
        finding.Value("invoice.currency").Value.Should().Be(currency);
    }

    [Theory]
    [InlineData(0.50)]  // below the 0.90 floor
    [InlineData(null)]  // unknown counts as below the floor
    public void MustNotFire_ForANonUsdCurrencyReadBelowTheConfidenceFloor_D123(double? confidence)
    {
        // A "EUR" read with doubt may be a misread "USD"; "not USD" would say more than was tested. D-115's "unclear" reports it.
        var facts = Facts(Fee(0)) with
        {
            Invoice = Invoice() with { Currency = "EUR", FieldConfidence = Confidence().With(BillingFieldNames.Currency, (decimal?)confidence) },
        };

        Run(Required("narrative"), facts).Should().BeEmpty();
    }

    [Fact]
    public void MustFire_ForANonUsdCurrencyReadAtTheConfidenceFloor_D123()
    {
        // Control: the floor itself is reliable (the evaluator's test is confidence >= floor).
        var facts = Facts(Fee(0)) with
        {
            Invoice = Invoice() with { Currency = "EUR", FieldConfidence = Confidence().With(BillingFieldNames.Currency, Threshold) },
        };

        Run(Required("narrative"), facts).Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.CurrencyNotUsd);
    }

    [Fact]
    public void TheCurrencyCheck_DoesNotDependOnTheFieldsTheRuleLists()
    {
        var facts = Facts(Fee(0)) with { Invoice = Invoice() with { Currency = "EUR" } };

        Run(Required("narrative"), facts).Should().ContainSingle().Which.ReasonCode.Should().Be(BillingReasonCodes.CurrencyNotUsd);
    }

    [Theory]
    [InlineData("USD")]
    [InlineData("usd")]
    [InlineData(" Usd ")]
    [InlineData("$")] // D-119: a bare "$" is USD
    [InlineData(" $ ")]
    [InlineData("US$")] // 072 W review: unambiguous
    [InlineData("USD$")]
    [InlineData(null)] // not read: no evidence of a foreign currency
    [InlineData("")]
    public void MustNotFire_ForUsd_OrForACurrencyThatWasNotRead(string? currency)
    {
        var facts = Facts(Fee(0)) with { Invoice = Invoice() with { Currency = currency } };

        Run(Required("narrative"), facts).Should().BeEmpty();
    }

    [Fact]
    public void ANonUsdInvoice_GivesAFinding_NotAnException_EvenWhenEverythingElseIsMissing()
    {
        var facts = Facts(Fee(0) with { Date = null, TimekeeperName = null }) with
        {
            Invoice = Invoice() with { Currency = "JPY", FirmInvoiceNumber = null },
        };

        var act = () => Evaluate(Required(AllFields), facts);

        act.Should().NotThrow();
        act().Select(f => f.ReasonCode).Should().Contain(BillingReasonCodes.CurrencyNotUsd);
    }

    private static IReadOnlyList<BillingFinding> Evaluate(BillingRuleDefinition rule, BillingFactSet facts) =>
        BillingRuleHandlers.Evaluate(rule, Origin, facts);
}
