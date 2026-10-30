using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 1, ApprovedTimekeeper (design.md 6.1; spec FR-23): a fee line's timekeeper must be an Approved timekeeper of the
/// firm on the line date. Fires for every fee line whose roster match (task 046) did not produce a timekeeper, whatever the
/// reason: an unknown name, a known person who is not approved on the line date, an ambiguous or incomplete name, an AI-assisted
/// match that was not clear enough, a line with no name or no date. The finding keeps 046's reason as its reason code.
/// </summary>
/// <remarks>
/// <b>It only holds (D-89).</b> An unmatched timekeeper is a reference-missing field, so 046 caps its combined confidence at 0.80,
/// below the 0.90 threshold; this handler reports that confidence unchanged, and the finding therefore reads a low-confidence
/// value. It cannot be a rejection (task 073 requires every field read to be reliable). Nothing here maps an unmatched name to
/// another timekeeper: the match is 046's, and a line it left unmatched stays unmatched (FR-23).
/// <para><b>Judged from</b> the roster match only. The roster (017) has no core-record scope, so "approved for the firm (and core
/// record)" is judged at the firm level; a per-core-record roster would extend the resolution, not this handler.</para>
/// <para><b>Reads:</b> TimekeeperAsBilled, Date (both extracted). Expense lines have no timekeeper and are skipped.</para>
/// </remarks>
internal static class ApprovedTimekeeperHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            if (line.LineType != InvoiceLineType.Fee || line.Timekeeper is { IsMatched: true })
            {
                continue;
            }

            var reason = line.Timekeeper?.Reason ?? TimekeeperUnmatchedReason.None;
            var values = new[]
            {
                context.LineTimekeeper(line),
                context.LineDate(line),
                HandlerContext.Reference("timekeeper.unmatchedReason", reason.ToString()),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("line.timekeeperName", BillingText.Text(line.TimekeeperName));

            findings.Add(context.Finding(BillingSubject.ForLine(line.Index), ReasonCodeOf(reason), values, facts));
        }

        return findings;
    }

    private static string ReasonCodeOf(TimekeeperUnmatchedReason reason) => reason switch
    {
        TimekeeperUnmatchedReason.NameMissing => BillingReasonCodes.TimekeeperNameMissing,
        TimekeeperUnmatchedReason.LineDateMissing => BillingReasonCodes.TimekeeperLineDateMissing,
        TimekeeperUnmatchedReason.NameIncomplete => BillingReasonCodes.TimekeeperNameIncomplete,
        TimekeeperUnmatchedReason.NotOnRoster => BillingReasonCodes.TimekeeperNotOnRoster,
        TimekeeperUnmatchedReason.NotApprovedOnLineDate => BillingReasonCodes.TimekeeperNotApprovedOnLineDate,
        TimekeeperUnmatchedReason.Ambiguous => BillingReasonCodes.TimekeeperAmbiguous,
        TimekeeperUnmatchedReason.AiBelowThreshold => BillingReasonCodes.TimekeeperAiBelowThreshold,
        TimekeeperUnmatchedReason.AiNotClearlyAhead => BillingReasonCodes.TimekeeperAiNotClearlyAhead,
        TimekeeperUnmatchedReason.AiUnavailable => BillingReasonCodes.TimekeeperAiUnavailable,

        // None, or no resolution at all on a fee line: the match was not established, so the line is not approved.
        _ => BillingReasonCodes.TimekeeperUnmatched,
    };
}
