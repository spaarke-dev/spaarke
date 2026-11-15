using FluentAssertions;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.BillingEvaluationKit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing;

/// <summary>
/// ebilling-r1 task 113 fix round: <see cref="BillingJudgedDates"/> is the ONE rule for the dates an invoice is judged on. It is used by the
/// evaluator, the review banner and the Procedure change's close; these tests pin the helper to what the evaluator really resolves.
/// </summary>
public class BillingJudgedDatesTests
{
    private static readonly DateOnly Jan20 = new(2026, 1, 20);
    private static readonly DateOnly Feb15 = new(2026, 2, 15);
    private static readonly DateOnly Mar15 = new(2026, 3, 15);
    private static readonly DateOnly Received = new(2026, 3, 20);

    [Fact(DisplayName = "113: the judged dates are the invoice date and each line's date (else the invoice date), distinct and ascending")]
    public void Of_IsTheInvoiceDateAndEachLineDate_DistinctAscending()
    {
        BillingJudgedDates.Of(Mar15, new DateOnly?[] { Mar15, Feb15, null, Feb15, Jan20 }).Should().Equal(Jan20, Feb15, Mar15);
        BillingJudgedDates.Of(Mar15, Array.Empty<DateOnly?>()).Should().Equal([Mar15], "an invoice with no evaluated line is judged on its own date");
        BillingJudgedDates.Of(Mar15, new DateOnly?[] { null, null }).Should().Equal(Mar15);
    }

    [Theory(DisplayName = "113: only Fee and Expense rows are evaluated; Adjustment and a row with no cost type are not")]
    [InlineData((int)InvoiceLineCostType.Fee, true)]
    [InlineData((int)InvoiceLineCostType.Expense, true)]
    [InlineData((int)InvoiceLineCostType.Adjustment, false)]
    [InlineData(null, false)]
    [InlineData(7, false)]
    public void IsEvaluated_IsFeeAndExpenseOnly(int? costType, bool expected) =>
        BillingJudgedDates.IsEvaluated(costType).Should().Be(expected);

    public static IEnumerable<object?[]> Requests() =>
    [
        [Mar15, new DateOnly?[] { Feb15, Mar15 }],
        [Mar15, new DateOnly?[] { Feb15, null }],
        [Mar15, new DateOnly?[] { null, null, null }],
        [Mar15, new DateOnly?[] { Jan20, Feb15, Mar15, Jan20 }],
        [Mar15, Array.Empty<DateOnly?>()],
        [null, new DateOnly?[] { Feb15, null }],   // no invoice date: the received date is the invoice's judging date
        [null, Array.Empty<DateOnly?>()],
    ];

    [Theory(DisplayName = "113: the helper's dates equal the dates the REAL evaluator resolves (ProcedureBasis.Dates) for the same request")]
    [MemberData(nameof(Requests))]
    public void Of_EqualsTheDatesTheEvaluatorResolves(DateOnly? invoiceDate, DateOnly?[] lineDates)
    {
        var request = Request(lineDates.Select((d, i) => Fee(i) with { Date = d }).ToArray()) with { InvoiceDate = invoiceDate, ReceivedOn = Received };

        var result = Evaluator().Evaluate(request, Loaded());

        var helper = BillingJudgedDates.Of(request.InvoiceDate ?? request.ReceivedOn, request.Lines.Select(l => l.Date));
        result.ProcedureBasis!.Dates.Select(d => d.WorkDate).Should().Equal(helper);
    }
}
