using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Queries sprk_communicationaccount records from Dataverse with Redis caching.
/// Replaces the concept of querying sprk_approvedsender with a proper account-based model.
/// Registered as concrete type in AddCommunicationModule() per ADR-010.
/// </summary>
public sealed class CommunicationAccountService
{
    private const string SendEnabledCacheKey = "comm:accounts:send-enabled";
    private const string ReceiveEnabledCacheKey = "comm:accounts:receive-enabled";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ICommunicationDataverseService _communicationService;
    private readonly IGenericEntityService _genericEntityService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<CommunicationAccountService> _logger;

    public CommunicationAccountService(
        ICommunicationDataverseService communicationService,
        IGenericEntityService genericEntityService,
        IDistributedCache cache,
        ILogger<CommunicationAccountService> logger)
    {
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns all active communication accounts where sprk_sendenabled eq true.
    /// Results are cached in Redis with a 5-minute TTL.
    /// </summary>
    public async Task<CommunicationAccount[]> QuerySendEnabledAccountsAsync(CancellationToken ct = default)
    {
        return await GetCachedAccountsAsync(
            SendEnabledCacheKey,
            "sprk_sendenabled eq true and statecode eq 0",
            "sprk_emailaddress,sprk_displayname,sprk_isdefaultsender,sprk_accounttype,sprk_securitygroupid,sprk_securitygroupname,sprk_name,sprk_verificationstatus,sprk_lastverified,sprk_archiveoutgoingoptin,sprk_sendstoday,sprk_dailysendlimit",
            ct);
    }

    /// <summary>
    /// Returns all active communication accounts where sprk_receiveenabled eq true.
    /// Results are cached in Redis with a 5-minute TTL.
    /// </summary>
    public async Task<CommunicationAccount[]> QueryReceiveEnabledAccountsAsync(CancellationToken ct = default)
    {
        return await GetCachedAccountsAsync(
            ReceiveEnabledCacheKey,
            "sprk_receiveenabled eq true and statecode eq 0",
            "sprk_emailaddress,sprk_displayname,sprk_accounttype,sprk_name,sprk_graphsubscriptionid,sprk_graphsubscriptionexpiry,sprk_monitorfolder,sprk_autocreaterecords,sprk_archiveincomingoptin,sprk_securitygroupid,sprk_securitygroupname",
            ct);
    }

    /// <summary>
    /// Returns the default send-enabled account, or null if none found.
    /// </summary>
    public async Task<CommunicationAccount?> GetDefaultSendAccountAsync(CancellationToken ct = default)
    {
        var accounts = await QuerySendEnabledAccountsAsync(ct);
        return accounts.FirstOrDefault(a => a.IsDefaultSender) ?? accounts.FirstOrDefault();
    }

    /// <summary>
    /// Returns a send-enabled account by email address (case-insensitive), or null if not found.
    /// </summary>
    public async Task<CommunicationAccount?> GetSendAccountByEmailAsync(string email, CancellationToken ct = default)
    {
        var accounts = await QuerySendEnabledAccountsAsync(ct);
        return accounts.FirstOrDefault(a =>
            string.Equals(a.EmailAddress, email, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<CommunicationAccount[]> GetCachedAccountsAsync(
        string cacheKey, string filter, string select, CancellationToken ct)
    {
        // Check Redis cache first
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, ct);
            if (cached is not null)
            {
                var cachedAccounts = JsonSerializer.Deserialize<CommunicationAccount[]>(cached);
                if (cachedAccounts is not null)
                {
                    _logger.LogDebug("Returning {Count} communication accounts from cache ({Key})", cachedAccounts.Length, cacheKey);
                    return cachedAccounts;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read communication accounts from Redis cache ({Key}); querying Dataverse", cacheKey);
        }

        // Query Dataverse
        CommunicationAccount[] accounts;
        try
        {
            var entities = await _communicationService.QueryCommunicationAccountsAsync(filter, select, ct);
            accounts = entities.Select(MapToCommunicationAccount).ToArray();
            _logger.LogDebug("Retrieved {Count} communication accounts from Dataverse ({Key})", accounts.Length, cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query communication accounts from Dataverse ({Key}); returning empty", cacheKey);
            return Array.Empty<CommunicationAccount>();
        }

        // Cache the result
        try
        {
            var serialized = JsonSerializer.Serialize(accounts);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            };
            await _cache.SetStringAsync(cacheKey, serialized, cacheOptions, ct);
            _logger.LogDebug("Cached {Count} communication accounts ({Key}) with {Ttl} TTL", accounts.Length, cacheKey, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache communication accounts ({Key}); continuing without cache", cacheKey);
        }

        return accounts;
    }

    private static CommunicationAccount MapToCommunicationAccount(Entity entity)
    {
        return new CommunicationAccount
        {
            Id = entity.Id,
            Name = entity.GetAttributeValue<string>("sprk_name") ?? string.Empty,
            EmailAddress = entity.GetAttributeValue<string>("sprk_emailaddress") ?? string.Empty,
            DisplayName = entity.GetAttributeValue<string>("sprk_displayname"),
            AccountType = entity.Contains("sprk_accounttype")
                ? (AccountType)(entity.GetAttributeValue<OptionSetValue>("sprk_accounttype")?.Value ?? 100000000)
                : AccountType.SharedAccount,
            SendEnabled = entity.GetAttributeValue<bool>("sprk_sendenabled"),
            IsDefaultSender = entity.GetAttributeValue<bool>("sprk_isdefaultsender"),
            ReceiveEnabled = entity.GetAttributeValue<bool>("sprk_receiveenabled"),
            MonitorFolder = entity.GetAttributeValue<string>("sprk_monitorfolder"),
            AutoCreateRecords = entity.GetAttributeValue<bool>("sprk_autocreaterecords"),
            ArchiveIncomingOptIn = entity.Contains("sprk_archiveincomingoptin")
                ? entity.GetAttributeValue<bool?>("sprk_archiveincomingoptin")
                : null,
            ArchiveOutgoingOptIn = entity.Contains("sprk_archiveoutgoingoptin")
                ? entity.GetAttributeValue<bool?>("sprk_archiveoutgoingoptin")
                : null,
            SubscriptionId = entity.GetAttributeValue<string>("sprk_graphsubscriptionid"),
            SubscriptionExpiry = entity.Contains("sprk_graphsubscriptionexpiry")
                ? entity.GetAttributeValue<DateTime?>("sprk_graphsubscriptionexpiry")
                : null,
            SecurityGroupId = entity.GetAttributeValue<string>("sprk_securitygroupid"),
            SecurityGroupName = entity.GetAttributeValue<string>("sprk_securitygroupname"),
            VerificationStatus = entity.Contains("sprk_verificationstatus")
                ? (VerificationStatus?)(entity.GetAttributeValue<OptionSetValue>("sprk_verificationstatus")?.Value)
                : null,
            LastVerified = entity.Contains("sprk_lastverified")
                ? entity.GetAttributeValue<DateTime?>("sprk_lastverified")
                : null,
            VerificationMessage = entity.GetAttributeValue<string>("sprk_verificationmessage"),
            SendsToday = entity.GetAttributeValue<int>("sprk_sendstoday"),
            DailySendLimit = entity.Contains("sprk_dailysendlimit")
                ? entity.GetAttributeValue<int?>("sprk_dailysendlimit")
                : null
        };
    }

    /// <summary>
    /// Increments sprk_sendstoday on the Dataverse record for the given account.
    /// Best-effort: callers should catch exceptions and not fail the send.
    /// Bypasses cache — writes directly to Dataverse, then invalidates the send-enabled cache.
    /// </summary>
    public async Task IncrementSendCountAsync(Guid accountId, CancellationToken ct = default)
    {
        // Read current value directly from Dataverse (bypass cache for accuracy)
        var entity = await _genericEntityService.RetrieveAsync(
            "sprk_communicationaccount", accountId, new[] { "sprk_sendstoday" }, ct);

        var currentCount = entity.GetAttributeValue<int>("sprk_sendstoday");
        var newCount = currentCount + 1;

        await _genericEntityService.UpdateAsync(
            "sprk_communicationaccount",
            accountId,
            new Dictionary<string, object> { ["sprk_sendstoday"] = newCount },
            ct);

        _logger.LogDebug(
            "Incremented send count for account {AccountId}: {OldCount} → {NewCount}",
            accountId, currentCount, newCount);

        // Invalidate cache so next query picks up updated count
        await InvalidateSendEnabledCacheAsync(ct);
    }

    /// <summary>
    /// Resets sprk_sendstoday to 0 for all active communication accounts.
    /// Called by DailySendCountResetService at midnight UTC.
    /// </summary>
    public async Task ResetAllSendCountsAsync(CancellationToken ct = default)
    {
        var filter = "statecode eq 0 and sprk_sendstoday gt 0";
        var select = "sprk_sendstoday";

        Entity[] entities;
        try
        {
            entities = await _communicationService.QueryCommunicationAccountsAsync(filter, select, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query accounts for daily send count reset");
            throw;
        }

        if (entities.Length == 0)
        {
            _logger.LogDebug("No accounts with non-zero send counts; skipping reset");
            return;
        }

        var updates = entities
            .Select(e => (e.Id, new Dictionary<string, object> { ["sprk_sendstoday"] = 0 }))
            .ToList();

        await _genericEntityService.BulkUpdateAsync("sprk_communicationaccount", updates, ct);

        _logger.LogInformation(
            "Reset daily send counts for {Count} communication accounts", entities.Length);

        // Invalidate cache so queries pick up zeroed counts
        await InvalidateSendEnabledCacheAsync(ct);
    }

    /// <summary>
    /// Removes the send-enabled cache entry so the next query fetches fresh data from Dataverse.
    /// </summary>
    private async Task InvalidateSendEnabledCacheAsync(CancellationToken ct)
    {
        try
        {
            await _cache.RemoveAsync(SendEnabledCacheKey, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate send-enabled cache after send count update");
        }
    }
}
