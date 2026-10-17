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

    [Fact]
    public void ExpenseLines_AreJudgedToo()
    {
        var facts = Facts(Expense(0) with { Date = Close.AddDays(10) }) with { CoreRecord = Core(Open, Close) };

        Run(Window(), facts).Should().ContainSingle();
    }

    [Fact]
    public void ReadFields_AreTheLineDateOnly_AndTheCoreRecordValuesAreReferenceValues()
    {
        var finding = Run(Window(), FactsFor(Core(Open, Close, closed: true), Close.AddDays(9))).Single();

        finding.ReadFields.Should().Equal(new FieldRef(0, BillingFieldNames.Date));
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
