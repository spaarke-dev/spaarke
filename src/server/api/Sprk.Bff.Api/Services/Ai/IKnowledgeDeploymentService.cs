using Azure.Search.Documents;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Ai;

/// <summary>
/// Manages RAG knowledge deployment configurations and provides access to Azure AI Search clients.
/// Supports 3 deployment models: Shared (multi-tenant), Dedicated (per-customer), CustomerOwned (BYOK).
/// </summary>
/// <remarks>
/// Deployment model is determined by:
/// 1. Tenant-specific configuration in sprk_aiknowledgedeployment Dataverse entity
/// 2. Falls back to default from AnalysisOptions.DefaultRagModel
///
/// Index routing:
/// - Shared: spaarke-knowledge-index with tenantId filter
/// - Dedicated: {tenantId}-knowledge-index in Spaarke subscription
/// - CustomerOwned: Customer-provided index in customer's Azure subscription
/// </remarks>
public interface IKnowledgeDeploymentService
{
    /// <summary>
    /// Gets the RAG deployment configuration for a tenant.
    /// </summary>
    /// <param name="tenantId">Tenant identifier (Dataverse organization ID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deployment configuration including model type and index settings.</returns>
    Task<KnowledgeDeploymentConfig> GetDeploymentConfigAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a SearchClient configured for the tenant's deployment model.
    /// </summary>
    /// <param name="tenantId">Tenant identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Configured SearchClient for the tenant's knowledge index.</returns>
    Task<SearchClient> GetSearchClientAsync(
        string tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a SearchClient for a specific deployment ID (for explicit deployment selection).
    /// </summary>
    /// <param name="deploymentId">sprk_aiknowledgedeployment record ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Configured SearchClient for the deployment.</returns>
    Task<SearchClient> GetSearchClientByDeploymentAsync(
        Guid deploymentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a deployment configuration for a tenant.
    /// </summary>
    /// <param name="config">Deployment configuration to save.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Saved configuration with generated ID if new.</returns>
    Task<KnowledgeDeploymentConfig> SaveDeploymentConfigAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates that a CustomerOwned deployment is accessible.
    /// Tests connection to customer's Azure AI Search instance.
    /// </summary>
    /// <param name="config">CustomerOwned deployment configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Validation result with any errors.</returns>
    Task<DeploymentValidationResult> ValidateCustomerOwnedDeploymentAsync(
        KnowledgeDeploymentConfig config,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RAG knowledge deployment configuration.
/// Maps to sprk_aiknowledgedeployment Dataverse entity.
/// </summary>
public record KnowledgeDeploymentConfig
{
    /// <summary>
    /// Deployment record ID (sprk_aiknowledgedeploymentid).
    /// </summary>
    public Guid? Id { get; init; }

    /// <summary>
    /// Tenant identifier (Dataverse organization ID).
    /// </summary>
    public string TenantId { get; init; } = string.Empty;

    /// <summary>
    /// Display name for the deployment.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Deployment model type.
    /// </summary>
    public RagDeploymentModel Model { get; init; } = RagDeploymentModel.Shared;

    /// <summary>
    /// Azure AI Search service endpoint.
    /// Required for Dedicated and CustomerOwned models.
    /// </summary>
    public string? SearchEndpoint { get; init; }

    /// <summary>
    /// Index name in Azure AI Search.
    /// Defaults: Shared="spaarke-knowledge-index", Dedicated="{tenantId}-knowledge", CustomerOwned=customer-specified
    /// </summary>
    public string IndexName { get; init; } = "spaarke-knowledge-index";

    /// <summary>
    /// Key Vault secret name containing the API key (for CustomerOwned).
    /// Format: kv://{vault-name}/{secret-name}
    /// </summary>
    public string? ApiKeySecretName { get; init; }

    /// <summary>
    /// Whether this deployment is active.
    /// Inactive deployments are skipped during routing.
    /// </summary>
    public bool IsActive { get; init; } = true;

    /// <summary>
    /// Timestamp when deployment was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Timestamp when deployment was last modified.
    /// </summary>
    public DateTimeOffset? ModifiedAt { get; init; }
}

/// <summary>
/// Result of validating a CustomerOwned deployment.
/// </summary>
public record DeploymentValidationResult
{
    /// <summary>
    /// Whether the validation passed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Error message if validation failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Number of documents in the index (if accessible).
    /// </summary>
    public long? DocumentCount { get; init; }

    /// <summary>
    /// Index schema version (if accessible).
    /// </summary>
    public string? SchemaVersion { get; init; }

    /// <summary>
    /// Creates a successful validation result.
    /// </summary>
    public static DeploymentValidationResult Success(long documentCount, string? schemaVersion = null) =>
        new() { IsValid = true, DocumentCount = documentCount, SchemaVersion = schemaVersion };

    /// <summary>
    /// Creates a failed validation result.
    /// </summary>
    public static DeploymentValidationResult Failure(string errorMessage) =>
        new() { IsValid = false, ErrorMessage = errorMessage };
}
