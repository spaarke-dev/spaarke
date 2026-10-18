using FluentAssertions;
using Sprk.Bff.Api.Services.Finance.Resolution;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;
using Xunit;
using static Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers.BillingHandlerKit;

namespace Sprk.Bff.Api.Tests.Domain.Signals.Billing.Handlers;

/// <summary>
/// ebilling-r1 task 071, rule type 8 CoreRecordWindow (design.md 6.1). Maintain-class (ADR-038): pure logic, no mocks. The core
/// record in these tests opens 2026-01-10 and closes 2026-03-20.
/// </summary>
public class CoreRecordWindowHandlerTests
{
    private static readonly DateOnly Open = new(2026, 1, 10);
    private static readonly DateOnly Close = new(2026, 3, 20);

    private static BillingRuleDefinition Window(int grace = 0) => Rule($$"""{"type":"CoreRecordWindow","graceDays":{{grace}}}""");

    private static BillingFactSet FactsFor(BillingCoreRecordFacts? core, params DateOnly?[] lineDates) =>
        Facts(lineDates.Select((d, i) => Fee(i) with { Date = d }).ToArray()) with { CoreRecord = core };

    private static BillingCoreRecordFacts Core(DateOnly? open = null, DateOnly? close = null, bool closed = false) =>
        new() { OpenDate = open, CloseDate = close, IsClosed = closed };

    [Fact]
    public void MustFire_ForALineBeforeTheOpenDate()
    {
        var finding = Run(Window(), FactsFor(Core(Open, Close), Open.AddDays(-1))).Should().ContainSingle().Subject;

        finding.ReasonCode.Should().Be(BillingReasonCodes.BeforeCoreRecordOpened);
        finding.SuggestedAdjustment.Should().BeNull();
        finding.FactValues.Should().Contain("coreRecord.openDate", "2026-01-10").And.Contain("coreRecord.closeDate", "2026-03-20").And.Contain("rule.graceDays", "0");
        finding.FactValues.Keys.Should().BeEquivalentTo(BillingRuleCatalogue.TemplateFactFields(RuleType.CoreRecordWindow));
    }

    [Fact]
    public void MustFire_ForALineAfterTheCloseDate()
    {
        Run(Window(), FactsFor(Core(Open, Close), Close.AddDays(1))).Should().ContainSingle()
            .Which.ReasonCode.Should().Be(BillingReasonCodes.AfterCoreRecordClosed);
    }

    [Fact]
    public void MustNotFire_ForLinesInsideTheWindow_IncludingBothEdges()
    {
        Run(Window(), FactsFor(Core(Open, Close), Open, Open.AddDays(30), Close)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(7, -7, false)] // exactly the grace before the open date holds
    [InlineData(7, -8, true)]
    [InlineData(7, 7, false)] // exactly the grace after the close date holds
    [InlineData(7, 8, true)]
    [InlineData(0, -1, true)]
    [InlineData(365, -365, false)]
    public void TheGraceDaysWidenTheWindowOnBothSides(int grace, int offsetFromTheEdge, bool fires)
    {
        var edge = offsetFromTheEdge < 0 ? Open : Close;
        var date = edge.AddDays(offsetFromTheEdge);

        Run(Window(grace), FactsFor(Core(Open, Close), date)).Any().Should().Be(fires);
    }

    [Fact]
    public void ACoreRecordWithOnlyAnOpenDate_OnlyHasALowerEdge_AndOneWithOnlyACloseDateOnlyAnUpperEdge()
    {
        Run(Window(), FactsFor(Core(open: Open), new DateOnly(2030, 1, 1))).Should().BeEmpty();
        Run(Window(), FactsFor(Core(open: Open), Open.AddDays(-1))).Should().ContainSingle();
        Run(Window(), FactsFor(Core(close: Close), new DateOnly(2000, 1, 1))).Should().BeEmpty();
        Run(Window(), FactsFor(Core(close: Close), Close.AddDays(1))).Should().ContainSingle();
    }

    [Fact]
    public void ACoreRecordWithNoDatesThatIsNotClosed_HasNoWindowToBeOutside()
    {
        Run(Window(), FactsFor(Core(), new DateOnly(1999, 1, 1), new DateOnly(2099, 1, 1))).Should().BeEmpty();
    }

    [Fact]
    public void WithNoCoreRecordFacts_NothingIsJudged()
    {
        Run(Window(), FactsFor(null, new DateOnly(1999, 1, 1))).Should().BeEmpty();
    }

    [Fact]
    public void AClosedCoreRecordWithACloseDate_IsJudgedByThatDate_AFinalInvoiceForWorkInsideTheWindowHolds()
    {
        var closed = Core(Open, Close, closed: true);

        Run(Window(), FactsFor(closed, Close.AddDays(-5))).Should().BeEmpty();
        Run(Window(), FactsFor(closed, Close.AddDays(5))).Should().ContainSingle()
            .Which.ReasonCode.Should().Be(BillingReasonCodes.AfterCoreRecordClosed);
    }

    [Fact]
    public void D111_AMatterClosedOn30June_FlagsALineDated5July_ButNotEarlierWorkBilledLaterOnASeptemberInvoice()
    {
        // The owner's scenario: the matter closed 2026-06-30 and has a close date; the invoice is dated in September.
        var closed = Core(new DateOnly(2026, 1, 10), new DateOnly(2026, 6, 30), closed: true);
        var facts = FactsFor(closed, new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 30), new DateOnly(2026, 7, 5)) with
        {
            Invoice = Invoice() with { InvoiceDate = new DateOnly(2026, 9, 12) },
        };

        var findings = Run(Window(), facts);

        var finding = findings.Should().ContainSingle("only the line dated after the close date is flagged").Subject;
        finding.Subject.Should().Be(BillingSubject.ForLine(2));
        finding.ReasonCode.Should().Be(BillingReasonCodes.AfterCoreRecordClosed);
    }

    [Fact]
    public void AClosedCoreRecordWithNoCloseDate_FlagsEveryLine_EvenOneWithNoDate()
    {
        var facts = FactsFor(Core(open: Open, closed: true), Open.AddDays(5), null);

        var findings = Run(Window(), facts);

        findings.Select(f => f.ReasonCode).Should().OnlyContain(c => c == BillingReasonCodes.CoreRecordClosed);
        findings.Select(f => f.Subject.LineIndex).Should().Equal(0, 1);
    }

    [Fact]
    public void ALineBeforeTheOpenDate_OfAClosedCoreRecordWithNoCloseDate_IsReportedAsBeforeOpen()
    {
        Run(Window(), FactsFor(Core(open: Open, closed: true), Open.AddDays(-3))).Single()
            .ReasonCode.Should().Be(BillingReasonCodes.BeforeCoreRecordOpened);
    }

    [Fact]
    public void ALineWithNoDate_OnAnOpenCoreRecord_IsNotJudged()
    {
        Run(Window(), FactsFor(Core(Open, Close), (DateOnly?)null)).Should().BeEmpty();
    }

    // ── D-105: a reversal legitimately carries the old date ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("-1.0", "575.00")] // negative hours
    [InlineData("1.0", "-575.00")] // negative amount
    public void MustNotFire_ForACreditLineOutsideTheWindow_D105(string hours, string amount)
    {
        var before = Fee(0) with { Date = Open.AddDays(-30), Hours = D(hours), Amount = D(amount) };
        var after = Fee(1) with { Date = Close.AddDays(30), Hours = D(hours), Amount = D(amount) };
        var facts = Facts(before, after) with { CoreRecord = Core(Open, Close) };

        Run(Window(), facts).Should().BeEmpty("a reversal carries the date of the charge it reverses");
    }

    [Fact]
    public void MustNotFire_ForACreditLine_OnAClosedCoreRecordWithNoCloseDate_D105()
    {
        var facts = Facts(Fee(0) with { Amount = -575m }, Fee(1) with { Hours = -1m, Date = null }) with
        {
            CoreRecord = Core(open: Open, closed: true),
        };

        Run(Window(), facts).Should().BeEmpty("the closed-record reason is a charge finding too");
    }

    [Fact]
    public void MustFire_ForTheChargeLinesAroundACredit_TheCreditIsSkippedAndTheRestAreStillJudged_D105()
    {
        var late = Close.AddDays(30);
        var facts = Facts(Fee(0) with { Date = late }, Fee(1) with { Date = late, Amount = -575m }, Fee(2) with { Date = late }) with
        {
            CoreRecord = Core(Open, Close),
        };

        var findings = Run(Window(), facts);

        findings.Select(f => f.Subject.LineIndex).Should().Equal(0, 2);
        findings.Should().OnlyContain(f => f.ReasonCode == BillingReasonCodes.AfterCoreRecordClosed);
    }

    [Fact]
    public void ExpenseLines_AreJudgedToo()
    {
        var facts = Facts(Expense(0) with { Date = Close.AddDays(10) }) with { CoreRecord = Core(Open, Close) };

        Run(Window(), facts).Should().ContainSingle();
    }

    [Fact]
    public void ReadFields_AreTheLineDate_ThenTheHoursAndAmountTheCreditTestRead_AndTheCoreRecordValuesAreReferenceValues()
    {
        var finding = Run(Window(), FactsFor(Core(Open, Close, closed: true), Close.AddDays(9))).Single();

        finding.ReadFields.Should().Equal(
            new FieldRef(0, BillingFieldNames.Date), new FieldRef(0, BillingFieldNames.Hours), new FieldRef(0, BillingFieldNames.Amount));
        finding.Value("coreRecord.closeDate").IsExtracted.Should().BeFalse();
        finding.Value("coreRecord.closed").Value.Should().Be("true");
    }

    [Fact]
    public void AMissingCoreRecordDate_IsLeftOutOfTheTemplateValues_NotWrittenAsNull()
    {
        var finding = Run(Window(), FactsFor(Core(open: Open), Open.AddDays(-2))).Single();

        finding.FactValues.Should().ContainKey("coreRecord.openDate").And.NotContainKey("coreRecord.closeDate");
    }
}
