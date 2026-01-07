using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Request model for POST /api/ai/analysis/execute.
/// Executes a new analysis with configurable action, scopes, and output type.
/// Supports multi-document analysis via DocumentIds array (Phase 2 feature).
/// </summary>
public record AnalysisExecuteRequest
{
    /// <summary>
    /// Document IDs to analyze.
    /// Phase 1: Only DocumentIds[0] is processed.
    /// Phase 2: All documents are processed and synthesized.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one document ID is required")]
    public Guid[] DocumentIds { get; init; } = [];

    /// <summary>
    /// The analysis action to perform (e.g., Summarize, Review Agreement).
    /// References sprk_analysisaction entity.
    /// Optional when PlaybookId is provided (uses playbook's default action).
    /// </summary>
    public Guid? ActionId { get; init; }

    /// <summary>
    /// Optional skill IDs to apply (e.g., Concise writing, Legal terminology).
    /// References sprk_analysisskill entities.
    /// </summary>
    public Guid[]? SkillIds { get; init; }

    /// <summary>
    /// Optional knowledge source IDs for RAG grounding.
    /// References sprk_analysisknowledge entities.
    /// </summary>
    public Guid[]? KnowledgeIds { get; init; }

    /// <summary>
    /// Optional tool IDs to enable (e.g., Entity extractor).
    /// References sprk_analysistool entities.
    /// </summary>
    public Guid[]? ToolIds { get; init; }

    /// <summary>
    /// Output type for the analysis result.
    /// </summary>
    public AnalysisOutputType OutputType { get; init; } = AnalysisOutputType.Document;

    /// <summary>
    /// Optional playbook ID to use pre-configured scopes.
    /// When provided, SkillIds/KnowledgeIds/ToolIds are loaded from playbook.
    /// References sprk_analysisplaybook entity.
    /// </summary>
    public Guid? PlaybookId { get; init; }
}

/// <summary>
/// Output type for analysis results.
/// </summary>
public enum AnalysisOutputType
{
    /// <summary>Working document saved to SPE.</summary>
    Document = 0,

    /// <summary>Email draft created in Dataverse.</summary>
    Email = 1,

    /// <summary>Teams message (Phase 2).</summary>
    Teams = 2,

    /// <summary>Dataverse notification (Phase 2).</summary>
    Notification = 3,

    /// <summary>Power Automate workflow trigger (Phase 2).</summary>
    Workflow = 4
}
