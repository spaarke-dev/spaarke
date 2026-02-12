using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// AI output from Playbook A: Attachment Classification.
/// Contains classification result only — entity matching (matter/vendor) is handler-side.
/// </summary>
public record ClassificationResult
{
    [JsonPropertyName("classification")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DocumentClassification Classification { get; init; }

    [JsonPropertyName("confidence")]
    public decimal Confidence { get; init; }

    [JsonPropertyName("hints")]
    public InvoiceHints? Hints { get; init; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentClassification
{
    InvoiceCandidate,
    NotInvoice,
    Unknown
}
