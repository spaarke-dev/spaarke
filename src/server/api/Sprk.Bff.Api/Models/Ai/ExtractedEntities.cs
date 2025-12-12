using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Named entities extracted from a document by AI analysis.
/// Each category contains entities explicitly mentioned in the document text.
/// </summary>
public class ExtractedEntities
{
    /// <summary>
    /// Company, law firm, agency, or organization names mentioned in the document.
    /// </summary>
    [JsonPropertyName("organizations")]
    public string[] Organizations { get; set; } = [];

    /// <summary>
    /// Person names mentioned in the document.
    /// For emails, includes sender and recipient names.
    /// </summary>
    [JsonPropertyName("people")]
    public string[] People { get; set; } = [];

    /// <summary>
    /// Monetary values, quantities, or percentages mentioned in the document.
    /// Examples: "$10,000", "50%", "100 units"
    /// </summary>
    [JsonPropertyName("amounts")]
    public string[] Amounts { get; set; } = [];

    /// <summary>
    /// Specific dates, date ranges, or time periods mentioned in the document.
    /// Examples: "January 15, 2025", "Q1 2025", "next 30 days"
    /// </summary>
    [JsonPropertyName("dates")]
    public string[] Dates { get; set; } = [];

    /// <summary>
    /// Classification of the document type.
    /// Values: contract, invoice, proposal, report, letter, memo, email, agreement, statement, other
    /// </summary>
    [JsonPropertyName("documentType")]
    public string DocumentType { get; set; } = "other";

    /// <summary>
    /// Matter numbers, case IDs, invoice numbers, PO numbers, reference codes, and other identifiers.
    /// These are high-value for record matching.
    /// </summary>
    [JsonPropertyName("references")]
    public string[] References { get; set; } = [];
}
