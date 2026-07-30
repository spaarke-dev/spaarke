using System.Globalization;
using System.Text.RegularExpressions;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// The as-built <c>sprk_emailupdatefield.sprk_fieldtype</c> CHOICE integers (task 001 verification /
/// <c>notes/001-operator-schema-verification.md</c>), mapped to the canonical coercion-hint labels the
/// PROPOSE-FIELD-UPDATES Action + <see cref="EmailUpdateFieldCoercion"/> reason over.
/// </summary>
public static class EmailUpdateFieldTypes
{
    public const string Text = "Text";
    public const string Lookup = "Lookup";
    public const string OptionSet = "OptionSet";
    public const string Number = "Number";
    public const string DateTime = "DateTime";
    public const string Boolean = "Boolean";
    public const string Memo = "Memo";
    public const string Currency = "Currency";

    /// <summary>Maps the as-built <c>sprk_fieldtype</c> option-set integer to a canonical label; null for an
    /// unrecognized integer (the caller then skips that allow-list row — never guesses a type).</summary>
    public static string? FromOptionSet(int value) => value switch
    {
        100000000 => Text,
        100000001 => Lookup,
        100000002 => OptionSet,
        100000003 => Number,
        100000004 => DateTime,
        100000005 => Boolean,
        100000006 => Memo,
        100000007 => Currency,
        _ => null,
    };
}

/// <summary>
/// Pure, side-effect-free coercion of a proposed raw string value to its allow-listed field type (FR-09).
/// The propose path STORES proposals (it does not write the record — that is task 031), so coercion here
/// (a) VALIDATES the raw value is shape-compatible with the field type (dropping a Number that is not
/// numeric, a Boolean that is not a bool, a DateTime that is not a date), and (b) NORMALIZES it to a
/// canonical string form task 031's <c>IActionSeam.UpdateRecordAsync</c> can consume. Choice/Lookup labels
/// are left as human-readable labels (031 resolves label → Dataverse value with target metadata);
/// whole-vs-decimal Number resolution is likewise a task-031 apply-time concern (metadata-driven).
/// </summary>
public static class EmailUpdateFieldCoercion
{
    /// <summary>
    /// Attempts to coerce <paramref name="rawValue"/> to the given <paramref name="fieldType"/>. Returns
    /// <c>false</c> (and leaves <paramref name="coerced"/> null) when the raw value cannot be shaped to the
    /// type — the caller DROPS that proposal rather than storing an ill-typed value.
    /// </summary>
    public static bool TryCoerce(string fieldType, string? rawValue, out string? coerced)
    {
        coerced = null;
        var trimmed = rawValue?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        switch (fieldType)
        {
            case EmailUpdateFieldTypes.Text:
            case EmailUpdateFieldTypes.Memo:
            case EmailUpdateFieldTypes.OptionSet: // label preserved; 031 resolves label -> option-set integer
            case EmailUpdateFieldTypes.Lookup:    // label preserved; 031 resolves label -> target record id
                coerced = trimmed;
                return true;

            case EmailUpdateFieldTypes.Number:
            case EmailUpdateFieldTypes.Currency:
                // Strip common currency symbols + thousands separators before parsing; normalize to invariant.
                var numeric = Regex.Replace(trimmed, @"[$£€,\s]", string.Empty);
                if (decimal.TryParse(numeric, NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                {
                    coerced = d.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
                return false;

            case EmailUpdateFieldTypes.Boolean:
                var b = trimmed.ToLowerInvariant();
                if (b is "true" or "yes" or "y" or "1")
                {
                    coerced = "true";
                    return true;
                }
                if (b is "false" or "no" or "n" or "0")
                {
                    coerced = "false";
                    return true;
                }
                return false;

            case EmailUpdateFieldTypes.DateTime:
                if (System.DateTime.TryParse(
                        trimmed,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out var dt))
                {
                    // Date-only when the time component is midnight, else a full ISO-8601 timestamp.
                    coerced = dt.TimeOfDay == TimeSpan.Zero
                        ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                        : dt.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
                    return true;
                }
                return false;

            default:
                // Unknown field type ⇒ never store an ill-typed proposal.
                return false;
        }
    }
}

/// <summary>
/// Pure, side-effect-free verify-cited-text gate (NFR-06). A proposal is stored ONLY when its cited
/// <c>quotedText</c> can be LOCATED in the source communication/attachment text — a proposal whose citation
/// cannot be verified is DROPPED, never stored (a fabricated or hallucinated citation is the exact trust
/// failure this gate prevents). Matching is whitespace- and case-insensitive so trivial normalization
/// differences (the model re-wrapping a line, casing) do not defeat a genuine verbatim quote, while the
/// substantive text must genuinely be present.
/// </summary>
public static class CitationVerifier
{
    /// <summary>Assembles the single searchable source text from the message's subject, body, and (when
    /// present) extracted attachment text — the ONLY material a proposal may cite from.</summary>
    public static string BuildSourceText(string? subject, string? bodyText, string? attachmentText)
    {
        return string.Join(
            "\n",
            new[] { subject, bodyText, attachmentText }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    /// <summary>Returns <c>true</c> when <paramref name="quotedText"/> can be located in
    /// <paramref name="sourceText"/> after whitespace-collapse + case-fold normalization.</summary>
    public static bool IsCitedTextPresent(string? sourceText, string? quotedText)
    {
        if (string.IsNullOrWhiteSpace(quotedText) || string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var haystack = Normalize(sourceText);
        var needle = Normalize(quotedText);
        return needle.Length > 0 && haystack.Contains(needle, StringComparison.Ordinal);
    }

    private static string Normalize(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim().ToLowerInvariant();
}
