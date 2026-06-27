using System.Globalization;
using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Mechanical, zero-LLM validator for the <c>outcome-extraction@v1</c> LLM response. Rejects
/// malformed output BEFORE the three downstream mechanical gates (D-P9 grounding,
/// D-P10 confidence, D-P10 emission) consume it, so a malformed response can be retried once
/// (per the playbook node config <c>outputSchema.retryOnce: true</c>) instead of polluting the
/// <c>spaarke-insights-index</c>.
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c>. Singleton-safe (stateless pure functions over inputs).
/// Implemented in C# rather than via a JSON-Schema runtime to keep the BFF publish size flat
/// (zero new package adds per the BFF binding governance §10) — the <c>.schema.json</c>
/// sibling file remains the canonical documented contract for external readers.
/// </para>
/// </summary>
public static class OutcomeExtractionResponseValidator
{
    private static readonly HashSet<string> AllowedOutcomeCategories = new(StringComparer.Ordinal)
    {
        "favorable_to_client",
        "unfavorable_to_client",
        "neutral",
        "mixed",
        "unclear"
    };

    /// <summary>
    /// Per <c>SPEC-phase-1-minimum.md §3.4</c>: when a field is non-null its evidence quote AND
    /// confidence > 0.0 are REQUIRED. The reverse is also required: a null field must have
    /// null evidence and confidence == 0.0 (the prompt says so explicitly). Both directions are
    /// validated so we catch hallucinated quotes for absent fields too.
    /// </summary>
    private static readonly string[] GatedFields =
    {
        "outcomeCategory",
        "settlementAmount",
        "outcomeDate",
        "matterDurationDays"
    };

    /// <summary>
    /// Parses + validates the raw JSON. On success returns the strongly-typed response; on
    /// failure returns an <see cref="OutcomeExtractionValidationResult.Failure"/> with the
    /// violation list.
    /// </summary>
    /// <param name="rawJson">
    /// The raw JSON string returned by the LLM. Any preamble (markdown fences, commentary) MUST
    /// be stripped by the caller before invoking this validator.
    /// </param>
    public static OutcomeExtractionValidationResult Validate(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return OutcomeExtractionValidationResult.Failure("Response body was empty or whitespace.");
        }

        OutcomeExtractionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<OutcomeExtractionResponse>(rawJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return OutcomeExtractionValidationResult.Failure($"Response was not valid JSON: {ex.Message}");
        }

        if (parsed is null)
        {
            return OutcomeExtractionValidationResult.Failure("Response deserialized to null.");
        }

        var errors = new List<string>();

        // outcomeCategory enum
        if (parsed.OutcomeCategory is not null && !AllowedOutcomeCategories.Contains(parsed.OutcomeCategory))
        {
            errors.Add(
                $"outcomeCategory '{parsed.OutcomeCategory}' is not in the allowed enum " +
                $"[{string.Join(", ", AllowedOutcomeCategories)}].");
        }

        // settlementAmount sign
        if (parsed.SettlementAmount is < 0)
        {
            errors.Add($"settlementAmount must be >= 0 (got {parsed.SettlementAmount}).");
        }

        // settlementCurrency: ISO 4217 (three uppercase letters) when present
        if (!string.IsNullOrEmpty(parsed.SettlementCurrency) && !IsIso4217(parsed.SettlementCurrency))
        {
            errors.Add($"settlementCurrency '{parsed.SettlementCurrency}' is not a valid ISO 4217 code (expected 3 uppercase letters).");
        }

        // outcomeDate: ISO 8601 yyyy-MM-dd when present
        if (!string.IsNullOrEmpty(parsed.OutcomeDate) && !IsIsoDate(parsed.OutcomeDate))
        {
            errors.Add($"outcomeDate '{parsed.OutcomeDate}' is not a valid ISO 8601 date (expected yyyy-MM-dd).");
        }

        // matterDurationDays sign
        if (parsed.MatterDurationDays is < 0)
        {
            errors.Add($"matterDurationDays must be >= 0 (got {parsed.MatterDurationDays}).");
        }

        // Per-field invariants: non-null field <=> non-null evidence quote AND confidence > 0.0
        // (and null field <=> null evidence AND confidence == 0.0). Catches hallucination both
        // ways before downstream gates see it.
        foreach (var fieldName in GatedFields)
        {
            var (hasValue, evidenceQuote, confidence) = ProjectField(parsed, fieldName);

            if (hasValue)
            {
                if (string.IsNullOrWhiteSpace(evidenceQuote))
                {
                    errors.Add($"Field '{fieldName}' is non-null but evidence quote is missing — schema-invalid (SPEC §3.4 mandates verbatim evidence for every extracted value).");
                }
                if (confidence <= 0.0)
                {
                    errors.Add($"Field '{fieldName}' is non-null but confidence is {confidence:F2} — non-null fields require confidence > 0.0.");
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(evidenceQuote))
                {
                    errors.Add($"Field '{fieldName}' is null but evidence quote was supplied — schema-invalid (hallucinated quote for absent field).");
                }
                if (confidence > 0.0)
                {
                    errors.Add($"Field '{fieldName}' is null but confidence is {confidence:F2} — null fields require confidence == 0.0.");
                }
            }

            // Confidence range
            if (confidence < 0.0 || confidence > 1.0)
            {
                errors.Add($"Field '{fieldName}' confidence {confidence:F2} is outside [0.0, 1.0].");
            }
        }

        // keyTerms: each entry must have non-empty term + description
        for (var i = 0; i < parsed.KeyTerms.Count; i++)
        {
            var keyTerm = parsed.KeyTerms[i];
            if (string.IsNullOrWhiteSpace(keyTerm.Term))
            {
                errors.Add($"keyTerms[{i}].term is empty.");
            }
            if (string.IsNullOrWhiteSpace(keyTerm.Description))
            {
                errors.Add($"keyTerms[{i}].description is empty.");
            }
        }

        return errors.Count == 0
            ? OutcomeExtractionValidationResult.Success(parsed)
            : OutcomeExtractionValidationResult.Failure(errors);
    }

    /// <summary>Projects (value-is-non-null, evidence quote, confidence) for one gated field.</summary>
    private static (bool HasValue, string? EvidenceQuote, double Confidence) ProjectField(
        OutcomeExtractionResponse response, string fieldName) => fieldName switch
        {
            "outcomeCategory" => (
                response.OutcomeCategory is not null,
                response.Evidence.OutcomeCategory,
                response.Confidence.OutcomeCategory),
            "settlementAmount" => (
                response.SettlementAmount is not null,
                response.Evidence.SettlementAmount,
                response.Confidence.SettlementAmount),
            "outcomeDate" => (
                response.OutcomeDate is not null,
                response.Evidence.OutcomeDate,
                response.Confidence.OutcomeDate),
            "matterDurationDays" => (
                response.MatterDurationDays is not null,
                response.Evidence.MatterDurationDays,
                response.Confidence.MatterDurationDays),
            _ => throw new ArgumentOutOfRangeException(nameof(fieldName), fieldName, "Unknown gated field name.")
        };

    private static bool IsIso4217(string code)
    {
        if (code.Length != 3) return false;
        foreach (var ch in code)
        {
            if (ch < 'A' || ch > 'Z') return false;
        }
        return true;
    }

    private static bool IsIsoDate(string date)
    {
        return DateTime.TryParseExact(
            date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = false
    };
}

/// <summary>
/// Outcome of validating a raw <c>outcome-extraction@v1</c> LLM response. Use
/// <see cref="IsValid"/> to branch; the <see cref="Errors"/> list is non-empty iff
/// <see cref="IsValid"/> is false.
/// </summary>
public sealed record OutcomeExtractionValidationResult
{
    public bool IsValid { get; init; }
    public OutcomeExtractionResponse? Response { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    public static OutcomeExtractionValidationResult Success(OutcomeExtractionResponse response) => new()
    {
        IsValid = true,
        Response = response,
        Errors = Array.Empty<string>()
    };

    public static OutcomeExtractionValidationResult Failure(string error) => new()
    {
        IsValid = false,
        Response = null,
        Errors = new[] { error }
    };

    public static OutcomeExtractionValidationResult Failure(IReadOnlyList<string> errors) => new()
    {
        IsValid = false,
        Response = null,
        Errors = errors
    };
}
