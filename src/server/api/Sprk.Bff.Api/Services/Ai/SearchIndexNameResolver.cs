using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Server-side resolver for the per-record <c>sprk_searchindexname</c> value used by background
/// indexing jobs. Mirrors the wizard-side <c>resolveSearchIndexNameForRecord</c> helper from
/// <c>DocumentUploadWizard/AssociateToStep</c> (FR-WIZ-06) — same 3-step chain, same fall-through
/// semantics — but runs server-side so the Email-to-Document and Office Add-in routes (which do
/// not pass through the wizard) get correct routing.
/// </summary>
/// <remarks>
/// <para>
/// multi-container-multi-index-r1 indexer-routing-fix (Tier 3) — introduced as the defensive
/// fall-back inside <c>RagIndexingJobHandler</c>, <c>BulkRagIndexingJobHandler</c>, and
/// <c>IndexingWorkerHostedService</c>. When the enqueueing site has set
/// <c>RagIndexingJobPayload.SearchIndexName</c> explicitly (e.g., Office Add-in flow), the resolver
/// is NOT consulted. When the payload value is null/empty, the handler calls
/// <see cref="ResolveAsync(string?, string?, string?, CancellationToken)"/> to look up the value
/// from Dataverse before invoking <see cref="IFileIndexingService.IndexFileAppOnlyAsync"/>.
/// </para>
/// <para>
/// 3-step chain (same precedence as the wizard helper):
/// </para>
/// <list type="number">
///   <item><description>If <paramref name="documentId"/> is provided, look up
///     <c>sprk_document.sprk_searchindexname</c>. Non-empty wins.</description></item>
///   <item><description>If <paramref name="parentEntityType"/> and <paramref name="parentEntityId"/>
///     are provided, look up the parent record's <c>sprk_searchindexname</c>. Non-empty wins.</description></item>
///   <item><description>From the parent's owning Business Unit (<c>_owningbusinessunit_value</c>),
///     look up <c>businessunit.sprk_searchindexname</c>. Non-empty wins.</description></item>
///   <item><description>Return <c>null</c> → caller falls through to the BFF tenant-default chain
///     (<see cref="IKnowledgeDeploymentService.GetSearchClientAsync(string, CancellationToken)"/>).</description></item>
/// </list>
/// <para>
/// Reuses the existing <see cref="IGenericEntityService"/> for Dataverse retrieves (no new
/// Dataverse client is introduced — ADR-010 DI minimalism). Each step is wrapped in a
/// try/catch so a transient Dataverse failure on one step does not poison the entire resolver;
/// the chain continues and eventually returns <c>null</c> if every step fails (tenant-default
/// fall-through still works).
/// </para>
/// </remarks>
public interface ISearchIndexNameResolver
{
    /// <summary>
    /// Resolves the AI Search index name for a document via the 3-step chain
    /// (document → parent → BU). Returns <c>null</c> when no value is found, so the caller
    /// falls through to the BFF tenant-default chain.
    /// </summary>
    /// <param name="documentId">Optional Dataverse <c>sprk_document</c> record id (GUID string).</param>
    /// <param name="parentEntityType">
    /// Optional parent entity logical name (e.g., <c>sprk_matter</c>, <c>sprk_project</c>,
    /// <c>sprk_invoice</c>, <c>sprk_workassignment</c>, <c>sprk_event</c>).
    /// </param>
    /// <param name="parentEntityId">Optional parent record id (GUID string).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The resolved <c>sprk_searchindexname</c>, or <c>null</c> when no value is found.</returns>
    Task<string?> ResolveAsync(
        string? documentId,
        string? parentEntityType,
        string? parentEntityId,
        CancellationToken ct);
}

/// <inheritdoc cref="ISearchIndexNameResolver"/>
public sealed class SearchIndexNameResolver : ISearchIndexNameResolver
{
    private const string SearchIndexNameAttribute = "sprk_searchindexname";
    private const string OwningBusinessUnitAttribute = "owningbusinessunit";
    private const string BusinessUnitEntity = "businessunit";

    private readonly IGenericEntityService _entityService;
    private readonly ILogger<SearchIndexNameResolver> _logger;

    public SearchIndexNameResolver(
        IGenericEntityService entityService,
        ILogger<SearchIndexNameResolver> logger)
    {
        _entityService = entityService ?? throw new ArgumentNullException(nameof(entityService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAsync(
        string? documentId,
        string? parentEntityType,
        string? parentEntityId,
        CancellationToken ct)
    {
        // Step 1: Document-level explicit value (highest precedence)
        if (!string.IsNullOrWhiteSpace(documentId) && Guid.TryParse(documentId, out var docGuid))
        {
            try
            {
                var doc = await _entityService.RetrieveAsync(
                    "sprk_document",
                    docGuid,
                    new[] { SearchIndexNameAttribute },
                    ct);

                var docValue = GetStringAttribute(doc, SearchIndexNameAttribute);
                if (!string.IsNullOrWhiteSpace(docValue))
                {
                    _logger.LogDebug(
                        "SearchIndexNameResolver: resolved from sprk_document {DocumentId} = {IndexName}",
                        documentId, docValue);
                    return docValue;
                }
            }
            catch (Exception ex)
            {
                // Don't poison the chain — fall through to parent/BU lookup. Tenant default is the
                // ultimate safety net.
                _logger.LogWarning(
                    ex,
                    "SearchIndexNameResolver: sprk_document lookup failed for {DocumentId}; continuing chain",
                    documentId);
            }
        }

        // Step 2 + 3: Parent record explicit value, then parent's owning BU
        if (!string.IsNullOrWhiteSpace(parentEntityType)
            && !string.IsNullOrWhiteSpace(parentEntityId)
            && Guid.TryParse(parentEntityId, out var parentGuid))
        {
            Entity? parent = null;
            try
            {
                parent = await _entityService.RetrieveAsync(
                    parentEntityType,
                    parentGuid,
                    new[] { SearchIndexNameAttribute, OwningBusinessUnitAttribute },
                    ct);

                var parentValue = GetStringAttribute(parent, SearchIndexNameAttribute);
                if (!string.IsNullOrWhiteSpace(parentValue))
                {
                    _logger.LogDebug(
                        "SearchIndexNameResolver: resolved from parent {EntityType} {EntityId} = {IndexName}",
                        parentEntityType, parentEntityId, parentValue);
                    return parentValue;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SearchIndexNameResolver: parent {EntityType} {EntityId} lookup failed; continuing chain",
                    parentEntityType, parentEntityId);
            }

            // Step 3: walk owning BU from the parent (only attempted when parent fetch succeeded)
            if (parent is not null
                && parent.Contains(OwningBusinessUnitAttribute)
                && parent[OwningBusinessUnitAttribute] is EntityReference buRef
                && buRef.Id != Guid.Empty)
            {
                try
                {
                    var bu = await _entityService.RetrieveAsync(
                        BusinessUnitEntity,
                        buRef.Id,
                        new[] { SearchIndexNameAttribute },
                        ct);

                    var buValue = GetStringAttribute(bu, SearchIndexNameAttribute);
                    if (!string.IsNullOrWhiteSpace(buValue))
                    {
                        _logger.LogDebug(
                            "SearchIndexNameResolver: resolved from owning BU {BusinessUnitId} = {IndexName}",
                            buRef.Id, buValue);
                        return buValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "SearchIndexNameResolver: businessunit {BusinessUnitId} lookup failed",
                        buRef.Id);
                }
            }
        }

        // Step 4: fall through — caller uses BFF tenant default
        _logger.LogDebug(
            "SearchIndexNameResolver: no value resolved (documentId={DocumentId}, parentEntityType={ParentEntityType}, parentEntityId={ParentEntityId}) — caller will use tenant default",
            documentId ?? "(null)", parentEntityType ?? "(null)", parentEntityId ?? "(null)");
        return null;
    }

    private static string? GetStringAttribute(Entity? entity, string attribute)
    {
        if (entity is null || !entity.Contains(attribute))
        {
            return null;
        }
        return entity[attribute] as string;
    }
}
