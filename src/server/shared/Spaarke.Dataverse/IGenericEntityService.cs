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

    /// <summary>
    /// Executes a FetchXML query and returns the matching <see cref="EntityCollection"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Added 2026-06-10 for multi-container-multi-index-r1 Phase G (task 102) to support the
    /// lookup-first <see cref="Sprk.Bff.Api.Services.Ai.SearchIndexNameResolver"/> chain and the
    /// new <see cref="Sprk.Bff.Api.Services.Ai.DataverseAllowedIndexesProvider"/> allow-list lookup.
    /// Both consumers need FetchXml <c>link-entity</c> support (outer-join cascade across the
    /// doc → parent → BU chain projecting <c>idx.sprk_searchindexname</c>), which
    /// <see cref="QueryExpression"/> cannot express ergonomically.
    /// </para>
    /// <para>
    /// Implementations delegate to the SDK <c>ServiceClient.RetrieveMultipleAsync(FetchExpression, ct)</c>
    /// overload. The Web API implementation throws <c>NotImplementedException</c> (existing pattern —
    /// the BFF runtime always resolves <see cref="DataverseServiceClientImpl"/>).
    /// </para>
    /// </remarks>
    /// <param name="fetch">A SDK <see cref="FetchExpression"/> wrapping a FetchXml payload.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default);

    Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Associates a primary entity with one or more related entities via a many-to-many
    /// relationship. Each <paramref name="relatedEntities"/> entry must reference an
    /// existing row of the related entity type used in the relationship.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generic N:N pattern — used by the Insights Engine D-P3 admin endpoint (task 012)
    /// to attach supporting <c>sprk_matter</c> rows to a new <c>sprk_precedent</c> via
    /// the <c>sprk_precedent_matter</c> relationship. Existing analysis scope wiring uses
    /// the domain-specific <see cref="IAnalysisDataverseService.AssociateScopesAsync"/>;
    /// this method exposes the same primitive without baking the entity types into the
    /// interface.
    /// </para>
    /// <para>
    /// Already-existing associations should be tolerated by implementations (Dataverse
    /// surfaces these as <c>HTTP 400</c> / <c>InvalidOperationException</c>; callers
    /// generally want idempotent semantics).
    /// </para>
    /// </remarks>
    /// <param name="entityLogicalName">Logical name of the primary entity (e.g. <c>sprk_precedent</c>).</param>
    /// <param name="entityId">Row id of the primary entity.</param>
    /// <param name="relationshipName">Schema name of the relationship (e.g. <c>sprk_precedent_matter</c>).</param>
    /// <param name="relatedEntities">References to the related rows to attach.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AssociateAsync(
        string entityLogicalName,
        Guid entityId,
        string relationshipName,
        IEnumerable<EntityReference> relatedEntities,
        CancellationToken ct = default);
}
