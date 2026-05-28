using System.Text.Json;

namespace Sprk.Bff.Api.Services.Ai.Insights.Extraction;

/// <summary>
/// Mechanical, zero-LLM projection helper that transposes the validated
/// <see cref="OutcomeExtractionResponse"/> (three-sibling-map shape: fields / evidence /
/// confidence) into the single <see cref="ExtractionResult.Fields"/> dictionary that the
/// downstream <see cref="IObservationEmitter"/> consumes.
/// <para>
/// Per <c>SPEC-phase-1-minimum.md §3.4</c>: fields the LLM returned as null MUST be omitted
/// from <see cref="ExtractionResult.Fields"/> (D-P10 only sees fields the LLM actually
/// attempted; "not present" and "extracted with zero confidence" are different).
/// </para>
/// <para>
/// Lives in Zone A per <c>SPEC §3.5</c>. Pure static — singleton-safe by construction.
/// </para>
/// </summary>
public static class OutcomeExtractionProjection
{
    /// <summary>
    /// Builds an <see cref="ExtractionResult"/> from a validated
    /// <see cref="OutcomeExtractionResponse"/> + provenance inputs (subject, document ref,
    /// tenant, asOf, scope). Caller MUST validate the response first via
    /// <see cref="OutcomeExtractionResponseValidator.Validate(string)"/>; passing an
    /// unvalidated response risks producing fields with mismatched evidence/confidence.
    /// </summary>
    /// <param name="response">The validated LLM response.</param>
    /// <param name="subject">Observation subject (e.g., <c>matter:M-2024-0341</c>).</param>
    /// <param name="documentRef">Source document reference (e.g., <c>spe://drive/.../item/closing-letter.docx</c>).</param>
    /// <param name="tenantId">Tenant identifier (propagates to every emitted Observation).</param>
    /// <param name="asOf">Wall-clock timestamp the extraction completed at.</param>
    /// <param name="scope">Optional matter-scope context for the emitted Observations.</param>
    public static ExtractionResult Build(
        OutcomeExtractionResponse response,
        string subject,
        string documentRef,
        string tenantId,
        DateTimeOffset asOf,
        ExtractionScope? scope = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentRef);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var fields = new Dictionary<string, ExtractionField>(StringComparer.OrdinalIgnoreCase);

        // outcomeCategory (enum -> string)
        if (response.OutcomeCategory is not null)
        {
            fields["outcomeCategory"] = new ExtractionField
            {
                Value = JsonValue(response.OutcomeCategory),
                Quote = response.Evidence.OutcomeCategory ?? string.Empty,
                Confidence = response.Confidence.OutcomeCategory,
                DisplayHint = "enum"
            };
        }

        // settlementAmount (decimal -> number)
        if (response.SettlementAmount is { } amount)
        {
            // Currency hint chooses between currency-usd (most common) and a generic "currency"
            // when the LLM returned a non-USD ISO code.
            var displayHint = string.Equals(response.SettlementCurrency, "USD", StringComparison.Ordinal)
                || string.IsNullOrEmpty(response.SettlementCurrency)
                    ? "currency-usd"
                    : $"currency-{response.SettlementCurrency.ToLowerInvariant()}";

            fields["settlementAmount"] = new ExtractionField
            {
                Value = JsonValue(amount),
                Quote = response.Evidence.SettlementAmount ?? string.Empty,
                Confidence = response.Confidence.SettlementAmount,
                DisplayHint = displayHint
            };
        }

        // outcomeDate (ISO date string -> string)
        if (!string.IsNullOrEmpty(response.OutcomeDate))
        {
            fields["outcomeDate"] = new ExtractionField
            {
                Value = JsonValue(response.OutcomeDate),
                Quote = response.Evidence.OutcomeDate ?? string.Empty,
                Confidence = response.Confidence.OutcomeDate,
                DisplayHint = "date"
            };
        }

        // matterDurationDays (int -> number)
        if (response.MatterDurationDays is { } days)
        {
            fields["matterDurationDays"] = new ExtractionField
            {
                Value = JsonValue(days),
                Quote = response.Evidence.MatterDurationDays ?? string.Empty,
                Confidence = response.Confidence.MatterDurationDays,
                DisplayHint = "duration-days"
            };
        }

        // NOTE: keyTerms[] is NOT emitted as per-field Observations in Phase 1 — they are
        // descriptive metadata, not gated facts. SPEC-phase-1-minimum.md §3.4 lists the gated
        // fields explicitly. keyTerms surface on the D-P11 review row (Phase 1.5+).

        return new ExtractionResult
        {
            Subject = subject,
            DocumentRef = documentRef,
            TenantId = tenantId,
            ProducedBy = new ProducerIdentity
            {
                Kind = "playbook",
                Id = "playbook://outcome-extraction@v1",
                Version = "v1"
            },
            AsOf = asOf,
            Fields = fields,
            Scope = scope
        };
    }

    /// <summary>
    /// Wraps an arbitrary CLR value as a <see cref="JsonElement"/> for the
    /// <see cref="ExtractionField.Value"/> slot. Uses <see cref="JsonSerializer"/> so the
    /// produced element round-trips cleanly through the Observation envelope.
    /// </summary>
    private static JsonElement JsonValue<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonDocument.Parse(json).RootElement.Clone();
    }
}
