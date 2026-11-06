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
    public void CreditsThatExceedTheirTypesCharges_AreSummedSigned_AndNotRefused_D127()
    {
        // The expense credit has no expense charge to offset it: the expenses total is -50 (the column accepts it since D-127).
        var result = InvoiceTotalsCalculator.Calculate(950m, new[] { Fee(1000m), Expense(-50m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(1000m);
        result.ExpensesTotal.Should().Be(-50m);
        result.ApprovedTotal.Should().Be(950m);
    }

    [Fact]
    public void AllCreditInvoice_GivesANegativeTotal_AndIsNotRefused_D127()
    {
        // A credit memo: the billed total is the signed sum, and so are the fees and the approved total.
        var result = InvoiceTotalsCalculator.Calculate(-300m, new[] { Fee(-200m), Fee(-100m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(-300m);
        result.ExpensesTotal.Should().Be(0m);
        result.LineAdjustmentsTotal.Should().Be(0m);
        result.InvoiceAdjustmentsTotal.Should().Be(0m);
        result.ApprovedTotal.Should().Be(-300m);
    }

    [Fact]
    public void AllCreditInvoice_WithFeeAndExpenseCredits_KeepsEachCostTypeNegative_D127()
    {
        var result = InvoiceTotalsCalculator.Calculate(-125.50m, new[] { Fee(-100m), Expense(-25.50m) });

        result.IsRefused.Should().BeFalse();
        result.FeesTotal.Should().Be(-100m);
        result.ExpensesTotal.Should().Be(-25.50m);
        result.ApprovedTotal.Should().Be(-125.50m);
    }

    [Fact]
    public void MixedInvoice_NetsChargesAndCredits_AndTheNetMayBeNegative_D127()
    {
        // Net positive: 1000 - 400 = 600.
        var positive = InvoiceTotalsCalculator.Calculate(600m, new[] { Fee(1000m), Fee(-400m) });
        positive.IsRefused.Should().BeFalse();
        positive.FeesTotal.Should().Be(600m);
        positive.ApprovedTotal.Should().Be(600m);

        // Net negative: 300 - 1000 = -700, still stored as is.
        var negative = InvoiceTotalsCalculator.Calculate(-700m, new[] { Fee(300m), Fee(-1000m) });
        negative.IsRefused.Should().BeFalse();
        negative.FeesTotal.Should().Be(-700m);
        negative.ApprovedTotal.Should().Be(-700m);
    }

    [Fact]
    public void D129_1_AnInvoiceThatWasNotNegative_StillRefusesAnApprovedTotalBelowZero()
    {
        // FR-32 floor: it applies when the billed total is zero or more. Billed 100 (200 of work, a 100 credit), 150 of charges disallowed: -50, refused.
        var result = InvoiceTotalsCalculator.Calculate(
            100m, new[] { Fee(200m), Fee(-100m), Fee(150m, InvoiceLineStatus.Disallowed) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
        result.ApprovedTotal.Should().Be(-50m);
    }

    [Fact]
    public void D129_1_ABilledTotalOfExactlyZero_StillHasTheFloor()
    {
        var result = InvoiceTotalsCalculator.Calculate(0m, new[] { Fee(100m), Fee(-100m), Fixed(10m) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
    }

    [Fact]
    public void D129_1_ACreditMemo_HasNoFloor_EvenWhenAdjustmentsPushTheApprovedTotalFurtherNegative()
    {
        // Billed -100; a fixed adjustment of 500 takes the approved total to -600. No refusal: a credit memo has no extra limit.
        var result = InvoiceTotalsCalculator.Calculate(-100m, new[] { Fee(-100m), Fixed(500m) });

        result.Refusal.Should().BeNull();
        result.ApprovedTotal.Should().Be(-600m);
    }

    [Fact]
    public void D129_3_ReducingACredit_AcceptsPartOfIt_AndRaisesThePayable()
    {
        // 1000 of work and a 100 credit: billed 900. The credit is reduced to -40 (only 40 of credit accepted): its adjustment is -60,
        // so the approved total is 900 - (-60) = 960, which is 60 more than before.
        var credit = Fee(-100m, InvoiceLineStatus.Reduced, approved: -40m);

        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), credit });

        result.IsRefused.Should().BeFalse();
        result.LineAdjustmentsTotal.Should().Be(-60m);
        result.ApprovedTotal.Should().Be(960m);
    }

    [Theory]
    [InlineData(0)]      // accept none of the credit
    [InlineData(-100)]   // accept all of it
    [InlineData(-0.01)]
    public void D129_3_AReducedCredit_AtTheEdgesOfItsRange_IsAccepted(double approved)
    {
        var credit = Fee(-100m, InvoiceLineStatus.Reduced, approved: (decimal)approved);

        InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), credit }).IsRefused.Should().BeFalse();
    }

    [Theory]
    [InlineData(-150)]  // more credit than billed
    [InlineData(10)]    // flipping the sign
    [InlineData(-100.01)]
    public void D129_3_AReducedCredit_BeyondItsRange_IsStillRefused(double approved)
    {
        var credit = Fee(-100m, InvoiceLineStatus.Reduced, approved: (decimal)approved);

        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), credit });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
        result.Refusal.LineId.Should().Be(credit.Id);
    }

    [Fact]
    public void D129_3_AReducedCreditWithNoApprovedAmount_IsRefused()
    {
        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), Fee(-100m, InvoiceLineStatus.Reduced) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
    }

    [Fact]
    public void D129_3_AReducedCharge_KeepsItsRange_AndNegativeApprovedAmountIsStillRefused()
    {
        InvoiceTotalsCalculator.Calculate(500m, new[] { Fee(500m, InvoiceLineStatus.Reduced, 500m) }).IsRefused.Should().BeFalse();
        InvoiceTotalsCalculator.Calculate(500m, new[] { Fee(500m, InvoiceLineStatus.Reduced, 500.01m) }).Refusal!.Code
            .Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
        InvoiceTotalsCalculator.Calculate(500m, new[] { Fee(500m, InvoiceLineStatus.Reduced, -1m) }).Refusal!.Code
            .Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
    }

    [Fact]
    public void DisallowedCreditLine_IsSummedSigned_AndRaisesThePayableTotal_D128()
    {
        // 1000 of work, a 100 credit: billed 900. Disallowing the credit: its adjustment is -100, so the approved total is 900 - (-100) = 1000.
        var result = InvoiceTotalsCalculator.Calculate(900m, new[] { Fee(1000m), Fee(-100m, InvoiceLineStatus.Disallowed) });

        result.IsRefused.Should().BeFalse("nothing needs to offset a disallowed credit (D-128)");
        result.LineAdjustmentsTotal.Should().Be(-100m);
        result.ApprovedTotal.Should().Be(1000m, "disallowing a credit raises the payable total");
    }

    [Fact]
    public void DisallowedCredit_OnACreditMemo_TakesTheApprovedTotalToZero_D128()
    {
        var result = InvoiceTotalsCalculator.Calculate(-100m, new[] { Fee(-100m, InvoiceLineStatus.Disallowed) });

        result.IsRefused.Should().BeFalse();
        result.LineAdjustmentsTotal.Should().Be(-100m);
        result.ApprovedTotal.Should().Be(0m);
    }

    [Fact]
    public void DisallowedCredit_OffsetByADisallowedCharge_NetsToNoChange_D128()
    {
        // Disallowed charge 100 (adjustment +100) and disallowed credit -100 (adjustment -100): the approved total is the billed total.
        var result = InvoiceTotalsCalculator.Calculate(
            900m, new[] { Fee(1000m), Fee(100m, InvoiceLineStatus.Disallowed), Fee(-100m, InvoiceLineStatus.Disallowed) });

        result.IsRefused.Should().BeFalse();
        result.LineAdjustmentsTotal.Should().Be(0m);
        result.ApprovedTotal.Should().Be(900m);
    }

    [Fact]
    public void D129_2_PercentAdjustment_OnANetNegativeFeeBase_AdjustsNothing()
    {
        // The rule: value% x the fee base after the fee lines' own adjustments, with the base clamped at zero. Base = 300 - 1000 = -700,
        // so 10% is 0 and the invoice stays at -700.
        var percent = Percent(10m);

        var result = InvoiceTotalsCalculator.Calculate(-700m, new[] { Fee(300m), Fee(-1000m), percent });

        result.IsRefused.Should().BeFalse();
        result.AdjustmentLineAmounts[percent.Id].Should().Be(0m);
        result.InvoiceAdjustmentsTotal.Should().Be(0m);
        result.ApprovedTotal.Should().Be(-700m);
    }

    [Fact]
    public void D129_2_PercentAdjustment_OnAnAllCreditInvoice_AdjustsNothing_ButAFixedAdjustmentStillApplies()
    {
        var percent = Percent(25m);
        var fixedLine = Fixed(20m);

        var result = InvoiceTotalsCalculator.Calculate(-400m, new[] { Fee(-400m), percent, fixedLine });

        result.AdjustmentLineAmounts[percent.Id].Should().Be(0m);
        result.AdjustmentLineAmounts[fixedLine.Id].Should().Be(20m);
        result.ApprovedTotal.Should().Be(-420m);
    }

    [Fact]
    public void D129_2_PercentAdjustment_OnAPositiveBase_IsUnchanged_EvenWithACreditOnTheInvoice()
    {
        // Base = 1000 - 200 = 800 (positive), so 10% = 80 as before; an expense credit never enters the base.
        var percent = Percent(10m);

        var result = InvoiceTotalsCalculator.Calculate(750m, new[] { Fee(1000m), Fee(-200m), Expense(-50m), percent });

        result.AdjustmentLineAmounts[percent.Id].Should().Be(80m);
        result.ApprovedTotal.Should().Be(670m);
    }

    [Fact]
    public void D129_2_PercentAdjustment_TakesADisallowedCreditOutOfTheBase_ThenClampsAtZero()
    {
        // Fees 300 and -1000, the credit disallowed: its adjustment is -1000, so the base is -700 - (-1000) = 300 and 10% = 30.
        var percent = Percent(10m);
        var result = InvoiceTotalsCalculator.Calculate(
            -700m, new[] { Fee(300m), Fee(-1000m, InvoiceLineStatus.Disallowed), percent });

        result.IsRefused.Should().BeFalse();
        result.LineAdjustmentsTotal.Should().Be(-1000m);
        result.AdjustmentLineAmounts[percent.Id].Should().Be(30m);
        result.ApprovedTotal.Should().Be(-700m + 1000m - 30m);

        // A disallowed CHARGE can also take the base below zero: fees 100 and -300 (net -200), the 100 charge disallowed: base -200 - 100 < 0 -> 0.
        var clamped = Percent(10m);
        var second = InvoiceTotalsCalculator.Calculate(
            -200m, new[] { Fee(100m, InvoiceLineStatus.Disallowed), Fee(-300m), clamped });

        second.AdjustmentLineAmounts[clamped.Id].Should().Be(0m);
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

    // ── Task 164a-2 (EB-D-192, EB-D-199, add-line T8): a reviewer-added line is outside billed, fees, expenses and the percent base ──

    private static InvoiceLineInput AddedFee(decimal amount, InvoiceLineStatus? status = InvoiceLineStatus.Flagged, decimal? approved = null) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Fee, amount, approved, status, null, null, ReviewerAdded: true);

    private static InvoiceLineInput AddedExpense(decimal amount, InvoiceLineStatus? status = InvoiceLineStatus.Flagged, decimal? approved = null) =>
        new(Guid.NewGuid(), InvoiceLineCostType.Expense, amount, approved, status, null, null, ReviewerAdded: true);

    [Theory]
    [InlineData(InvoiceLineStatus.Flagged, null, 1200)]     // undecided: the amount counts while the reviewer decides
    [InlineData(InvoiceLineStatus.Pending, null, 1200)]
    [InlineData(InvoiceLineStatus.Clean, null, 1200)]
    [InlineData(InvoiceLineStatus.Accepted, 200, 1200)]      // accepted: billed + the amount
    [InlineData(InvoiceLineStatus.Reduced, 150, 1150)]       // reduced: billed + the approved amount
    [InlineData(InvoiceLineStatus.Reduced, 0, 1000)]
    [InlineData(InvoiceLineStatus.Disallowed, 0, 1000)]      // disallowed: billed + 0, never billed - 200
    public void AddedFeeLine_ContributesPerEb192_AndLeavesBilledFeesAndExpensesUnchanged(InvoiceLineStatus status, int? approved, int expectedApproved)
    {
        var firm = Fee(1000m, InvoiceLineStatus.Accepted, 1000m);
        var added = AddedFee(200m, status, approved is null ? null : (decimal)approved);

        var result = InvoiceTotalsCalculator.Calculate(1000m, new[] { firm, added });

        result.IsRefused.Should().BeFalse();
        result.ApprovedTotal.Should().Be((decimal)expectedApproved);
        result.FeesTotal.Should().Be(1000m, "the added line is not in the fees total");
        result.ExpensesTotal.Should().Be(0m);
        result.LineAdjustmentsTotal.Should().Be(0m, "an added line has no firm-line adjustment, whatever its decision");
        result.InvoiceAdjustmentsTotal.Should().Be(0m);
    }

    [Theory]
    [InlineData(InvoiceLineStatus.Flagged, null, 1200)]
    [InlineData(InvoiceLineStatus.Accepted, 200, 1200)]
    [InlineData(InvoiceLineStatus.Reduced, 150, 1150)]
    [InlineData(InvoiceLineStatus.Disallowed, 0, 1000)]
    public void AddedExpenseLine_ContributesPerEb192_AndLeavesTheExpensesTotalUnchanged(InvoiceLineStatus status, int? approved, int expectedApproved)
    {
        var result = InvoiceTotalsCalculator.Calculate(
            1000m, new[] { Fee(800m), Expense(200m), AddedExpense(200m, status, approved is null ? null : (decimal)approved) });

        result.ApprovedTotal.Should().Be((decimal)expectedApproved);
        result.FeesTotal.Should().Be(800m);
        result.ExpensesTotal.Should().Be(200m, "the added line is not in the expenses total");
        result.LineAdjustmentsTotal.Should().Be(0m);
    }

    [Fact]
    public void AddedLines_AreOutsideThePercentBase_AndAreCombinedWithFirmLineAndInvoiceAdjustments()
    {
        // billed 1000 (fees 1000); a firm fee of 300 is disallowed (line adjustment 300); an added fee of 500 is Accepted.
        // Percent base = fees 1000 - 300 = 700 (the added 500 is NOT in it): 10% = 70. Fixed 30.
        // approved = billed 1000 + added 500 - line adjustments 300 - invoice adjustments (70 + 30) = 1100.
        var result = InvoiceTotalsCalculator.Calculate(
            1000m,
            new[]
            {
                Fee(700m, InvoiceLineStatus.Accepted, 700m), Fee(300m, InvoiceLineStatus.Disallowed),
                AddedFee(500m, InvoiceLineStatus.Accepted, 500m), Percent(10m), Fixed(30m),
            });

        result.FeesTotal.Should().Be(1000m);
        result.LineAdjustmentsTotal.Should().Be(300m);
        result.InvoiceAdjustmentsTotal.Should().Be(100m);
        result.ApprovedTotal.Should().Be(1100m);
    }

    [Fact]
    public void AnAddedLine_Disallowed_NeverSubtractsFromTheFirmsBilledLines()
    {
        var withAdded = InvoiceTotalsCalculator.Calculate(
            1000m, new[] { Fee(1000m, InvoiceLineStatus.Accepted, 1000m), AddedFee(400m, InvoiceLineStatus.Disallowed) });
        var without = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m, InvoiceLineStatus.Accepted, 1000m) });

        withAdded.ApprovedTotal.Should().Be(without.ApprovedTotal).And.Be(1000m);
    }

    [Fact]
    public void AnAddedCredit_IsSummedSigned_ByTheSameRule()
    {
        // A reviewer-added credit of -50: undecided or Accepted it reduces what is payable; Disallowed it contributes 0.
        InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), AddedFee(-50m) }).ApprovedTotal.Should().Be(950m);
        InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), AddedFee(-50m, InvoiceLineStatus.Disallowed) }).ApprovedTotal.Should().Be(1000m);
        InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), AddedFee(-50m, InvoiceLineStatus.Reduced, -20m) }).ApprovedTotal.Should().Be(980m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(250.0)]
    public void AReducedAddedLine_WithAnUnusableApprovedAmount_IsRefused_LikeAnyReducedLine(double? approved)
    {
        var added = AddedFee(200m, InvoiceLineStatus.Reduced, approved is null ? null : (decimal)approved);

        var result = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), added });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.LineApprovedAmountInvalid);
        result.Refusal.LineId.Should().Be(added.Id);
    }

    [Fact]
    public void AnAddedLine_DoesNotLiftTheFr32FloorOrChangeTheBilledTotal()
    {
        // billed 100; a fixed adjustment of 150 exceeds it. The added line (undecided, 30) raises the approved total to -20, still below zero: refused.
        var result = InvoiceTotalsCalculator.Calculate(100m, new[] { Fee(100m), AddedFee(30m), Fixed(150m) });

        result.Refusal!.Code.Should().Be(InvoiceTotalsRefusalCodes.ApprovedTotalNegative);
        result.ApprovedTotal.Should().Be(-20m);
        result.FeesTotal.Should().Be(100m);
    }

    // Must-not-fire: the marker alone decides. An intake line (ReviewerAdded false) behaves exactly as before, whatever its amount or status.
    [Theory]
    [InlineData(InvoiceLineStatus.Flagged, null, 1000, 0)]       // undecided: nothing added, nothing taken
    [InlineData(InvoiceLineStatus.Accepted, 200, 1000, 0)]       // accepted: nothing added (an intake line is in billed already)
    [InlineData(InvoiceLineStatus.Reduced, 150, 950, 50)]        // reduced: the billed total less the 50 taken off
    [InlineData(InvoiceLineStatus.Disallowed, 0, 800, 200)]      // disallowed: the billed total less the whole line
    public void AnIntakeLine_ReviewerAddedFalse_BehavesExactlyAsBefore(InvoiceLineStatus status, int? approved, int expectedApproved, int expectedLineAdjustment)
    {
        var intake = Fee(200m, status, approved is null ? null : (decimal)approved);
        intake.ReviewerAdded.Should().BeFalse("the default for every existing caller");

        var result = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(800m, InvoiceLineStatus.Accepted, 800m), intake });

        result.ApprovedTotal.Should().Be((decimal)expectedApproved);
        result.LineAdjustmentsTotal.Should().Be((decimal)expectedLineAdjustment);
        result.FeesTotal.Should().Be(1000m, "an intake line is in the fees total");
    }

    [Fact]
    public void TheReviewerAddedFlag_IsIgnoredOnAnAdjustmentLine()
    {
        var adjustment = new InvoiceLineInput(Guid.NewGuid(), InvoiceLineCostType.Adjustment, 0m, null, null, InvoiceAdjustmentMethod.Fixed, 40m, ReviewerAdded: true);

        var result = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m), adjustment });

        result.InvoiceAdjustmentsTotal.Should().Be(40m);
        result.ApprovedTotal.Should().Be(960m);
    }

    // Known limit (164a-2 F1): the marker can be removed from a genuine added line by anyone who may write the line. The line is then an intake line to the
    // calculator. Pinned exactly: the billed total is an input and does not move; fees (or expenses) are overstated by the line's amount; the
    // approved total is UNDERSTATED by the same amount, because the line's contribution is lost and, if it was Reduced or Disallowed, a firm-line
    // adjustment is charged against billed money that was never billed. Spend follows the approved total.
    [Theory(DisplayName = "164a-2 F1 known limit: a genuine added line whose marker was removed is counted as billed: fees overstated, approved understated, both by its amount")]
    [InlineData(InvoiceLineStatus.Flagged, null)]
    [InlineData(InvoiceLineStatus.Accepted, 200)]
    [InlineData(InvoiceLineStatus.Reduced, 150)]
    [InlineData(InvoiceLineStatus.Disallowed, 0)]
    public void AnAddedLine_WithItsMarkerRemoved_IsCountedAsBilled(InvoiceLineStatus status, int? approved)
    {
        decimal? ap = approved is null ? null : (decimal)approved;
        InvoiceLineInput Added(bool marked) =>
            new(Guid.NewGuid(), InvoiceLineCostType.Fee, 200m, ap, status, null, null, ReviewerAdded: marked);

        var withMarker = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m, InvoiceLineStatus.Accepted, 1000m), Added(true) });
        var withoutMarker = InvoiceTotalsCalculator.Calculate(1000m, new[] { Fee(1000m, InvoiceLineStatus.Accepted, 1000m), Added(false) });

        withoutMarker.FeesTotal.Should().Be(withMarker.FeesTotal + 200m, "fees are overstated by the line's amount");
        withMarker.ApprovedTotal.Should().Be(withoutMarker.ApprovedTotal + 200m, "approved is understated by the line's amount");
    }
}
