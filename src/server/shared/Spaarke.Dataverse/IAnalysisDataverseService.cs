namespace Spaarke.Dataverse;

/// <summary>
/// Analysis record and output operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IAnalysisDataverseService
{
    Task<AnalysisEntity?> GetAnalysisAsync(string id, CancellationToken ct = default);
    Task<AnalysisActionEntity?> GetAnalysisActionAsync(string id, CancellationToken ct = default);
    /// <summary>
    /// Creates an <c>sprk_analysis</c> record.
    ///
    /// <para><paramref name="documentId"/> anchors the analysis to a source document (the historical
    /// behavior; also the FR-D9 "regarding = document" path). It is now OPTIONAL: pass <c>null</c> to
    /// create a document-less analysis, VALID only when <paramref name="regarding"/> supplies a
    /// matter/project association (FR-D9 — "Set related record"). Callers MUST supply at least one of
    /// <paramref name="documentId"/> or <paramref name="regarding"/>; supplying neither throws
    /// <see cref="ArgumentException"/>.</para>
    ///
    /// <para>When <paramref name="regarding"/> is supplied, the ADR-024 polymorphic <c>regarding</c>
    /// field-set (entity-specific lookup + denormalized resolver fields) is written on the analysis so
    /// it surfaces on the parent's Analyses tab.</para>
    ///
    /// <para><paramref name="owningTeamId"/> (unified-access-control-r2 task 146) is the team that owns the new row,
    /// resolved by the BFF's <c>IRecordOwnershipResolver</c> from the document and/or regarding record — the named
    /// Secure team when either is secure. REQUIRED: a create without it throws
    /// <see cref="InvalidOperationException"/> before any write, because this create is app-only and an unset owner
    /// makes the BFF application user own an AI analysis of a secure document in the root business unit. Optional
    /// in the signature only so the parameter could be added without reordering; every caller passes it.</para>
    ///
    /// <para><paramref name="createdByPersonId"/> (task 146 c1-r1, owner round 13 item 9) is the person who asked for the
    /// analysis, written as <see cref="RecordCreatorPersonColumn.LogicalName"/> — this create is app-only, so
    /// <c>createdby</c> is the application user. <c>null</c> for a writer that acts for nobody (a background profile).</para>
    /// </summary>
    Task<Guid> CreateAnalysisAsync(Guid? documentId, string? name = null, Guid? playbookId = null, AnalysisRegardingTarget? regarding = null, Guid? owningTeamId = null, Guid? createdByPersonId = null, CancellationToken ct = default);

    /// <summary>
    /// Creates an <c>sprk_analysisoutput</c> row. <see cref="AnalysisOutputEntity.OwningTeamId"/> is REQUIRED (task 146):
    /// an analysis output carries the analysis's content, so it is owned like its analysis — a create without an owner
    /// throws <see cref="InvalidOperationException"/> before any write.
    /// </summary>
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
