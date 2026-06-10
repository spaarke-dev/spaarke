using System.Collections.Concurrent;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages RAG knowledge deployments and provides SearchClient routing.
/// Implements the 3 deployment models: Shared, Dedicated, CustomerOwned.
/// </summary>
/// <remarks>
/// Phase 1 Implementation:
/// - Shared and Dedicated models fully supported
/// - CustomerOwned model requires Key Vault integration (connection strings)
/// - Deployment configs cached in-memory (TRACKED: GitHub #229 - Dataverse persistence)
///
/// Index naming (configurable via AnalysisOptions):
/// - Shared: Uses AnalysisOptions.SharedIndexName (single multi-tenant index)
/// - Dedicated: "{tenantId}-knowledge" (per-customer index)
/// - CustomerOwned: Customer-specified index name
/// </remarks>
public class KnowledgeDeploymentService : IKnowledgeDeploymentService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly SecretClient? _secretClient;
    private readonly AnalysisOptions _options;
    private readonly AiSearchOptions? _aiSearchOptions;
    private readonly IAllowedIndexesProvider? _allowedIndexesProvider;
    private readonly ILogger<KnowledgeDeploymentService> _logger;

    // In-memory cache for deployment configs (TRACKED: GitHub #229 - Move to Dataverse)
    private readonly ConcurrentDictionary<string, KnowledgeDeploymentConfig> _configCache = new();
    private readonly ConcurrentDictionary<string, SearchClient> _clientCache = new();

    /// <summary>
    /// Constructs the resolver.
    /// </summary>
    /// <remarks>
    /// <para>The <paramref name="aiSearchOptions"/> and <paramref name="allowedIndexesProvider"/>
    /// parameters are OPTIONAL (default to <c>null</c>) so existing test fixtures that construct
    /// <see cref="KnowledgeDeploymentService"/> directly with only
    /// (<c>SearchIndexClient</c>, <c>IOptions&lt;AnalysisOptions&gt;</c>, <c>ILogger</c>) continue
    /// to compile UNCHANGED — multi-container-multi-index-r1 spec NFR-02 (backward-compat) is the
    /// binding requirement.</para>
    /// <para>In production DI: <c>IOptions&lt;AiSearchOptions&gt;</c> is registered by
    /// <see cref="Sprk.Bff.Api.Infrastructure.DI.JobProcessingModule"/>; the
    /// <see cref="IAllowedIndexesProvider"/> is registered by
    /// <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/> (Phase G task 102) when
    /// the compound AI gate is ON. Both parameters resolve automatically.</para>
    /// <para><b>Allow-list resolution order</b> (Phase G):
    /// (1) when <paramref name="allowedIndexesProvider"/> is non-null, the provider is consulted
    /// (cached Dataverse query of <c>sprk_aisearchindex</c>; falls back internally to appsettings);
    /// (2) when the provider is null but <paramref name="aiSearchOptions"/> is non-null, the
    /// appsettings <see cref="AiSearchOptions.AllowedIndexes"/> array is used directly (legacy path
    /// retained for tests + AI-OFF DI graph);
    /// (3) when both are null, the call fails closed (no allow-list → no caller-supplied index
    /// permitted).</para>
    /// </remarks>
    public KnowledgeDeploymentService(
        SearchIndexClient searchIndexClient,
        IOptions<AnalysisOptions> options,
        ILogger<KnowledgeDeploymentService> logger,
        SecretClient? secretClient = null,
        IOptions<AiSearchOptions>? aiSearchOptions = null,
        IAllowedIndexesProvider? allowedIndexesProvider = null)
    {
        _searchIndexClient = searchIndexClient;
        _secretClient = secretClient;
        _options = options.Value;
        _aiSearchOptions = aiSearchOptions?.Value;
        _allowedIndexesProvider = allowedIndexesProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<KnowledgeDeploymentConfig> GetDeploymentConfigAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        _logger.LogDebug("Getting deployment config for tenant {TenantId}", tenantId);

        // Check cache first
        if (_configCache.TryGetValue(tenantId, out var cachedConfig))
        {
            _logger.LogDebug("Found cached config for tenant {TenantId}, model={Model}", tenantId, cachedConfig.Model);
            return Task.FromResult(cachedConfig);
        }

        // TRACKED: GitHub #229 - Load from Dataverse sprk_aiknowledgedeployment entity
        // For now, return default config based on AnalysisOptions
        var defaultConfig = CreateDefaultConfig(tenantId);

        _configCache[tenantId] = defaultConfig;
        _logger.LogInformation("Created default deployment config for tenant {TenantId}, model={Model}",
            tenantId, defaultConfig.Model);

        return Task.FromResult(defaultConfig);
    }

    /// <inheritdoc />
    public Task<SearchClient> GetSearchClientAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
        => GetSearchClientAsync(tenantId, indexName: null, cancellationToken);

    /// <inheritdoc />
    public async Task<SearchClient> GetSearchClientAsync(
        string tenantId,
        string? indexName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        // Multi-container-multi-index-r1 Phase B (FR-BFF-01..04) — caller-supplied explicit index.
        // When non-empty, validate against the allow-list and bind a SearchClient to that
        // index name (resolved via the tenant's SearchIndexClient endpoint). When null/whitespace,
        // fall through to the existing 2-tier chain (FR-BFF-04 backward-compat per NFR-02).
        //
        // Phase G (task 102): when an IAllowedIndexesProvider is registered, the provider is the
        // source of truth (cached Dataverse query of sprk_aisearchindex). Otherwise the legacy
        // appsettings AllowedIndexes array is used directly.
        if (!string.IsNullOrWhiteSpace(indexName))
        {
            await ValidateAllowedIndexAsync(indexName, cancellationToken);
            return GetOrCreateExplicitIndexClient(tenantId, indexName);
        }

        var config = await GetDeploymentConfigAsync(tenantId, cancellationToken);
        return await GetOrCreateSearchClientAsync(config, cancellationToken);
    }

    /// <summary>
    /// Validates a caller-supplied index name against the configured allow-list. On miss,
    /// throws <see cref="SdapProblemException"/> with stable code <c>INDEX_NOT_ALLOWED</c>
    /// mapping to ProblemDetails 400 per ADR-019 + spec NFR-08.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Allow-list resolution order (Phase G):
    /// </para>
    /// <list type="number">
    ///   <item><description>When <see cref="IAllowedIndexesProvider"/> is registered, the provider
    ///     is consulted (cached Dataverse query of <c>sprk_aisearchindex</c>; the provider itself
    ///     falls back to <see cref="AiSearchOptions.AllowedIndexes"/> on Dataverse failure /
    ///     empty result per Phase G spec §13 Q4).</description></item>
    ///   <item><description>Otherwise the legacy appsettings array is used directly. This path is
    ///     retained for (a) AI-OFF DI graphs that don't register the provider, and (b) test fixtures
    ///     that construct the service without the optional dependency.</description></item>
    ///   <item><description>When neither source is available, FAILS CLOSED — caller-supplied indexes
    ///     are rejected outright.</description></item>
    /// </list>
    /// <para>
    /// Case-insensitive comparison: Azure AI Search index names are documented as case-insensitive
    /// at the service level, and operator-configured allow-list values should not require
    /// exact-case matching by callers.
    /// </para>
    /// </remarks>
    private async Task ValidateAllowedIndexAsync(string indexName, CancellationToken ct)
    {
        bool isAllowed;
        int allowedCount;
        string allowListSource;

        if (_allowedIndexesProvider is not null)
        {
            // Phase G — Dataverse-backed source of truth (with internal appsettings fallback)
            isAllowed = await _allowedIndexesProvider.IsAllowedAsync(indexName, ct);
            // The provider does not expose a count; for diagnostic extension we fall back to the
            // appsettings count when present (it is the floor — Dataverse-loaded set is always
            // >= appsettings on the happy path).
            allowedCount = _aiSearchOptions?.AllowedIndexes?.Length ?? -1;
            allowListSource = "DataverseAllowedIndexesProvider";
        }
        else
        {
            // Legacy path — direct appsettings array. Fails closed when no options registered.
            var allowedIndexes = _aiSearchOptions?.AllowedIndexes ?? Array.Empty<string>();
            allowedCount = allowedIndexes.Length;
            isAllowed = allowedIndexes.Any(allowed =>
                string.Equals(allowed, indexName, StringComparison.OrdinalIgnoreCase));
            allowListSource = "AiSearchOptions.AllowedIndexes";
        }

        if (!isAllowed)
        {
            _logger.LogWarning(
                "Rejecting caller-supplied indexName '{RejectedIndexName}' — not present in allow-list (source: {AllowListSource}, knownCount: {AllowedCount}).",
                indexName,
                allowListSource,
                allowedCount);

            throw new SdapProblemException(
                code: "INDEX_NOT_ALLOWED",
                title: "AI Search index not allowed",
                detail: $"The requested AI Search index '{indexName}' is not in the configured allow-list. Contact your administrator to enable this index.",
                statusCode: 400,
                extensions: new Dictionary<string, object>
                {
                    ["indexName"] = indexName,
                    // Preserve the wire shape — existing clients (and the test suite at task 010)
                    // assert on the `allowedCount` extension key. When the provider is the source,
                    // we use the appsettings count as a best-effort floor (the Dataverse-loaded
                    // count isn't surfaced by the provider for caching reasons).
                    ["allowedCount"] = Math.Max(allowedCount, 0)
                });
        }

        _logger.LogDebug(
            "Caller-supplied indexName '{IndexName}' validated against {AllowListSource}.",
            indexName,
            allowListSource);
    }

    /// <summary>
    /// Returns a SearchClient bound to the explicit (validated) index name, reusing the
    /// tenant's <see cref="SearchIndexClient"/> endpoint. Caches per (tenant, index) pair to
    /// match the existing client-cache strategy in <see cref="GetOrCreateSearchClientAsync"/>.
    /// </summary>
    private SearchClient GetOrCreateExplicitIndexClient(string tenantId, string indexName)
    {
        var cacheKey = $"{tenantId}:explicit:{indexName}";

        if (_clientCache.TryGetValue(cacheKey, out var cachedClient))
        {
            return cachedClient;
        }

        _logger.LogDebug(
            "Creating explicit-index SearchClient for tenant {TenantId}, indexName={IndexName}",
            tenantId,
            indexName);

        var client = _searchIndexClient.GetSearchClient(indexName);
        _clientCache[cacheKey] = client;
        return client;
    }

    /// <inheritdoc />
    public async Task<SearchClient> GetSearchClientByDeploymentAsync(
        Guid deploymentId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting SearchClient for deployment {DeploymentId}", deploymentId);

        // Find config by deployment ID
        var config = _configCache.Values.FirstOrDefault(c => c.Id == deploymentId);

        if (config == null)
        {
            // TRACKED: GitHub #229 - Load from Dataverse by ID
            throw new InvalidOperationException($"Deployment {deploymentId} not found. Dataverse integration pending.");
        }

        return await GetOrCreateSearchClientAsync(config, cancellationToken);
    }

    /// <inheritdoc />
    public Task<KnowledgeDeploymentConfig> SaveDeploymentConfigAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        _logger.LogInformation("Saving deployment config for tenant {TenantId}, model={Model}",
            config.TenantId, config.Model);

        // Assign ID if new
        var savedConfig = config with
        {
            Id = config.Id ?? Guid.NewGuid(),
            ModifiedAt = DateTimeOffset.UtcNow,
            CreatedAt = config.CreatedAt == default ? DateTimeOffset.UtcNow : config.CreatedAt
        };

        // TRACKED: GitHub #229 - Persist to Dataverse sprk_aiknowledgedeployment entity
        // For now, update cache
        _configCache[config.TenantId] = savedConfig;

        // Invalidate any cached SearchClient for this tenant
        _clientCache.TryRemove(config.TenantId, out _);

        return Task.FromResult(savedConfig);
    }

    /// <inheritdoc />
    public async Task<DeploymentValidationResult> ValidateCustomerOwnedDeploymentAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Model != RagDeploymentModel.CustomerOwned)
        {
            return DeploymentValidationResult.Failure("Validation only applicable to CustomerOwned deployments");
        }

        if (string.IsNullOrEmpty(config.SearchEndpoint))
        {
            return DeploymentValidationResult.Failure("SearchEndpoint is required for CustomerOwned deployment");
        }

        if (string.IsNullOrEmpty(config.ApiKeySecretName))
        {
            return DeploymentValidationResult.Failure("ApiKeySecretName is required for CustomerOwned deployment");
        }

        _logger.LogInformation("Validating CustomerOwned deployment for tenant {TenantId}", config.TenantId);

        try
        {
            // Get API key from Key Vault
            var apiKey = await GetApiKeyFromKeyVaultAsync(config.ApiKeySecretName, cancellationToken);

            // Create client and test connection
            var client = new SearchClient(
                new Uri(config.SearchEndpoint),
                config.IndexName,
                new AzureKeyCredential(apiKey));

            // Try to get document count to validate connectivity
            var response = await client.GetDocumentCountAsync(cancellationToken);

            _logger.LogInformation("CustomerOwned deployment validated successfully. Document count: {Count}",
                response.Value);

            return DeploymentValidationResult.Success(response.Value);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex, "CustomerOwned deployment validation failed for tenant {TenantId}", config.TenantId);
            return DeploymentValidationResult.Failure($"Azure AI Search error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating CustomerOwned deployment for tenant {TenantId}",
                config.TenantId);
            return DeploymentValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }

    #region Private Methods

    private KnowledgeDeploymentConfig CreateDefaultConfig(string tenantId)
    {
        var model = _options.DefaultRagModel;

        return model switch
        {
            RagDeploymentModel.Shared => new KnowledgeDeploymentConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = "Default Shared Deployment",
                Model = RagDeploymentModel.Shared,
                IndexName = _options.SharedIndexName,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            RagDeploymentModel.Dedicated => new KnowledgeDeploymentConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Dedicated Deployment - {tenantId}",
                Model = RagDeploymentModel.Dedicated,
                IndexName = $"{SanitizeTenantId(tenantId)}-knowledge",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            RagDeploymentModel.CustomerOwned => new KnowledgeDeploymentConfig
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Name = $"Customer-Owned Deployment - {tenantId}",
                Model = RagDeploymentModel.CustomerOwned,
                IsActive = false, // Requires manual configuration
                CreatedAt = DateTimeOffset.UtcNow
            },
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unknown deployment model")
        };
    }

    private async Task<SearchClient> GetOrCreateSearchClientAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{config.TenantId}:{config.Model}:{config.IndexName}";

        if (_clientCache.TryGetValue(cacheKey, out var cachedClient))
        {
            return cachedClient;
        }

        SearchClient client = config.Model switch
        {
            RagDeploymentModel.Shared => CreateSharedClient(config),
            RagDeploymentModel.Dedicated => CreateDedicatedClient(config),
            RagDeploymentModel.CustomerOwned => await CreateCustomerOwnedClientAsync(config, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(config.Model), config.Model, "Unknown deployment model")
        };

        _clientCache[cacheKey] = client;
        _logger.LogDebug("Created and cached SearchClient for tenant {TenantId}, model={Model}",
            config.TenantId, config.Model);

        return client;
    }

    private SearchClient CreateSharedClient(KnowledgeDeploymentConfig config)
    {
        _logger.LogDebug("Creating Shared SearchClient for index {IndexName}", config.IndexName);
        return _searchIndexClient.GetSearchClient(config.IndexName);
    }

    private SearchClient CreateDedicatedClient(KnowledgeDeploymentConfig config)
    {
        _logger.LogDebug("Creating Dedicated SearchClient for index {IndexName}", config.IndexName);

        // Dedicated indexes are still in our subscription, just with different index names
        return _searchIndexClient.GetSearchClient(config.IndexName);
    }

    private async Task<SearchClient> CreateCustomerOwnedClientAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(config.SearchEndpoint))
        {
            throw new InvalidOperationException(
                $"CustomerOwned deployment for tenant {config.TenantId} requires SearchEndpoint");
        }

        if (string.IsNullOrEmpty(config.ApiKeySecretName))
        {
            throw new InvalidOperationException(
                $"CustomerOwned deployment for tenant {config.TenantId} requires ApiKeySecretName");
        }

        _logger.LogDebug("Creating CustomerOwned SearchClient for tenant {TenantId}, endpoint={Endpoint}",
            config.TenantId, config.SearchEndpoint);

        var apiKey = await GetApiKeyFromKeyVaultAsync(config.ApiKeySecretName, cancellationToken);

        return new SearchClient(
            new Uri(config.SearchEndpoint),
            config.IndexName,
            new AzureKeyCredential(apiKey));
    }

    private async Task<string> GetApiKeyFromKeyVaultAsync(
        string secretName,
        CancellationToken cancellationToken)
    {
        if (_secretClient == null)
        {
            throw new InvalidOperationException(
                "Key Vault SecretClient not configured. Required for CustomerOwned deployments.");
        }

        _logger.LogDebug("Retrieving API key from Key Vault: {SecretName}", secretName);

        // Parse secret name (format: kv://{vault-name}/{secret-name} or just secret-name)
        var actualSecretName = secretName.StartsWith("kv://")
            ? secretName.Split('/').Last()
            : secretName;

        var secret = await _secretClient.GetSecretAsync(actualSecretName, cancellationToken: cancellationToken);
        return secret.Value.Value;
    }

    private static string SanitizeTenantId(string tenantId)
    {
        // Azure AI Search index names must be lowercase and alphanumeric
        return new string(tenantId.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == '-')
            .ToArray());
    }

    #endregion
}
