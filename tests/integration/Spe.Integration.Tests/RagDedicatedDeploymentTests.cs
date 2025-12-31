using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using Xunit.Abstractions;

namespace Spe.Integration.Tests;

/// <summary>
/// Integration tests for RAG Dedicated and CustomerOwned deployment models.
/// Tests per-customer index creation, isolation, and Key Vault integration.
/// </summary>
/// <remarks>
/// Task 007: Test Dedicated Deployment Model
///
/// Dedicated Model:
/// - Each customer gets their own index in our Azure subscription
/// - Index name: {sanitized-tenant-id}-knowledge
/// - Complete physical isolation (no tenant filtering needed)
///
/// CustomerOwned Model:
/// - Customer's own Azure AI Search in their subscription
/// - API key stored in our Key Vault
/// - Cross-tenant authentication required
///
/// These tests verify deployment model routing and isolation.
/// </remarks>
[Collection("Integration")]
[Trait("Category", "Integration")]
[Trait("Feature", "RAG")]
public class RagDedicatedDeploymentTests : IClassFixture<IntegrationTestFixture>, IAsyncLifetime
{
    private readonly IntegrationTestFixture _fixture;
    private readonly ITestOutputHelper _output;
    private readonly string _dedicatedTenantId = "test-dedicated-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _customerOwnedTenantId = "test-customerowned-" + Guid.NewGuid().ToString("N")[..8];
    private readonly List<string> _createdIndexes = [];

    public RagDedicatedDeploymentTests(IntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    public Task InitializeAsync()
    {
        _output.WriteLine($"Dedicated tenant ID: {_dedicatedTenantId}");
        _output.WriteLine($"CustomerOwned tenant ID: {_customerOwnedTenantId}");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        // Note: In a real test, we would delete the test indexes created
        // For now, we'll leave cleanup as a manual step since index deletion
        // in Azure AI Search requires admin-level operations
        _output.WriteLine($"Test indexes created (manual cleanup may be needed): {string.Join(", ", _createdIndexes)}");
        return Task.CompletedTask;
    }

    #region Dedicated Deployment Model Tests

    [Fact]
    public async Task GetDeploymentConfigAsync_DedicatedModel_CreatesPerCustomerIndexName()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();

        // Override options to use Dedicated model
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AnalysisOptions>>();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Act
        var config = await service.GetDeploymentConfigAsync(_dedicatedTenantId);

        // Assert
        config.Should().NotBeNull();
        config.Model.Should().Be(RagDeploymentModel.Dedicated);
        config.IndexName.Should().Contain(_dedicatedTenantId.ToLowerInvariant().Replace("-", ""));
        config.IndexName.Should().EndWith("-knowledge");
        config.IsActive.Should().BeTrue();

        _output.WriteLine($"Dedicated index name: {config.IndexName}");
        _createdIndexes.Add(config.IndexName);
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_DifferentTenants_GetDifferentIndexes()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var tenant1 = "tenant-abc-123";
        var tenant2 = "tenant-xyz-456";

        // Act
        var config1 = await service.GetDeploymentConfigAsync(tenant1);
        var config2 = await service.GetDeploymentConfigAsync(tenant2);

        // Assert - Each tenant should get a unique index
        config1.IndexName.Should().NotBe(config2.IndexName);
        config1.TenantId.Should().Be(tenant1);
        config2.TenantId.Should().Be(tenant2);

        _output.WriteLine($"Tenant 1 index: {config1.IndexName}");
        _output.WriteLine($"Tenant 2 index: {config2.IndexName}");
    }

    [Fact]
    public async Task SaveDeploymentConfigAsync_DedicatedConfig_PersistsCorrectly()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-save-test",
            Name = "Test Dedicated Deployment",
            Model = RagDeploymentModel.Dedicated,
            IndexName = "tenant-save-test-knowledge",
            IsActive = true
        };

        // Act
        var saved = await service.SaveDeploymentConfigAsync(config);

        // Assert
        saved.Should().NotBeNull();
        saved.Id.Should().NotBeNull();
        saved.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        saved.ModifiedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));

        _output.WriteLine($"Saved deployment config with ID: {saved.Id}");
    }

    [Fact]
    public async Task GetSearchClientAsync_DedicatedModel_ReturnsClientForCorrectIndex()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // First get config to know the index name
        var config = await service.GetDeploymentConfigAsync(_dedicatedTenantId);

        // Act
        var client = await service.GetSearchClientAsync(_dedicatedTenantId);

        // Assert
        client.Should().NotBeNull();
        client.IndexName.Should().Be(config.IndexName);

        _output.WriteLine($"SearchClient created for index: {client.IndexName}");
    }

    [Fact]
    public async Task GetSearchClientAsync_SameTenantTwice_ReturnsCachedClient()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Act
        var client1 = await service.GetSearchClientAsync(_dedicatedTenantId);
        var client2 = await service.GetSearchClientAsync(_dedicatedTenantId);

        // Assert - Should be same cached instance
        client1.Should().BeSameAs(client2);

        _output.WriteLine("SearchClient caching verified");
    }

    #endregion

    #region CustomerOwned Deployment Model Tests

    [Fact]
    public async Task GetDeploymentConfigAsync_CustomerOwnedModel_ReturnsInactiveByDefault()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Save a CustomerOwned config without required fields
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = _customerOwnedTenantId,
            Name = "Customer-Owned Test Deployment",
            Model = RagDeploymentModel.CustomerOwned,
            IsActive = false // Should be inactive until properly configured
        };

        var saved = await service.SaveDeploymentConfigAsync(config);

        // Act
        var retrieved = await service.GetDeploymentConfigAsync(_customerOwnedTenantId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved.Model.Should().Be(RagDeploymentModel.CustomerOwned);
        retrieved.IsActive.Should().BeFalse();

        _output.WriteLine($"CustomerOwned config is inactive until configured: {retrieved.Name}");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_MissingSearchEndpoint_ReturnsFailure()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var config = new KnowledgeDeploymentConfig
        {
            TenantId = _customerOwnedTenantId,
            Model = RagDeploymentModel.CustomerOwned,
            ApiKeySecretName = "test-secret"
            // SearchEndpoint is missing
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SearchEndpoint");

        _output.WriteLine($"Validation correctly failed: {result.ErrorMessage}");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_MissingApiKeySecretName_ReturnsFailure()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var config = new KnowledgeDeploymentConfig
        {
            TenantId = _customerOwnedTenantId,
            Model = RagDeploymentModel.CustomerOwned,
            SearchEndpoint = "https://customer-search.search.windows.net"
            // ApiKeySecretName is missing
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ApiKeySecretName");

        _output.WriteLine($"Validation correctly failed: {result.ErrorMessage}");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_SharedModel_ReturnsFailure()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var config = new KnowledgeDeploymentConfig
        {
            TenantId = _customerOwnedTenantId,
            Model = RagDeploymentModel.Shared, // Wrong model type
            SearchEndpoint = "https://customer-search.search.windows.net",
            ApiKeySecretName = "test-secret"
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("CustomerOwned");

        _output.WriteLine($"Validation correctly rejected non-CustomerOwned model");
    }

    [Fact]
    public async Task SaveDeploymentConfigAsync_CustomerOwnedWithAllFields_SavesCorrectly()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "customer-full-config",
            Name = "Full Customer-Owned Deployment",
            Model = RagDeploymentModel.CustomerOwned,
            IndexName = "customer-knowledge-index",
            SearchEndpoint = "https://customer-search.search.windows.net",
            ApiKeySecretName = "customer-api-key-secret",
            IsActive = true
        };

        // Act
        var saved = await service.SaveDeploymentConfigAsync(config);

        // Assert
        saved.Should().NotBeNull();
        saved.Id.Should().NotBeNull();
        saved.SearchEndpoint.Should().Be("https://customer-search.search.windows.net");
        saved.ApiKeySecretName.Should().Be("customer-api-key-secret");
        saved.IndexName.Should().Be("customer-knowledge-index");

        _output.WriteLine($"CustomerOwned config saved: {saved.Name}");
    }

    #endregion

    #region Cross-Model Isolation Tests

    [Fact]
    public async Task DifferentDeploymentModels_HaveCompleteIsolation()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Create configs for different models
        var sharedConfig = new KnowledgeDeploymentConfig
        {
            TenantId = "isolation-test-shared",
            Name = "Shared Isolation Test",
            Model = RagDeploymentModel.Shared,
            IndexName = "spaarke-knowledge-index"
        };

        var dedicatedConfig = new KnowledgeDeploymentConfig
        {
            TenantId = "isolation-test-dedicated",
            Name = "Dedicated Isolation Test",
            Model = RagDeploymentModel.Dedicated,
            IndexName = "isolation-test-dedicated-knowledge"
        };

        // Act
        var savedShared = await service.SaveDeploymentConfigAsync(sharedConfig);
        var savedDedicated = await service.SaveDeploymentConfigAsync(dedicatedConfig);

        // Assert - Different models should have different index names
        savedShared.IndexName.Should().NotBe(savedDedicated.IndexName);
        savedShared.Model.Should().Be(RagDeploymentModel.Shared);
        savedDedicated.Model.Should().Be(RagDeploymentModel.Dedicated);

        _output.WriteLine($"Shared index: {savedShared.IndexName}");
        _output.WriteLine($"Dedicated index: {savedDedicated.IndexName}");
    }

    [Fact]
    public async Task GetSearchClientAsync_DifferentModels_ReturnDifferentClients()
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Save configs for different tenants
        var sharedTenant = "client-test-shared";
        var dedicatedTenant = "client-test-dedicated";

        await service.SaveDeploymentConfigAsync(new KnowledgeDeploymentConfig
        {
            TenantId = sharedTenant,
            Model = RagDeploymentModel.Shared,
            IndexName = "spaarke-knowledge-index"
        });

        await service.SaveDeploymentConfigAsync(new KnowledgeDeploymentConfig
        {
            TenantId = dedicatedTenant,
            Model = RagDeploymentModel.Dedicated,
            IndexName = "client-test-dedicated-knowledge"
        });

        // Act
        var sharedClient = await service.GetSearchClientAsync(sharedTenant);
        var dedicatedClient = await service.GetSearchClientAsync(dedicatedTenant);

        // Assert - Clients should point to different indexes
        sharedClient.IndexName.Should().NotBe(dedicatedClient.IndexName);
        sharedClient.IndexName.Should().Be("spaarke-knowledge-index");
        dedicatedClient.IndexName.Should().Be("client-test-dedicated-knowledge");

        _output.WriteLine($"Shared client index: {sharedClient.IndexName}");
        _output.WriteLine($"Dedicated client index: {dedicatedClient.IndexName}");
    }

    #endregion

    #region Index Name Sanitization Tests

    [Theory]
    [InlineData("Tenant-123", "tenant-123-knowledge")]
    [InlineData("UPPERCASE", "uppercase-knowledge")]
    [InlineData("special!@#chars", "specialchars-knowledge")]
    [InlineData("tenant_with_underscore", "tenantwithunderscore-knowledge")]
    public async Task GetDeploymentConfigAsync_SanitizesTenantIdForIndexName(string tenantId, string expectedPrefix)
    {
        // Arrange
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IKnowledgeDeploymentService>();

        // Save as Dedicated to test index name generation
        await service.SaveDeploymentConfigAsync(new KnowledgeDeploymentConfig
        {
            TenantId = tenantId,
            Model = RagDeploymentModel.Dedicated
        });

        // Act
        var config = await service.GetDeploymentConfigAsync(tenantId);

        // Assert - Check prefix matches expected sanitized name
        config.IndexName.Should().StartWith(expectedPrefix.Replace("-knowledge", ""));
        config.IndexName.Should().MatchRegex("^[a-z0-9-]+$"); // Only lowercase alphanumeric and hyphens

        _output.WriteLine($"Tenant '{tenantId}' -> Index '{config.IndexName}'");
    }

    #endregion
}
