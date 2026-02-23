using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Validates that a requested sender mailbox is in the approved senders list.
/// Phase 1: Reads from CommunicationOptions.ApprovedSenders[] configuration only (synchronous Resolve).
/// Phase 2: Merges BFF config with Dataverse sprk_communicationaccount records via CommunicationAccountService,
///          cached in Redis (async ResolveAsync). Falls back to config-only on service failure.
/// </summary>
public sealed class ApprovedSenderValidator
{
    private const string CacheKey = "communication:approved-senders";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly CommunicationOptions _options;
    private readonly CommunicationAccountService _accountService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ApprovedSenderValidator> _logger;

    public ApprovedSenderValidator(
        IOptions<CommunicationOptions> options,
        CommunicationAccountService accountService,
        IDistributedCache cache,
        ILogger<ApprovedSenderValidator> logger)
    {
        _options = options.Value;
        _accountService = accountService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolves the sender for a communication request (synchronous, config-only).
    /// If fromMailbox is null, returns the default sender.
    /// If fromMailbox is specified, validates it against the approved list.
    /// </summary>
    /// <param name="fromMailbox">Requested sender mailbox, or null for default.</param>
    /// <returns>Validation result with resolved sender or error.</returns>
    public ApprovedSenderResult Resolve(string? fromMailbox)
    {
        var senders = _options.ApprovedSenders ?? Array.Empty<ApprovedSenderConfig>();

        if (fromMailbox is null)
        {
            return ResolveDefault(senders);
        }

        return ResolveExplicit(fromMailbox, senders);
    }

    /// <summary>
    /// Resolves the sender for a communication request (async, merges BFF config + Dataverse).
    /// Queries CommunicationAccountService for send-enabled accounts, merges with BFF config
    /// (Dataverse wins on email match), and caches the merged list in Redis with a 5-minute TTL.
    /// Falls back to config-only on CommunicationAccountService failure.
    /// </summary>
    /// <param name="fromMailbox">Requested sender mailbox, or null for default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Validation result with resolved sender or error.</returns>
    public async Task<ApprovedSenderResult> ResolveAsync(string? fromMailbox, CancellationToken ct = default)
    {
        var senders = await GetMergedSendersAsync(ct);

        if (fromMailbox is null)
        {
            return ResolveDefault(senders);
        }

        return ResolveExplicit(fromMailbox, senders);
    }

    /// <summary>
    /// Gets the merged approved senders list from cache or by querying CommunicationAccountService and merging with config.
    /// </summary>
    private async Task<ApprovedSenderConfig[]> GetMergedSendersAsync(CancellationToken ct)
    {
        // Check Redis cache first
        try
        {
            var cached = await _cache.GetStringAsync(CacheKey, ct);
            if (cached is not null)
            {
                var cachedSenders = JsonSerializer.Deserialize<ApprovedSenderConfig[]>(cached);
                if (cachedSenders is not null)
                {
                    _logger.LogDebug("Returning {Count} approved senders from cache", cachedSenders.Length);
                    return cachedSenders;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read approved senders from Redis cache; proceeding with account service query");
        }

        // Query CommunicationAccountService for send-enabled accounts
        var configSenders = _options.ApprovedSenders ?? Array.Empty<ApprovedSenderConfig>();
        ApprovedSenderConfig[] accountSenders;

        try
        {
            var accounts = await _accountService.QuerySendEnabledAccountsAsync(ct);
            accountSenders = MapToApprovedSenderConfigs(accounts);
            _logger.LogDebug("Retrieved {Count} send-enabled accounts from CommunicationAccountService", accountSenders.Length);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query CommunicationAccountService for send-enabled accounts; returning config-only senders");
            return configSenders;
        }

        // Merge: config senders as base, account senders overlay (Dataverse wins on email match)
        var merged = MergeSenders(configSenders, accountSenders);

        // Cache the merged result with 5-minute TTL
        try
        {
            var serialized = JsonSerializer.Serialize(merged);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            };
            await _cache.SetStringAsync(CacheKey, serialized, cacheOptions, ct);
            _logger.LogDebug("Cached {Count} merged approved senders with {Ttl} TTL", merged.Length, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache approved senders in Redis; continuing without cache");
        }

        return merged;
    }

    /// <summary>
    /// Maps CommunicationAccount objects to ApprovedSenderConfig objects.
    /// Uses DisplayName with fallback to Name for the display name.
    /// </summary>
    private static ApprovedSenderConfig[] MapToApprovedSenderConfigs(CommunicationAccount[] accounts)
    {
        return accounts
            .Select(a => new ApprovedSenderConfig
            {
                Email = a.EmailAddress,
                DisplayName = a.DisplayName ?? a.Name,
                IsDefault = a.IsDefaultSender
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Email))
            .ToArray();
    }

    /// <summary>
    /// Merges config senders with account senders from CommunicationAccountService.
    /// Starts with config senders as base, then overlays account senders.
    /// Account senders (Dataverse) win on email match (case-insensitive).
    /// </summary>
    private static ApprovedSenderConfig[] MergeSenders(ApprovedSenderConfig[] configSenders, ApprovedSenderConfig[] accountSenders)
    {
        var merged = new Dictionary<string, ApprovedSenderConfig>(StringComparer.OrdinalIgnoreCase);

        foreach (var s in configSenders)
        {
            merged[s.Email] = s;
        }

        // Overlay account senders (Dataverse wins on match)
        foreach (var s in accountSenders)
        {
            merged[s.Email] = s;
        }

        return merged.Values.ToArray();
    }

    private ApprovedSenderResult ResolveDefault(ApprovedSenderConfig[] senders)
    {
        // Check for explicit default
        var defaultSender = senders.FirstOrDefault(s => s.IsDefault);
        if (defaultSender is not null)
        {
            return ApprovedSenderResult.Valid(defaultSender.Email, defaultSender.DisplayName);
        }

        // Check for DefaultMailbox in options
        if (!string.IsNullOrWhiteSpace(_options.DefaultMailbox))
        {
            var matchByDefault = senders.FirstOrDefault(s =>
                string.Equals(s.Email, _options.DefaultMailbox, StringComparison.OrdinalIgnoreCase));

            if (matchByDefault is not null)
            {
                return ApprovedSenderResult.Valid(matchByDefault.Email, matchByDefault.DisplayName);
            }
        }

        // Fall back to first sender
        if (senders.Length > 0)
        {
            return ApprovedSenderResult.Valid(senders[0].Email, senders[0].DisplayName);
        }

        return ApprovedSenderResult.NoDefaultSender();
    }

    private static ApprovedSenderResult ResolveExplicit(string fromMailbox, ApprovedSenderConfig[] senders)
    {
        var match = senders.FirstOrDefault(s =>
            string.Equals(s.Email, fromMailbox, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return ApprovedSenderResult.Valid(match.Email, match.DisplayName);
        }

        return ApprovedSenderResult.InvalidSender(fromMailbox);
    }
}
