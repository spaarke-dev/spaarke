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
    /// Retrieves the MOST RECENT <c>sprk_analysisoutput</c> row for <paramref name="analysisId"/> whose
    /// <c>sprk_name</c> matches <paramref name="name"/> exactly (the same categorization-by-name
    /// convention <c>AnalysisResultPersistence.PersistReviewMemoAsync</c> writes under — see that
    /// method's remarks on why <c>OutputTypeId</c> is not used for lookup). Returns <c>null</c> when no
    /// matching row exists — callers treat that as "not generated yet", never as an error. Added for
    /// FR-14 (ai-advanced-capabilities-agreements-r1 task 051) — the Review Summary Memo READ path; the
    /// existing surface only had a CREATE method (<see cref="CreateAnalysisOutputAsync"/>).
    /// </summary>
    Task<AnalysisOutputEntity?> GetLatestAnalysisOutputByNameAsync(Guid analysisId, string name, CancellationToken ct = default);

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
