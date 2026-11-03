namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 8, CoreRecordWindow (design.md 6.1): a line dated outside the core record's open and close dates (widened by the
/// grace days), or billed against a core record that is closed to billing. The core record's billing status decides which (D-116).
/// </summary>
/// <remarks>
/// <para><b>By billing status (D-116, <see cref="CoreRecordBillingStatus"/>), per line, first match wins:</b></para>
/// <list type="bullet">
/// <item><b>Open</b>: billing is allowed. Only the open date is checked: a line dated more than <c>graceDays</c> before it is flagged
/// (<c>before_open</c>). The close date and the closed state are ignored.</item>
/// <item><b>Final</b>: the open date as for Open; then a line dated more than <c>graceDays</c> after the close date is flagged
/// (<c>after_close</c>); with no close date every charge line is flagged, a line with no date included (<c>core_record_closed</c>,
/// fail safe). Work dated on or before the close date and billed later is not flagged.</item>
/// <item><b>Closed</b>: closed to billing. Every charge line is flagged (<c>closed_to_billing</c>), whatever its date; the rule's author
/// decides the consequence.</item>
/// <item><b>Empty</b> (a row from before the column) or a value outside the set: D-111, unchanged. <c>before_open</c>; <c>after_close</c>;
/// or the core record is inactive and has no close date, so there is no window to be inside (<c>core_record_closed</c>). A closed core
/// record with a close date is judged by that date.</item>
/// </list>
/// <para>A date exactly at a window edge (with the grace) holds. A date the core record does not have (no open date, no close date) is
/// not a boundary. With no core record in the fact set nothing is judged (072 does not call this rule then). A line with no date can
/// only fire on a reason that flags every charge line. The rule's Describe text (<see cref="BillingRuleCatalogue"/>) says exactly
/// this.</para>
/// <para><b>Credit lines are skipped (D-105).</b> A line with negative hours or a negative amount
/// (<see cref="BillingMath.IsCreditLine"/>) is not judged on any reason, the billing-status ones included: a reversal legitimately
/// carries the old date of the charge it reverses.</para>
/// <para><b>Reads:</b> Date, and the line's Hours and Amount (all extracted): the credit test that precedes every reason reads Hours
/// and Amount (<see cref="HandlerContext.WithCreditTestReads"/>). The open date, close date, closed state and billing status are
/// reference values (core record).</para>
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
            // A credit line (D-105) is skipped on every check, the closed-record one included: a reversal legitimately carries
            // the date of the charge it reverses, which may be outside the window. It stays visible to the reviewer.
            if (BillingMath.IsCreditLine(line))
            {
                continue;
            }

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
                HandlerContext.Reference("coreRecord.billingStatus", CoreRecordBillingStatus.Label(core.BillingStatus)),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("coreRecord.openDate", BillingText.Date(core.OpenDate))
                .Add("coreRecord.closeDate", BillingText.Date(core.CloseDate))
                .Add("rule.graceDays", BillingText.Number(grace));

            findings.Add(context.Finding(BillingSubject.ForLine(line.Index), reason, context.WithCreditTestReads(line, values), facts));
        }

        return findings;
    }

    private static string? ReasonOf(DateOnly? lineDate, BillingCoreRecordFacts core, int grace)
    {
        switch (core.BillingStatus)
        {
            case CoreRecordBillingStatus.Closed:
                return BillingReasonCodes.CoreRecordClosedToBilling;

            case CoreRecordBillingStatus.Open:
                return BeforeOpen(lineDate, core, grace) ? BillingReasonCodes.BeforeCoreRecordOpened : null;

            case CoreRecordBillingStatus.Final:
                if (BeforeOpen(lineDate, core, grace))
                {
                    return BillingReasonCodes.BeforeCoreRecordOpened;
                }

                if (core.CloseDate is null)
                {
                    return BillingReasonCodes.CoreRecordClosed;
                }

                return AfterClose(lineDate, core, grace) ? BillingReasonCodes.AfterCoreRecordClosed : null;

            default:
                // Empty, or a value outside the set: D-111.
                if (BeforeOpen(lineDate, core, grace))
                {
                    return BillingReasonCodes.BeforeCoreRecordOpened;
                }

                if (AfterClose(lineDate, core, grace))
                {
                    return BillingReasonCodes.AfterCoreRecordClosed;
                }

                return core.IsClosed && core.CloseDate is null ? BillingReasonCodes.CoreRecordClosed : null;
        }
    }

    private static bool BeforeOpen(DateOnly? lineDate, BillingCoreRecordFacts core, int grace) =>
        lineDate is { } date && core.OpenDate is { } open && date.DayNumber < open.DayNumber - grace;

    private static bool AfterClose(DateOnly? lineDate, BillingCoreRecordFacts core, int grace) =>
        lineDate is { } date && core.CloseDate is { } close && date.DayNumber > close.DayNumber + grace;
}
