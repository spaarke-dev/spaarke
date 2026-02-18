using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// AI output from Playbook B: Invoice Extraction.
/// Does NOT include VisibilityState — set deterministically in handler code, never by LLM.
/// </summary>
public record ExtractionResult
{
    [JsonPropertyName("header")]
    public InvoiceHeader Header { get; init; } = null!;

    [JsonPropertyName("lineItems")]
    public BillingEventLine[] LineItems { get; init; } = [];

    [JsonPropertyName("extractionConfidence")]
    public decimal ExtractionConfidence { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
