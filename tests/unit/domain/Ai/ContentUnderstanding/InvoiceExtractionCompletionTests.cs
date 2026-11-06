using FluentAssertions;
using Sprk.Bff.Api.Services.Ai.ContentUnderstanding;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Xunit;

namespace Sprk.Bff.Api.Tests.Domain.Ai.ContentUnderstanding;

/// <summary>
/// ebilling-r1 task 043: the deterministic completion step, ported from the task 005 spike's <c>complete()</c>
/// (<c>projects/ebilling-r1/notes/spikes/extraction/score.py</c>), plus the day/month post-check. Each case is one of
/// the spike's rules, with a must-not-fire control beside it.
/// </summary>
public sealed class InvoiceExtractionCompletionTests
{
    // ── rule 2: clock notation read as a decimal ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(1.30, 300, 450, 1.5)]   // 1:30 → 1.5
    [InlineData(0.45, 200, 150, 0.75)]  // 0:45 → 0.75
    [InlineData(2.20, 60, 140, 2.33)]   // 2:20 → 2.333…, rounded to 2.33; 2.33 × 60 = 139.80, within 0.60 of 140
    public void Complete_HoursThatOnlyFitAsClockNotation_AreConverted(double hours, double rate, double amount, double expected)
    {
        var line = Complete(Fee((decimal)hours, (decimal)rate, (decimal)amount)).Hours;

        line.Value.Should().Be((decimal)expected);
        line.ReturnedValue.Should().Be((decimal)hours);
        line.Repair.Should().Be(ExtractedValueRepair.ClockHoursConverted);
    }

    [Fact]
    public void Complete_ClockConversionOutsideTheSpikeTolerance_IsNotApplied()
    {
        // 2:20 × 600: 2.33 × 600 = 1398, 2 away from 1400 — beyond the spike's 0.60, so the hours stay as read.
        Complete(Fee(2.20m, 600m, 1400m)).Hours.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_HoursThatAlreadyFit_AreLeftAlone()
    {
        var hours = Complete(Fee(1.5m, 300m, 450m)).Hours;

        hours.Value.Should().Be(1.5m);
        hours.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_MismatchThatClockNotationDoesNotExplain_IsLeftForTheRules()
    {
        // 1.30 × 300 = 390 and 1.5 × 300 = 450, but the row bills 600: not a notation problem, so nothing changes.
        var hours = Complete(Fee(1.30m, 300m, 600m)).Hours;

        hours.Value.Should().Be(1.30m);
        hours.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_FractionOfSixtyOrMore_IsNotClockNotation()
    {
        // .75 cannot be minutes; 1.75 × 100 = 175 ≠ 200, and the conversion is not attempted.
        Complete(Fee(1.75m, 100m, 200m)).Hours.Repair.Should().Be(ExtractedValueRepair.None);
    }

    // ── rule 1: rate from the invoice's own timekeeper summary ─────────────────────────────────────────

    [Fact]
    public void Complete_RowWithoutRate_TakesTheTimekeepersSummaryRate()
    {
        var invoice = Invoice(
            [Fee(2m, null, 900m, timekeeper: "Imani Wickham (Associate)")],
            [Summary("Imani Wickham", 450m)]);

        var rate = InvoiceExtractionCompletion.Complete(invoice).Lines[0].Rate;

        rate.Value.Should().Be(450m);
        rate.Repair.Should().Be(ExtractedValueRepair.RateFromTimekeeperSummary);
        rate.Page.Should().Be(2, "the value's source is the summary row");
        rate.ReturnedValue.Should().BeNull();
    }

    [Fact]
    public void Complete_RowBilledByInitials_MatchesTheSummaryNameCarryingThem()
    {
        var invoice = Invoice([Fee(1m, null, 450m, timekeeper: "IW")], [Summary("Imani Wickham (IW)", 450m)]);

        InvoiceExtractionCompletion.Complete(invoice).Lines[0].Rate.Repair.Should().Be(ExtractedValueRepair.RateFromTimekeeperSummary);
    }

    [Fact]
    public void Complete_RowWithItsOwnRate_IgnoresTheSummary()
    {
        var invoice = Invoice([Fee(2m, 400m, 800m, timekeeper: "Imani Wickham")], [Summary("Imani Wickham", 450m)]);

        var rate = InvoiceExtractionCompletion.Complete(invoice).Lines[0].Rate;

        rate.Value.Should().Be(400m);
        rate.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_RowWithNoTimekeeper_IsNotGivenTheRateOfASummaryNameThatNormalisesToNothing()
    {
        // Deviation from the spike (documented in the class): "(AB)" alone would otherwise key the empty name.
        var invoice = Invoice([Fee(2m, null, 900m, timekeeper: null)], [Summary("(AB)", 999m)]);

        var rate = InvoiceExtractionCompletion.Complete(invoice).Lines[0].Rate;

        rate.Value.Should().Be(450m, "rule 3 computes it from the row instead");
        rate.Repair.Should().Be(ExtractedValueRepair.ComputedFromRow);
    }

    // ── rule 3: arithmetic fallbacks ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_MissingRate_IsAmountOverHours()
    {
        var rate = Complete(Fee(3m, null, 1000m)).Rate;

        rate.Value.Should().Be(333.33m);
        rate.Repair.Should().Be(ExtractedValueRepair.ComputedFromRow);
        rate.Page.Should().BeNull();
        rate.Confidence.Should().BeNull();
    }

    [Fact]
    public void Complete_MissingHours_IsAmountOverRate()
    {
        var hours = Complete(Fee(null, 400m, 1000m)).Hours;

        hours.Value.Should().Be(2.5m);
        hours.Repair.Should().Be(ExtractedValueRepair.ComputedFromRow);
    }

    [Fact]
    public void Complete_MissingAmount_IsHoursTimesRate()
    {
        var amount = Complete(Fee(1.25m, 410m, null)).Amount;

        amount.Value.Should().Be(512.5m);
        amount.Repair.Should().Be(ExtractedValueRepair.ComputedFromRow);
    }

    [Fact]
    public void Complete_ZeroHoursAndNoRate_LeavesTheRateEmpty()
    {
        var rate = Complete(Fee(0m, null, 100m)).Rate;

        rate.Value.Should().BeNull();
        rate.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_ExpenseRow_IsNotTouchedByTheFeeRules()
    {
        var expense = Line(InvoiceLineType.Expense, hours: null, rate: null, amount: 50m);

        Complete(expense).Should().BeEquivalentTo(expense);
    }

    // ── day/month post-check ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Complete_DayFirstDateReadMonthFirst_IsSwappedIntoThePeriod()
    {
        // Period July 2025; the row's 05/07/2025 (5 July) was read as 2025-05-07.
        var invoice = Invoice([Fee(1m, 100m, 100m, date: new DateOnly(2025, 5, 7))], [],
            new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 31));

        var date = InvoiceExtractionCompletion.Complete(invoice).Lines[0].Date;

        date.Value.Should().Be(new DateOnly(2025, 7, 5));
        date.ReturnedValue.Should().Be(new DateOnly(2025, 5, 7));
        date.Repair.Should().Be(ExtractedValueRepair.DayMonthSwapped);
    }

    [Theory]
    [InlineData(2025, 7, 15)]   // inside the period
    [InlineData(2025, 6, 13)]   // outside, but day 13 cannot be a month
    [InlineData(2025, 6, 2)]    // outside, and the swap (2 June → 6 February) is outside too
    public void Complete_DateThatTheSwapDoesNotExplain_IsLeftAlone(int year, int month, int day)
    {
        var invoice = Invoice([Fee(1m, 100m, 100m, date: new DateOnly(year, month, day))], [],
            new DateOnly(2025, 7, 1), new DateOnly(2025, 7, 31));

        var date = InvoiceExtractionCompletion.Complete(invoice).Lines[0].Date;

        date.Value.Should().Be(new DateOnly(year, month, day));
        date.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Fact]
    public void Complete_NoBillingPeriod_LeavesDatesAlone()
    {
        var invoice = Invoice([Fee(1m, 100m, 100m, date: new DateOnly(2025, 5, 7))], []);

        InvoiceExtractionCompletion.Complete(invoice).Lines[0].Date.Repair.Should().Be(ExtractedValueRepair.None);
    }

    [Theory]
    [InlineData("Jane Q. Doe (Partner)", "janeqdoe")]
    [InlineData("O'Brien-Smith", "obriensmith")]
    [InlineData(null, "")]
    public void NormaliseTimekeeper_MatchesTheSpike(string? name, string expected) =>
        InvoiceExtractionCompletion.NormaliseTimekeeper(name).Should().Be(expected);

    // ── builders ───────────────────────────────────────────────────────────────────────────────────────

    private static ExtractedInvoiceLine Complete(ExtractedInvoiceLine line) =>
        InvoiceExtractionCompletion.Complete(Invoice([line], [])).Lines[0];

    private static ExtractedInvoiceLine Fee(
        decimal? hours, decimal? rate, decimal? amount, string? timekeeper = "Tk", DateOnly? date = null) =>
        Line(InvoiceLineType.Fee, hours, rate, amount, timekeeper, date);

    private static ExtractedInvoiceLine Line(
        InvoiceLineType type, decimal? hours, decimal? rate, decimal? amount, string? timekeeper = null, DateOnly? date = null) =>
        new()
        {
            LineType = type,
            RowIndex = 0,
            Date = Read<DateOnly?>(date),
            TimekeeperAsBilled = Read<string?>(timekeeper),
            TitleAsBilled = Read<string?>(null),
            Hours = Read<decimal?>(hours),
            Rate = Read<decimal?>(rate),
            Quantity = Read<decimal?>(null),
            UnitCost = Read<decimal?>(null),
            Amount = Read<decimal?>(amount),
            Narrative = Read<string?>(null),
            TaskCodeAsBilled = Read<string?>(null),
            ActivityCodeAsBilled = Read<string?>(null),
            ExpenseCodeAsBilled = Read<string?>(null),
        };

    private static ExtractedTimekeeperSummaryRow Summary(string name, decimal rate) => new()
    {
        Name = Read<string?>(name),
        Title = Read<string?>(null),
        Hours = Read<decimal?>(null),
        Rate = Read<decimal?>(rate, page: 2),
        Amount = Read<decimal?>(null),
    };

    private static ExtractedInvoiceField<T> Read<T>(T value, int page = 1) => value is null
        ? new ExtractedInvoiceField<T> { Confidence = 0.9m }
        : new ExtractedInvoiceField<T>
        {
            Value = value,
            ReturnedValue = value,
            Confidence = 0.8m,
            Page = page,
            BoundingBox = new ExtractedBoundingBox(page, 0.1m, 0.1m, 0.1m, 0.01m),
        };

    private static ExtractedInvoice Invoice(
        IReadOnlyList<ExtractedInvoiceLine> lines,
        IReadOnlyList<ExtractedTimekeeperSummaryRow> summary,
        DateOnly? periodStart = null,
        DateOnly? periodEnd = null) => new()
        {
            Header = new ExtractedInvoiceHeader
            {
                FirmName = Read<string?>(null),
                FirmInvoiceNumber = Read<string?>("1"),
                InvoiceDate = Read<DateOnly?>(null),
                BillingPeriodStart = Read<DateOnly?>(periodStart),
                BillingPeriodEnd = Read<DateOnly?>(periodEnd),
                ClientName = Read<string?>(null),
                MatterName = Read<string?>(null),
                FirmMatterReference = Read<string?>(null),
                PurchaseOrderAsQuoted = Read<string?>(null),
                FeesTotal = Read<decimal?>(null),
                ExpensesTotal = Read<decimal?>(null),
                AdjustmentsTotal = Read<decimal?>(null),
                TotalDue = Read<decimal?>(null),
                Currency = Read<string?>(null),
            },
            Lines = lines,
            TimekeeperSummary = summary,
            PageCount = 2,
            AnalyzerId = "a",
            ApiVersion = "v",
        };
}
