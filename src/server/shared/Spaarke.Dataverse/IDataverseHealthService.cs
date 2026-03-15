namespace Spaarke.Dataverse;

/// <summary>
/// Dataverse connectivity and health check operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IDataverseHealthService
{
    Task<bool> TestConnectionAsync();
    Task<bool> TestDocumentOperationsAsync();
}
