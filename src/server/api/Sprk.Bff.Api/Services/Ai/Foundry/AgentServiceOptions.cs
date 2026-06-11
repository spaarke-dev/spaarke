using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Foundry;

/// <summary>
/// Configuration options for <see cref="AgentServiceClient"/>.
///
/// Bound from appsettings section <c>AgentService</c> in Program.cs.
/// Validated at startup via <c>ValidateOnStart()</c> (ADR-010: Options + ValidateOnStart pattern).
///
/// ADR-018: Kill switch — <see cref="Enabled"/> must be checked before every operation.
/// ADR-016: Concurrency — <see cref="MaxConcurrency"/> controls the SemaphoreSlim gate.
/// ADR-009: Redis — <see cref="ThreadCacheExpiryMinutes"/> controls sliding expiry for thread ID cache.
/// </summary>
public sealed class AgentServiceOptions
{
    /// <summary>Configuration section name used for binding in Program.cs and AiModule.cs.</summary>
    public const string SectionName = "AgentService";

    /// <summary>
    /// Kill switch (ADR-018). When <c>false</c>, all <see cref="AgentServiceClient"/> operations
    /// immediately throw <see cref="FeatureDisabledException"/> before any network call is made.
    /// Default: <c>false</c> (opt-in: must be explicitly enabled in configuration).
    /// </summary>
    [Required]
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// Azure AI Foundry project endpoint URI.
    /// Format: <c>https://&lt;hub-name&gt;.services.ai.azure.com/api/projects/&lt;project-name&gt;</c>
    /// or the AI Foundry project connection string (endpoint format accepted by <see cref="Azure.AI.Projects.AgentsClient"/>).
    /// Required when <see cref="Enabled"/> is <c>true</c>; validated at use-site
    /// (<c>AgentServiceClient.CreateAgentsClient</c>) NOT at startup, so DI containers
    /// in tests + Spaarke Dev environments without Foundry configured can still build.
    /// Mirrors the BingGroundingOptions hardening pattern (R6 Wave B-G8).
    /// </summary>
    public Uri? Endpoint { get; init; }

    /// <summary>
    /// Azure AI Foundry Agent ID (the pre-provisioned agent to attach threads/runs to).
    /// Retrieve from the AI Foundry Studio or via the Azure AI Projects SDK.
    /// Required when <see cref="Enabled"/> is <c>true</c>; validated at use-site
    /// (<c>AgentServiceClient.CreateAgentsClient</c>) NOT at startup. See <see cref="Endpoint"/>.
    /// </summary>
    public string AgentId { get; init; } = string.Empty;

    /// <summary>
    /// Maximum number of concurrent Foundry Agent operations per BFF instance (ADR-016).
    /// Enforced via a <see cref="System.Threading.SemaphoreSlim"/> keyed by tenant.
    /// Operations that cannot acquire the semaphore within 30 seconds throw
    /// <see cref="ConcurrencyLimitExceededException"/> (HTTP 429 equivalent).
    /// Default: 4 concurrent operations.
    /// </summary>
    [Required]
    [Range(1, 64)]
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>
    /// Sliding expiry (in minutes) for the Redis thread ID cache (ADR-009).
    /// Cache key pattern: <c>agent-thread:{tenantId}</c>.
    /// After this window of inactivity, the thread ID is evicted and a new thread is created
    /// on the next call to <see cref="AgentServiceClient.CreateOrResumeThreadAsync"/>.
    /// Default: 60 minutes.
    /// </summary>
    [Required]
    [Range(1, 1440)]
    public int ThreadCacheExpiryMinutes { get; init; } = 60;
}
