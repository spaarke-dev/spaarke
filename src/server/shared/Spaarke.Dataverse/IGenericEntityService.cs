using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Spaarke.Dataverse;

/// <summary>
/// Generic entity CRUD, metadata, and query operations.
/// Part of the IDataverseService composite (ISP segregation).
/// </summary>
public interface IGenericEntityService
{
    Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default);
    Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default);
    Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default);
    Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default);
    Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default);
    Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default);
    Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default);
    Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default);
    Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default);
    Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default);
}
