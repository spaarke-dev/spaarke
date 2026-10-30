using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 4, DailyHoursCap (design.md 6.1): one timekeeper's fee hours on one day, within the invoice, must not exceed the cap
/// (R1 does not look across invoices). Flag only.
/// </summary>
/// <remarks>
/// <para><b>Grouping.</b> Fee lines with a date and hours are grouped by timekeeper and date. The timekeeper is the roster match
/// (task 046) when there is one, so two spellings of one person count together; otherwise the billed name (trimmed, whitespace
/// collapsed, case ignored); a line with neither is not judged. Credits (negative hours) are summed in. The rule fires when
/// the total is <b>above</b> the cap; exactly the cap holds.</para>
/// <para><b>One finding per timekeeper-day</b>, on the first line of the group (invoice order), not one per line: the cap is a
/// property of the day. It reads the hours, date and timekeeper of every line in the group.</para>
/// </remarks>
internal static class DailyHoursCapHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var cap = BillingRuleHandlers.Require(context.Rule.MaxHoursPerDay, context, "maxHoursPerDay");

        // Groups keep first-appearance order, so the findings come out in invoice order whatever the dictionary does.
        var order = new List<(string Timekeeper, DateOnly Date)>();
        var groups = new Dictionary<(string Timekeeper, DateOnly Date), List<BillingLineFacts>>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.Date is not { } date || line.Hours is null)
            {
                continue;
            }

            var timekeeper = TimekeeperKey(line);
            if (timekeeper is null)
            {
                continue;
            }

            var key = (timekeeper, date);
            if (!groups.TryGetValue(key, out var members))
            {
                members = [];
                groups[key] = members;
                order.Add(key);
            }

            members.Add(line);
        }

        var findings = new List<BillingFinding>();
        foreach (var key in order)
        {
            var members = groups[key];
            var total = members.Sum(m => m.Hours!.Value);
            if (total <= cap)
            {
                continue;
            }

            var first = members[0];
            var values = new List<BillingValueRead>();
            foreach (var member in members)
            {
                values.Add(context.LineHours(member));
                values.Add(context.LineDate(member));
                values.Add(context.LineTimekeeper(member));
            }

            values.Add(HandlerContext.Reference("dailyHours", BillingText.Number(total)));

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(first.Date))
                .Add("line.timekeeperName", BillingText.Text(first.TimekeeperName))
                .Add("dailyHours", BillingText.Number(total))
                .Add("rule.maxHoursPerDay", BillingText.Number(cap));

            findings.Add(context.Finding(BillingSubject.ForLine(first.Index), BillingReasonCodes.DailyHoursOverCap, values, facts));
        }

        return findings;
    }

    private static string? TimekeeperKey(BillingLineFacts line)
    {
        if (line.Timekeeper is { IsMatched: true, TimekeeperId: { } id })
        {
            return "id:" + id.ToString("D");
        }

        var name = BillingText.Key(line.TimekeeperName);
        return name is null ? null : "name:" + name;
    }
}
