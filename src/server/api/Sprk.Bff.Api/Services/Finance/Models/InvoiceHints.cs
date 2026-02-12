using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// Best-effort invoice header hints extracted during classification.
/// All properties nullable — AI extraction is best-effort.
/// </summary>
public record InvoiceHints
{
    [JsonPropertyName("vendorName")]
    public string? VendorName { get; init; }

    [JsonPropertyName("invoiceNumber")]
    public string? InvoiceNumber { get; init; }

    [JsonPropertyName("invoiceDate")]
    public string? InvoiceDate { get; init; }  // YYYY-MM-DD format string

    [JsonPropertyName("totalAmount")]
    public decimal? TotalAmount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    [JsonPropertyName("matterReference")]
    public string? MatterReference { get; init; }
}
