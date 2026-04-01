using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Agent;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Agent;

/// <summary>
/// Unit tests for AgentConfigurationService.
/// Validates caching behavior, capability toggles, and role-based access.
/// </summary>
public class AgentConfigurationServiceTests
{
    private const string TenantId = "test-tenant-001";
    private readonly MemoryDistributedCache _cache;
    private readonly Mock<ILogger<AgentConfigurationService>> _loggerMock;

    public AgentConfigurationServiceTests()
    {
        _cache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        _loggerMock = new Mock<ILogger<AgentConfigurationService>>();
    }

    private AgentConfigurationService CreateService(AgentConfigurationOptions? options = null)
    {
        options ??= new AgentConfigurationOptions();
        return new AgentConfigurationService(
            _cache,
            _loggerMock.Object,
            Options.Create(options));
    }

    #region GetExposedPlaybookIdsAsync Tests

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_WhenNotCached_ReturnsDefaults()
    {
        var playbookId1 = Guid.NewGuid();
        var playbookId2 = Guid.NewGuid();
        var service = CreateService(new AgentConfigurationOptions
        {
            DefaultExposedPlaybookIds = new List<Guid> { playbookId1, playbookId2 }
        });

        var result = await service.GetExposedPlaybookIdsAsync(TenantId);

        result.Should().HaveCount(2);
        result.Should().Contain(playbookId1);
        result.Should().Contain(playbookId2);
    }

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_WhenCached_ReturnsCachedValues()
    {
        var cachedIds = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:exposed-playbooks",
            JsonSerializer.Serialize(cachedIds));

        var service = CreateService(new AgentConfigurationOptions
        {
            DefaultExposedPlaybookIds = new List<Guid> { Guid.NewGuid() } // different from cached
        });

        var result = await service.GetExposedPlaybookIdsAsync(TenantId);

        result.Should().HaveCount(3);
        result.Should().BeEquivalentTo(cachedIds);
    }

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_WhenNoDefaults_ReturnsEmptyList()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            DefaultExposedPlaybookIds = null
        });

        var result = await service.GetExposedPlaybookIdsAsync(TenantId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_CachesResultAfterFirstCall()
    {
        var playbookId = Guid.NewGuid();
        var service = CreateService(new AgentConfigurationOptions
        {
            DefaultExposedPlaybookIds = new List<Guid> { playbookId }
        });

        // First call should populate cache
        await service.GetExposedPlaybookIdsAsync(TenantId);

        // Verify cache was populated
        var cached = await _cache.GetStringAsync($"agent:config:{TenantId}:exposed-playbooks");
        cached.Should().NotBeNull();

        var cachedIds = JsonSerializer.Deserialize<List<Guid>>(cached!);
        cachedIds.Should().Contain(playbookId);
    }

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_DifferentTenants_HaveSeparateCaches()
    {
        var tenant1Id = Guid.NewGuid();
        var tenant2Id = Guid.NewGuid();

        await _cache.SetStringAsync(
            "agent:config:tenant-A:exposed-playbooks",
            JsonSerializer.Serialize(new List<Guid> { tenant1Id }));
        await _cache.SetStringAsync(
            "agent:config:tenant-B:exposed-playbooks",
            JsonSerializer.Serialize(new List<Guid> { tenant2Id }));

        var service = CreateService();

        var resultA = await service.GetExposedPlaybookIdsAsync("tenant-A");
        var resultB = await service.GetExposedPlaybookIdsAsync("tenant-B");

        resultA.Should().Contain(tenant1Id);
        resultB.Should().Contain(tenant2Id);
        resultA.Should().NotIntersectWith(resultB);
    }

    #endregion

    #region IsCapabilityEnabledAsync Tests

    [Theory]
    [InlineData(AgentCapability.DocumentSearch, true)]
    [InlineData(AgentCapability.PlaybookInvocation, true)]
    [InlineData(AgentCapability.EmailDrafting, true)]
    [InlineData(AgentCapability.MatterQueries, true)]
    [InlineData(AgentCapability.AnalysisHandoff, true)]
    public async Task IsCapabilityEnabledAsync_WhenNotCached_ReturnsDefaultTrue(
        AgentCapability capability, bool expected)
    {
        var service = CreateService(); // all defaults are true

        var result = await service.IsCapabilityEnabledAsync(TenantId, capability);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenDocumentSearchDisabled_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            EnableDocumentSearch = false
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.DocumentSearch);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenPlaybookInvocationDisabled_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            EnablePlaybookInvocation = false
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.PlaybookInvocation);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenEmailDraftingDisabled_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            EnableEmailDrafting = false
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.EmailDrafting);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenMatterQueriesDisabled_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            EnableMatterQueries = false
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.MatterQueries);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenAnalysisHandoffDisabled_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            EnableAnalysisHandoff = false
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.AnalysisHandoff);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenCached_ReturnsCachedValue()
    {
        var capabilities = new Dictionary<string, bool>
        {
            [AgentCapability.DocumentSearch.ToString()] = false,
            [AgentCapability.EmailDrafting.ToString()] = true
        };
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:capabilities",
            JsonSerializer.Serialize(capabilities));

        // Options say enabled, but cache says disabled
        var service = CreateService(new AgentConfigurationOptions
        {
            EnableDocumentSearch = true
        });

        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.DocumentSearch);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_WhenCachedButCapabilityMissing_FallsBackToOptions()
    {
        var capabilities = new Dictionary<string, bool>
        {
            [AgentCapability.DocumentSearch.ToString()] = false
        };
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:capabilities",
            JsonSerializer.Serialize(capabilities));

        var service = CreateService(new AgentConfigurationOptions
        {
            EnableEmailDrafting = false
        });

        // EmailDrafting not in cache, should fall back to options
        var result = await service.IsCapabilityEnabledAsync(TenantId, AgentCapability.EmailDrafting);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsCapabilityEnabledAsync_UnknownCapability_ReturnsTrue()
    {
        var service = CreateService();

        var result = await service.IsCapabilityEnabledAsync(TenantId, (AgentCapability)999);

        result.Should().BeTrue();
    }

    #endregion

    #region IsRolePermittedAsync Tests

    [Fact]
    public async Task IsRolePermittedAsync_WhenNoRolesConfigured_ReturnsTrue()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = null
        });

        var result = await service.IsRolePermittedAsync(TenantId, "AnyRole");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRolePermittedAsync_WhenEmptyRolesList_ReturnsTrue()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = new List<string>()
        });

        var result = await service.IsRolePermittedAsync(TenantId, "AnyRole");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRolePermittedAsync_WhenRoleIsAllowed_ReturnsTrue()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = new List<string> { "Attorney", "Paralegal", "Admin" }
        });

        var result = await service.IsRolePermittedAsync(TenantId, "Attorney");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRolePermittedAsync_WhenRoleIsNotAllowed_ReturnsFalse()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = new List<string> { "Attorney", "Paralegal" }
        });

        var result = await service.IsRolePermittedAsync(TenantId, "Guest");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsRolePermittedAsync_CaseInsensitiveComparison()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = new List<string> { "Attorney" }
        });

        var result = await service.IsRolePermittedAsync(TenantId, "attorney");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsRolePermittedAsync_CaseInsensitiveComparison_UpperCase()
    {
        var service = CreateService(new AgentConfigurationOptions
        {
            AllowedRoles = new List<string> { "attorney" }
        });

        var result = await service.IsRolePermittedAsync(TenantId, "ATTORNEY");

        result.Should().BeTrue();
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Fact]
    public async Task InvalidateCacheAsync_RemovesPlaybookCache()
    {
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:exposed-playbooks",
            JsonSerializer.Serialize(new List<Guid> { Guid.NewGuid() }));

        var service = CreateService();
        await service.InvalidateCacheAsync(TenantId);

        var cached = await _cache.GetStringAsync($"agent:config:{TenantId}:exposed-playbooks");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_RemovesCapabilitiesCache()
    {
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:capabilities",
            JsonSerializer.Serialize(new Dictionary<string, bool> { ["DocumentSearch"] = false }));

        var service = CreateService();
        await service.InvalidateCacheAsync(TenantId);

        var cached = await _cache.GetStringAsync($"agent:config:{TenantId}:capabilities");
        cached.Should().BeNull();
    }

    [Fact]
    public async Task InvalidateCacheAsync_DoesNotAffectOtherTenants()
    {
        var otherTenant = "other-tenant";
        await _cache.SetStringAsync(
            $"agent:config:{TenantId}:exposed-playbooks",
            "will-be-cleared");
        await _cache.SetStringAsync(
            $"agent:config:{otherTenant}:exposed-playbooks",
            "should-remain");

        var service = CreateService();
        await service.InvalidateCacheAsync(TenantId);

        var cleared = await _cache.GetStringAsync($"agent:config:{TenantId}:exposed-playbooks");
        var remaining = await _cache.GetStringAsync($"agent:config:{otherTenant}:exposed-playbooks");

        cleared.Should().BeNull();
        remaining.Should().Be("should-remain");
    }

    [Fact]
    public async Task InvalidateCacheAsync_LogsInvalidation()
    {
        var service = CreateService();

        await service.InvalidateCacheAsync(TenantId);

        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(TenantId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task InvalidateCacheAsync_SucceedsWhenNoCacheExists()
    {
        var service = CreateService();

        // Should not throw even when there's nothing to invalidate
        var act = () => service.InvalidateCacheAsync("nonexistent-tenant");

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task GetExposedPlaybookIdsAsync_RespectsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var service = CreateService(new AgentConfigurationOptions
        {
            DefaultExposedPlaybookIds = new List<Guid> { Guid.NewGuid() }
        });

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => service.GetExposedPlaybookIdsAsync(TenantId, cts.Token));
    }

    #endregion
}
