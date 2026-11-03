namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 5, NonBillableActivity (design.md 6.1): a line whose AI-assigned activity or task code is in the rule's list of
/// non-billable codes. Flag only, with a suggested disallow of the line.
/// </summary>
/// <remarks>
/// <para><b>Low-confidence codes are flagged, never silently used (NFR-05).</b> The codes are the AI's (task 047), not facts.
/// The handler judges the code it is given, fires on a listed code whatever its confidence, and carries the coding confidence on
/// the finding, marked below the reliability threshold when it is: the reviewer sees a doubtful code as doubtful. A code that is
/// not there is not used at all (a line the AI did not code does not fire); the firm's own code is RequiredData's and the
/// firm-versus-AI mismatch finding's concern, not this rule's. The codes in the list match without regard to case.</para>
/// <para><b>Suggested adjustment:</b> disallow, with the line amount to the cent (<c>null</c> if the line has no amount).
/// <b>Never for a credit line (D-105):</b> a line with negative hours or a negative amount (<see cref="BillingMath.IsCreditLine"/>)
/// still gets its finding, so the reviewer sees the credit, but with no suggested adjustment: disallowing a negative amount would
/// raise the bill by what the firm credited.</para>
/// <para><b>Reads:</b> ActivityCode, TaskCode (both examined, with the coding confidence), Amount, Date, TimekeeperAsBilled, and
/// Hours when the line has them: the credit test that decides the suggested adjustment reads Hours and Amount
/// (<see cref="HandlerContext.WithCreditTestReads"/>).</para>
/// </remarks>
internal static class NonBillableActivityHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var codes = new HashSet<string>(
            BillingRuleHandlers.RequireList(context.Rule.Codes, context, "codes"), StringComparer.OrdinalIgnoreCase);

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            var activity = BillingText.Text(line.ActivityCode);
            var task = BillingText.Text(line.TaskCode);
            if (!(activity is not null && codes.Contains(activity)) && !(task is not null && codes.Contains(task)))
            {
                continue;
            }

            var values = new[]
            {
                context.LineField(line, "line.activityCode", BillingFieldNames.ActivityCode, activity),
                context.LineField(line, "line.taskCode", BillingFieldNames.TaskCode, task),
                context.LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)),
                context.LineDate(line),
                context.LineTimekeeper(line),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
                .Add("line.activityCode", activity)
                .Add("line.taskCode", task);

            // D-105: the finding stays (the reviewer sees a credit booked to a non-billable activity), but a credit is never
            // disallowed: removing a negative amount would raise the bill by the amount the firm credited.
            BillingSuggestedAdjustment? disallow = BillingMath.IsCreditLine(line)
                ? null
                : new BillingSuggestedAdjustment(
                    BillingAdjustmentKind.Disallow, line.Amount is { } amount ? BillingMath.ToCent(amount) : null);

            findings.Add(context.Finding(
                BillingSubject.ForLine(line.Index), BillingReasonCodes.NonBillableCode, context.WithCreditTestReads(line, values), facts, disallow));
        }

        return findings;
    }
}
