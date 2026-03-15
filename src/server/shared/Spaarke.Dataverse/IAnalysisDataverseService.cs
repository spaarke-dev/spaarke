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
}
