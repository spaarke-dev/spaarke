namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 8, CoreRecordWindow (design.md 6.1): a line dated outside the core record's open and close dates (widened by the
/// grace days), or billed against a core record that is closed.
/// </summary>
/// <remarks>
/// <para><b>The check, per line, first match wins:</b> the line date is more than <c>graceDays</c> before the open date
/// (<c>before_open</c>); more than <c>graceDays</c> after the close date (<c>after_close</c>); or the core record is closed and has
/// no close date, so there is no window to be inside (<c>core_record_closed</c>). A date exactly at a window edge (with the
/// grace) holds. A date the core record does not have (no open date, no close date) is not a boundary.</para>
/// <para><b>"Or core record closed" (owner-visible reading).</b> A closed core record with a close date is judged by that date: a
/// final invoice for work done inside the window is legitimate, so closure alone does not flag lines dated inside it. Closure
/// without a close date flags every line. If the owner wants any line on a closed core record flagged, it is one condition here.</para>
/// <para>With no core record in the fact set nothing is judged (072 does not call this rule then). A line with no date can only
/// fire on the closed-without-close-date reason.</para>
/// <para><b>Reads:</b> Date (extracted). The open date, close date and closed state are reference values (core record).</para>
/// </remarks>
internal static class CoreRecordWindowHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var grace = BillingRuleHandlers.Require(context.Rule.GraceDays, context, "graceDays");
        var findings = new List<BillingFinding>();
        if (context.Facts.CoreRecord is not { } core)
        {
            return findings;
        }

        foreach (var line in context.Facts.Lines)
        {
            var reason = ReasonOf(line.Date, core, grace);
            if (reason is null)
            {
                continue;
            }

            var values = new[]
            {
                context.LineDate(line),
                HandlerContext.Reference("coreRecord.openDate", BillingText.Date(core.OpenDate)),
                HandlerContext.Reference("coreRecord.closeDate", BillingText.Date(core.CloseDate)),
                HandlerContext.Reference("coreRecord.closed", core.IsClosed ? "true" : "false"),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("coreRecord.openDate", BillingText.Date(core.OpenDate))
                .Add("coreRecord.closeDate", BillingText.Date(core.CloseDate))
                .Add("rule.graceDays", BillingText.Number(grace));

            findings.Add(context.Finding(BillingSubject.ForLine(line.Index), reason, values, facts));
        }

        return findings;
    }

    private static string? ReasonOf(DateOnly? lineDate, BillingCoreRecordFacts core, int grace)
    {
        if (lineDate is { } date)
        {
            if (core.OpenDate is { } open && date.DayNumber < open.DayNumber - grace)
            {
                return BillingReasonCodes.BeforeCoreRecordOpened;
            }

            if (core.CloseDate is { } close && date.DayNumber > close.DayNumber + grace)
            {
                return BillingReasonCodes.AfterCoreRecordClosed;
            }
        }

        return core.IsClosed && core.CloseDate is null ? BillingReasonCodes.CoreRecordClosed : null;
    }
}
