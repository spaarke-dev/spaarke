namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Validates whether a caller-supplied Azure AI Search index name is allowed.
/// </summary>
/// <remarks>
/// <para>
/// Introduced for multi-container-multi-index-r1 Phase G (task 102). Replaces the static
/// <c>AiSearchOptions.AllowedIndexes</c> array (curated in appsettings + App Service config)
/// with a Dataverse-backed source of truth (the new <c>sprk_aisearchindex</c> catalog
/// table). The default implementation,
/// <see cref="DataverseAllowedIndexesProvider"/>, caches the active-row set in
/// <see cref="Microsoft.Extensions.Caching.Memory.IMemoryCache"/> with a 5-minute TTL.
/// </para>
/// <para>
/// Consumed by <see cref="KnowledgeDeploymentService"/> to validate the
/// per-record / per-request <c>searchIndexName</c> parameter before binding a
/// <see cref="Azure.Search.Documents.SearchClient"/>. On a rejection, the consumer surfaces
/// <c>ProblemDetails 400 INDEX_NOT_ALLOWED</c> per ADR-019 + NFR-08; the wire shape is
/// unchanged from the appsettings-driven validator.
/// </para>
/// </remarks>
public interface IAllowedIndexesProvider
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="indexName"/> matches an Active row in
    /// <c>sprk_aisearchindex</c> (statecode = 0). Case-insensitive comparison.
    /// </summary>
    /// <remarks>
    /// On a Dataverse fetch failure OR an empty active-row result, the implementation
    /// falls back to the appsettings <see cref="Sprk.Bff.Api.Configuration.AiSearchOptions.AllowedIndexes"/>
    /// array and emits a single WARNING log entry per cache TTL cycle (per Phase G spec §13 Q4).
    /// </remarks>
    Task<bool> IsAllowedAsync(string indexName, CancellationToken ct);
}
