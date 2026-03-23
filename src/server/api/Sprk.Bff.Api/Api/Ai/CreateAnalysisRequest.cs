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
    Guid[] ToolIds);

/// <summary>
/// Response body for POST /api/ai/analysis/create.
/// </summary>
/// <param name="AnalysisId">GUID of the newly created sprk_analysis record.</param>
public record CreateAnalysisResponse(Guid AnalysisId);
