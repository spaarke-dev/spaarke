using System.Net;
using Microsoft.Azure.Cosmos;
using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Cosmos DB-backed implementation of <see cref="IPinnedContextRepository"/> for the
/// R6 Pillar 7 / task 065 user-curated <see cref="PinnedContextItem"/> entity.
///
/// <para>
/// Storage: Cosmos container <c>memory</c> (REUSED — same container as
/// <see cref="MatterMemoryService"/> and <c>WorkspaceStateService</c> durable rows), partition
/// key <c>/tenantId</c> per ADR-014 binding. Document discriminator
/// <c>documentType = "pinned-context"</c> co-exists with matter-memory + workspace-tab
/// documents on the same partition without id collision (the <c>pinned-context_</c> id prefix
/// is the disambiguator).
/// </para>
///
/// <para>
/// Document id format: <c>pinned-context_{tenantId}_{pinId}</c>. The <c>{pinId}</c> portion
/// is the stable pin identifier supplied by the caller (typically a GUID); the
/// <c>{tenantId}</c> portion mirrors the partition key for query convenience but is NOT
/// the disambiguator (the prefix is).
/// </para>
///
/// <para>
/// Lifetime: Scoped — matches the consumer endpoint scopes. <see cref="CosmosClient"/> is
/// Singleton (injected); the scoped wrapper is stateless. Pattern mirrors
/// <c>WorkspaceStateService</c> (R6 Pillar 6a) for consistency.
/// </para>
///
/// <para>
/// Placement (CLAUDE.md §10 / ADR-013): memory plumbing only. NO AI-internal dependencies
/// (<c>IOpenAiClient</c>, <c>IPlaybookService</c>, etc.). AI-internal callers (task 067,
/// task 070) consume this repository directly per the 2026-05-20 refined ADR-013 boundary.
/// </para>
///
/// <para>
/// ADR-015 invariant: pin <see cref="PinnedContextItem.Content"/> is user-authored memory
/// content; persisted verbatim. The repository does NOT log content bodies — only the
/// deterministic identifiers (tenantId, userId, pinId, pinType) appear in telemetry.
/// </para>
/// </summary>
public sealed class PinnedContextRepository : IPinnedContextRepository
{
    /// <summary>Cosmos container name (reused with <see cref="MatterMemoryService"/> and workspace-tab durable rows).</summary>
    internal const string ContainerName = "memory";

    /// <summary>Cosmos document discriminator value — also embedded in the id prefix.</summary>
    internal const string DocumentTypeValue = "pinned-context";

    /// <summary>Cosmos document-id prefix that disambiguates from matter-memory + workspace-tab docs.</summary>
    internal const string IdPrefix = "pinned-context";

    /// <summary>Hard ceiling on the pin <see cref="PinnedContextItem.Content"/> length, enforced at write time per the model XML doc binding.</summary>
    internal const int MaxContentLength = 1000;

    /// <summary>Hard ceiling on the pin <see cref="PinnedContextItem.Title"/> length, mirrors the model XML doc.</summary>
    internal const int MaxTitleLength = 200;

    private readonly Container _container;
    private readonly ILogger<PinnedContextRepository> _logger;

    public PinnedContextRepository(
        CosmosClient cosmosClient,
        IConfiguration configuration,
        ILogger<PinnedContextRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(cosmosClient);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(logger);

        var databaseName = configuration["CosmosPersistence:DatabaseName"]
            ?? throw new InvalidOperationException(
                "CosmosPersistence:DatabaseName is not configured. " +
                "Add this setting to appsettings.json or Azure App Service configuration.");

        _container = cosmosClient.GetContainer(databaseName, ContainerName);
        _logger = logger;
    }

    /// <summary>
    /// Constructor used by tests that want to inject a Container mock directly without
    /// constructing a full <see cref="CosmosClient"/> mock graph. Mirrors the
    /// <see cref="MatterMemoryService"/> internal constructor pattern.
    /// </summary>
    internal PinnedContextRepository(Container container, ILogger<PinnedContextRepository> logger)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(logger);
        _container = container;
        _logger = logger;
    }

    /// <summary>
    /// Builds the Cosmos document id: <c>pinned-context_{tenantId}_{pinId}</c>.
    /// </summary>
    internal static string BuildDocumentId(string tenantId, string pinId)
        => $"{IdPrefix}_{tenantId}_{pinId}";

    // =========================================================================
    // IPinnedContextRepository
    // =========================================================================

    /// <inheritdoc/>
    public async Task CreateAsync(PinnedContextItem pin, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin.UserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(pin.Content);

        // Length caps enforced at the service layer per the PinnedContextItem XML doc
        // (kept off DataAnnotations to keep the POCO clean for Cosmos serialization).
        if (pin.Title.Length > MaxTitleLength)
        {
            throw new ArgumentException(
                $"PinnedContextItem.Title length {pin.Title.Length} exceeds the maximum {MaxTitleLength}.",
                nameof(pin));
        }
        if (pin.Content.Length > MaxContentLength)
        {
            throw new ArgumentException(
                $"PinnedContextItem.Content length {pin.Content.Length} exceeds the maximum {MaxContentLength}.",
                nameof(pin));
        }

        await _container.CreateItemAsync(
            item: pin,
            partitionKey: new PartitionKey(pin.TenantId),
            cancellationToken: ct);

        _logger.LogDebug(
            "PinnedContextRepository: Created pin {PinId} (tenant={TenantId}, user={UserId}, type={PinType})",
            pin.Id, pin.TenantId, pin.UserId, pin.PinType);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PinnedContextItem>> GetByMatterAsync(
        string tenantId,
        string matterId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(matterId);

        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @type AND c.matterId = @matterId")
                .WithParameter("@type", DocumentTypeValue)
                .WithParameter("@matterId", matterId);

            var requestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tenantId),
            };

            return await ExecuteQueryAsync(query, requestOptions, ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<PinnedContextItem>();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PinnedContextItem>> GetByUserAsync(
        string tenantId,
        string userId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        try
        {
            var query = new QueryDefinition(
                "SELECT * FROM c WHERE c.documentType = @type AND c.userId = @userId")
                .WithParameter("@type", DocumentTypeValue)
                .WithParameter("@userId", userId);

            var requestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(tenantId),
            };

            return await ExecuteQueryAsync(query, requestOptions, ct);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Array.Empty<PinnedContextItem>();
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string tenantId, string pinId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pinId);

        var id = BuildDocumentId(tenantId, pinId);
        try
        {
            await _container.DeleteItemAsync<PinnedContextItem>(
                id: id,
                partitionKey: new PartitionKey(tenantId),
                cancellationToken: ct);

            _logger.LogDebug(
                "PinnedContextRepository: Deleted pin {PinId} (tenant={TenantId})",
                pinId, tenantId);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent — pin already gone is OK (stale-handle race protection).
            _logger.LogDebug(
                "PinnedContextRepository: DeleteAsync no-op (pinId={PinId} tenant={TenantId} already absent)",
                pinId, tenantId);
        }
    }

    // =========================================================================
    // Cosmos helpers
    // =========================================================================

    private async Task<IReadOnlyList<PinnedContextItem>> ExecuteQueryAsync(
        QueryDefinition query,
        QueryRequestOptions requestOptions,
        CancellationToken ct)
    {
        using var iterator = _container.GetItemQueryIterator<PinnedContextItem>(
            query, requestOptions: requestOptions);

        var results = new List<PinnedContextItem>();
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(ct);
            foreach (var item in page)
            {
                results.Add(item);
            }
        }
        return results;
    }
}
