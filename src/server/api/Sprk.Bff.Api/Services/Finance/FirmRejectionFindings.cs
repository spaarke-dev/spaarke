using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Services.Signals;
using Sprk.Bff.Api.Services.Signals.Billing;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// The rejection findings a firm may be told about, read from a submission's OWN stored findings (<c>sprk_invoicesubmission.sprk_findingsjson</c>,
/// written by <c>InvoiceSubmissionPersister.BuildFindingsJson</c>) for <see cref="FirmFindingsProjection"/> (ebilling-r1 task 130c, FR-18).
/// </summary>
/// <remarks>
/// <para><b>An allow-list parse.</b> Only the members named here are read: <c>kind</c>, <c>findings[]</c> and, per finding, <c>ruleType</c>,
/// <c>rejects</c>, <c>policyVersionId</c>, <c>subject</c>, <c>line</c>, <c>reasonCode</c>, <c>detailWithheld</c> and <c>valuesRead[]</c> (<c>name</c>
/// and <c>value</c> only). Never read, and so never projected: the stored <c>clause</c> and <c>clauseLocation</c> (the clause reaches a firm only
/// through <see cref="RuleSetService.GetFirmClauseAsync"/>), <c>policyId</c> and <c>policyCode</c> (internal identifiers), confidence and the
/// reliability flag (NFR-10), <c>suggestedAdjustment</c>, the Signal plan (its sentences are the internal wording) and the Procedure snapshot.</para>
/// <para><b>Which findings.</b> Only the findings that REJECTED the submission (<c>rejects</c> true) of an <c>AutoRejected</c> outcome. A
/// <c>Blocked</c> outcome is also a Rejected submission, but it is an internal hold awaiting a reviewer, not a decision a firm was given
/// (FR-38): it yields no finding. Pending, flagged and AI-observation findings never appear.</para>
/// <para><b>Fail closed (FR-24, security-class).</b> A finding whose rule type is not a known Billing type (a name this build does not know, a
/// number, an Ontology type) is REFUSED, whether or not it rejects. A malformed entry (wrong shape or type, a non-rejecting type claiming a
/// rejection, a missing or empty policy version, a line subject with no line) is dropped. Neither is surfaced raw: the Warning carries a stable
/// code and counts only, no content (ADR-015). A Duplicate finding, or one the writer marked <c>detailWithheld</c>, projects no values read: a
/// Duplicate quotes an EARLIER invoice's detail the caller may not be allowed to see (D-112, D-176).</para>
/// <para><b>Component justification (CLAUDE.md section 11).</b> Existing: <c>InvoiceReviewReadService.ParsedFindings</c> is the INTERNAL
/// reader of the same JSON (private; it carries confidence, the stored clause, the suggestion and the Signal plan) and
/// <c>InvoiceSubmissionAfterSteps.StoredFindings</c> reads it for the writers. Extension: neither can be narrowed to an allow-list without a
/// second rule set inside it, and neither refuses an unknown rule type. Cost of doing nothing: FR-18's report cannot be built, or is built by
/// copying the internal reader and its members.</para>
/// </remarks>
internal static class FirmRejectionFindings
{
    /// <summary>The stable log code for stored findings dropped or refused (counts only).</summary>
    internal const string DroppedCode = "EBILLING-FIRM-OUTCOME-REJECTION-FINDINGS-DROPPED";

    /// <summary>The stable log code for a stored findings document that cannot be read as a whole.</summary>
    internal const string UnreadableCode = "EBILLING-FIRM-OUTCOME-REJECTION-FINDINGS-UNREADABLE";

    internal const int MaxFindings = 200;
    internal const int MaxValuesPerFinding = 20;
    internal const int MaxValueTextLength = 200;

    private const int MaxLineIndex = 1_000_000;
    private const int MaxDepth = 32;

    /// <summary>One value a rule read: its name and the value as the submission stored it.</summary>
    internal sealed record ValueRead(string Name, string Value);

    /// <summary>One finding that rejected the submission. <paramref name="LineIndex"/> is zero-based; null for an invoice-level finding.</summary>
    internal sealed record Finding(RuleType RuleType, Guid PolicyVersionId, int? LineIndex, IReadOnlyList<ValueRead> ValuesRead);

    private enum EntryResult
    {
        Accepted,
        NotARejection,
        Malformed,
        UnknownRuleType,
    }

    /// <summary>
    /// The rejection findings in <paramref name="json"/>, in stored order. Never throws for the content: an unreadable document yields none and a
    /// Warning (<see cref="UnreadableCode"/>), dropped or refused entries a Warning (<see cref="DroppedCode"/>) with counts only.
    /// </summary>
    internal static IReadOnlyList<Finding> Parse(string? json, ILogger logger, Guid submissionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                LogUnreadable(logger, submissionId);
                return [];
            }

            // An internal hold (Blocked), an accepted outcome and anything else: no rejection a firm was given.
            if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), nameof(SubmissionEvaluationKind.AutoRejected), StringComparison.Ordinal))
            {
                return [];
            }

            if (!root.TryGetProperty("findings", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                LogUnreadable(logger, submissionId);
                return [];
            }

            var found = new List<Finding>();
            int malformed = 0, unknown = 0, overflow = 0;
            foreach (var entry in list.EnumerateArray())
            {
                var result = ParseEntry(entry, out var finding);
                switch (result)
                {
                    case EntryResult.Accepted when found.Count < MaxFindings:
                        found.Add(finding!);
                        break;
                    case EntryResult.Accepted:
                        overflow++;
                        break;
                    case EntryResult.Malformed:
                        malformed++;
                        break;
                    case EntryResult.UnknownRuleType:
                        unknown++;
                        break;
                }
            }

            if (malformed + unknown + overflow > 0)
            {
                logger.LogWarning(
                    "[{Code}] Submission {Id}: {Dropped} stored finding(s) were not shown ({Malformed} malformed, {UnknownRuleType} of an unknown rule type, {Overflow} over the cap).",
                    DroppedCode, submissionId, malformed + unknown + overflow, malformed, unknown, overflow);
            }

            return found;
        }
        catch (JsonException)
        {
            LogUnreadable(logger, submissionId);
            return [];
        }
    }

    private static void LogUnreadable(ILogger logger, Guid submissionId) =>
        logger.LogWarning("[{Code}] Submission {Id}: the stored findings could not be read; none is shown.", UnreadableCode, submissionId);

    private static EntryResult ParseEntry(JsonElement entry, out Finding? finding)
    {
        finding = null;
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return EntryResult.Malformed;
        }

        // The rule type first: an unknown type is refused whatever else the entry says (FR-24).
        if (!TryText(entry, "ruleType", out var typeText) || typeText is null)
        {
            return EntryResult.Malformed;
        }

        if (!TryKnownBillingType(typeText, out var ruleType))
        {
            return EntryResult.UnknownRuleType;
        }

        if (!entry.TryGetProperty("rejects", out var rejects))
        {
            return EntryResult.NotARejection;
        }

        if (rejects.ValueKind != JsonValueKind.True)
        {
            return rejects.ValueKind == JsonValueKind.False ? EntryResult.NotARejection : EntryResult.Malformed;
        }

        // A rejection by a type that can never reject is a damaged entry (I-5 among them: an AI observation never rejects).
        if (!BillingRuleCatalogue.IsRejectEligible(ruleType))
        {
            return EntryResult.Malformed;
        }

        if (!TryText(entry, "policyVersionId", out var versionText) || versionText is null
            || !Guid.TryParse(versionText, CultureInfo.InvariantCulture, out var versionId) || versionId == Guid.Empty)
        {
            return EntryResult.Malformed;
        }

        if (!TryText(entry, "reasonCode", out var reasonCode) || string.IsNullOrWhiteSpace(reasonCode))
        {
            return EntryResult.Malformed;
        }

        if (!TryText(entry, "subject", out var subject) || subject is null)
        {
            return EntryResult.Malformed;
        }

        int? lineIndex;
        switch (subject)
        {
            case nameof(BillingSubjectKind.Line):
                if (!entry.TryGetProperty("line", out var line) || line.ValueKind != JsonValueKind.Number
                    || !line.TryGetInt32(out var index) || index < 0 || index > MaxLineIndex)
                {
                    return EntryResult.Malformed;
                }

                lineIndex = index;
                break;
            case nameof(BillingSubjectKind.Invoice):
                if (entry.TryGetProperty("line", out var none) && none.ValueKind != JsonValueKind.Null)
                {
                    return EntryResult.Malformed;
                }

                lineIndex = null;
                break;
            default:
                return EntryResult.Malformed;
        }

        var withheld = false;
        if (entry.TryGetProperty("detailWithheld", out var withheldJson))
        {
            if (withheldJson.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return EntryResult.Malformed;
            }

            withheld = withheldJson.ValueKind == JsonValueKind.True;
        }

        var values = new List<ValueRead>();
        if (entry.TryGetProperty("valuesRead", out var read) && read.ValueKind != JsonValueKind.Null)
        {
            if (read.ValueKind != JsonValueKind.Array)
            {
                return EntryResult.Malformed;
            }

            foreach (var item in read.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !TryText(item, "name", out var name) || string.IsNullOrWhiteSpace(name)
                    || !TryText(item, "value", out var value))
                {
                    return EntryResult.Malformed;
                }

                // No text means nothing was read (a withheld value is stored without its text). A Duplicate quotes an earlier invoice, and a
                // withheld group shows no detail: neither projects a value.
                if (!string.IsNullOrWhiteSpace(value) && !withheld && ruleType != RuleType.Duplicate && values.Count < MaxValuesPerFinding)
                {
                    var text = value.Trim();
                    values.Add(new ValueRead(
                        name.Trim(), text.Length <= MaxValueTextLength ? text : text[..MaxValueTextLength].TrimEnd() + "..."));
                }
            }
        }

        finding = new Finding(ruleType, versionId, lineIndex, values);
        return EntryResult.Accepted;
    }

    /// <summary>A member that is a string, or absent / JSON null (then <paramref name="text"/> is null). False for any other type.</summary>
    private static bool TryText(JsonElement element, string name, out string? text)
    {
        text = null;
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        text = value.GetString();
        return true;
    }

    /// <summary>
    /// A rule type this consumer knows: exactly the NAME of a defined member (never a number, and never a comma-joined list: Enum parsing
    /// accepts "100000003" and "A, B", whose OR can be another defined member) of a Billing-domain type that is not the system marker (it has
    /// no rule body and no clause). Anything else is refused (FR-24).
    /// </summary>
    private static bool TryKnownBillingType(string text, out RuleType ruleType)
    {
        ruleType = default;
        if (!Array.Exists(KnownNames, name => string.Equals(name, text, StringComparison.Ordinal)))
        {
            return false;
        }

        ruleType = Enum.Parse<RuleType>(text);
        return RuleDomains.IsBilling(ruleType) && !BillingRuleCatalogue.IsSystemType(ruleType);
    }

    private static readonly string[] KnownNames = Enum.GetNames<RuleType>();
}
