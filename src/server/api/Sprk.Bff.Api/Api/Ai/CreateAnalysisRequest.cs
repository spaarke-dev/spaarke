using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Request body for POST /api/ai/analysis/create.
/// Creates a new sprk_analysis record and associates N:N scope items.
/// </summary>
/// <param name="Name">Display name for the analysis record.</param>
/// <param name="DocumentId">GUID of the source sprk_document record.</param>
/// <param name="SkillIds">GUIDs of sprk_analysisskill records to associate (N:N: sprk_analysis_skill).</param>
/// <param name="KnowledgeIds">GUIDs of sprk_analysisknowledge records to associate (N:N: sprk_analysis_knowledge).</param>
/// <param name="ToolIds">GUIDs of sprk_analysistool records to associate (N:N: sprk_analysis_tool).</param>
public record CreateAnalysisRequest(
    string Name,
    Guid DocumentId,
    Guid[] SkillIds,
    Guid[] KnowledgeIds,
    Guid[] ToolIds,
    Guid? PlaybookId = null,
    Guid? ActionId = null);

/// <summary>
/// Response body for POST /api/ai/analysis/create.
/// </summary>
/// <param name="AnalysisId">GUID of the newly created sprk_analysis record.</param>
public record CreateAnalysisResponse(Guid AnalysisId)
{
    /// <summary>
    /// Field-mapping-server-write-path-r1 task 043: what the caller is told about a create that stands (the create's warnings,
    /// as the child-record create returns them), for example that no fields were copied from a related record the caller
    /// cannot read. Omitted when there are none.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; init; }
}
