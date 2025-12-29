using System.Collections.Concurrent;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages RAG knowledge deployments and provides SearchClient routing.
/// Implements the 3 deployment models: Shared, Dedicated, CustomerOwned.
/// </summary>
/// <remarks>
/// Phase 1 Implementation:
/// - Shared and Dedicated models fully supported
/// - CustomerOwned model requires Key Vault integration (connection strings)
/// - Deployment configs cached in-memory (TODO: Dataverse persistence in future task)
///
/// Index naming:
/// - Shared: "spaarke-knowledge-index" (single multi-tenant index)
/// - Dedicated: "{tenantId}-knowledge" (per-customer index)
/// - CustomerOwned: Customer-specified index name
/// </remarks>
public class KnowledgeDeploymentService : IKnowledgeDeploymentService
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly SecretClient? _secretClient;
    private readonly AnalysisOptions _options;
    private readonly ILogger<KnowledgeDeploymentService> _logger;

    // In-memory cache for deployment configs (TODO: Move to Dataverse in future task)
    private readonly ConcurrentDictionary<string, KnowledgeDeploymentConfig> _configCache = new();
    private readonly ConcurrentDictionary<string, SearchClient> _clientCache = new();

    // Default shared index configuration
    private const string SharedIndexName = "spaarke-knowledge-index";

    public KnowledgeDeploymentService(
        SearchIndexClient searchIndexClient,
        IOptions<AnalysisOptions> options,
        ILogger<KnowledgeDeploymentService> logger,
        SecretClient? secretClient = null)
    {
        _searchIndexClient = searchIndexClient;
        _secretClient = secretClient;
        _options = options.Value;
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

        // TODO: Load from Dataverse sprk_aiknowledgedeployment entity
        // For now, return default config based on AnalysisOptions
        var defaultConfig = CreateDefaultConfig(tenantId);

        _configCache[tenantId] = defaultConfig;
        _logger.LogInformation("Created default deployment config for tenant {TenantId}, model={Model}",
            tenantId, defaultConfig.Model);

        return Task.FromResult(defaultConfig);
    }

    /// <inheritdoc />
    public async Task<SearchClient> GetSearchClientAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(tenantId);

        var config = await GetDeploymentConfigAsync(tenantId, cancellationToken);
        return await GetOrCreateSearchClientAsync(config, cancellationToken);
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
            // TODO: Load from Dataverse by ID
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

        // TODO: Persist to Dataverse sprk_aiknowledgedeployment entity
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
                IndexName = SharedIndexName,
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
