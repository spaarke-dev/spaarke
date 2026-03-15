namespace Spaarke.Dataverse;

/// <summary>
/// Composite interface for all Dataverse service operations.
/// Inherits 9 domain-focused interfaces following the Interface Segregation Principle (ISP).
/// Existing consumers continue to depend on IDataverseService unchanged.
/// New consumers should depend on the narrowest interface they need.
/// </summary>
public interface IDataverseService :
    IDocumentDataverseService,
    IAnalysisDataverseService,
    IGenericEntityService,
    IProcessingJobService,
    IEventDataverseService,
    IFieldMappingDataverseService,
    IKpiDataverseService,
    ICommunicationDataverseService,
    IDataverseHealthService
{
}
