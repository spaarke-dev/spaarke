namespace Sprk.Bff.Api.Api.Workspace.Models;

/// <summary>
/// Response DTO returned by POST /api/workspace/matters/pre-fill.
/// Contains field values extracted by the AI from the uploaded documents.
/// Unextracted fields are null; successfully extracted fields are listed in PreFilledFields.
/// </summary>
/// <remarks>
/// Field names use camelCase JSON serialization to match the front-end IAiPrefillFields interface.
/// The front-end applies these values directly to form state via APPLY_AI_PREFILL action,
/// then fuzzy-matches display names against Dataverse lookup tables to resolve GUIDs.
/// </remarks>
public record PreFillResponse(
    string? MatterTypeName,
    string? PracticeAreaName,
    string? MatterName,
    string? Summary,
    string? AssignedAttorneyName,
    string? AssignedParalegalName,
    string? AssignedOutsideCounselName,
    double Confidence,
    string[] PreFilledFields,
    string? _debugRawAiResponse = null)
{
    /// <summary>
    /// Returns an empty PreFillResponse indicating no fields were extracted (e.g., AI timeout).
    /// </summary>
    public static PreFillResponse Empty(string? debugInfo = null) =>
        new(
            MatterTypeName: null,
            PracticeAreaName: null,
            MatterName: null,
            Summary: null,
            AssignedAttorneyName: null,
            AssignedParalegalName: null,
            AssignedOutsideCounselName: null,
            Confidence: 0,
            PreFilledFields: [],
            _debugRawAiResponse: debugInfo);
}

/// <summary>
/// Response DTO returned by POST /api/workspace/projects/pre-fill.
/// Same pattern as PreFillResponse but with project-specific field names.
/// </summary>
public record ProjectPreFillResponse(
    string? ProjectTypeName,
    string? PracticeAreaName,
    string? ProjectName,
    string? Description,
    string? AssignedAttorneyName,
    string? AssignedParalegalName,
    string? AssignedOutsideCounselName,
    double Confidence,
    string[] PreFilledFields)
{
    /// <summary>
    /// Returns an empty ProjectPreFillResponse indicating no fields were extracted.
    /// </summary>
    public static ProjectPreFillResponse Empty() =>
        new(
            ProjectTypeName: null,
            PracticeAreaName: null,
            ProjectName: null,
            Description: null,
            AssignedAttorneyName: null,
            AssignedParalegalName: null,
            AssignedOutsideCounselName: null,
            Confidence: 0,
            PreFilledFields: []);
}
