using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// Extracted invoice header facts.
/// </summary>
public record InvoiceHeader
{
    [JsonPropertyName("invoiceNumber")]
    public string InvoiceNumber { get; init; } = null!;

    [JsonPropertyName("invoiceDate")]
    public string InvoiceDate { get; init; } = null!;  // YYYY-MM-DD

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = null!;

    [JsonPropertyName("vendorName")]
    public string VendorName { get; init; } = null!;

    [JsonPropertyName("vendorAddress")]
    public string? VendorAddress { get; init; }

    [JsonPropertyName("paymentTerms")]
    public string? PaymentTerms { get; init; }
}
