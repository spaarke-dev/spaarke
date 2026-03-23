namespace Spaarke.Dataverse;

/// <summary>
/// Analysis record and output operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IAnalysisDataverseService
{
    Task<AnalysisEntity?> GetAnalysisAsync(string id, CancellationToken ct = default);
    Task<AnalysisActionEntity?> GetAnalysisActionAsync(string id, CancellationToken ct = default);
    Task<Guid> CreateAnalysisAsync(Guid documentId, string? name = null, Guid? playbookId = null, CancellationToken ct = default);
    Task<Guid> CreateAnalysisOutputAsync(AnalysisOutputEntity output, CancellationToken ct = default);

    /// <summary>
    /// Associates skill, knowledge, and tool scope records with an analysis via N:N relationships.
    /// Empty collections are silently skipped. Already-existing associations are tolerated.
    /// Relationships: sprk_analysis_skill, sprk_analysis_knowledge, sprk_analysis_tool.
    /// </summary>
    Task AssociateScopesAsync(
        Guid analysisId,
        IEnumerable<Guid> skillIds,
        IEnumerable<Guid> knowledgeIds,
        IEnumerable<Guid> toolIds,
        CancellationToken cancellationToken = default);
}
