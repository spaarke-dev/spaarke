using Azure.Search.Documents.Indexes;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for KnowledgeDeploymentService - RAG deployment model routing.
/// Tests Phase 1 implementation: in-memory config cache and SearchClient creation.
/// </summary>
public class KnowledgeDeploymentServiceTests
{
    private readonly Mock<SearchIndexClient> _searchIndexClientMock;
    private readonly Mock<ILogger<KnowledgeDeploymentService>> _loggerMock;
    private readonly IOptions<AnalysisOptions> _options;

    public KnowledgeDeploymentServiceTests()
    {
        // SearchIndexClient requires URI and credential, mock it for tests
        _searchIndexClientMock = new Mock<SearchIndexClient>();
        _loggerMock = new Mock<ILogger<KnowledgeDeploymentService>>();
        _options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Shared,
            SharedIndexName = "spaarke-knowledge-index"
        });
    }

    private KnowledgeDeploymentService CreateService(SecretClient? secretClient = null)
    {
        return new KnowledgeDeploymentService(
            _searchIndexClientMock.Object,
            _options,
            _loggerMock.Object,
            secretClient);
    }

    #region GetDeploymentConfigAsync Tests

    [Fact]
    public async Task GetDeploymentConfigAsync_ValidTenantId_ReturnsDefaultSharedConfig()
    {
        // Arrange
        var service = CreateService();
        var tenantId = "tenant-001";

        // Act
        var result = await service.GetDeploymentConfigAsync(tenantId);

        // Assert
        result.Should().NotBeNull();
        result.TenantId.Should().Be(tenantId);
        result.Model.Should().Be(RagDeploymentModel.Shared);
        result.IndexName.Should().Be("spaarke-knowledge-index");
        result.IsActive.Should().BeTrue();
        result.Id.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_DedicatedModel_ReturnsCorrectIndexName()
    {
        // Arrange
        var options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Dedicated
        });
        var service = new KnowledgeDeploymentService(
            _searchIndexClientMock.Object,
            options,
            _loggerMock.Object);

        var tenantId = "tenant-002";

        // Act
        var result = await service.GetDeploymentConfigAsync(tenantId);

        // Assert
        result.Model.Should().Be(RagDeploymentModel.Dedicated);
        result.IndexName.Should().Be("tenant-002-knowledge");
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_CustomerOwnedModel_ReturnsInactiveByDefault()
    {
        // Arrange
        var options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.CustomerOwned
        });
        var service = new KnowledgeDeploymentService(
            _searchIndexClientMock.Object,
            options,
            _loggerMock.Object);

        var tenantId = "tenant-003";

        // Act
        var result = await service.GetDeploymentConfigAsync(tenantId);

        // Assert
        result.Model.Should().Be(RagDeploymentModel.CustomerOwned);
        result.IsActive.Should().BeFalse(); // Requires manual configuration
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_SameTenantTwice_ReturnsCachedConfig()
    {
        // Arrange
        var service = CreateService();
        var tenantId = "tenant-cached";

        // Act
        var first = await service.GetDeploymentConfigAsync(tenantId);
        var second = await service.GetDeploymentConfigAsync(tenantId);

        // Assert
        first.Should().BeSameAs(second); // Same instance from cache
        first.Id.Should().Be(second.Id);
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_NullTenantId_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.GetDeploymentConfigAsync(null!));
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_EmptyTenantId_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            async () => await service.GetDeploymentConfigAsync(string.Empty));
    }

    #endregion

    #region SaveDeploymentConfigAsync Tests

    [Fact]
    public async Task SaveDeploymentConfigAsync_NewConfig_AssignsIdAndTimestamps()
    {
        // Arrange
        var service = CreateService();
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-save",
            Name = "Test Deployment",
            Model = RagDeploymentModel.Dedicated,
            IndexName = "test-index"
        };

        // Act
        var result = await service.SaveDeploymentConfigAsync(config);

        // Assert
        result.Id.Should().NotBeNull();
        result.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        result.ModifiedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SaveDeploymentConfigAsync_ExistingConfig_UpdatesCacheAndModifiedAt()
    {
        // Arrange
        var service = CreateService();
        var tenantId = "tenant-update";

        // Get initial config
        var initial = await service.GetDeploymentConfigAsync(tenantId);
        var initialId = initial.Id;

        // Create updated config
        var updated = initial with { Name = "Updated Name" };

        // Act
        var result = await service.SaveDeploymentConfigAsync(updated);

        // Assert
        result.Id.Should().Be(initialId);
        result.Name.Should().Be("Updated Name");
        result.ModifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveDeploymentConfigAsync_NullConfig_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            async () => await service.SaveDeploymentConfigAsync(null!));
    }

    #endregion

    #region ValidateCustomerOwnedDeploymentAsync Tests

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_NonCustomerOwnedModel_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-validate",
            Model = RagDeploymentModel.Shared
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("CustomerOwned");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_MissingSearchEndpoint_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-validate",
            Model = RagDeploymentModel.CustomerOwned,
            ApiKeySecretName = "secret-name"
            // SearchEndpoint is null
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("SearchEndpoint");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_MissingApiKeySecretName_ReturnsFailure()
    {
        // Arrange
        var service = CreateService();
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-validate",
            Model = RagDeploymentModel.CustomerOwned,
            SearchEndpoint = "https://customer-search.search.windows.net"
            // ApiKeySecretName is null
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("ApiKeySecretName");
    }

    [Fact]
    public async Task ValidateCustomerOwnedDeploymentAsync_NoSecretClient_ThrowsInvalidOperationException()
    {
        // Arrange - No SecretClient provided
        var service = CreateService(secretClient: null);
        var config = new KnowledgeDeploymentConfig
        {
            TenantId = "tenant-validate",
            Model = RagDeploymentModel.CustomerOwned,
            SearchEndpoint = "https://customer-search.search.windows.net",
            ApiKeySecretName = "customer-api-key"
        };

        // Act
        var result = await service.ValidateCustomerOwnedDeploymentAsync(config);

        // Assert - Should fail because SecretClient is required
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Key Vault");
    }

    #endregion

    #region DeploymentValidationResult Tests

    [Fact]
    public void DeploymentValidationResult_Success_HasCorrectProperties()
    {
        // Arrange & Act
        var result = DeploymentValidationResult.Success(100, "v1.0");

        // Assert
        result.IsValid.Should().BeTrue();
        result.DocumentCount.Should().Be(100);
        result.SchemaVersion.Should().Be("v1.0");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void DeploymentValidationResult_Failure_HasCorrectProperties()
    {
        // Arrange & Act
        var result = DeploymentValidationResult.Failure("Connection failed");

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Be("Connection failed");
        result.DocumentCount.Should().BeNull();
    }

    #endregion

    #region Index Name Sanitization Tests

    [Fact]
    public async Task GetDeploymentConfigAsync_TenantIdWithSpecialChars_SanitizesIndexName()
    {
        // Arrange
        var options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Dedicated
        });
        var service = new KnowledgeDeploymentService(
            _searchIndexClientMock.Object,
            options,
            _loggerMock.Object);

        // Tenant ID with uppercase and special characters
        var tenantId = "Tenant_123!@#";

        // Act
        var result = await service.GetDeploymentConfigAsync(tenantId);

        // Assert - Index name should be sanitized (lowercase, alphanumeric, hyphens)
        result.IndexName.Should().Be("tenant123-knowledge");
    }

    [Fact]
    public async Task GetDeploymentConfigAsync_GuidTenantId_SanitizesCorrectly()
    {
        // Arrange
        var options = Options.Create(new AnalysisOptions
        {
            DefaultRagModel = RagDeploymentModel.Dedicated
        });
        var service = new KnowledgeDeploymentService(
            _searchIndexClientMock.Object,
            options,
            _loggerMock.Object);

        // GUID-style tenant ID (common in multi-tenant apps)
        var tenantId = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

        // Act
        var result = await service.GetDeploymentConfigAsync(tenantId);

        // Assert - GUID hyphens should be preserved
        result.IndexName.Should().Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890-knowledge");
    }

    #endregion
}

/// <summary>
/// Tests for KnowledgeDeploymentConfig record type.
/// </summary>
public class KnowledgeDeploymentConfigTests
{
    [Fact]
    public void KnowledgeDeploymentConfig_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var config = new KnowledgeDeploymentConfig();

        // Assert
        config.Model.Should().Be(RagDeploymentModel.Shared);
        config.IndexName.Should().Be("spaarke-knowledge-index");
        config.IsActive.Should().BeTrue();
        config.TenantId.Should().BeEmpty();
    }

    [Fact]
    public void KnowledgeDeploymentConfig_WithClause_CreatesNewInstance()
    {
        // Arrange
        var original = new KnowledgeDeploymentConfig
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-1",
            Name = "Original"
        };

        // Act
        var modified = original with { Name = "Modified" };

        // Assert
        modified.Name.Should().Be("Modified");
        modified.TenantId.Should().Be("tenant-1");
        modified.Id.Should().Be(original.Id);
        modified.Should().NotBeSameAs(original);
    }
}
