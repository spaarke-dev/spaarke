using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 7 InvoiceAge (design.md 6.1). Maintain-class (ADR-038): pure logic, no mocks. The invoice date
/// in the default fact set is 2026-03-31.
/// </summary>
public class InvoiceAgeHandlerTests
{
    private static readonly BillingRuleDefinition Age90 = Rule("""{"type":"InvoiceAge","maxAgeDays":90}""");

    private static BillingFactSet FactsDated(params DateOnly?[] lineDates) =>
        Facts(lineDates.Select((d, i) => Fee(i) with { Date = d }).ToArray());

    [Fact]
    public void MustFire_WhenALineIsDatedMoreThanNDaysBeforeTheInvoice()
    {
        var oldLine = InvoiceDate.AddDays(-91);

        var finding = Run(Age90, FactsDated(oldLine)).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.InvoiceTooOld);
        finding.Subject.Should().Be(BillingSubject.ForLine(0));
        finding.SuggestedAdjustment.Should().BeNull();
        finding.FactValues.Should().Contain("line.date", oldLine.ToString("yyyy-MM-dd")).And.Contain("invoice.date", "2026-03-31").And.Contain("rule.maxAgeDays", "90");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.InvoiceAge));
    }

    [Fact]
    public void MustNotFire_ForALineDatedExactlyNDaysBeforeTheInvoice()
    {
        Run(Age90, FactsDated(InvoiceDate.AddDays(-90))).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)] // dated after the invoice date: not old
    [InlineData(1)]
    [InlineData(89)]
    public void MustNotFire_ForARecentOrFutureDatedLine(int daysBeforeInvoice)
    {
        Run(Age90, FactsDated(InvoiceDate.AddDays(-daysBeforeInvoice))).Should().BeEmpty();
    }

    [Fact]
    public void OneDayPastTheEdge_Fires_AcrossAMonthAndAYearBoundary()
    {
        var yearEndLine = new DateOnly(2025, 12, 30);
        var facts = Facts(Fee(0) with { Date = yearEndLine }) with
        {
            Invoice = Invoice() with { InvoiceDate = yearEndLine.AddDays(91) },
        };

        Run(Age90, facts).Should().ContainSingle();
    }

    [Fact]
    public void WithNoInvoiceDate_NothingIsJudged()
    {
        var facts = Facts(Fee(0) with { Date = new DateOnly(2020, 1, 1) }) with { Invoice = Invoice() with { InvoiceDate = null } };

        Run(Age90, facts).Should().BeEmpty();
    }

    [Fact]
    public void ALineWithNoDate_IsNotJudged_AndEachLineIsJudgedOnItsOwnDate()
    {
        var findings = Run(Age90, FactsDated(null, InvoiceDate.AddDays(-200), InvoiceDate.AddDays(-10), InvoiceDate.AddDays(-91)));

        findings.Select(f => f.Subject.LineIndex).Should().Equal(1, 3);
    }

    // ── D-105: a reversal legitimately carries the old date ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("-1.0", "575.00")] // negative hours
    [InlineData("1.0", "-575.00")] // negative amount
    [InlineData("-1.0", "-575.00")]
    public void MustNotFire_ForACreditLineDatedFarBeforeTheInvoice_D105(string hours, string amount)
    {
        var credit = Fee(0) with { Date = InvoiceDate.AddDays(-400), Hours = D(hours), Amount = D(amount) };

        Run(Age90, Facts(credit)).Should().BeEmpty("a reversal carries the date of the charge it reverses");
    }

    [Fact]
    public void MustNotFire_ForAnExpenseCreditDatedFarBeforeTheInvoice_D105()
    {
        Run(Age90, Facts(Expense(0) with { Date = InvoiceDate.AddDays(-400), Amount = -40m })).Should().BeEmpty();
    }

    [Fact]
    public void MustFire_ForTheOldChargeLinesAroundACredit_TheCreditIsSkippedAndTheRestAreStillJudged_D105()
    {
        var old = InvoiceDate.AddDays(-400);
        var facts = Facts(Fee(0) with { Date = old }, Fee(1) with { Date = old, Hours = -1m }, Fee(2) with { Date = old });

        Run(Age90, facts).Select(f => f.Subject.LineIndex).Should().Equal(0, 2);
    }

    [Fact]
    public void ExpenseLines_AreJudgedToo()
    {
        Run(Age90, Facts(Expense(0) with { Date = InvoiceDate.AddDays(-120) })).Should().ContainSingle();
    }

    [Fact]
    public void ReadFields_AreTheLineDateAndTheInvoiceDate_ThenTheHoursAndAmountTheCreditTestRead()
    {
        Run(Age90, Facts(Fee(2) with { Date = InvoiceDate.AddDays(-120) })).Single().ReadFields.Should().Equal(
            new FieldRef(2, BillingFieldNames.Date), new FieldRef(null, BillingFieldNames.InvoiceDate),
            new FieldRef(2, BillingFieldNames.Hours), new FieldRef(2, BillingFieldNames.Amount));
    }

    [Fact]
    public void AReversalExtractedAsAPositiveAmountAtLowConfidence_IsJudgedAsACharge_ButTheFindingCarriesTheDoubtfulAmount_F4()
    {
        // A reversal printed "(1,437.50)" and extracted as +1437.50 at 0.50: it is not recognised as a credit, so InvoiceAge fires on
        // its (reliable) date. The finding must list the amount and its confidence, so task 073 does not auto-reject on it.
        var line = Expense(0) with
        {
            Date = InvoiceDate.AddDays(-120),
            Amount = 1437.50m,
            FieldConfidence = Confidence().With(BillingFieldNames.Amount, 0.50m),
        };

        var finding = Run(Age90, Facts(line)).Single();

        finding.ReadFields.Should().Contain(new FieldRef(0, BillingFieldNames.Amount));
        finding.Value("line.amount").Confidence.Should().Be(0.50m);
        finding.ReadsLowConfidenceValue.Should().BeTrue();
    }

    [Fact]
    public void ALowConfidenceInvoiceDate_IsFlagged()
    {
        var facts = Facts(Fee(0) with { Date = InvoiceDate.AddDays(-120) }) with
        {
            Invoice = Invoice() with { FieldConfidence = Confidence().With(BillingFieldNames.InvoiceDate, 0.5m) },
        };

        Run(Age90, facts).Single().Value("invoice.date").BelowReliabilityThreshold.Should().BeTrue();
    }
}
