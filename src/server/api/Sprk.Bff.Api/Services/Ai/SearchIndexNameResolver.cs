using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Server-side resolver for the per-record AI Search index value used by background
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
///     <c>sprk_document.sprk_ai_search_index</c> (lookup → <c>sprk_aisearchindex.sprk_searchindexname</c>).
///     Non-empty wins.</description></item>
///   <item><description>If <paramref name="parentEntityType"/> and <paramref name="parentEntityId"/>
///     are provided, look up the parent record's <c>sprk_ai_search_index</c> lookup. Non-empty wins.</description></item>
///   <item><description>From the parent's owning Business Unit (<c>_owningbusinessunit_value</c>),
///     look up <c>businessunit.sprk_ai_search_index</c>. Non-empty wins.</description></item>
///   <item><description>Return <c>null</c> → caller falls through to the BFF tenant-default chain
///     (<see cref="IKnowledgeDeploymentService.GetSearchClientAsync(string, CancellationToken)"/>).</description></item>
/// </list>
/// <para>
/// Phase G (2026-06-10, task 102): each step now executes a SINGLE FetchXml query with a
/// <c>link-entity name='sprk_aisearchindex'</c> outer-join projecting
/// <c>idx.sprk_searchindexname</c>. This replaces the legacy two-step pattern
/// (read text column → if empty, walk the lookup) with a single round-trip that returns BOTH the
/// linked-index name AND the deprecated text-column value. The text-column path is RETAINED as a
/// migration-safety fallback: if the lookup is null/empty but the legacy text column is set, the
/// resolver returns the text value and logs a structured WARNING (<c>PhaseG.TextFallback</c>) so
/// App Insights can confirm migration progress (task 110 removes this fallback after a 24–48 hr
/// soak with zero text-fallback hits).
/// </para>
/// <para>
/// <b>🚨 Lookup column name</b>: the actual schema name on the 7 source entities is
/// <c>sprk_ai_search_index</c> (derived by Dataverse from the display name "AI Search Index"
/// + the <c>sprk_</c> publisher prefix + lowercase + underscores). The primary key of the catalog
/// table <c>sprk_aisearchindex</c> stays as <c>sprk_aisearchindexid</c>. Both are honored in the
/// FetchXml below (<c>from='sprk_aisearchindexid' to='sprk_ai_search_index'</c>). See Phase G
/// spec §3.1.
/// </para>
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
    /// <returns>The resolved AI Search index name, or <c>null</c> when no value is found.</returns>
    Task<string?> ResolveAsync(
        string? documentId,
        string? parentEntityType,
        string? parentEntityId,
        CancellationToken ct);
}

/// <inheritdoc cref="ISearchIndexNameResolver"/>
public sealed class SearchIndexNameResolver : ISearchIndexNameResolver
{
    // Source-entity columns
    private const string LookupColumn = "sprk_ai_search_index";              // lookup on source entities (Matter/Project/Doc/...)
    private const string LegacyTextColumn = "sprk_searchindexname";          // deprecated text column on source entities
    private const string OwningBusinessUnitAttribute = "owningbusinessunit";

    // Catalog table (target of the lookup)
    private const string CatalogEntity = "sprk_aisearchindex";
    private const string CatalogPrimaryKey = "sprk_aisearchindexid";         // PK of catalog table — stays as-is
    private const string CatalogNameColumn = "sprk_searchindexname";         // physical Azure AI Search index name
    private const string AliasedIndexNameAttribute = "idx.sprk_searchindexname";

    // Source entities for each step
    private const string DocumentEntity = "sprk_document";
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
                var docValue = await ResolveFromEntityAsync(DocumentEntity, docGuid, ct);
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
            EntityReference? parentBuRef = null;
            try
            {
                var (parentValue, owningBuRef) = await ResolveFromEntityWithBuAsync(parentEntityType, parentGuid, ct);
                parentBuRef = owningBuRef;

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
            if (parentBuRef is not null && parentBuRef.Id != Guid.Empty)
            {
                try
                {
                    var buValue = await ResolveFromEntityAsync(BusinessUnitEntity, parentBuRef.Id, ct);
                    if (!string.IsNullOrWhiteSpace(buValue))
                    {
                        _logger.LogDebug(
                            "SearchIndexNameResolver: resolved from owning BU {BusinessUnitId} = {IndexName}",
                            parentBuRef.Id, buValue);
                        return buValue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "SearchIndexNameResolver: businessunit {BusinessUnitId} lookup failed",
                        parentBuRef.Id);
                }
            }
        }

        // Step 4: fall through — caller uses BFF tenant default
        _logger.LogDebug(
            "SearchIndexNameResolver: no value resolved (documentId={DocumentId}, parentEntityType={ParentEntityType}, parentEntityId={ParentEntityId}) — caller will use tenant default",
            documentId ?? "(null)", parentEntityType ?? "(null)", parentEntityId ?? "(null)");
        return null;
    }

    /// <summary>
    /// Executes a single FetchXml fetch against <paramref name="entityType"/> with a
    /// <c>link-entity</c> outer-join into <c>sprk_aisearchindex</c> via the
    /// <c>sprk_ai_search_index</c> lookup. Returns the aliased
    /// <c>idx.sprk_searchindexname</c> when the lookup resolves; otherwise falls back
    /// to the legacy <c>sprk_searchindexname</c> text column on the source entity
    /// (logged as <c>PhaseG.TextFallback</c>).
    /// </summary>
    private async Task<string?> ResolveFromEntityAsync(string entityType, Guid id, CancellationToken ct)
    {
        var fetchXml = BuildLookupFetchXml(entityType, id, includeOwningBu: false);
        var row = await RetrieveSingleAsync(fetchXml, ct);
        if (row is null)
        {
            return null;
        }

        return ExtractIndexName(row, entityType, id);
    }

    /// <summary>
    /// Same as <see cref="ResolveFromEntityAsync(string, Guid, CancellationToken)"/> but
    /// also returns the entity's <c>owningbusinessunit</c> reference so the resolver chain
    /// can cascade to step 3 without a second round-trip.
    /// </summary>
    private async Task<(string? IndexName, EntityReference? OwningBu)> ResolveFromEntityWithBuAsync(
        string entityType,
        Guid id,
        CancellationToken ct)
    {
        var fetchXml = BuildLookupFetchXml(entityType, id, includeOwningBu: true);
        var row = await RetrieveSingleAsync(fetchXml, ct);
        if (row is null)
        {
            return (null, null);
        }

        var indexName = ExtractIndexName(row, entityType, id);

        EntityReference? bu = null;
        if (row.Contains(OwningBusinessUnitAttribute)
            && row[OwningBusinessUnitAttribute] is EntityReference buRef
            && buRef.Id != Guid.Empty)
        {
            bu = buRef;
        }

        return (indexName, bu);
    }

    /// <summary>
    /// Builds the FetchXml for a single-record lookup with a <c>link-entity</c> outer-join
    /// into the <c>sprk_aisearchindex</c> catalog table. The legacy text column is
    /// projected on the source entity so the migration-safety fallback works in the same
    /// round-trip.
    /// </summary>
    private static string BuildLookupFetchXml(string entityType, Guid id, bool includeOwningBu)
    {
        // Choose the entity's PK attribute name. Custom Spaarke entities follow
        // {entityname}id; standard entities (businessunit) follow the same convention.
        var pkAttribute = entityType + "id";

        // The legacy text column lives on the SOURCE entity; the new lookup column is
        // sprk_ai_search_index. The catalog row is joined via link-entity. We project
        // both so the resolver can fall through atomically.
        var owningBuAttr = includeOwningBu
            ? $"<attribute name='{OwningBusinessUnitAttribute}' />"
            : string.Empty;

        return $@"<fetch top='1'>
  <entity name='{entityType}'>
    <attribute name='{LegacyTextColumn}' />
    {owningBuAttr}
    <filter>
      <condition attribute='{pkAttribute}' operator='eq' value='{id:D}' />
    </filter>
    <link-entity name='{CatalogEntity}'
                 from='{CatalogPrimaryKey}'
                 to='{LookupColumn}'
                 link-type='outer'
                 alias='idx'>
      <attribute name='{CatalogNameColumn}' />
    </link-entity>
  </entity>
</fetch>";
    }

    /// <summary>
    /// Executes the FetchXml and returns the first row, or <c>null</c> if no rows match.
    /// </summary>
    private async Task<Entity?> RetrieveSingleAsync(string fetchXml, CancellationToken ct)
    {
        var fetch = new FetchExpression(fetchXml);
        var results = await _entityService.RetrieveMultipleAsync(fetch, ct);
        return results.Entities.FirstOrDefault();
    }

    /// <summary>
    /// Extracts the index name from the FetchXml result. The lookup path (linked-entity
    /// aliased value) wins; the legacy text column is the migration-safety fallback.
    /// </summary>
    private string? ExtractIndexName(Entity row, string entityType, Guid id)
    {
        // Lookup-first: aliased value projected by the link-entity (idx.sprk_searchindexname).
        if (row.Contains(AliasedIndexNameAttribute)
            && row[AliasedIndexNameAttribute] is AliasedValue aliased
            && aliased.Value is string linkedName
            && !string.IsNullOrWhiteSpace(linkedName))
        {
            return linkedName;
        }

        // Migration-safety fallback: read the legacy text column from the source entity.
        if (row.Contains(LegacyTextColumn)
            && row[LegacyTextColumn] is string textValue
            && !string.IsNullOrWhiteSpace(textValue))
        {
            _logger.LogWarning(
                "PhaseG.TextFallback hit on {EntityType} {EntityId}",
                entityType, id);
            return textValue;
        }

        return null;
    }
}
