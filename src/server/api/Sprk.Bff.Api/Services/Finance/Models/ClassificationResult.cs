using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// Document classification verdict the attachment pipeline stores in <c>sprk_classification</c> (by name).
/// Produced by <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.IInvoiceAi.ClassifyAsync"/> (task 042) and mapped by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocumentClassification
{
    InvoiceCandidate,
    NotInvoice,
    Unknown
}
