namespace Spaarke.Dataverse;

/// <summary>
/// KPI assessment query operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IKpiDataverseService
{
    Task<KpiAssessmentRecord[]> QueryKpiAssessmentsAsync(
        Guid parentId,
        string parentLookupField = "sprk_matter",
        int? performanceArea = null,
        int top = 0,
        CancellationToken ct = default);

    Task<Dictionary<int, KpiAssessmentRecord[]>> BatchQueryKpiAssessmentsAsync(
        Guid parentId,
        string parentLookupField,
        int[] performanceAreas,
        int top = 0,
        CancellationToken ct = default);
}
