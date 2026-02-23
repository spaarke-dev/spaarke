using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication;

/// <summary>
/// Background service that manages Graph webhook subscriptions for inbound email monitoring.
/// Automatically creates, renews, and recreates subscriptions for all receive-enabled
/// communication accounts. No human in loop -- fully automated lifecycle.
///
/// Implements ADR-001 BackgroundService pattern with PeriodicTimer (30-minute interval).
/// Graph mail subscriptions have a maximum lifetime of 3 days; this service renews them
/// when expiry is less than 24 hours away.
/// </summary>
public sealed class GraphSubscriptionManager : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RenewalThreshold = TimeSpan.FromHours(24);
    private static readonly TimeSpan SubscriptionLifetime = TimeSpan.FromDays(3);

    private readonly CommunicationAccountService _accountService;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<GraphSubscriptionManager> _logger;
    private readonly string _notificationUrl;
    private readonly string _clientState;

    public GraphSubscriptionManager(
        CommunicationAccountService accountService,
        IGraphClientFactory graphClientFactory,
        IDataverseService dataverseService,
        IConfiguration configuration,
        ILogger<GraphSubscriptionManager> logger)
    {
        _accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _notificationUrl = configuration["Communication:WebhookNotificationUrl"]
            ?? throw new InvalidOperationException("Communication:WebhookNotificationUrl not configured");
        _clientState = configuration["Communication:WebhookClientState"]
            ?? throw new InvalidOperationException("Communication:WebhookClientState not configured");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "GraphSubscriptionManager starting with {Interval} minute interval, " +
            "renewal threshold {Threshold} hours, subscription lifetime {Lifetime} days",
            TickInterval.TotalMinutes, RenewalThreshold.TotalHours, SubscriptionLifetime.TotalDays);

        // Use PeriodicTimer for efficient periodic execution (ADR-001 pattern)
        using var timer = new PeriodicTimer(TickInterval);

        // Execute immediately on startup, then on interval
        await ManageSubscriptionsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken))
                {
                    await ManageSubscriptionsAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("GraphSubscriptionManager stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during subscription management cycle, will retry on next interval");
                // Continue running -- don't let one failure stop the service
            }
        }

        _logger.LogInformation("GraphSubscriptionManager stopped");
    }

    /// <summary>
    /// Manages subscriptions for all receive-enabled communication accounts.
    /// For each account: creates, renews, or recreates subscriptions as needed.
    /// </summary>
    private async Task ManageSubscriptionsAsync(CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString();

        _logger.LogDebug(
            "Starting subscription management cycle, correlation {CorrelationId}",
            correlationId);

        CommunicationAccount[] accounts;
        try
        {
            accounts = await _accountService.QueryReceiveEnabledAccountsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to query receive-enabled accounts, correlation {CorrelationId}",
                correlationId);
            return;
        }

        if (accounts.Length == 0)
        {
            _logger.LogDebug("No receive-enabled accounts found, skipping subscription management");
            return;
        }

        _logger.LogInformation(
            "Managing subscriptions for {Count} receive-enabled accounts, correlation {CorrelationId}",
            accounts.Length, correlationId);

        var created = 0;
        var renewed = 0;
        var recreated = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var account in accounts)
        {
            try
            {
                var action = await ManageAccountSubscriptionAsync(account, ct);
                switch (action)
                {
                    case SubscriptionAction.Created:
                        created++;
                        break;
                    case SubscriptionAction.Renewed:
                        renewed++;
                        break;
                    case SubscriptionAction.Recreated:
                        recreated++;
                        break;
                    case SubscriptionAction.Skipped:
                        skipped++;
                        break;
                }
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogError(ex,
                    "Failed to manage subscription for account {AccountId} ({Email}), correlation {CorrelationId}",
                    account.Id, account.EmailAddress, correlationId);
            }
        }

        _logger.LogInformation(
            "Subscription management cycle complete: {Created} created, {Renewed} renewed, " +
            "{Recreated} recreated, {Skipped} skipped, {Failed} failed, correlation {CorrelationId}",
            created, renewed, recreated, skipped, failed, correlationId);
    }

    /// <summary>
    /// Manages the subscription for a single communication account.
    /// Decision logic:
    ///   - No SubscriptionId -> CREATE new subscription
    ///   - SubscriptionExpiry less than 24h from now -> RENEW existing subscription
    ///   - Renewal fails (404 or error) -> DELETE old + CREATE new (RECREATE)
    ///   - Otherwise -> SKIP (subscription is healthy)
    /// </summary>
    private async Task<SubscriptionAction> ManageAccountSubscriptionAsync(
        CommunicationAccount account, CancellationToken ct)
    {
        // No subscription exists -- create one
        if (string.IsNullOrEmpty(account.SubscriptionId))
        {
            _logger.LogInformation(
                "No subscription for account {AccountId} ({Email}), creating new subscription",
                account.Id, account.EmailAddress);

            await CreateSubscriptionAsync(account, ct);
            return SubscriptionAction.Created;
        }

        // Check if renewal is needed (expiry < 24 hours from now)
        var needsRenewal = !account.SubscriptionExpiry.HasValue
            || account.SubscriptionExpiry.Value < DateTimeOffset.UtcNow.Add(RenewalThreshold);

        if (!needsRenewal)
        {
            _logger.LogDebug(
                "Subscription {SubscriptionId} for account {AccountId} ({Email}) is healthy, " +
                "expires {Expiry}",
                account.SubscriptionId, account.Id, account.EmailAddress,
                account.SubscriptionExpiry);

            return SubscriptionAction.Skipped;
        }

        // Try to renew the existing subscription
        _logger.LogInformation(
            "Subscription {SubscriptionId} for account {AccountId} ({Email}) needs renewal, " +
            "expires {Expiry}",
            account.SubscriptionId, account.Id, account.EmailAddress,
            account.SubscriptionExpiry);

        try
        {
            await RenewSubscriptionAsync(account, ct);
            return SubscriptionAction.Renewed;
        }
        catch (ODataError odataEx) when (odataEx.ResponseStatusCode == 404)
        {
            _logger.LogWarning(
                "Subscription {SubscriptionId} for account {AccountId} ({Email}) not found (404), " +
                "recreating",
                account.SubscriptionId, account.Id, account.EmailAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Renewal failed for subscription {SubscriptionId} on account {AccountId} ({Email}), " +
                "attempting delete + recreate",
                account.SubscriptionId, account.Id, account.EmailAddress);
        }

        // Renewal failed -- delete old subscription and create a new one
        await TryDeleteSubscriptionAsync(account.SubscriptionId, account, ct);
        await CreateSubscriptionAsync(account, ct);
        return SubscriptionAction.Recreated;
    }

    /// <summary>
    /// Creates a new Graph subscription for a communication account's mailbox.
    /// Updates the Dataverse account record with the new subscription ID and expiry.
    /// </summary>
    private async Task CreateSubscriptionAsync(CommunicationAccount account, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.ForApp();
        var monitorFolder = account.MonitorFolder ?? "Inbox";
        var expirationDateTime = DateTimeOffset.UtcNow.Add(SubscriptionLifetime);

        var subscription = new Subscription
        {
            ChangeType = "created",
            NotificationUrl = _notificationUrl,
            Resource = $"users/{account.EmailAddress}/mailFolders/{monitorFolder}/messages",
            ExpirationDateTime = expirationDateTime,
            ClientState = _clientState
        };

        var result = await graphClient.Subscriptions.PostAsync(subscription, cancellationToken: ct);

        _logger.LogInformation(
            "Created Graph subscription {SubscriptionId} for account {AccountId} ({Email}), " +
            "resource {Resource}, expires {Expiry}",
            result!.Id, account.Id, account.EmailAddress,
            subscription.Resource, result.ExpirationDateTime);

        // Update Dataverse account record with subscription info
        await UpdateAccountSubscriptionAsync(
            account.Id, result.Id!, result.ExpirationDateTime!.Value, ct);
    }

    /// <summary>
    /// Renews an existing Graph subscription by extending its expiration.
    /// Updates the Dataverse account record with the new expiry.
    /// </summary>
    private async Task RenewSubscriptionAsync(CommunicationAccount account, CancellationToken ct)
    {
        var graphClient = _graphClientFactory.ForApp();
        var newExpiry = DateTimeOffset.UtcNow.Add(SubscriptionLifetime);

        var renewal = new Subscription
        {
            ExpirationDateTime = newExpiry
        };

        var result = await graphClient.Subscriptions[account.SubscriptionId]
            .PatchAsync(renewal, cancellationToken: ct);

        _logger.LogInformation(
            "Renewed Graph subscription {SubscriptionId} for account {AccountId} ({Email}), " +
            "new expiry {Expiry}",
            account.SubscriptionId, account.Id, account.EmailAddress,
            result!.ExpirationDateTime);

        // Update Dataverse account record with new expiry
        await UpdateAccountSubscriptionAsync(
            account.Id, account.SubscriptionId!, result.ExpirationDateTime!.Value, ct);
    }

    /// <summary>
    /// Attempts to delete an existing Graph subscription. Logs but does not throw on failure
    /// (the subscription may already be expired/gone).
    /// </summary>
    private async Task TryDeleteSubscriptionAsync(
        string subscriptionId, CommunicationAccount account, CancellationToken ct)
    {
        try
        {
            var graphClient = _graphClientFactory.ForApp();
            await graphClient.Subscriptions[subscriptionId].DeleteAsync(cancellationToken: ct);

            _logger.LogInformation(
                "Deleted Graph subscription {SubscriptionId} for account {AccountId} ({Email})",
                subscriptionId, account.Id, account.EmailAddress);
        }
        catch (ODataError odataEx) when (odataEx.ResponseStatusCode == 404)
        {
            _logger.LogDebug(
                "Subscription {SubscriptionId} already gone (404) for account {AccountId} ({Email})",
                subscriptionId, account.Id, account.EmailAddress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to delete subscription {SubscriptionId} for account {AccountId} ({Email}), " +
                "continuing with create",
                subscriptionId, account.Id, account.EmailAddress);
        }
    }

    /// <summary>
    /// Updates the sprk_communicationaccount record in Dataverse with subscription info.
    /// Uses sprk_subscriptionid and sprk_subscriptionexpiry fields.
    /// </summary>
    private async Task UpdateAccountSubscriptionAsync(
        Guid accountId, string subscriptionId, DateTimeOffset expiry, CancellationToken ct)
    {
        var fields = new Dictionary<string, object>
        {
            ["sprk_subscriptionid"] = subscriptionId,
            ["sprk_subscriptionexpiry"] = expiry.UtcDateTime
        };

        await _dataverseService.UpdateAsync("sprk_communicationaccount", accountId, fields, ct);

        _logger.LogDebug(
            "Updated Dataverse account {AccountId} with subscription {SubscriptionId}, expiry {Expiry}",
            accountId, subscriptionId, expiry);
    }

    private enum SubscriptionAction
    {
        Created,
        Renewed,
        Recreated,
        Skipped
    }
}
