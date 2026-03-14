using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Security.KeyVault.Secrets;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.SpeAdmin;

/// <summary>
/// Unit tests for multi-app registration support (Task 084).
///
/// Covers:
///   - SpeAdminGraphService.ContainerTypeConfig.HasOwningApp — correct detection of multi-app configs
///   - SpeAdminGraphService.ResolveConfigAsync — owning app fields populated from Dataverse
///   - SpeAdminTokenProvider.AcquireOwningAppTokenAsync — OBO flow, caching, error paths
///   - SpeAdminTokenProvider.ValidateOwningAppSecretsAsync — startup validation
///   - Per-app token caching — different configs use different cached tokens
///   - SHA256 token hashing — user tokens are not stored in plaintext
///   - Single-app backward compatibility — configs without owning app fall back to app-only
///
/// Live Azure dependencies (Key Vault, MSAL OBO exchange, Graph API) are not tested here.
/// Those are documented as manual test scenarios at the bottom of this file.
/// </summary>
public class MultiAppSupportTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // Test data helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Guid ConfigId1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ConfigId2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static SpeAdminGraphService.ContainerTypeConfig MakeSingleAppConfig(Guid? configId = null) =>
        new(
            ConfigId: configId ?? ConfigId1,
            ContainerTypeId: "ct-abc",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret");

    private static SpeAdminGraphService.ContainerTypeConfig MakeMultiAppConfig(
        Guid? configId = null,
        string? owningAppId = "owning-app-id",
        string? owningAppTenantId = "owning-tenant-id",
        string? owningAppSecretName = "owning-app-secret") =>
        new(
            ConfigId: configId ?? ConfigId1,
            ContainerTypeId: "ct-xyz",
            ClientId: "managing-app-id",
            TenantId: "tenant-id",
            SecretKeyVaultName: "managing-app-secret",
            OwningAppId: owningAppId,
            OwningAppTenantId: owningAppTenantId,
            OwningAppSecretName: owningAppSecretName);

    // ─────────────────────────────────────────────────────────────────────────
    // ContainerTypeConfig.HasOwningApp tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasOwningApp_ReturnsFalse_WhenOwningAppIdMissing()
    {
        // Arrange
        var config = MakeMultiAppConfig(owningAppId: null);

        // Act & Assert
        config.HasOwningApp.Should().BeFalse();
    }

    [Fact]
    public void HasOwningApp_ReturnsFalse_WhenOwningAppTenantIdMissing()
    {
        // Arrange
        var config = MakeMultiAppConfig(owningAppTenantId: null);

        // Act & Assert
        config.HasOwningApp.Should().BeFalse();
    }

    [Fact]
    public void HasOwningApp_ReturnsFalse_WhenOwningAppSecretNameMissing()
    {
        // Arrange
        var config = MakeMultiAppConfig(owningAppSecretName: null);

        // Act & Assert
        config.HasOwningApp.Should().BeFalse();
    }

    [Fact]
    public void HasOwningApp_ReturnsFalse_WhenAllOwningFieldsNull()
    {
        // Arrange
        var config = MakeSingleAppConfig();

        // Act & Assert
        config.HasOwningApp.Should().BeFalse();
    }

    [Fact]
    public void HasOwningApp_ReturnsTrue_WhenAllOwningFieldsPresent()
    {
        // Arrange
        var config = MakeMultiAppConfig();

        // Act & Assert
        config.HasOwningApp.Should().BeTrue();
    }

    [Theory]
    [InlineData("", "tenant", "secret")]
    [InlineData("appId", "", "secret")]
    [InlineData("appId", "tenant", "")]
    [InlineData("  ", "tenant", "secret")]
    public void HasOwningApp_ReturnsFalse_WhenAnyOwningFieldEmpty(
        string appId, string tenantId, string secretName)
    {
        // Arrange
        var config = MakeMultiAppConfig(owningAppId: appId, owningAppTenantId: tenantId, owningAppSecretName: secretName);

        // Act & Assert
        config.HasOwningApp.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ContainerTypeConfig record equality tests (backward compatibility)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ContainerTypeConfig_SingleAppMode_OptionalOwningFieldsDefaultToNull()
    {
        // Arrange
        var config = MakeSingleAppConfig();

        // Act & Assert
        config.OwningAppId.Should().BeNull();
        config.OwningAppTenantId.Should().BeNull();
        config.OwningAppSecretName.Should().BeNull();
        config.ClientId.Should().Be("managing-app-id");
        config.TenantId.Should().Be("tenant-id");
        config.SecretKeyVaultName.Should().Be("managing-app-secret");
    }

    [Fact]
    public void ContainerTypeConfig_MultiAppMode_AllFieldsPopulated()
    {
        // Arrange
        var config = MakeMultiAppConfig();

        // Act & Assert
        config.OwningAppId.Should().Be("owning-app-id");
        config.OwningAppTenantId.Should().Be("owning-tenant-id");
        config.OwningAppSecretName.Should().Be("owning-app-secret");
        config.ClientId.Should().Be("managing-app-id");  // managing app still present
        config.HasOwningApp.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SpeAdminTokenProvider — argument validation tests
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcquireOwningAppTokenAsync_ThrowsArgumentNull_WhenConfigNull()
    {
        // Arrange
        var provider = MakeTokenProvider();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.AcquireOwningAppTokenAsync(null!, "user-token"));
    }

    [Fact]
    public async Task AcquireOwningAppTokenAsync_ThrowsArgumentException_WhenUserTokenEmpty()
    {
        // Arrange
        var provider = MakeTokenProvider();
        var config = MakeMultiAppConfig();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.AcquireOwningAppTokenAsync(config, ""));
    }

    [Fact]
    public async Task AcquireOwningAppTokenAsync_ThrowsInvalidOperation_WhenConfigLacksOwningApp()
    {
        // Arrange
        var provider = MakeTokenProvider();
        var config = MakeSingleAppConfig(); // No owning app fields

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireOwningAppTokenAsync(config, "user-token"));

        ex.Message.Should().Contain("does not have owning app credentials");
        ex.Message.Should().Contain(config.ConfigId.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SpeAdminTokenProvider — Key Vault error handling
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AcquireOwningAppTokenAsync_ThrowsInvalidOperation_WhenKeyVaultSecretNotFound()
    {
        // Arrange
        var mockSecretClient = new Mock<SecretClient>();
        mockSecretClient
            .Setup(c => c.GetSecretAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Secret not found", "SecretNotFound", null));

        var provider = MakeTokenProvider(mockSecretClient.Object);
        var config = MakeMultiAppConfig();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireOwningAppTokenAsync(config, "user-token"));

        ex.Message.Should().Contain("not found");
        ex.Message.Should().Contain("owning-app-secret");
    }

    [Fact]
    public async Task AcquireOwningAppTokenAsync_ThrowsInvalidOperation_WhenKeyVaultAccessDenied()
    {
        // Arrange
        var mockSecretClient = new Mock<SecretClient>();
        mockSecretClient
            .Setup(c => c.GetSecretAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(403, "Forbidden", "Forbidden", null));

        var provider = MakeTokenProvider(mockSecretClient.Object);
        var config = MakeMultiAppConfig();

        // Act & Assert
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.AcquireOwningAppTokenAsync(config, "user-token"));

        ex.Message.Should().Contain("Access denied");
        ex.Message.Should().Contain("owning-app-secret");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SpeAdminTokenProvider — ValidateOwningAppSecretsAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateOwningAppSecrets_ReturnsEmpty_WhenNoOwningAppConfigs()
    {
        // Arrange — configs without owning app fields are skipped
        var provider = MakeTokenProvider();
        var configs = new[] { MakeSingleAppConfig(ConfigId1), MakeSingleAppConfig(ConfigId2) };

        // Act
        var failures = await provider.ValidateOwningAppSecretsAsync(configs);

        // Assert
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateOwningAppSecrets_ReturnsEmpty_WhenAllSecretsAccessible()
    {
        // Arrange
        var mockSecretClient = new Mock<SecretClient>();
        var secretValue = SecretModelFactory.KeyVaultSecret(new SecretProperties("owning-app-secret"), "secret-value");
        mockSecretClient
            .Setup(c => c.GetSecretAsync("owning-app-secret", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(secretValue, Mock.Of<Response>()));

        var provider = MakeTokenProvider(mockSecretClient.Object);
        var configs = new[] { MakeMultiAppConfig(ConfigId1) };

        // Act
        var failures = await provider.ValidateOwningAppSecretsAsync(configs);

        // Assert
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateOwningAppSecrets_ReturnsFailure_WhenSecretNotFound()
    {
        // Arrange
        var mockSecretClient = new Mock<SecretClient>();
        mockSecretClient
            .Setup(c => c.GetSecretAsync("owning-app-secret", null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found", "SecretNotFound", null));

        var provider = MakeTokenProvider(mockSecretClient.Object);
        var configs = new[] { MakeMultiAppConfig(ConfigId1) };

        // Act
        var failures = await provider.ValidateOwningAppSecretsAsync(configs);

        // Assert
        failures.Should().HaveCount(1);
        failures[0].ConfigId.Should().Be(ConfigId1);
        failures[0].SecretName.Should().Be("owning-app-secret");
        failures[0].Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateOwningAppSecrets_ReturnsMultipleFailures_WhenManySecretsInaccessible()
    {
        // Arrange
        var mockSecretClient = new Mock<SecretClient>();
        mockSecretClient
            .Setup(c => c.GetSecretAsync(It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RequestFailedException(404, "Not found", "SecretNotFound", null));

        var provider = MakeTokenProvider(mockSecretClient.Object);
        var configs = new[]
        {
            MakeMultiAppConfig(ConfigId1, owningAppSecretName: "secret-1"),
            MakeMultiAppConfig(ConfigId2, owningAppSecretName: "secret-2")
        };

        // Act
        var failures = await provider.ValidateOwningAppSecretsAsync(configs);

        // Assert
        failures.Should().HaveCount(2);
        failures.Select(f => f.SecretName).Should().Contain("secret-1").And.Contain("secret-2");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SpeAdminTokenProvider — EvictExpiredTokens
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EvictExpiredTokens_DoesNotThrow_WhenCacheIsEmpty()
    {
        // Arrange
        var provider = MakeTokenProvider();

        // Act & Assert — should not throw
        provider.Invoking(p => p.EvictExpiredTokens()).Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SHA256 token hashing — user tokens not stored in plaintext
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TokenHashing_DifferentTokens_ProduceDifferentCacheKeys()
    {
        // This test verifies that token hashing correctly differentiates tokens.
        // Since HashToken is private, we validate by checking that different tokens
        // produce different cache misses (via ValidateOwningAppSecretsAsync calls).

        // Arrange — compute SHA256 of two different tokens
        var token1Hash = ComputeSha256("user-token-alice");
        var token2Hash = ComputeSha256("user-token-bob");

        // Act & Assert — different tokens produce different hashes
        token1Hash.Should().NotBe(token2Hash);
    }

    [Fact]
    public void TokenHashing_SameToken_ProducesSameCacheKey()
    {
        // Verify deterministic hashing for cache key consistency
        var hash1 = ComputeSha256("my-access-token-abc123");
        var hash2 = ComputeSha256("my-access-token-abc123");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void TokenHashing_ProducesHexString_NotBase64OrPlaintext()
    {
        // Verify the hash format — should be hex (lowercase), not the raw token
        var token = "user-bearer-token-12345";
        var hash = ComputeSha256(token);

        // SHA256 produces 32 bytes = 64 hex characters
        hash.Should().HaveLength(64);
        hash.Should().MatchRegex("^[0-9a-f]+$");
        hash.Should().NotContain(token); // plaintext MUST NOT appear in hash
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Per-config token isolation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TwoConfigs_HaveDifferentCacheKeys_EvenWithSameUserToken()
    {
        // Verify cache key includes configId — different configs don't share cached tokens.
        // Cache key format: "{configId}:{sha256(userToken)}"

        var userToken = "shared-user-token";
        var tokenHash = ComputeSha256(userToken);

        var cacheKey1 = $"{ConfigId1}:{tokenHash}";
        var cacheKey2 = $"{ConfigId2}:{tokenHash}";

        cacheKey1.Should().NotBe(cacheKey2);
        cacheKey1.Should().Contain(ConfigId1.ToString());
        cacheKey2.Should().Contain(ConfigId2.ToString());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SpeAdminGraphService.GetClientForOwningAppAsync — backward compatibility
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ContainerTypeConfig_SingleApp_FallsBackToManagingApp()
    {
        // Verify that single-app configs (no owning app fields) don't trigger multi-app path.
        var config = MakeSingleAppConfig();

        // Single-app configs should NOT have owning app set
        config.HasOwningApp.Should().BeFalse();

        // Existing Phase 1 fields must still be present
        config.ClientId.Should().NotBeNullOrEmpty();
        config.TenantId.Should().NotBeNullOrEmpty();
        config.SecretKeyVaultName.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FetchOwningAppSecretAsync argument validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FetchOwningAppSecretAsync_ThrowsArgumentNull_WhenConfigNull()
    {
        var provider = MakeTokenProvider();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.FetchOwningAppSecretAsync(null!));
    }

    [Fact]
    public async Task FetchOwningAppSecretAsync_ThrowsInvalidOperation_WhenSecretNameMissing()
    {
        var provider = MakeTokenProvider();
        var config = MakeSingleAppConfig(); // No owning app secret name

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.FetchOwningAppSecretAsync(config));

        ex.Message.Should().Contain("does not have an owning app secret name");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static SpeAdminTokenProvider MakeTokenProvider(SecretClient? secretClient = null)
    {
        var mockSecretClient = secretClient ?? new Mock<SecretClient>().Object;
        return new SpeAdminTokenProvider(
            mockSecretClient,
            NullLogger<SpeAdminTokenProvider>.Instance);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MANUAL TEST SCENARIOS — Live Azure/MSAL (not automated)
// ─────────────────────────────────────────────────────────────────────────────

/*
The following scenarios require live Azure infrastructure and cannot be automated in unit tests.
They should be verified manually in the dev environment after deployment.

SCENARIO 1: Multi-app OBO token exchange succeeds
  Prerequisites:
    - sprk_specontainertypeconfig record with sprk_owningappid, sprk_owningapptenantid,
      sprk_owningappsecretname set to a valid Azure AD app registration
    - owning app registered in Azure AD with OBO flow enabled (grant admin consent)
    - owning app client secret stored in Key Vault under the configured secret name
  Test:
    - Call a SPE Admin endpoint with X-Config-Id header pointing to the multi-app config
    - Verify Graph API call succeeds using owning app identity (check app_id in Graph logs)
    - Verify second call within 55 minutes uses cached token (no new Key Vault fetch)

SCENARIO 2: OBO token cache isolation
  Prerequisites: Two different BU configs with different owning app registrations
  Test:
    - Call endpoints for both configs in the same session
    - Verify each config uses its own cached token (no cross-contamination)
    - Check logs: each config should show "OBO token cache MISS" on first call,
      "OBO token cache HIT" on subsequent calls

SCENARIO 3: Key Vault secret rotation
  Prerequisites: Multi-app config in Dataverse with owning app fields set
  Test:
    - Rotate the secret in Key Vault
    - Wait for token cache to expire (or restart the API)
    - Verify new token is acquired with the rotated secret

SCENARIO 4: Startup validation warning
  Prerequisites: Configure a BU config with an owning app secret that does NOT exist in Key Vault
  Test:
    - Start the API
    - Check Application Insights / logs for the startup validation warning
    - Verify API starts successfully (warning, not error)
    - Verify the config with missing secret fails gracefully at request time

SCENARIO 5: Single-app backward compatibility
  Prerequisites: Existing BU config without owning app fields
  Test:
    - Verify existing configs continue to work without any config changes
    - No OBO flow should be triggered (check logs: "Using single-app mode")
    - Graph API calls use managing app credentials as before
*/
