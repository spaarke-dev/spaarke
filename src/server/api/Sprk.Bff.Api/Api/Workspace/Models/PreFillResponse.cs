namespace Sprk.Bff.Api.Api.Workspace.Models;

/// <summary>
/// Response DTO returned by POST /api/workspace/matters/pre-fill.
/// Contains field values extracted by the AI from the uploaded documents.
/// Unextracted fields are null; successfully extracted fields are listed in PreFilledFields.
/// </summary>
/// <param name="MatterType">Extracted matter type (e.g., "Litigation", "Transactional", "Advisory"). Null if not extracted.</param>
/// <param name="PracticeArea">Extracted practice area (e.g., "Corporate", "Employment Law", "Real Estate"). Null if not extracted.</param>
/// <param name="MatterName">Extracted or suggested matter name. Null if not extracted.</param>
/// <param name="Organization">Extracted client or counterparty organization name. Null if not extracted.</param>
/// <param name="EstimatedBudget">Extracted estimated budget amount in decimal form. Null if not extracted.</param>
/// <param name="KeyParties">Comma-separated list of key parties (individuals or organizations) extracted from documents. Null if not extracted.</param>
/// <param name="Summary">Brief narrative summary of the documents describing the matter context. Null if not extracted.</param>
/// <param name="Confidence">Overall confidence score from 0.0 (no extraction) to 1.0 (high confidence). Set to 0 on AI timeout or complete failure.</param>
/// <param name="PreFilledFields">Array of field names successfully extracted (non-null) from the documents.
/// Only field names that were actually populated are included (e.g., ["matterName", "organization", "summary"]).</param>
public record PreFillResponse(
    string? MatterType,
    string? PracticeArea,
    string? MatterName,
    string? Organization,
    decimal? EstimatedBudget,
    string? KeyParties,
    string? Summary,
    double Confidence,
    string[] PreFilledFields)
{
    /// <summary>
    /// Returns an empty PreFillResponse indicating no fields were extracted (e.g., AI timeout).
    /// </summary>
    public static PreFillResponse Empty() =>
        new(
            MatterType: null,
            PracticeArea: null,
            MatterName: null,
            Organization: null,
            EstimatedBudget: null,
            KeyParties: null,
            Summary: null,
            Confidence: 0,
            PreFilledFields: []);
}
