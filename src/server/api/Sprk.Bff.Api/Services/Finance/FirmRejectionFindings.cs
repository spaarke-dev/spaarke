using System.Globalization;
using System.Reflection;
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
/// <c>rejects</c>, <c>policyVersionId</c>, <c>subject</c>, <c>line</c>, <c>reasonCode</c>, <c>detailWithheld</c> and <c>valuesRead[]</c> (<c>name</c>,
/// <c>value</c> and whether <c>field</c> is present). A value is shown only when it was extracted from the firm's own invoice (the entry names
/// its field, the field is a known extracted field of the invoice (a <see cref="BillingFieldNames"/> constant) and it is not one the AI assigned: <see cref="BillingFieldNames.IsAiAssigned"/>) and its text is not an identifier. Never read, and so never projected: the stored <c>clause</c> and <c>clauseLocation</c> (the clause reaches a firm only
/// through <see cref="RuleSetService.GetFirmClauseAsync"/>), <c>policyId</c> and <c>policyCode</c> (internal identifiers), confidence and the
/// reliability flag (NFR-10), <c>suggestedAdjustment</c>, the Signal plan (its sentences are the internal wording) and the Procedure snapshot.</para>
/// <para><b>Which findings.</b> Only the findings that REJECTED the submission (<c>rejects</c> true) of an <c>AutoRejected</c> outcome. A
/// <c>Blocked</c> outcome is also a Rejected submission, but it is an internal hold awaiting a reviewer, not a decision a firm was given
/// (FR-38): it yields no finding. Pending, flagged and AI-observation findings never appear.</para>
/// <para><b>Fail closed (FR-24, security-class).</b> A finding whose rule type is not a known Billing type (a name this build does not know, a
/// number, an Ontology type) is REFUSED, whether or not it rejects. A malformed entry (wrong shape or type, a non-rejecting type claiming a
/// rejection, a missing or empty policy version, a line subject with no line) is dropped. Neither is surfaced raw: the Warning carries a stable
/// code and counts only, no content (ADR-015). A dropped or refused entry that claims to reject is counted in <see cref="ParseResult.NotShown"/>, so
/// the list is never presented as complete when a rejecting finding could not be shown. A Duplicate finding, or one the writer marked <c>detailWithheld</c>, projects no values read: a
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
    internal const int MaxValueNameLength = 100;

    private const int MaxLineIndex = 1_000_000;
    private const int MaxDepth = 32;

    /// <summary>One value a rule read: its name and the value as the submission stored it.</summary>
    internal sealed record ValueRead(string Name, string Value);

    /// <summary>One finding that rejected the submission. <paramref name="LineIndex"/> is zero-based; null for an invoice-level finding.</summary>
    internal sealed record Finding(RuleType RuleType, Guid PolicyVersionId, int? LineIndex, IReadOnlyList<ValueRead> ValuesRead);

    /// <summary>
    /// The result of a parse: the findings to show and how many rejecting findings could not be shown (<paramref name="NotShown"/>; zero when
    /// none): the valid ones past <see cref="MaxFindings"/>, and the entries that claim to reject (<c>rejects</c> true) but were dropped as
    /// malformed or refused as an unknown or non-rejecting type or reason. An entry that does not reject (<c>rejects</c> false or absent) is
    /// never counted.
    /// </summary>
    internal sealed record ParseResult(IReadOnlyList<Finding> Findings, int NotShown)
    {
        internal static ParseResult Empty { get; } = new([], 0);
    }

    private enum EntryResult
    {
        Accepted,
        NotARejection,
        Malformed,
        UnknownRuleType,
    }

    /// <summary>
    /// The rejection findings in <paramref name="json"/>, in stored order, and the number of valid rejection findings left out over the cap.
    /// Never throws for the content: an unreadable document yields none and a Warning (<see cref="UnreadableCode"/>); an entry that cannot be
    /// read (wrong shape, or text that cannot be decoded) is dropped on its own and counted, and dropped or refused entries give a Warning
    /// (<see cref="DroppedCode"/>) with counts only.
    /// </summary>
    internal static ParseResult Parse(string? json, ILogger logger, Guid submissionId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ParseResult.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = MaxDepth });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                LogUnreadable(logger, submissionId);
                return ParseResult.Empty;
            }

            // An internal hold (Blocked), an accepted outcome and anything else: no rejection a firm was given.
            if (!root.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
                || !string.Equals(kind.GetString(), nameof(SubmissionEvaluationKind.AutoRejected), StringComparison.Ordinal))
            {
                return ParseResult.Empty;
            }

            if (!root.TryGetProperty("findings", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                LogUnreadable(logger, submissionId);
                return ParseResult.Empty;
            }

            var found = new List<Finding>();
            int malformed = 0, unknown = 0, overflow = 0, droppedRejecting = 0;
            foreach (var entry in list.EnumerateArray())
            {
                // One entry that cannot be read (for example text that cannot be decoded) is dropped on its own; it does not make the
                // whole document unreadable.
                Finding? finding;
                EntryResult result;
                try
                {
                    result = ParseEntry(entry, out finding);
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException or JsonException)
                {
                    result = EntryResult.Malformed;
                    finding = null;
                }

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
                        droppedRejecting += ClaimsToReject(entry) ? 1 : 0;
                        break;
                    case EntryResult.UnknownRuleType:
                        unknown++;
                        droppedRejecting += ClaimsToReject(entry) ? 1 : 0;
                        break;
                }
            }

            if (malformed + unknown + overflow > 0)
            {
                logger.LogWarning(
                    "[{Code}] Submission {Id}: {Dropped} stored finding(s) were not shown ({Malformed} malformed, {UnknownRuleType} of an unknown rule type, {Overflow} over the cap).",
                    DroppedCode, submissionId, malformed + unknown + overflow, malformed, unknown, overflow);
            }

            return new ParseResult(found, overflow + droppedRejecting);
        }
        catch (JsonException)
        {
            LogUnreadable(logger, submissionId);
            return ParseResult.Empty;
        }
    }

    /// <summary>True when the entry is an object whose <c>rejects</c> member is JSON true (reads no other member; never throws).</summary>
    private static bool ClaimsToReject(JsonElement entry) =>
        entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("rejects", out var rejects) && rejects.ValueKind == JsonValueKind.True;

    /// <summary>
    /// <see cref="BillingFieldNames.IsAiAssigned"/> without regard to case, on the trimmed name: the projection must not show a value the AI
    /// assigned whatever the casing a stored entry carries.
    /// </summary>
    private static bool IsAiAssignedField(string field)
    {
        var name = field.Trim();
        return name.StartsWith(BillingFieldNames.ObservationPrefix, StringComparison.OrdinalIgnoreCase)
            || BillingFieldNames.AiAssigned.Any(ai => string.Equals(ai, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The extracted-field names a value may carry to be shown: every <see cref="BillingFieldNames"/> constant that names a field of the firm's
    /// own invoice (not <see cref="BillingFieldNames.ObservationPrefix"/>, not AI-assigned), compared without regard to case. Built once from the
    /// constants, so a field added to <see cref="BillingFieldNames"/> is allowed with no second list to keep in step.
    /// </summary>
    private static readonly IReadOnlySet<string> ExtractedFieldNames = typeof(BillingFieldNames)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
        .Select(f => (string)f.GetRawConstantValue()!)
        .Where(name => name != BillingFieldNames.ObservationPrefix && !BillingFieldNames.IsAiAssigned(name))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the trimmed <paramref name="field"/> names an extracted field of the invoice that is not AI-assigned (an allow-list).</summary>
    private static bool IsShownField(string field) =>
        ExtractedFieldNames.Contains(field.Trim()) && !IsAiAssignedField(field);

    internal static void LogUnreadable(ILogger logger, Guid submissionId) =>
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

        // A hold-only type never rejects, and a reason the type cannot reject on is not a rejection (the catalogue's allow-list).
        if (!TryText(entry, "reasonCode", out var reasonCode) || string.IsNullOrWhiteSpace(reasonCode)
            || BillingRuleCatalogue.IsHoldOnly(ruleType) || !BillingRuleCatalogue.IsRejectCapableReason(ruleType, reasonCode))
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

                // Only a value extracted from the firm's own invoice is shown: the writer records the extracted field's name with it, and
                // a value read from the system's own tables carries none. A Duplicate quotes an earlier invoice, and a withheld group shows
                // no detail: neither projects a value.
                var fieldName = item.TryGetProperty("field", out var fieldJson) && fieldJson.ValueKind == JsonValueKind.String
                    ? fieldJson.GetString()
                    : null;
                var extracted = !string.IsNullOrWhiteSpace(fieldName);

                // Only a value whose field is a known extracted field of the invoice is shown (an allow-list); a value the AI assigned
                // (a code, an observation) is the model's output, not something on the firm's invoice, and an unrecognised field is not shown.
                if (!extracted || !IsShownField(fieldName!)
                    || withheld || ruleType == RuleType.Duplicate || values.Count >= MaxValuesPerFinding)
                {
                    continue;
                }

                // A name that is too long is not a name a firm can use: the value is dropped.
                var trimmedName = name.Trim();
                if (trimmedName.Length > MaxValueNameLength)
                {
                    continue;
                }

                // A value with no text is kept as an empty value: it names the field that was read and found empty (a missing field).
                var text = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

                // Backstop: a value that is an identifier is never shown, whatever its entry says.
                if (Guid.TryParse(text, out _))
                {
                    continue;
                }

                values.Add(new ValueRead(trimmedName, Truncate(text)));
            }
        }

        finding = new Finding(ruleType, versionId, lineIndex, values);
        return EntryResult.Accepted;
    }

    /// <summary>
    /// <paramref name="text"/> cut to <see cref="MaxValueTextLength"/> characters and marked with "..."; a cut that would fall between the two
    /// halves of a surrogate pair (one character) steps back so the text stays valid.
    /// </summary>
    private static string Truncate(string text)
    {
        if (text.Length <= MaxValueTextLength)
        {
            return text;
        }

        var cut = MaxValueTextLength;
        if (char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }

        return text[..cut].TrimEnd() + "...";
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
