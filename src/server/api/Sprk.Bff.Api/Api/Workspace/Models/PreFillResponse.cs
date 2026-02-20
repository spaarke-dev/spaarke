namespace Sprk.Bff.Api.Api.Workspace.Models;

/// <summary>
/// Response DTO returned by POST /api/workspace/matters/pre-fill.
/// Contains field values extracted by the AI from the uploaded documents.
/// Unextracted fields are null; successfully extracted fields are listed in PreFilledFields.
/// </summary>
/// <remarks>
/// Field names use camelCase JSON serialization to match the front-end IAiPrefillFields interface:
///   matterTypeName, practiceAreaName, matterName, summary
/// The front-end applies these values directly to ICreateMatterFormState via APPLY_AI_PREFILL action.
/// </remarks>
/// <param name="MatterTypeName">Extracted matter type display name (e.g., "Litigation", "Licensing"). Null if not extracted.</param>
/// <param name="PracticeAreaName">Extracted practice area display name (e.g., "Corporate", "Intellectual Property"). Null if not extracted.</param>
/// <param name="MatterName">Extracted or suggested matter name. Null if not extracted.</param>
/// <param name="Summary">Brief narrative summary of the documents describing the matter context. Null if not extracted.</param>
/// <param name="Confidence">Overall confidence score from 0.0 (no extraction) to 1.0 (high confidence). Set to 0 on AI timeout or complete failure.</param>
/// <param name="PreFilledFields">Array of field names successfully extracted (non-null) from the documents.
/// Only field names that were actually populated are included (e.g., ["matterName", "matterTypeName", "summary"]).</param>
public record PreFillResponse(
    string? MatterTypeName,
    string? PracticeAreaName,
    string? MatterName,
    string? Summary,
    double Confidence,
    string[] PreFilledFields)
{
    /// <summary>
    /// Returns an empty PreFillResponse indicating no fields were extracted (e.g., AI timeout).
    /// </summary>
    public static PreFillResponse Empty() =>
        new(
            MatterTypeName: null,
            PracticeAreaName: null,
            MatterName: null,
            Summary: null,
            Confidence: 0,
            PreFilledFields: []);
}
