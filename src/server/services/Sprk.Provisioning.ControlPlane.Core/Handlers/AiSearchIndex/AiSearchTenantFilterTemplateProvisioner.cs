// -----------------------------------------------------------------------------
// AiSearchTenantFilterTemplateProvisioner.cs
//
// Production <see cref="IAiSearchTenantFilterTemplateProvisioner"/> (task 124,
// Wave G-2) REPLACING StubAiSearchTenantFilterTemplateProvisioner, which only
// logged the intended template contents and returned Success — DS-4 §2 flags
// this as "I2 provisioning half not real" (spec.md FR-29 / design.md §4D I2).
// This is the RUNTIME-provisioning half of the tenant-isolation invariant; the
// Wave-C6 ArchTest is the STATIC-scan half (unchanged by this task).
//
// ESCALATION-TRIGGER RESOLUTION (per this task's POML <escalation>): searched
// the codebase for an "already-real" verifier of the TEMPLATE ARTIFACT itself
// (as opposed to the per-index invariant verifier, RestApiAiSearchIndexVerifier,
// which DS-4 §2 separately calls "real" for a different reason — it asserts
// the `tenantId` filterable FIELD exists on each index, not that a per-tenant
// template artifact exists). No such artifact-schema verifier exists in code
// yet — task 173 (I2 acceptance probe, `<deps>124</deps>`) is what will assert
// enforcement, and per task 173's own POML it does so via a LIVE SAMPLE QUERY
// against the customer's Search endpoint (proving the tenantId filter is
// actually applied server-side), not by re-reading this provisioner's storage
// artifact. The escalation trigger therefore does NOT fire: there is no
// existing verifier schema to reverse-engineer against, so the artifact shape
// below is this task's own design, guided entirely by
// IAiSearchTenantFilterTemplateProvisioner's documented seam contract (see
// that file's header): given (SearchEndpoint, TenantId, CustomerId,
// IndexNames), provision a per-tenant artifact whose contents include the
// literal filter predicate `tenantId eq '{TenantId}'` for each index; second
// call with unchanged inputs = idempotent no-op (PUT semantics).
//
// WHY COSMOS (of design.md's three candidates — Cosmos per-tenant document /
// Search-Service scoring profile / BFF-owned template catalog):
//   - Azure AI Search has NO server-side "saved filter template" primitive
//     (scoring profiles influence ranking, not filter predicates) — the
//     "Search-Service scoring profile" candidate does not actually model this.
//   - A BFF-owned template catalog would require a NEW cross-service contract
//     between L2 and the BFF for a value the BFF's own query-builders
//     (RagQueryBuilder / SearchFilterBuilder / RecordSearchService) already
//     compute programmatically from the caller's tenantId claim at query time
//     — this artifact is an AUDITABLE PROVISIONING RECORD proving the
//     onboarding-time enforcement decision was made explicitly (§4D I1 audit
//     trail, parity with the retired Stub's "log the predicate verbatim"
//     intent), not a runtime query dependency for the BFF.
//   - L2 already owns a Cosmos account + a shared <see cref="CosmosClient"/>
//     singleton (Modules.CosmosModule) — extending it with one more container
//     is the minimal-new-surface choice per CLAUDE.md §11 (Existing: Cosmos
//     account + client; Extension: one additional container; Cost-of-doing-
//     nothing: the I2 runtime-provisioning half stays permanently unenforced).
//
// WHY A SEPARATE CONTAINER (not the `runs` container): the `runs` container
// carries a 365-day TTL (audit-retention scoped, cosmos-provisioning.bicep).
// A tenant-filter template is a PERMANENT tenant-isolation control that MUST
// outlive any individual ProvisioningRun's retention window — mixing it into
// `runs` would either inherit an inappropriate TTL or require per-item TTL
// overrides that ProvisioningRun.cs's own comments document as unreliable
// under this project's Newtonsoft-based Cosmos serializer. The new
// `tenantFilterTemplates` container is created on first use (server-less
// account — no throughput to declare) via Container.CreateIfNotExistsAsync,
// partitioned by `/customerId` (§4D I3 parity with the `runs` container).
//
// TEST BOUNDARY (ADR-038): the Microsoft.Azure.Cosmos SDK is NOT built on
// Azure.Core's HttpPipeline (no fake-transport seam exists for it — this is
// why CosmosProvisioningRunRepository itself has NO unit test file, only the
// env-guarded CosmosSmokeTests.cs). This project's established pattern for
// Cosmos-backed collaborators is therefore: keep Cosmos behind an interface
// (<see cref="ITenantFilterTemplateStore"/>, mirroring
// IProvisioningRunRepository) and unit-test the PROVISIONER against a
// hand-rolled fake of that interface (never Mock&lt;T&gt;). The concrete
// <see cref="CosmosTenantFilterTemplateStore"/> is exercised only by the
// handler's own live path — no new env-guarded smoke test was added for it in
// this task (out of acceptance-criteria scope; the fake/emulated-store tests
// below satisfy criteria 3 + 4 in full).
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Net;
using Microsoft.Azure.Cosmos;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;

/// <summary>
/// Storage seam for the per-tenant AI Search filter-template artifact.
/// Production impl is Cosmos-backed (<see cref="CosmosTenantFilterTemplateStore"/>);
/// unit tests inject a hand-rolled fake (parity with
/// <see cref="Repositories.IProvisioningRunRepository"/>'s test pattern).
/// </summary>
public interface ITenantFilterTemplateStore
{
    /// <summary>Reads the current template document for a customer, or <c>null</c> if none exists yet.</summary>
    Task<TenantFilterTemplateDocument?> TryReadAsync(string customerId, CancellationToken cancellationToken);

    /// <summary>Upserts the template document (PUT semantics — overwrite-safe on every call).</summary>
    Task WriteAsync(TenantFilterTemplateDocument document, CancellationToken cancellationToken);
}

/// <summary>
/// Cosmos document — one per customer, containing the literal
/// <c>tenantId eq '{TenantId}'</c> filter predicate recorded per canonical
/// index at onboarding time. Container: <see cref="CosmosTenantFilterTemplateStore.ContainerName"/>,
/// partition key <c>/customerId</c>, NO TTL (permanent tenant-isolation
/// control — see file header).
/// </summary>
public sealed class TenantFilterTemplateDocument
{
    /// <summary>Cosmos-required document id. Deterministic: <see cref="CosmosTenantFilterTemplateStore.BuildDocumentId"/>.</summary>
    [Newtonsoft.Json.JsonProperty("id")]
    public string Id { get; set; } = default!;

    /// <summary>Partition key. Customer short-id — §4D I3 partition-key predicate parity with ProvisioningRun.</summary>
    [Newtonsoft.Json.JsonProperty("customerId")]
    public string CustomerId { get; set; } = default!;

    /// <summary>Entra tenant id embedded verbatim in every <see cref="Filters"/> predicate — §4D I1 never-default.</summary>
    [Newtonsoft.Json.JsonProperty("tenantId")]
    public string TenantId { get; set; } = default!;

    /// <summary>Shared-platform AI Search endpoint this template applies to (Model 1 only).</summary>
    [Newtonsoft.Json.JsonProperty("searchEndpoint")]
    public string SearchEndpoint { get; set; } = default!;

    /// <summary>One entry per canonical index carrying that index's mandatory filter predicate.</summary>
    [Newtonsoft.Json.JsonProperty("filters")]
    public IList<TenantIndexFilterEntry> Filters { get; set; } = new List<TenantIndexFilterEntry>();

    /// <summary>UTC timestamp of the last write (provisioning or re-provisioning).</summary>
    [Newtonsoft.Json.JsonProperty("updatedOn")]
    public DateTimeOffset UpdatedOn { get; set; }
}

/// <summary>Single per-index filter-predicate record inside a <see cref="TenantFilterTemplateDocument"/>.</summary>
public sealed class TenantIndexFilterEntry
{
    /// <summary>Canonical index name this predicate applies to.</summary>
    [Newtonsoft.Json.JsonProperty("indexName")]
    public string IndexName { get; set; } = default!;

    /// <summary>Literal OData filter predicate, e.g. <c>tenantId eq '00000000-...'</c>.</summary>
    [Newtonsoft.Json.JsonProperty("filterPredicate")]
    public string FilterPredicate { get; set; } = default!;
}

/// <summary>
/// Cosmos-backed <see cref="ITenantFilterTemplateStore"/>. Reuses the shared
/// <see cref="CosmosClient"/> singleton registered by Modules.CosmosModule
/// (CLAUDE.md §11 extend-don't-duplicate) against a NEW, TTL-less container.
/// </summary>
public sealed class CosmosTenantFilterTemplateStore : ITenantFilterTemplateStore
{
    /// <summary>Container name — sibling of Modules.CosmosModule.DefaultContainerName ("runs") in the same database.</summary>
    public const string ContainerName = "tenantFilterTemplates";

    private readonly CosmosClient _cosmosClient;
    private readonly string _databaseName;
    private readonly ILogger<CosmosTenantFilterTemplateStore> _logger;
    private readonly SemaphoreSlim _containerEnsureLock = new(1, 1);
    private volatile bool _containerEnsured;

    /// <summary>Constructs the store bound to the shared platform Cosmos account.</summary>
    public CosmosTenantFilterTemplateStore(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<CosmosTenantFilterTemplateStore> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        ArgumentNullException.ThrowIfNull(logger);
        _cosmosClient = cosmosClient;
        _databaseName = databaseName;
        _logger = logger;
    }

    /// <summary>Deterministic document id for a customer's template (one document per customer).</summary>
    public static string BuildDocumentId(string customerId) => $"tenant-filter-template-{customerId}";

    /// <inheritdoc/>
    public async Task<TenantFilterTemplateDocument?> TryReadAsync(string customerId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        var container = await GetContainerAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // §4D I3: explicit PartitionKey — never a cross-partition read.
            var response = await container.ReadItemAsync<TenantFilterTemplateDocument>(
                id: BuildDocumentId(customerId),
                partitionKey: new PartitionKey(customerId),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(TenantFilterTemplateDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.CustomerId);
        var container = await GetContainerAsync(cancellationToken).ConfigureAwait(false);

        // Upsert — PUT semantics per IAiSearchTenantFilterTemplateProvisioner's
        // documented contract; idempotent regardless of the provisioner's own
        // no-op short-circuit (AiSearchTenantFilterTemplateProvisioner.IsUnchanged).
        await container.UpsertItemAsync(
            document, new PartitionKey(document.CustomerId), cancellationToken: cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Tenant-filter template written: customerId={CustomerId} tenantId={TenantId} indexCount={IndexCount}",
            document.CustomerId, document.TenantId, document.Filters.Count);
    }

    private async Task<Container> GetContainerAsync(CancellationToken cancellationToken)
    {
        if (!_containerEnsured)
        {
            await _containerEnsureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!_containerEnsured)
                {
                    var database = _cosmosClient.GetDatabase(_databaseName);
                    // NO container-level TTL — see file header ("WHY A SEPARATE CONTAINER").
                    // Serverless account: no throughput parameter needed.
                    await database.CreateContainerIfNotExistsAsync(ContainerName, "/customerId", cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                    _containerEnsured = true;
                    _logger.LogInformation(
                        "Ensured Cosmos container '{Container}' exists in database '{Database}' (no TTL).",
                        ContainerName, _databaseName);
                }
            }
            finally
            {
                _containerEnsureLock.Release();
            }
        }

        return _cosmosClient.GetContainer(_databaseName, ContainerName);
    }
}

/// <inheritdoc cref="IAiSearchTenantFilterTemplateProvisioner"/>
public sealed class AiSearchTenantFilterTemplateProvisioner : IAiSearchTenantFilterTemplateProvisioner
{
    private readonly ITenantFilterTemplateStore _store;
    private readonly ILogger<AiSearchTenantFilterTemplateProvisioner> _logger;

    /// <summary>Constructs the provisioner over the given <see cref="ITenantFilterTemplateStore"/>.</summary>
    public AiSearchTenantFilterTemplateProvisioner(
        ITenantFilterTemplateStore store,
        ILogger<AiSearchTenantFilterTemplateProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AiSearchTenantFilterTemplateOutcome> ProvisionAsync(
        AiSearchTenantFilterTemplateRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SearchEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);

        var filters = BuildFilterEntries(request.TenantId, request.IndexNames);
        var existing = await _store.TryReadAsync(request.CustomerId, cancellationToken).ConfigureAwait(false);

        if (existing is not null && IsUnchanged(existing, request.TenantId, request.SearchEndpoint, filters))
        {
            // Idempotency test (acceptance criterion #4): given the template
            // already exists with matching content, re-running is a genuine
            // no-op — the store's WriteAsync is NOT called a second time.
            _logger.LogInformation(
                "H2b tenant-filter template no-op (already current): customerId={CustomerId} tenantId={TenantId} indexCount={IndexCount}",
                request.CustomerId, request.TenantId, filters.Count);
            return new AiSearchTenantFilterTemplateOutcome.Success();
        }

        var document = new TenantFilterTemplateDocument
        {
            Id = CosmosTenantFilterTemplateStore.BuildDocumentId(request.CustomerId),
            CustomerId = request.CustomerId,
            TenantId = request.TenantId,
            SearchEndpoint = request.SearchEndpoint,
            Filters = filters,
            UpdatedOn = DateTimeOffset.UtcNow,
        };

        // Real write (acceptance criterion #3) — not a logged success message.
        await _store.WriteAsync(document, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "H2b tenant-filter template provisioned: customerId={CustomerId} tenantId={TenantId} endpoint={Endpoint} indexCount={IndexCount}",
            request.CustomerId, request.TenantId, request.SearchEndpoint, filters.Count);

        return new AiSearchTenantFilterTemplateOutcome.Success();
    }

    /// <summary>
    /// Builds one filter entry per requested index, all carrying the SAME
    /// literal <c>tenantId eq '{TenantId}'</c> predicate (§4D I2 / FR-29) —
    /// OData string-literal escaping mirrors RagQueryBuilder / RecordSearchService's
    /// EscapeODataValue convention (doubled single-quote).
    /// </summary>
    private static IList<TenantIndexFilterEntry> BuildFilterEntries(string tenantId, ImmutableArray<string> indexNames)
    {
        var escapedTenantId = tenantId.Replace("'", "''", StringComparison.Ordinal);
        var predicate = $"tenantId eq '{escapedTenantId}'";

        var entries = new List<TenantIndexFilterEntry>(indexNames.Length);
        foreach (var indexName in indexNames)
        {
            entries.Add(new TenantIndexFilterEntry { IndexName = indexName, FilterPredicate = predicate });
        }
        return entries;
    }

    /// <summary>
    /// True when the stored document already reflects the requested
    /// (tenantId, searchEndpoint, per-index predicate set) exactly — the
    /// idempotency check backing acceptance criterion #4.
    /// </summary>
    private static bool IsUnchanged(
        TenantFilterTemplateDocument existing,
        string tenantId,
        string searchEndpoint,
        IList<TenantIndexFilterEntry> filters)
    {
        if (!string.Equals(existing.TenantId, tenantId, StringComparison.Ordinal))
        {
            return false;
        }
        if (!string.Equals(existing.SearchEndpoint, searchEndpoint, StringComparison.Ordinal))
        {
            return false;
        }
        if (existing.Filters.Count != filters.Count)
        {
            return false;
        }

        var existingByIndex = existing.Filters.ToDictionary(f => f.IndexName, f => f.FilterPredicate, StringComparer.Ordinal);
        foreach (var entry in filters)
        {
            if (!existingByIndex.TryGetValue(entry.IndexName, out var existingPredicate)
                || !string.Equals(existingPredicate, entry.FilterPredicate, StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }
}
