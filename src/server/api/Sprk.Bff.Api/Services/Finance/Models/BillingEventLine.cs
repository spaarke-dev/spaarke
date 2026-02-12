using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// A single billing line item extracted from an invoice.
/// CostType is string ("Fee"/"Expense") — conversion to OptionSetValue in handler.
/// </summary>
public record BillingEventLine
{
    [JsonPropertyName("lineNumber")]
    public int LineNumber { get; init; }

    [JsonPropertyName("description")]
    public string Description { get; init; } = null!;

    [JsonPropertyName("costType")]
    public string CostType { get; init; } = null!;  // "Fee" or "Expense"

    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    [JsonPropertyName("currency")]
    public string Currency { get; init; } = null!;

    [JsonPropertyName("eventDate")]
    public string? EventDate { get; init; }  // YYYY-MM-DD or null

    [JsonPropertyName("roleClass")]
    public string? RoleClass { get; init; }  // Partner, Associate, Paralegal, Other, Unknown

    [JsonPropertyName("hours")]
    public decimal? Hours { get; init; }

    [JsonPropertyName("rate")]
    public decimal? Rate { get; init; }
}
