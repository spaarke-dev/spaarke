using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Resolution;

/// <summary>
/// <see cref="ArithmeticChecker"/> (ebilling-r1 task 046, FR-23, D-47): the arithmetic signal. A failed check marks every field it involved,
/// because arithmetic cannot say which value is wrong.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the checks decide which fields are capped below the reliability threshold, so a check that never fires, or
/// fires on a rounding difference, changes which invoices a person must review.
/// </remarks>
public sealed class ArithmeticCheckerTests
{
    private static InvoiceResolutionLine Fee(decimal? hours, decimal? rate, decimal? amount) => new()
    {
        LineType = InvoiceLineType.Fee,
        Hours = new ResolutionField<decimal?>(hours),
        Rate = new ResolutionField<decimal?>(rate),
        Amount = new ResolutionField<decimal?>(amount),
    };

    private static InvoiceResolutionLine Expense(decimal? quantity, decimal? unitCost, decimal? amount) => new()
    {
        LineType = InvoiceLineType.Expense,
        Quantity = new ResolutionField<decimal?>(quantity),
        UnitCost = new ResolutionField<decimal?>(unitCost),
        Amount = new ResolutionField<decimal?>(amount),
    };

    private static InvoiceResolutionInput Invoice(
        IReadOnlyList<InvoiceResolutionLine> lines,
        decimal? fees = null,
        decimal? expenses = null,
        decimal? adjustments = null,
        decimal? totalDue = null) => new()
    {
        FirmId = Guid.NewGuid(),
        Header = new InvoiceResolutionHeader
        {
            FeesTotal = new ResolutionField<decimal?>(fees),
            ExpensesTotal = new ResolutionField<decimal?>(expenses),
            AdjustmentsTotal = new ResolutionField<decimal?>(adjustments),
            TotalDue = new ResolutionField<decimal?>(totalDue),
        },
        Lines = lines,
    };

    private static ArithmeticOutcome Outcome(ArithmeticReport report, int? line, string field) => report.OutcomeOf(line, field);

    [Fact]
    public void LineProduct_Matching_PassesAllThreeFields()
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(2.5m, 400m, 1000m)]));

        report.Checks.Should().ContainSingle().Which.Passed.Should().BeTrue();
        Outcome(report, 0, ResolutionFieldNames.Hours).Should().Be(ArithmeticOutcome.Passed);
        Outcome(report, 0, ResolutionFieldNames.Rate).Should().Be(ArithmeticOutcome.Passed);
        Outcome(report, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Passed);
    }

    [Fact]
    public void LineProduct_Mismatch_FailsAllThreeFields_BecauseNoneCanBeBlamed()
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(2.5m, 400m, 900m)]));

        var check = report.Checks.Should().ContainSingle().Subject;
        check.Kind.Should().Be(ArithmeticCheckKind.LineProduct);
        check.Passed.Should().BeFalse();
        check.Expected.Should().Be(1000m);
        check.Actual.Should().Be(900m);
        Outcome(report, 0, ResolutionFieldNames.Hours).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 0, ResolutionFieldNames.Rate).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Failed);
    }

    [Theory]
    [InlineData(1000.00, true)]
    [InlineData(1000.01, true)]
    [InlineData(999.99, true)]
    [InlineData(1000.02, false)]
    [InlineData(999.98, false)]
    public void LineProduct_ToleranceIsOneCent_InclusiveAtTheBoundary(double amount, bool passes)
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(2.5m, 400m, (decimal)amount)]));

        report.Checks.Single().Passed.Should().Be(passes);
    }

    [Theory]
    [InlineData(416.66, true)]
    [InlineData(416.67, true)]
    [InlineData(416.68, false)]
    public void LineProduct_RoundsTheProductToCentsBeforeComparing(double amount, bool passes)
    {
        // 1.25 x 333.33 = 416.6625, which is 416.66 to the cent.
        var report = ArithmeticChecker.Check(Invoice([Fee(1.25m, 333.33m, (decimal)amount)]));

        report.Checks.Single().Expected.Should().Be(416.66m);
        report.Checks.Single().Passed.Should().Be(passes);
    }

    [Fact]
    public void ExpenseLine_UsesQuantityAndUnitCost()
    {
        var passing = ArithmeticChecker.Check(Invoice([Expense(3m, 12.50m, 37.50m)]));
        var failing = ArithmeticChecker.Check(Invoice([Expense(3m, 12.50m, 40m)]));

        passing.Checks.Should().ContainSingle().Which.Passed.Should().BeTrue();
        Outcome(passing, 0, ResolutionFieldNames.Quantity).Should().Be(ArithmeticOutcome.Passed);
        Outcome(passing, 0, ResolutionFieldNames.UnitCost).Should().Be(ArithmeticOutcome.Passed);
        Outcome(failing, 0, ResolutionFieldNames.Quantity).Should().Be(ArithmeticOutcome.Failed);
        Outcome(failing, 0, ResolutionFieldNames.UnitCost).Should().Be(ArithmeticOutcome.Failed);
        Outcome(failing, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Failed);
    }

    [Theory]
    [InlineData(null, 400.0, 1000.0)]
    [InlineData(2.5, null, 1000.0)]
    [InlineData(2.5, 400.0, null)]
    [InlineData(null, null, null)]
    public void LineProduct_WithAValueMissing_DoesNotRun_SoNoFieldIsMarked(double? hours, double? rate, double? amount)
    {
        var report = ArithmeticChecker.Check(Invoice([Fee((decimal?)hours, (decimal?)rate, (decimal?)amount)]));

        report.Checks.Should().BeEmpty();
        Outcome(report, 0, ResolutionFieldNames.Hours).Should().Be(ArithmeticOutcome.NotApplicable);
        Outcome(report, 0, ResolutionFieldNames.Rate).Should().Be(ArithmeticOutcome.NotApplicable);
        Outcome(report, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.NotApplicable);
    }

    [Fact]
    public void FeesSection_RowsThatAddUp_PassTheTotalAndTheRowAmounts()
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(1m, 100m, 100m), Fee(2m, 100m, 200m)], fees: 300m));

        report.Checks.Should().Contain(c => c.Kind == ArithmeticCheckKind.FeesSection && c.Passed);
        Outcome(report, null, ResolutionFieldNames.FeesTotal).Should().Be(ArithmeticOutcome.Passed);
    }

    [Fact]
    public void FeesSection_RowsThatDoNotAddUp_FailTheTotalAndEveryFeeAmount_ButNotTheExpenseAmounts()
    {
        var report = ArithmeticChecker.Check(Invoice(
            [Fee(1m, 100m, 100m), Fee(2m, 100m, 200m), Expense(1m, 50m, 50m)],
            fees: 350m));

        var check = report.Checks.Single(c => c.Kind == ArithmeticCheckKind.FeesSection);
        check.Passed.Should().BeFalse();
        check.Expected.Should().Be(300m);
        check.Actual.Should().Be(350m);
        Outcome(report, null, ResolutionFieldNames.FeesTotal).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 1, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 2, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Passed);
    }

    [Fact]
    public void FeesSection_ALostRowAmount_FailsTheSum_InsteadOfSlippingThrough()
    {
        // The row amount was not read; the total of 300 cannot be reproduced from the 200 that was.
        var report = ArithmeticChecker.Check(Invoice([Fee(1m, 100m, null), Fee(2m, 100m, 200m)], fees: 300m));

        report.Checks.Should().Contain(c => c.Kind == ArithmeticCheckKind.FeesSection && !c.Passed);
        Outcome(report, null, ResolutionFieldNames.FeesTotal).Should().Be(ArithmeticOutcome.Failed);
    }

    [Fact]
    public void ExpensesSection_IsCheckedSeparately()
    {
        var report = ArithmeticChecker.Check(Invoice(
            [Fee(1m, 100m, 100m), Expense(1m, 20m, 20m), Expense(2m, 15m, 30m)],
            fees: 100m,
            expenses: 60m));

        report.Checks.Should().Contain(c => c.Kind == ArithmeticCheckKind.ExpensesSection && !c.Passed);
        Outcome(report, null, ResolutionFieldNames.ExpensesTotal).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, null, ResolutionFieldNames.FeesTotal).Should().Be(ArithmeticOutcome.Passed);
    }

    [Fact]
    public void SectionCheck_WithNoSectionTotal_DoesNotRun()
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(1m, 100m, 100m)]));

        report.Checks.Should().NotContain(c => c.Kind == ArithmeticCheckKind.FeesSection);
        report.Checks.Should().NotContain(c => c.Kind == ArithmeticCheckKind.ExpensesSection);
    }

    [Fact]
    public void TotalDue_IsFeesPlusExpensesPlusAdjustments_WithANegativeAdjustment()
    {
        InvoiceResolutionLine[] rows = [Fee(10m, 100m, 1000m), Expense(1m, 200m, 200m)];
        var passing = ArithmeticChecker.Check(Invoice(rows, fees: 1000m, expenses: 200m, adjustments: -150m, totalDue: 1050m));
        var failing = ArithmeticChecker.Check(Invoice(rows, fees: 1000m, expenses: 200m, adjustments: -150m, totalDue: 1200m));

        passing.Checks.Should().OnlyContain(c => c.Passed);
        passing.Checks.Should().ContainSingle(c => c.Kind == ArithmeticCheckKind.TotalDue);
        failing.Checks.Where(c => !c.Passed).Should().ContainSingle().Which.Kind.Should().Be(ArithmeticCheckKind.TotalDue);

        var check = failing.Checks.Single(c => c.Kind == ArithmeticCheckKind.TotalDue);
        check.Passed.Should().BeFalse();
        check.Expected.Should().Be(1050m);
        check.Actual.Should().Be(1200m);
        foreach (var field in new[]
        {
            ResolutionFieldNames.TotalDue, ResolutionFieldNames.FeesTotal, ResolutionFieldNames.ExpensesTotal, ResolutionFieldNames.AdjustmentsTotal,
        })
        {
            Outcome(failing, null, field).Should().Be(ArithmeticOutcome.Failed, field);
        }
    }

    [Fact]
    public void TotalDue_AnAbsentComponentCountsAsZero_AndIsNotMarked()
    {
        var report = ArithmeticChecker.Check(Invoice([Fee(10m, 100m, 1000m)], fees: 1000m, totalDue: 1000m));

        report.Checks.Should().OnlyContain(c => c.Passed);
        report.Checks.Should().ContainSingle(c => c.Kind == ArithmeticCheckKind.TotalDue);
        Outcome(report, null, ResolutionFieldNames.ExpensesTotal).Should().Be(ArithmeticOutcome.NotApplicable);
        Outcome(report, null, ResolutionFieldNames.AdjustmentsTotal).Should().Be(ArithmeticOutcome.NotApplicable);
    }

    [Fact]
    public void TotalDue_WithNoComponentAtAll_DoesNotRun()
    {
        var report = ArithmeticChecker.Check(Invoice([], totalDue: 1000m));

        report.Checks.Should().BeEmpty();
        Outcome(report, null, ResolutionFieldNames.TotalDue).Should().Be(ArithmeticOutcome.NotApplicable);
    }

    [Fact]
    public void AFieldInSeveralChecks_IsFailedWhenAnyFails_WhateverTheOrder()
    {
        // The row product passes, the fees section fails: the row's amount is in both and is Failed.
        var report = ArithmeticChecker.Check(Invoice([Fee(1m, 100m, 100m)], fees: 999m));

        report.Checks.Should().Contain(c => c.Kind == ArithmeticCheckKind.LineProduct && c.Passed);
        report.Checks.Should().Contain(c => c.Kind == ArithmeticCheckKind.FeesSection && !c.Passed);
        Outcome(report, 0, ResolutionFieldNames.Amount).Should().Be(ArithmeticOutcome.Failed);
        Outcome(report, 0, ResolutionFieldNames.Hours).Should().Be(ArithmeticOutcome.Passed);
    }

    [Fact]
    public void ACleanInvoice_PassesEveryCheck()
    {
        var report = ArithmeticChecker.Check(Invoice(
            [Fee(2m, 300m, 600m), Fee(1.5m, 200m, 300m), Expense(2m, 25m, 50m)],
            fees: 900m,
            expenses: 50m,
            adjustments: -100m,
            totalDue: 850m));

        report.Checks.Should().HaveCount(6); // 3 products, 2 sections, 1 total due
        report.Checks.Should().OnlyContain(c => c.Passed);
    }

    [Fact]
    public void Check_IsDeterministic()
    {
        var input = Invoice([Fee(1m, 100m, 100m), Expense(1m, 5m, 6m)], fees: 100m, expenses: 5m, totalDue: 105m);

        ArithmeticChecker.Check(input).Checks.Should().BeEquivalentTo(ArithmeticChecker.Check(input).Checks);
    }
}
