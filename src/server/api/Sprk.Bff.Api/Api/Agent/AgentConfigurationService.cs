using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Sprk.Bff.Api.Api.Agent;

/// <summary>
/// Manages admin configuration for the M365 Copilot agent:
/// which playbooks are exposed, per-role restrictions, and feature toggles.
/// Configuration can be stored in Dataverse or appsettings with Redis cache overlay.
/// </summary>
public sealed class AgentConfigurationService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<AgentConfigurationService> _logger;
    private readonly AgentConfigurationOptions _options;

    private const string CacheKeyPrefix = "agent:config:";
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
    };

    public AgentConfigurationService(
        IDistributedCache cache,
        ILogger<AgentConfigurationService> logger,
        IOptions<AgentConfigurationOptions> options)
    {
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Returns the list of playbook IDs that are exposed to the Copilot agent
    /// for a given tenant. If no explicit configuration exists, returns all public playbooks.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> GetExposedPlaybookIdsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}:exposed-playbooks";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (cached is not null)
        {
            return JsonSerializer.Deserialize<List<Guid>>(cached) ?? new List<Guid>();
        }

        // TODO: Query Dataverse for sprk_agentconfiguration entity
        // to get admin-configured playbook list.
        // For now, return configured defaults from options.
        var defaults = _options.DefaultExposedPlaybookIds ?? new List<Guid>();

        await _cache.SetStringAsync(cacheKey,
            JsonSerializer.Serialize(defaults), CacheOptions, cancellationToken);

        return defaults;
    }

    /// <summary>
    /// Checks if a specific capability is enabled for the agent in this tenant.
    /// </summary>
    public async Task<bool> IsCapabilityEnabledAsync(
        string tenantId,
        AgentCapability capability,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{tenantId}:capabilities";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (cached is not null)
        {
            var caps = JsonSerializer.Deserialize<Dictionary<string, bool>>(cached);
            if (caps is not null && caps.TryGetValue(capability.ToString(), out var enabled))
                return enabled;
        }

        // Default capabilities from options
        return capability switch
        {
            AgentCapability.DocumentSearch => _options.EnableDocumentSearch,
            AgentCapability.PlaybookInvocation => _options.EnablePlaybookInvocation,
            AgentCapability.EmailDrafting => _options.EnableEmailDrafting,
            AgentCapability.MatterQueries => _options.EnableMatterQueries,
            AgentCapability.AnalysisHandoff => _options.EnableAnalysisHandoff,
            _ => true
        };
    }

    /// <summary>
    /// Checks if a user role is permitted to use the Copilot agent.
    /// </summary>
    public async Task<bool> IsRolePermittedAsync(
        string tenantId,
        string userRole,
        CancellationToken cancellationToken = default)
    {
        if (_options.AllowedRoles is null || _options.AllowedRoles.Count == 0)
            return true; // No role restrictions configured

        return _options.AllowedRoles.Contains(userRole, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Invalidates cached configuration for a tenant (called when admin changes settings).
    /// </summary>
    public async Task InvalidateCacheAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync($"{CacheKeyPrefix}{tenantId}:exposed-playbooks", cancellationToken);
        await _cache.RemoveAsync($"{CacheKeyPrefix}{tenantId}:capabilities", cancellationToken);

        _logger.LogInformation(
            "Invalidated agent configuration cache for tenant {TenantId}", tenantId);
    }
}

/// <summary>
/// Agent capabilities that can be enabled/disabled per tenant.
/// </summary>
public enum AgentCapability
{
    DocumentSearch,
    PlaybookInvocation,
    EmailDrafting,
    MatterQueries,
    AnalysisHandoff
}

/// <summary>
/// Configuration options for the Copilot agent. Loaded from appsettings via Options pattern.
/// </summary>
public sealed class AgentConfigurationOptions
{
    public const string SectionName = "CopilotAgent";

    /// <summary>Default playbook IDs exposed to the agent when no Dataverse config exists.</summary>
    public List<Guid>? DefaultExposedPlaybookIds { get; set; }

    /// <summary>Roles permitted to use the Copilot agent. Empty = all roles allowed.</summary>
    public List<string>? AllowedRoles { get; set; }

    // Feature toggles (default: all enabled)
    public bool EnableDocumentSearch { get; set; } = true;
    public bool EnablePlaybookInvocation { get; set; } = true;
    public bool EnableEmailDrafting { get; set; } = true;
    public bool EnableMatterQueries { get; set; } = true;
    public bool EnableAnalysisHandoff { get; set; } = true;
}
