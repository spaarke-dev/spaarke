namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 9, Duplicate (design.md 6.1; D-88; spec FR-36): the firm invoice number was already submitted by the firm, and/or a
/// line repeats an earlier line, compared on the rule's <c>matchFields</c>.
/// </summary>
/// <remarks>
/// <para><b>Invoice level</b> (when <c>firmInvoiceNumber</c> is a match field): the number, compared with whitespace and case
/// ignored, equals the number of an earlier <b>active</b> submission of the firm. Rejected and Returned attempts are ignored
/// (FR-36, D-37), so a corrected resubmission of a rejected invoice is not a duplicate; so is the submission itself. The finding
/// cites the lowest-numbered active attempt as the original. Whether it rejects or only flags is the policy version's
/// consequence, never decided here (D-88).</para>
/// <para><b>Line level</b> (the other match fields): a line is a duplicate when every configured field of it equals the same field
/// of an earlier line, in an active prior submission (reported first) or earlier in this invoice. The earlier line is not flagged,
/// the repeat is. A line missing any configured field is not compared (nothing to match); a line's timekeeper is its roster match
/// when it has one, else its billed name (so two spellings of one rostered person are one, and a rostered line never equals an
/// unrostered one: known limit, the unrostered line is already an ApprovedTimekeeper finding); narratives compare with whitespace
/// and case ignored; hours, amount and date exactly. A line-level finding never rejects (task 073).</para>
/// <para><b>Reads:</b> the invoice level reads FirmInvoiceNumber. The line level reads TimekeeperAsBilled and Date, the configured
/// Hours, Narrative and Amount, and FirmInvoiceNumber (it is a template token). The original's tracking number is a reference value.</para>
/// </remarks>
internal static class DuplicateHandler
{
    private const char KeySeparator = '\u001F';

    // The vocabulary of 070's schema, in a fixed order, so the same rule gives the same comparison whatever order its body lists.
    private static readonly string[] LineFieldOrder = ["timekeeper", "lineDate", "hours", "narrative", "amount"];

    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var matchFields = BillingRuleHandlers.RequireList(context.Rule.MatchFields, context, "matchFields");
        var priors = ActivePriors(context.Facts);

        var findings = new List<BillingFinding>();
        if (matchFields.Contains("firmInvoiceNumber", StringComparer.Ordinal))
        {
            InvoiceLevel(context, priors, findings);
        }

        var lineFields = LineFieldOrder.Where(f => matchFields.Contains(f, StringComparer.Ordinal)).ToArray();
        if (lineFields.Length > 0)
        {
            LineLevel(context, priors, lineFields, findings);
        }

        return findings;
    }

    /// <summary>Active attempts other than this submission, the lowest attempt number first (ties by tracking number).</summary>
    private static List<BillingPriorSubmission> ActivePriors(BillingFactSet facts)
    {
        var self = facts.Invoice.SubmissionId;
        return facts.PriorSubmissions
            .Where(p => p.Status == BillingPriorStatus.Active && (self is null || p.SubmissionId != self.Value))
            .OrderBy(p => p.Sequence)
            .ThenBy(p => p.TrackingNumber, StringComparer.Ordinal)
            .ToList();
    }

    private static void InvoiceLevel(HandlerContext context, List<BillingPriorSubmission> priors, List<BillingFinding> findings)
    {
        var invoice = context.Facts.Invoice;
        var number = BillingText.Key(invoice.FirmInvoiceNumber);
        if (number is null)
        {
            return;
        }

        var original = priors.FirstOrDefault(p => BillingText.Key(p.FirmInvoiceNumber) == number);
        if (original is null)
        {
            return;
        }

        var values = new[]
        {
            context.HeaderField("invoice.firmInvoiceNumber", BillingFieldNames.FirmInvoiceNumber, BillingText.Text(invoice.FirmInvoiceNumber)),
            HandlerContext.Reference("duplicateOf.trackingNumber", original.TrackingNumber),
        };

        var facts = new FactValues()
            .Add("invoice.firmInvoiceNumber", BillingText.Text(invoice.FirmInvoiceNumber))
            .Add("duplicateOf.trackingNumber", original.TrackingNumber);

        findings.Add(context.Finding(BillingSubject.ForInvoice(), BillingReasonCodes.DuplicateInvoiceNumber, values, facts));
    }

    private static void LineLevel(
        HandlerContext context, List<BillingPriorSubmission> priors, string[] lineFields, List<BillingFinding> findings)
    {
        // First writer wins, in the deterministic order of the priors and then of their lines.
        var priorKeys = new Dictionary<string, BillingPriorSubmission>(StringComparer.Ordinal);
        foreach (var prior in priors)
        {
            foreach (var priorLine in prior.Lines)
            {
                var key = KeyOf(lineFields, priorLine.TimekeeperId, priorLine.TimekeeperName, priorLine.Date, priorLine.Hours, priorLine.Narrative, priorLine.Amount);
                if (key is not null)
                {
                    priorKeys.TryAdd(key, prior);
                }
            }
        }

        var earlierKeys = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in context.Facts.Lines)
        {
            var matched = line.Timekeeper is { IsMatched: true } ? line.Timekeeper.TimekeeperId : null;
            var key = KeyOf(lineFields, matched, line.TimekeeperName, line.Date, line.Hours, line.Narrative, line.Amount);
            if (key is null)
            {
                continue;
            }

            if (priorKeys.TryGetValue(key, out var original))
            {
                findings.Add(LineFinding(context, line, lineFields, BillingReasonCodes.DuplicateOfPriorSubmissionLine, original.TrackingNumber, earlierLineIndex: null));
            }
            else if (earlierKeys.TryGetValue(key, out var earlierIndex))
            {
                findings.Add(LineFinding(context, line, lineFields, BillingReasonCodes.DuplicateOfEarlierLine, context.Facts.Invoice.TrackingNumber, earlierIndex));
            }
            else
            {
                earlierKeys[key] = line.Index;
            }
        }
    }

    private static BillingFinding LineFinding(
        HandlerContext context, BillingLineFacts line, string[] lineFields, string reasonCode, string? originalTrackingNumber, int? earlierLineIndex)
    {
        var invoice = context.Facts.Invoice;
        var values = new List<BillingValueRead> { context.LineTimekeeper(line), context.LineDate(line) };
        if (lineFields.Contains("hours")) values.Add(context.LineHours(line));
        if (lineFields.Contains("narrative")) values.Add(context.LineField(line, "line.narrative", BillingFieldNames.Narrative, BillingText.Text(line.Narrative)));
        if (lineFields.Contains("amount")) values.Add(context.LineField(line, "line.amount", BillingFieldNames.Amount, BillingText.Money(line.Amount)));
        values.Add(context.HeaderField("invoice.firmInvoiceNumber", BillingFieldNames.FirmInvoiceNumber, BillingText.Text(invoice.FirmInvoiceNumber)));
        values.Add(HandlerContext.Reference("duplicateOf.trackingNumber", originalTrackingNumber));
        if (earlierLineIndex is { } earlier)
        {
            values.Add(HandlerContext.Reference("duplicateOf.lineIndex", BillingText.Number(earlier)));
        }

        var facts = new FactValues()
            .Add("invoice.firmInvoiceNumber", BillingText.Text(invoice.FirmInvoiceNumber))
            .Add("line.date", BillingText.Date(line.Date))
            .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName))
            .Add("duplicateOf.trackingNumber", originalTrackingNumber);

        return context.Finding(BillingSubject.ForLine(line.Index), reasonCode, values, facts);
    }

    /// <summary>
    /// The comparison key of a line on the configured fields, or <c>null</c> when the line lacks any of them. Hours and amount
    /// are written without trailing zeros, so 1.0 and 1.00 are one value.
    /// </summary>
    private static string? KeyOf(
        string[] lineFields, Guid? timekeeperId, string? timekeeperName, DateOnly? date, decimal? hours, string? narrative, decimal? amount)
    {
        var parts = new List<string>(lineFields.Length);
        foreach (var field in lineFields)
        {
            string? part = field switch
            {
                "timekeeper" => timekeeperId is { } id ? "id:" + id.ToString("D") : BillingText.Key(timekeeperName) is { } name ? "name:" + name : null,
                "lineDate" => BillingText.Date(date),
                "hours" => BillingText.Number(hours),
                "narrative" => BillingText.Key(narrative),
                "amount" => BillingText.Number(amount),
                _ => null,
            };

            if (part is null)
            {
                return null;
            }

            parts.Add(part);
        }

        return string.Join(KeySeparator, parts);
    }
}
