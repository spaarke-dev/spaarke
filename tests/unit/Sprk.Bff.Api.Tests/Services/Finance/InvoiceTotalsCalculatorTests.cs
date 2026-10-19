using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance;

/// <summary>
/// <see cref="InvoiceTotalsCalculator"/> (ebilling-r1 task 016, FR-07, I-1, D-42, FR-32): the one definition of an invoice's totals.
/// </summary>
/// <remarks>
/// Maintain-class (ADR-038): the approved total is the amount that goes to AP, so the formula, the "percent on fees after line
/// adjustments, expenses excluded" rule, the rounding and the negative-total refusal are pinned with literal figures.
/// </remarks>
public sealed class InvoiceTotalsCalculatorTests
{
    private static InvoiceLineInput Fee(decimal amount, InvoiceLineStatus? status = null, decimal? approved = null) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Fee, amount, approved, status, null, null);

    private static InvoiceLineInput Expense(decimal amount, InvoiceLineStatus? status = null, decimal? approved = null) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Expense, amount, approved, status, null, null);

    private static InvoiceLineInput Percent(decimal percent) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Adjustment, 0m, null, null, InvoiceAdjustmentMethod.Percent, percent);

    private static InvoiceLineInput Fixed(decimal amount) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Adjustment, 0m, null, null, InvoiceAdjustmentMethod.Fixed, amount);

    [Fact]
    public void NoDecisions_ApprovedEqualsBilled_AndBothAdjustmentTotalsAreZero()
    {
        var result = InvoiceTotalsCalculator.Calculate(1200m, new[] { Fee(1000m), Expense(200m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(1000m);
        result.ExpensesTotal.Should().Be(200m);
        result.LineAdjustmentsTotal.Should().Be(0m);
        result.InvoiceAdjustmentsTotal.Should().Be(0m);
        result.ApprovedTotal.Should().Be(1200m);
    }

    [Theory]
    [InlineData(InvoiceLineStatus.Pending)]
    [InlineData(InvoiceLineStatus.Clean)]
    [InlineData(InvoiceLineStatus.Flagged)]
    [InlineData(InvoiceLineStatus.Accepted)]
    public void UndecidedOrAcceptedLine_AdjustsNothing(InvoiceLineStatus status)
    {
        var result = InvoiceTotalsCalculator.Calculate(500m, new[] { Fee(500m, status, approved: 100m) });

        result.LineAdjustmentsTotal.Should().Be(0m);
        result.ApprovedTotal.Should().Be(500m);
    }

    [Fact]
    public void ReducedAndDisallowedFeeLines_AddUpToTheLineAdjustmentsTotal()
    {
        // 500 reduced to 300 (adjustment 200) + 200 disallowed (adjustment 200) = 400.
        var result = InvoiceTotalsCalculator.Calculate(
            700m, new[] { Fee(500m, InvoiceLineStatus.Reduced, 300m), Fee(200m, InvoiceLineStatus.Disallowed) });

        result.LineAdjustmentsTotal.Should().Be(400m);
        result.ApprovedTotal.Should().Be(300m);
    }

    [Fact]
    public void ReducedAndDisallowedExpenseLines_CountAsLineAdjustments()
    {
        var result = InvoiceTotalsCalculator.Calculate(
            300m, new[] { Expense(100m, InvoiceLineStatus.Disallowed), Expense(200m, InvoiceLineStatus.Reduced, 150m) });

        result.ExpensesTotal.Should().Be(300m);
        result.LineAdjustmentsTotal.Should().Be(150m);
        result.ApprovedTotal.Should().Be(150m);
    }

    [Fact]
    public void PercentAdjustment_IsTakenOnFeesAfterLineAdjustments_ExpensesExcluded()
    {
        // fees 1000, one fee line reduced by 100 -> base 900; expenses 500 never enter the base. 10% of 900 = 90.
        var percent = Percent(10m);
        var result = InvoiceTotalsCalculator.Calculate(
            1500m, new[] { Fee(400m, InvoiceLineStatus.Reduced, 300m), Fee(600m), Expense(500m), percent });

        result.AdjustmentLineAmounts[percent.Id].Should().Be(90m);
        result.LineAdjustmentsTotal.Should().Be(100m);
        result.InvoiceAdjustmentsTotal.Should().Be(90m);
        result.ApprovedTotal.Should().Be(1310m);
    }

    [Fact]
    public void ExpenseLineAdjustment_DoesNotShrinkThePercentBase()
    {
        var percent = Percent(10m);
        var result = InvoiceTotalsCalculator.Calculate(
            1500m, new[] { Fee(1000m), Expense(500m, InvoiceLineStatus.Disallowed), percent });

        result.AdjustmentLineAmounts[percent.Id].Should().Be(100m);
    }

    [Fact]
    public void TwoPercentAdjustments_ShareOneBase_TheyDoNotCompound()
    {
        var a = Percent(10m);
        var b = Percent(10m);
        var result = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), a, b });

        result.AdjustmentLineAmounts[a.Id].Should().Be(100m);
        result.AdjustmentLineAmounts[b.Id].Should().Be(100m);
        result.ApprovedTotal.Should().Be(800m);
    }

    [Fact]
    public void FixedAdjustment_IsItsValue_AndMixesWithPercentAndLineAdjustments()
    {
        var fixedLine = Fixed(25m);
        var percent = Percent(5m);
        var result = InvoiceTotalsCalculator.Calculate(
            1200m,
            new[] { Fee(1000m, InvoiceLineStatus.Reduced, 800m), Expense(200m, InvoiceLineStatus.Disallowed), fixedLine, percent });

        // line adjustments 200 + 200 = 400; percent base 1000 - 200 = 800 -> 40; fixed 25.
        result.AdjustmentLineAmounts[fixedLine.Id].Should().Be(25m);
        result.AdjustmentLineAmounts[percent.Id].Should().Be(40m);
        result.LineAdjustmentsTotal.Should().Be(400m);
        result.InvoiceAdjustmentsTotal.Should().Be(65m);
        result.ApprovedTotal.Should().Be(735m);
    }

    [Fact]
    public void AdjustmentsBeyondTheBilledTotal_AreRefused_WithTheStableReasonCode()
    {
        var result = InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), Fixed(150m) });

        result.IsRefused.Should().BeTrue();
        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
        result.ApprovedTotal.Should().Be(-50m);
    }

    [Fact]
    public void ApprovedTotalOfExactlyZero_IsNotRefused()
    {
        InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), Fixed(100m) }).IsRefused.Should().BeFalse();
    }

    [Fact]
    public void PercentAdjustment_RoundsAwayFromZeroAtTheHalfCent()
    {
        // Exact midpoint: 50% of 0.01 = 0.005 -> 0.01 (away from zero; banker's rounding would give 0.00).
        var midpoint = Percent(50m);
        var result = InvoiceTotalsCalculator.Calculate(0.01m, new[] { Fee(0.01m), midpoint });

        result.AdjustmentLineAmounts[midpoint.Id].Should().Be(0.01m);
        result.ApprovedTotal.Should().Be(0m);
    }

    [Fact]
    public void PercentAdjustment_JustBelowTheMidpoint_RoundsDown()
    {
        // 0.5% of 100.10 = 0.5005 -> 0.50 ; 0.5% of 100.30 = 0.5015 -> 0.50 ; 0.5% of 100.90 = 0.5045 -> 0.50 ; 0.5% of 101.10 = 0.5055 -> 0.51
        var below = Percent(0.5m);
        InvoiceTotalsCalculator.Calculate(100.90m, new[] { Fee(100.90m), below }).AdjustmentLineAmounts[below.Id].Should().Be(0.50m);

        var above = Percent(0.5m);
        InvoiceTotalsCalculator.Calculate(101.10m, new[] { Fee(101.10m), above }).AdjustmentLineAmounts[above.Id].Should().Be(0.51m);
    }

    [Fact]
    public void ThirdOfAPercent_IsRoundedToTwoDecimals_AndTheTotalsMatchTheStoredAmounts()
    {
        var percent = Percent(33.3333m);
        var result = InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), percent });

        result.AdjustmentLineAmounts[percent.Id].Should().Be(33.33m);
        result.InvoiceAdjustmentsTotal.Should().Be(33.33m);
        result.ApprovedTotal.Should().Be(66.67m);
    }

    [Fact]
    public void ApprovedTotal_UsesTheBilledTotal_NotFeesPlusExpenses()
    {
        // A billed total that disagrees with the lines (e.g. a discount that is not a line) still drives I-1.
        var result = InvoiceTotalsCalculator.Calculate(1100m, new[] { Fee(1000m, InvoiceLineStatus.Disallowed) });

        result.FeesTotal.Should().Be(1000m);
        result.ApprovedTotal.Should().Be(100m);
    }

    [Fact]
    public void NoLines_ApprovedEqualsBilled()
    {
        var result = InvoiceTotalsCalculator.Calculate(250m, Array.Empty<InvoiceLineInput>());

        result.ApprovedTotal.Should().Be(250m);
        result.FeesTotal.Should().Be(0m);
    }

    // ---- fail closed: input the formula cannot place refuses the whole calculation ----

    [Fact]
    public void LineWithoutCostType_IsRefused_NotSkipped()
    {
        var line = new InvoiceLineInput(Guid.NewGuid(), null, 100m, null, null, null, null);

        var result = InvoiceTotalsCalculator.Calculate(100m, new[] { line });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineCostTypeUnknown);
        result.Refusal.LineId.Should().Be(line.Id);
    }

    [Fact]
    public void MissingBilledTotal_IsRefused_NotTreatedAsZero()
    {
        var result = InvoiceTotalsCalculator.Calculate(null, new[] { Fee(100m) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.BilledTotalMissing);
    }

    // D-121 (ebilling-r1 task 049 part A): credit lines keep their negative amount and are summed as signed values. These replace
    // NegativeFeeOrExpenseAmount_IsRefused, which pinned the pre-D-121 refusal of any negative Fee or Expense amount.

    [Fact]
    public void CreditFeeLine_IsSummedAsASignedAmount()
    {
        // 1000 of work, a 100 credit: the billed total is the net 900 and nothing is adjusted.
        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), Fee(-100m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(900m);
        result.ApprovedTotal.Should().Be(900m);
    }

    [Fact]
    public void CreditExpenseLine_IsSummedInTheExpenseTotal_AndNotTheFees()
    {
        var result = InvoiceTotalsCalculator.Calculate(1150m, new[] { Fee(1000m), Expense(200m), Expense(-50m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(1000m);
        result.ExpensesTotal.Should().Be(150m);
        result.ApprovedTotal.Should().Be(1150m);
    }

    [Fact]
    public void PercentAdjustment_IsTakenOnTheFeesNetOfTheCredit()
    {
        // Base = 1000 - 200 = 800, so 10% = 80; the approved total is 800 - 80.
        var result = InvoiceTotalsCalculator.Calculate(800m, new[] { Fee(1000m), Fee(-200m), Percent(10m) });

        result.IsRefused.Should().BeFalse();
        result.InvoiceAdjustmentsTotal.Should().Be(80m);
        result.ApprovedTotal.Should().Be(720m);
    }

    [Fact]
    public void CreditsThatExceedTheirTypesCharges_AreRefused_BecauseTheStoredTotalCannotBeNegative()
    {
        // The expense credit has no expense charge to offset it: sprk_expensestotal (minimum 0) could not hold -50.
        var result = InvoiceTotalsCalculator.Calculate(950m, new[] { Fee(1000m), Expense(-50m) });

        result.IsRefused.Should().BeTrue();
        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.TotalBelowStorableMinimum);
        result.FeesTotal.Should().Be(0m, "a refused result carries no figures that could be stored");
        result.ExpensesTotal.Should().Be(0m);
    }

    [Fact]
    public void AllCreditInvoice_IsRefused_BecauseTheFeesTotalWouldBeNegative()
    {
        var result = InvoiceTotalsCalculator.Calculate(-300m, new[] { Fee(-300m) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.TotalBelowStorableMinimum);
    }

    [Fact]
    public void ReducedCreditLine_IsRefused_BecauseNoApprovedAmountIsValidForIt()
    {
        var credit = Fee(-100m, InvoiceLineStatus.Reduced, approved: 0m);

        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), credit });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
        result.Refusal.LineId.Should().Be(credit.Id);
    }

    [Fact]
    public void DisallowedCreditLine_WithNothingToOffsetIt_IsRefused_ButIsSummedWhenTheChargesCoverIt()
    {
        // Disallowing a credit puts a negative figure in the line-adjustments total; alone it could not be stored.
        var alone = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), Fee(-100m, InvoiceLineStatus.Disallowed) });
        alone.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.TotalBelowStorableMinimum);

        // Offset by a disallowed charge of 100: line adjustments = 100 + (-100) = 0, so the approved total is the billed total.
        var offset = InvoiceTotalsCalculator.Calculate(
            900m, new[] { Fee(1000m), Fee(100m, InvoiceLineStatus.Disallowed), Fee(-100m, InvoiceLineStatus.Disallowed) });
        offset.IsRefused.Should().BeFalse();
        offset.LineAdjustmentsTotal.Should().Be(0m);
    }

    [Fact]
    public void AdjustmentLinesOwnStatus_IsNotRead()
    {
        // Only Fee and Expense lines are decided; a Disallowed status on an Adjustment line changes nothing.
        var adjustment = new InvoiceLineInput(
            Guid.NewGuid(), InvoiceLineCostType.Adjustment, 0m, null, InvoiceLineStatus.Disallowed, InvoiceAdjustmentMethod.Fixed, 20m);

        var result = InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), adjustment });

        result.AdjustmentLineAmounts[adjustment.Id].Should().Be(20m);
        result.ApprovedTotal.Should().Be(80m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(600.0)]
    public void ReducedLine_WithAnUnusableApprovedAmount_IsRefused(double? approved)
    {
        var line = Fee(500m, InvoiceLineStatus.Reduced, approved is null ? null : (decimal)approved);

        var result = InvoiceTotalsCalculator.Calculate(500m, new[] { line });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
        result.Refusal.LineId.Should().Be(line.Id);
    }

    [Fact]
    public void AdjustmentLine_WithoutMethodOrValue_OrOutOfRange_IsRefused()
    {
        InvoiceLineInput Adjustment(InvoiceAdjustmentMethod? method, decimal? value) =>
            new(Guid.NewGuid(), InvoiceLineCostType.Adjustment, 0m, null, null, method, value);

        foreach (var bad in new[]
                 {
                     Adjustment(null, 10m),
                     Adjustment(InvoiceAdjustmentMethod.Percent, null),
                     Adjustment(InvoiceAdjustmentMethod.Percent, -1m),
                     Adjustment(InvoiceAdjustmentMethod.Percent, 100.01m),
                     Adjustment(InvoiceAdjustmentMethod.Fixed, -5m),
                     Adjustment((InvoiceAdjustmentMethod)7, 5m),
                 })
        {
            InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), bad }).Refusal!.Code
                .Should().Be(InvoiceTotalsRefusalCodes.AdjustmentInvalid);
        }
    }
}
