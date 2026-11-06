using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Resolution;

namespace Sprk.Bff.Api.Services.Signals.Billing.Handlers;

/// <summary>
/// Rule type 11, AiObservation (design.md 6.1; I-5): a line carries an AI observation of the configured kind (block billing or
/// vague description) with a confidence at or above the rule's threshold. Flag only: an AI observation never rejects a submission.
/// </summary>
/// <remarks>
/// <para><b>The check.</b> Among the line's observations of the configured kind, those with confidence at or above
/// <c>minConfidence</c> qualify (a confidence exactly at the threshold qualifies; below it does not). One finding per line, for the
/// qualifying observation with the highest confidence (the first on a tie). An observation below the rule's own threshold does
/// not fire and is not used.</para>
/// <para><b>Confidence.</b> The observation's own confidence (task 047) is the confidence of the value read, and is marked below
/// the reliability threshold when it is (NFR-05); the reviewer sees the quoted text next to it. The <c>observation</c> token is
/// the kind in the rule's vocabulary (<c>blockBilling</c> / <c>vagueDescription</c>); the quoted text is a value read, not a token.</para>
/// <para><b>Reads:</b> the observation (Observation.BlockBilling or Observation.VagueDescription, with its own confidence) and Date.</para>
/// </remarks>
internal static class AiObservationHandler
{
    internal static IReadOnlyList<BillingFinding> Evaluate(HandlerContext context)
    {
        var kind = context.Rule.Observation switch
        {
            "blockBilling" => InvoiceLineObservationType.BlockBilling,
            "vagueDescription" => InvoiceLineObservationType.VagueDescription,
            _ => throw new BillingRuleRefusedException(
                BillingRuleRefusedException.ParameterMissing,
                context.Rule.RuleType,
                "An AiObservation rule has no known 'observation' kind; it is refused, not evaluated."),
        };

        var minConfidence = BillingRuleHandlers.Require(context.Rule.MinConfidence, context, "minConfidence");
        var fieldName = kind == InvoiceLineObservationType.BlockBilling
            ? BillingFieldNames.ObservationBlockBilling
            : BillingFieldNames.ObservationVagueDescription;
        var reasonCode = kind == InvoiceLineObservationType.BlockBilling
            ? BillingReasonCodes.AiBlockBilling
            : BillingReasonCodes.AiVagueDescription;

        var findings = new List<BillingFinding>();
        foreach (var line in context.Facts.Lines)
        {
            InvoiceLineObservation? best = null;
            foreach (var observation in line.Observations)
            {
                if (observation.Type == kind && observation.Confidence >= minConfidence
                    && (best is null || observation.Confidence > best.Confidence))
                {
                    best = observation;
                }
            }

            if (best is null)
            {
                continue;
            }

            var values = new[]
            {
                context.Extracted(
                    "line.observation", new FieldRef(line.Index, fieldName), BillingText.Text(best.QuotedText), best.Confidence),
                context.LineDate(line),
            };

            var facts = new FactValues()
                .Add("line.date", BillingText.Date(line.Date))
                .Add("observation", context.Rule.Observation)
                .Add("observationConfidence", BillingText.Number(best.Confidence))
                .Add("rule.minConfidence", BillingText.Number(minConfidence));

            findings.Add(context.Finding(BillingSubject.ForLine(line.Index), reasonCode, values, facts));
        }

        return findings;
    }
}
